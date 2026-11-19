using Wbskt.Internal;

namespace Wbskt;

public sealed class WbsktClient : IWbsktClient
{
    private readonly ClientConfig _config;
    private readonly IClientStorage _storage;
    private readonly AuthClient _auth;
    private readonly SocketClient _socket;
    private readonly OutboundBuffer _outbound;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly TokenBucket _pacer = new(ClientProtocol.SendPerSecond, ClientProtocol.SendBurst);
    private readonly CancellationTokenSource _cts = new();
    internal ReconnectPolicy Reconnect { get; init; } = new();
    private readonly ClientTokenCache _tokens;

    /// <summary>How often a connected client checks whether its socket is still open.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    
    private volatile bool _shouldReconnect = true;
    // The reconnect monitor of the current start, and what stops it; replaced by each start after a stop.
    private Task _monitor = Task.CompletedTask;
    private CancellationTokenSource _run = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    // Set on the monitor's flow, so a handler it raises (OnConnectFailed, OnSuperseded, OnConnected) can
    // stop or dispose the client without waiting for the monitor that is running it.
    private static readonly AsyncLocal<WbsktClient?> MonitorOf = new();
    // Counts connections. The one whose capabilities are out is recorded by its number, so an announcement
    // still finishing for an old connection cannot mark a newer one announced; nothing else is sent before them.
    private int _connection;
    private int _announcedConnection = -1;
    private Guid? _resolvedRefId;
    private string? _resolvedSecret;
    private ClientCapabilities? _lastCapabilities;
    private int _disposed;
    // 1 while started: from StartAsync until the client stops for good or is disposed.
    private int _started;

    public event Action<string, object?, string?>? OnMessageReceived;

    public Func<WbsktMessage, CancellationToken, Task>? MessageHandler
    {
        get => _socket.MessageHandler;
        set => _socket.MessageHandler = value;
    }

    public event Action<Exception>? OnHandlerError;

    public event Action<Exception>? OnConnectFailed;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public event Action? OnSuperseded;

    public int PendingMessageCount => _outbound.Count;

    /// <inheritdoc/>
    public long DroppedMessageCount => _outbound.DroppedCount;

    public bool IsConnected => _socket.IsConnected && Announced;

    /// <summary>
    /// A client named <paramref name="clientName"/> on the hosted service, enrolling with
    /// <paramref name="policyPin"/> the first time and keeping its credentials in a file
    /// (<see cref="FileClientStorage.ForClient(string)"/>) for every start after that.
    /// </summary>
    public WbsktClient(string clientName, string policyPin)
        : this(new ClientConfig(WbsktDefaults.ApiUrl, WbsktDefaults.SocketUrl, clientName, policyPin), FileClientStorage.ForClient(clientName))
    {
    }

    /// <summary>Custom URLs (self-hosted, staging) and buffering, with credentials kept in a file.</summary>
    public WbsktClient(ClientConfig config)
        : this(config, FileClientStorage.ForClient(config.ClientName))
    {
    }

    /// <exception cref="ArgumentException">
    /// The configuration can never connect: an empty or over-long name, an API URL that isn't an absolute
    /// http(s) URL, a socket URL that isn't an absolute ws(s) URL, or a negative buffer size.
    /// </exception>
    public WbsktClient(ClientConfig config, IClientStorage storage)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(storage);
        Validate(config);
        _config = config;
        _storage = storage;
        _auth = new AuthClient(config);
        _socket = new SocketClient(config.BaseSocketUrl);
        _outbound = new OutboundBuffer(config.OfflineBufferSize);
        _tokens = new ClientTokenCache(() => _auth.LoginAsync(_resolvedRefId!.Value, _resolvedSecret!));

