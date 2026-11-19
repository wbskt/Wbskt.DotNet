using System.Diagnostics.CodeAnalysis;

namespace Wbskt.Internal;

/// <summary>Which message types an app may send: 1-64 characters and not one the protocol reserves.</summary>
internal static class ClientMessageRules
{
    public const int MaxTypeLength = MessageTypes.MaxTypeLength;

    public static bool IsValidType([NotNullWhen(true)] string? messageType) =>
        !string.IsNullOrWhiteSpace(messageType) && messageType.Length <= MaxTypeLength;

    /// <summary>Why a message of this type may not be sent, or null when it may.</summary>
    public static string? FindViolation(string? messageType)
    {
        if (!IsValidType(messageType))
        {
            return $"Message type must be 1-{MaxTypeLength} characters.";
        }

        if (MessageTypes.IsReserved(messageType))
        {
            return $"Message type '{messageType}' is reserved for the platform protocol.";
        }

        return null;
    }
}
