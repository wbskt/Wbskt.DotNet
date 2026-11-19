using System.Text.Json.Serialization;

namespace Wbskt.Internal;

/// <summary>
/// Represents a message exchanged between the client and server.
/// </summary>
internal record ProtocolMessage(
    [property: JsonPropertyName("type")]      string  Type,
    [property: JsonPropertyName("payload")]   object  Payload,
    [property: JsonPropertyName("commandId")] string? CommandId = null
)
{
    /// <summary>
    /// When the client produced the message (client clock). Set by the SDK on application
    /// messages so a reading sent late, after the client was offline, keeps its original time.
    /// A missing or unreadable value is treated as "sent now" by the platform.
    /// </summary>
    [JsonPropertyName("sentAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? SentAt { get; init; }
}
