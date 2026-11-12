using System.Text.Json;

namespace Wbskt;

/// <summary>A message the platform sent this client.</summary>
/// <param name="Type">The message type, such as a command name.</param>
/// <param name="Payload">The payload as sent, or <c>null</c> when there was none. Read it with <see cref="JsonElement"/>'s methods or <c>Deserialize&lt;T&gt;()</c>.</param>
/// <param name="CommandId">Set when the platform asked for a delivery ack; the SDK has already sent it.</param>
public sealed record WbsktMessage(string Type, JsonElement? Payload, string? CommandId);
