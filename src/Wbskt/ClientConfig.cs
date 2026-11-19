namespace Wbskt;

/// <param name="OfflineBufferSize">
/// How many messages <see cref="IWbsktClient.SendAsync"/> keeps in memory while the client is
/// offline; they are sent in order, with their original send time, once it reconnects. When the
/// buffer is full the oldest message is dropped. 0 turns buffering off, and sending while
/// offline throws instead. The buffer is not persisted, so it is lost if the process exits.
/// </param>
public record ClientConfig(
    string BaseApiUrl,    
    string BaseSocketUrl, 
    string ClientName,    
    string? PolicyPin = null,
    int OfflineBufferSize = 1000
)
{
    /// <summary>
    /// The commands the client takes, announced on every connection so the console's send panel offers them.
    /// <see cref="IWbsktClient.UpdateCapabilitiesAsync"/> replaces them while the client runs.
    /// </summary>
    public IReadOnlyList<CommandCapability> Commands { get; init; } = [];

    /// <summary>
    /// The version of the code the client runs (its firmware, or the application), shown in the console.
    /// Not the SDK's own version, which the client reports by itself.
    /// </summary>
    public string? FirmwareVersion { get; init; }
}
