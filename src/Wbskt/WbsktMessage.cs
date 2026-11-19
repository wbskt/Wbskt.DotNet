using System.Text.Json;
using Wbskt.Internal;

namespace Wbskt;

/// <summary>A message the platform sent this client.</summary>
/// <param name="Type">The message type, such as a command name.</param>
/// <param name="Payload">The payload as sent, or <c>null</c> when there was none or it was JSON <c>null</c>. Read it with <see cref="PayloadAs{T}"/> or <see cref="JsonElement"/>'s methods.</param>
/// <param name="CommandId">Set when the platform asked for a delivery ack; the SDK has already sent it.</param>
public sealed record WbsktMessage(string Type, JsonElement? Payload, string? CommandId)
{
    /// <summary>
    /// The payload as a <typeparamref name="T"/>, matching property names ignoring case, so <c>record LedSet(bool On)</c>
    /// reads <c>{"on":true}</c>. <c>default</c> when there was no payload.
    /// </summary>
    /// <exception cref="JsonException">The payload doesn't fit <typeparamref name="T"/>.</exception>
    public T? PayloadAs<T>() => Payload is { } payload ? payload.Deserialize<T>(ProtocolJson.Options) : default;
}
