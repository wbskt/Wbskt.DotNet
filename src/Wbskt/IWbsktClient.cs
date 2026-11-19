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
    /// <see cref="MessageHandler"/>, <see cref="OnDisconnected"/>, <see cref="OnConnectFailed"/> and <see cref="OnSuperseded"/>). The connection stays up, and
    /// the client still reconnects.
    /// </summary>
    event Action<Exception>? OnHandlerError;
    event Action? OnConnected;

    /// <summary>
    /// A connection attempt failed, with the exception that stopped it: an unreachable URL or a TLS error,
    /// or a <see cref="WbsktConnectException"/> for a refused registration or login (a wrong PIN, a full
    /// policy, approval pending, a disabled client), carrying the HTTP status and the server's code. Raised for every attempt; the client keeps
    /// retrying on its schedule, except after a <see cref="RegistrationRequiredException"/> (no saved
    /// credentials and no PIN), a <see cref="WbsktConnectException"/> that is
    /// <see cref="WbsktConnectException.IsFinal"/> (a wrong PIN or name, or a
    /// <see cref="CredentialsRejectedException"/>: the client was deleted or its secret rotated) or a
    /// <see cref="CredentialsNotSavedException"/> (it registered but could not save the credentials), when it
    /// stops: fix the cause and start it again, or create a new client with a corrected PIN or name.
    /// </summary>
    event Action<Exception>? OnConnectFailed;
    event Action? OnDisconnected;

    /// <summary>
    /// A newer connection with these credentials replaced this one. Now and then that is this client
    /// reconnecting; raised again and again, another client is probably using the same credentials.
    /// </summary>
    event Action? OnSuperseded;

    /// <summary>Messages waiting in the offline buffer.</summary>
    int PendingMessageCount { get; }

    /// <summary>
    /// Messages the offline buffer has dropped since the client was built, oldest first, to make room for newer
    /// ones while it was full.
    /// </summary>
    long DroppedMessageCount { get; }

    /// <summary>Connected, with the client's capabilities sent, so a message sent now goes straight out.</summary>
    bool IsConnected { get; }

    Task StartAsync();

    /// <summary>
    /// Closes the connection normally (1000) and stops reconnecting. The offline buffer and the handlers
    /// are kept, and <see cref="StartAsync"/> connects again. Messages sent while stopped wait in the buffer.
    /// </summary>
    Task StopAsync();

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
