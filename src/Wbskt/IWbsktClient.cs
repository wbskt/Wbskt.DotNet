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
    Func<WbsktMessage, Task>? MessageHandler { get; set; }

    /// <summary>
    /// A handler of this client's events threw (<see cref="OnMessageReceived"/>, <see cref="OnConnected"/>,
    /// <see cref="OnDisconnected"/> and the client's <c>OnSuperseded</c>). The connection stays up, and
    /// the client still reconnects.
    /// </summary>
    event Action<Exception>? OnHandlerError;
    event Action? OnConnected;
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
