using System.Text.Json;

namespace Wbskt.Internal;

/// <summary>
/// The one place a message becomes the bytes on the wire, so the size the client checks is the size it sends.
/// </summary>
internal static class ProtocolJson
{
    public static byte[] Serialize(ProtocolMessage message) => JsonSerializer.SerializeToUtf8Bytes(message);

    /// <exception cref="ArgumentException">The message is over <see cref="ClientProtocol.MaxMessageBytes"/>.</exception>
    public static void EnsureWithinLimit(ProtocolMessage message, string paramName)
    {
        var size = Serialize(message).Length;
        if (size > ClientProtocol.MaxMessageBytes)
        {
            throw new ArgumentException($"The message is {size} bytes; the limit is {ClientProtocol.MaxMessageBytes}.", paramName);
        }
    }
}
