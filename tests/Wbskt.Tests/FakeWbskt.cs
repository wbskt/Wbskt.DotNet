using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Wbskt.Internal;

namespace Wbskt.Tests;

/// <summary>
/// The login endpoint and the socket on one loopback listener, for tests that run a whole
/// <see cref="WbsktClient"/>. Every frame a client sends is kept in <see cref="Received"/>.
/// </summary>
internal sealed class FakeWbskt : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serve;
    private int _logins;

    public FakeWbskt()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add($"{Url}/");
        _listener.Start();
        _serve = Task.Run(ServeAsync);
    }

    public string Url { get; }

    public string SocketUrl => Url.Replace("http://", "ws://");

    public int Logins => Volatile.Read(ref _logins);

    public ConcurrentQueue<WebSocket> Sockets { get; } = new();

    public ConcurrentQueue<JsonElement> Received { get; } = new();

    /// <summary>The close status each client sent, in order; a link dropped without a close is not here.</summary>
    public ConcurrentQueue<WebSocketCloseStatus?> Closes { get; } = new();

    public WbsktClient NewClient() => NewClient(new ClientConfig(Url, SocketUrl, "test", "pin"));

    /// <summary>A client that reconnects at once, so a test of a dropped connection doesn't wait out the jitter.</summary>
    public WbsktClient NewClient(ClientConfig config) =>
        new(config, new SavedStorage(Guid.NewGuid(), "secret")) { Reconnect = new ReconnectPolicy(new NoJitter()) };

    private sealed class NoJitter : Random
    {
        public override double NextDouble() => 0;
    }

    /// <summary>Waits until a frame of <paramref name="type"/> has arrived.</summary>
    public async Task<JsonElement> WaitForAsync(string type, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            foreach (var frame in Received)
            {
                if (frame.GetProperty("type").GetString() == type)
                {
                    return frame;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"no {type} frame arrived");
    }

    /// <summary>Waits until <paramref name="count"/> frames of <paramref name="type"/> have arrived.</summary>
    public async Task WaitForCountAsync(string type, int count, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (Received.Count(frame => frame.GetProperty("type").GetString() == type) < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"fewer than {count} {type} frames arrived");
            }

            await Task.Delay(20);
        }
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            if (context.Request.IsWebSocketRequest)
            {
                _ = Task.Run(() => ReceiveAsync(context));
                continue;
            }

            Interlocked.Increment(ref _logins);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync("{\"accessToken\":\"token\",\"expiresIn\":3600}"u8.ToArray());
            context.Response.Close();
        }
    }

    private async Task ReceiveAsync(HttpListenerContext context)
    {
        var accepted = await context.AcceptWebSocketAsync(WebSocketBinding.Subprotocol);
        var socket = accepted.WebSocket;
        Sockets.Enqueue(socket);
        var buffer = new byte[128 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Closes.Enqueue(result.CloseStatus);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                    return;
                }

                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
                Received.Enqueue(doc.RootElement.Clone());
            }
        }
        catch (Exception)
        {
            // The client went away.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Close();
        await _serve.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _cts.Dispose();
    }

    internal sealed class SavedStorage(Guid refId, string secret) : IClientStorage
    {
        public bool Cleared { get; private set; }

        public Task SaveCredentialsAsync(Guid id, string value) => Task.CompletedTask;

        public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync() => Task.FromResult<(Guid?, string?)>((refId, secret));

        public Task ClearCredentialsAsync()
        {
            Cleared = true;
            return Task.CompletedTask;
        }
    }
}