        // Forward internal events to public surface
        _socket.OnMessageReceived += (type, payload, commandId) => Handlers.Raise(OnMessageReceived, handler => handler(type, payload, commandId), OnHandlerError);
        _socket.OnHandlerError += error => Handlers.Report(error, OnHandlerError);
        _socket.OnConnected += HandleConnected;
        _socket.OnDisconnected += HandleDisconnect;
    }

    /// <summary>
    /// Makes the first connection attempt and returns once it has finished, connected or not. A
    /// failed attempt is raised on <see cref="OnConnectFailed"/> and retried in the background; only
    /// <see cref="RegistrationRequiredException"/>, <see cref="CredentialsRejectedException"/> and
    /// <see cref="CredentialsNotSavedException"/> are thrown, because no retry can fix them.
    /// </summary>
    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        // A second call while started would start a second reconnect loop racing the first.
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
        {
            return;
        }

        if (_run.IsCancellationRequested)
        {
            _run.Dispose();
            _run = new CancellationTokenSource();
        }

        _shouldReconnect = true;
        var run = _run.Token;
        var failures = 0;
        TimeSpan? retryIn = null;
        try
        {
            await ConnectInternalAsync();
        }
        catch (Exception ex) when (IsFinal(ex))
        {
            RaiseConnectFailed(ex);
            Volatile.Write(ref _started, 0);
            throw;
        }
        catch (Exception ex)
        {
            RaiseConnectFailed(ex);
            retryIn = FailureDelay(ex, failures++);
        }

        // Stopped or disposed while the first attempt ran: close what it may have opened, start nothing.
        if (run.IsCancellationRequested)
        {
            await _socket.CloseAsync(SocketClient.CloseTimeout);
            return;
        }

        _monitor = Task.Run(() => MonitorReconnectionAsync(failures, retryIn, run), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        await StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            _shouldReconnect = false;
            await _run.CancelAsync();
            if (MonitorOf.Value != this)
            {
                await _monitor;
            }

            await _socket.CloseAsync(SocketClient.CloseTimeout);
            Volatile.Write(ref _started, 0);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>The longest client name the platform stores.</summary>
    internal const int MaxClientNameLength = 100;

    // Refused here, where the mistake is made: every one of these would otherwise be retried for ever.
    private static void Validate(ClientConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ClientName) || config.ClientName.Length > MaxClientNameLength)
        {
            throw new ArgumentException($"ClientName must be 1-{MaxClientNameLength} characters.", nameof(config));
        }

        if (!IsAbsolute(config.BaseApiUrl, Uri.UriSchemeHttp, Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"BaseApiUrl must be an absolute http or https URL, not '{config.BaseApiUrl}'.", nameof(config));
        }

        if (!IsAbsolute(config.BaseSocketUrl, Uri.UriSchemeWs, Uri.UriSchemeWss))
        {
            throw new ArgumentException($"BaseSocketUrl must be an absolute ws or wss URL, not '{config.BaseSocketUrl}'.", nameof(config));
        }

        if (config.OfflineBufferSize < 0)
        {
            throw new ArgumentException("OfflineBufferSize can't be negative; 0 turns buffering off.", nameof(config));
        }

        // They go out first on every connection, so commands too big to send would never connect.
        ProtocolJson.Frame(new ProtocolMessage(MessageTypes.Capabilities, Effective(config, null)), nameof(config));
    }

    private static bool IsAbsolute(string? url, params string[] schemes) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);

    private async Task ConnectInternalAsync()
    {
        if (!_resolvedRefId.HasValue)
        {
            await ResolveCredentialsAsync();
        }

        // Reuses the last token while it is still valid. Throws CredentialsRejectedException when the client was
        // deleted or its secret rotated; the credentials are kept, as registering again would bring a deleted
        // device back as a new client.
        var token = await _tokens.GetAsync();

        try
        {
            await _socket.ConnectAsync(token);
        }
        catch (Exception) when (_socket.TokenRejected)
        {
            _tokens.Invalidate();
            throw;
        }
    }

    private void HandleConnected()
    {
        // Counted here, when a socket really opened: a connect that found one already open must not
        // count, or the open connection would never count as announced and nothing would be sent.
        var connection = Interlocked.Increment(ref _connection);

        // Always advertise SDK metadata (merged with any app-declared commands) on every successful
        // connection, before the app hears of it, so a handler that throws cannot hold it back.
        _ = AnnounceCapabilitiesAsync(connection);
        Handlers.Raise(OnConnected, OnHandlerError);
    }

    private async Task AnnounceCapabilitiesAsync(int connection)
    {
        try
        {
            // Capabilities describe the connection, so they go out first and are never buffered.
            await _socket.SendAsync(new ProtocolMessage(MessageTypes.Capabilities, BuildEffectiveCapabilities(_lastCapabilities)));
        }
        catch (Exception)
        {
            // Connection may have dropped mid-handshake; the reconnect monitor will retry and re-announce.
            return;
        }

        Volatile.Write(ref _announcedConnection, connection);
        await FlushOutboundAsync();
    }

    private ClientCapabilities BuildEffectiveCapabilities(ClientCapabilities? app) => Effective(_config, app);

    private static ClientCapabilities Effective(ClientConfig config, ClientCapabilities? app)
    {
        // Auto-detected SDK metadata; app-supplied values win when explicitly set, then the configuration's.
        var agent = app is { Agent.Length: > 0 } ? app.Agent : SdkInfo.AgentName;
        var version = app is { Version.Length: > 0 } ? app.Version : SdkInfo.Version;
        var os = app is { OS.Length: > 0 } ? app.OS : SdkInfo.Platform;
        return new ClientCapabilities(agent, version, os, app?.Capabilities ?? [.. config.Commands], app?.FirmwareVersion ?? config.FirmwareVersion);
    }

    private bool Announced => Volatile.Read(ref _announcedConnection) == Volatile.Read(ref _connection);

    private void HandleDisconnect()
    {
        Volatile.Write(ref _announcedConnection, -1);
        Handlers.Raise(OnDisconnected, OnHandlerError);
    }

    private async Task MonitorReconnectionAsync(int failures, TimeSpan? retryIn, CancellationToken run)
    {
        MonitorOf.Value = this;
        try
        {
            await MonitorReconnectionCoreAsync(failures, retryIn, run);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Stopped or disposed: the normal end.
        }
    }

    private async Task MonitorReconnectionCoreAsync(int failures, TimeSpan? retryIn, CancellationToken run)
    {
        if (retryIn is { } firstWait)
        {
            await Task.Delay(firstWait, run);
        }

        while (!run.IsCancellationRequested)
        {
            if (!_shouldReconnect || _socket.IsConnected)
            {
                failures = 0;
                await Task.Delay(PollInterval, run);
                continue;
            }

            if (failures == 0)
            {
                // Just dropped. Every client that shared this socket host noticed at the same moment,
                // so each waits a random part of the window before its first attempt.
                await _socket.WaitForDropAsync();
                var close = _socket.LastClose;
                if (close == ServerClose.Revoked)
                {
                    // Lost access: the token still validates until it expires, so log in afresh.
                    _tokens.Invalidate();
                }
                else if (close == ServerClose.Superseded)
                {
                    Handlers.Raise(OnSuperseded, OnHandlerError);
                }

                await Task.Delay(Reconnect.FirstDelay(close), run);
                if (_socket.IsConnected)
                {
                    continue;
                }
            }

            try
            {
                await ConnectInternalAsync();
                failures = 0;

                // A stop that came while this attempt was connecting closes what it opened.
                if (run.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception ex) when (IsFinal(ex))
            {
                // No credentials and no PIN, credentials refused for good, or new ones not saved: no retry can fix them.
                RaiseConnectFailed(ex);
                Volatile.Write(ref _started, 0);
                return;
            }
            catch (Exception ex)
            {
                RaiseConnectFailed(ex);
                await Task.Delay(FailureDelay(ex, failures++), run);
            }
        }
    }

    private static bool IsFinal(Exception error) =>
        error is RegistrationRequiredException or CredentialsRejectedException or CredentialsNotSavedException;

    private void RaiseConnectFailed(Exception error)
    {
        if (!_cts.IsCancellationRequested)
        {
            Handlers.Raise(OnConnectFailed, handler => handler(error), OnHandlerError);
        }
    }

    private TimeSpan FailureDelay(Exception error, int failures) =>
        error is WbsktConnectException { IsThrottled: true, RetryAfter: { } retryAfter }
            ? Reconnect.AfterThrottle(retryAfter)
            : Reconnect.RetryDelay(failures);

    /// <exception cref="ArgumentException">The capabilities are over 64 KB once serialised.</exception>
    public async Task UpdateCapabilitiesAsync(ClientCapabilities capabilities)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        // Checked before they are kept: they go out first on every connection, so too big would never connect.
        var frame = ProtocolJson.Frame(new ProtocolMessage(MessageTypes.Capabilities, BuildEffectiveCapabilities(capabilities)), nameof(capabilities));
        _lastCapabilities = capabilities;

        if (_socket.IsConnected)
        {
            await _socket.SendAsync(frame.Bytes);
        }
    }

    /// <exception cref="ArgumentException">
    /// The type is empty, too long, or reserved for the protocol (<c>sys.*</c>, <c>capabilities</c>,
    /// <c>state.report</c>), or the message is over 64 KB once serialised.
    /// </exception>
    public Task SendAsync(string type, object payload)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        if (ClientMessageRules.FindViolation(type) is { } violation)
        {
            throw new ArgumentException(violation, nameof(type));
        }

        return EnqueueAsync(type, payload, nameof(payload));
    }

    private async Task EnqueueAsync(string type, object payload, string paramName)
    {
        // Serialised now, so a payload the app reuses (one dictionary refilled every reading) keeps the values
        // it had when it was sent. Checked now too: the gateway closes the connection on a bigger one, and a
        // buffered one would be resent first on every reconnect and never get through.
        var frame = ProtocolJson.Frame(new ProtocolMessage(type, payload) { SentAt = DateTimeOffset.UtcNow }, paramName);

        if (_outbound.Capacity <= 0)
        {
            if (!Announced)
            {
                throw new InvalidOperationException("Socket not connected.");
            }

            await _flushLock.WaitAsync();
            try
            {
                await _pacer.TakeAsync(_cts.Token);
                await _socket.SendAsync(frame.Bytes);
            }
            finally
            {
                _flushLock.Release();
            }

            return;
        }

        // Every message goes through the buffer so a backlog from an outage is never overtaken.
        _outbound.Enqueue(frame);
        await FlushOutboundAsync();
    }

    private async Task FlushOutboundAsync()
    {
        while (Announced && _socket.IsConnected && _outbound.Count > 0)
        {
            // A flush already running sends this message too, so the caller doesn't wait behind a paced backlog.
            if (!await _flushLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                while (Announced && _socket.IsConnected && _outbound.TryPeek(out var next))
                {
                    try
                    {
                        await _pacer.TakeAsync(_cts.Token);
                        await _socket.SendAsync(next.Bytes);
                    }
                    catch (Exception)
                    {
                        // Keep it queued; the next connection resends it.
                        return;
                    }

                    _outbound.RemoveIfHead(next);
                }
            }
            finally
            {
                _flushLock.Release();
            }
        }
    }

    /// <exception cref="ArgumentException">The report is over 64 KB once serialised.</exception>
    public async Task ReportStateAsync(IReadOnlyDictionary<string, object?> patch)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        // Partial update: only the reported variables change; the platform keeps the rest.
        await EnqueueAsync(MessageTypes.StateReport, patch, nameof(patch));
    }

    private async Task ResolveCredentialsAsync()
    {
        var (savedId, savedSecret) = await _storage.LoadCredentialsAsync();

        if (savedId.HasValue && !string.IsNullOrEmpty(savedSecret))
        {
            _resolvedSecret = savedSecret;
            _resolvedRefId = savedId.Value;
            return;
        }

        if (string.IsNullOrEmpty(_config.PolicyPin))
        {
            throw new RegistrationRequiredException();
        }

        var (newId, newSecret) = await _auth.RegisterAsync(_config.PolicyPin);

        // Kept before saving, so this process never registers twice even when the save fails.
        _resolvedSecret = newSecret;
        _resolvedRefId = newId;
        try
        {
            await _storage.SaveCredentialsAsync(newId, newSecret);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CredentialsNotSavedException(newId, ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Stopped first, so the gateway sees a normal close and no reconnect races the disposal.
        await StopCoreAsync();
        await _cts.CancelAsync();
        _auth.Dispose();
        await _socket.DisposeAsync();
        _cts.Dispose();
        _run.Dispose();
        _flushLock.Dispose();
        _lifecycle.Dispose();
    }
}
