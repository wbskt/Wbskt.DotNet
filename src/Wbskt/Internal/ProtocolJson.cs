using System.Text.Encodings.Web;
using System.Text.Json;

namespace Wbskt.Internal;

/// <summary>
/// The one place a message becomes the bytes on the wire, so the size the client checks is the size it sends.
/// </summary>
internal static class ProtocolJson
{
    /// <summary>
    /// camelCase property names, as the Python and Arduino clients send and the console's declared command
    /// parameters use; dictionary keys stay as given. Reading matches names ignoring case.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] Serialize(ProtocolMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Options);

    /// <summary>
    /// The message as the bytes that will be sent, serialised now: a payload the app changes later (a reused
    /// dictionary) can't change what was queued, nor grow past the limit after it was checked.
    /// </summary>
    /// <exception cref="ArgumentException">The message is over <see cref="ClientProtocol.MaxMessageBytes"/>.</exception>
    public static OutboundFrame Frame(ProtocolMessage message, string paramName)
    {
        var bytes = Serialize(message);
        if (bytes.Length > ClientProtocol.MaxMessageBytes)
        {
            throw new ArgumentException($"The message is {bytes.Length} bytes; the limit is {ClientProtocol.MaxMessageBytes}.", paramName);
        }

        return new OutboundFrame(message.Type, bytes);
    }
}

/// <summary>A message serialised for the wire. A class, so the buffer can tell two equal frames apart.</summary>
internal sealed class OutboundFrame(string type, byte[] bytes)
{
    public string Type { get; } = type;

    public ReadOnlyMemory<byte> Bytes { get; } = bytes;
}
