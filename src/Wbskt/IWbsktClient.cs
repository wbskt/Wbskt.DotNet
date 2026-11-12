namespace Wbskt;

public interface IWbsktClient : IAsyncDisposable
{
    // type, payload, commandId (non-null when the platform requests a delivery ack; the SDK acks automatically)
    // Handed over one at a time, in arrival order, on the SDK's own task: a slow handler doesn't delay pongs.
    event Action<string, object?, string?>? OnMessageReceived;

    /// <summary>
    /// The async way to handle messages: awaited for each one, after <see cref="OnMessageReceived"/>, before
    /// the next is handed over, so messages are still handled one at a time and in order. An exception,
    /// before or after an <c>await</c>, goes to <see cref="OnHandlerError"/>. Use this rather than an
    /// <c>async</c> lambda on <see cref="OnMessageReceived"/>, which is <c>async void</c>: an exception
    /// after its first <c>await</c> ends the process.
    /// </summary>
    /// <remarks>
    /// The token is cancelled when the client is disposed; pass it on to anything slow. A handler must not
    /// wait for a later message, which can't arrive until it returns. Disposing the client from a handler
    /// is fine, and disposing elsewhere waits up to five seconds for a running handler.
    /// </remarks>
    Func<WbsktMessage, CancellationToken, Task>? MessageHandler { get; set; }

    /// <summary>
    /// A handler of this client's events threw (<see cref="OnMessageReceived"/>, <see cref="OnConnected"/>,
    /// <see cref="MessageHandler"/>, <see cref="OnDisconnected"/>, <see cref="OnConnectFailed"/> and the client's <c>OnSuperseded</c>). The connection stays up, and
    /// the client still reconnects.
    /// </summary>
    event Action<Exception>? OnHandlerError;
    event Action? OnConnected;

    /// <summary>
    /// A connection attempt failed, with the exception that stopped it: an unreachable URL or a TLS error,
    /// or a refused registration or login (a wrong PIN, a full policy, approval pending, a disabled
    /// client), for now told apart only by its message. Raised for every attempt; the client keeps
    /// retrying on its schedule, except after a <see cref="RegistrationRequiredException"/> (the saved
    /// credentials were rejected and there is no PIN), when it stops for good: dispose it and create a new
    /// client with a PIN.
    /// </summary>
    event Action<Exception>? OnConnectFailed;
    event Action? OnDisconnected;

    /// <summary>Messages waiting in the offline buffer.</summary>
    int PendingMessageCount { get; }

    Task StartAsync();

    /// <summary>
    /// Sends an application message. The types "capabilities", "state.report" and anything
    /// prefixed "sys." are reserved for the platform protocol — use
    /// <see cref="UpdateCapabilitiesAsync"/> / <see cref="ReportStateAsync"/> instead; they throw <see cref="ArgumentException"/>.
    /// A message over 64 KB once serialised throws <see cref="ArgumentException"/> too, rather than
    /// ending the connection. Each message carries the time it was sent. While offline, messages wait in memory
    /// (up to <see cref="ClientConfig.OfflineBufferSize"/>) and go out in order on reconnect.
    /// </summary>
    Task SendAsync(string type, object payload);
    Task UpdateCapabilitiesAsync(ClientCapabilities capabilities);
    Task ReportStateAsync(IReadOnlyDictionary<string, object?> patch);
}
