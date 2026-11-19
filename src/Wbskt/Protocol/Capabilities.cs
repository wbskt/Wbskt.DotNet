using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt;

// The payload of the "capabilities" message (MessageTypes.Capabilities). The SDK sends these and the
// management host reads them, so property names are the contract.

/// <summary>The type of a command parameter, sent in lower case (<c>"number"</c>).</summary>
[JsonConverter(typeof(DataTypeConverter))]
public enum DataType
{
    String,
    Number,
    Boolean,
    Object
}

/// <summary>A parameter a command takes, as the console's send panel shows it.</summary>
/// <param name="IsRequired">Whether the console asks for a value before it sends the command.</param>
/// <param name="DefaultValue">What the console fills in to start with.</param>
public record PropertySchema(
    string Name,
    DataType DataType = DataType.String,
    bool IsRequired = false,
    string? DefaultValue = null)
{
    /// <summary>The name the console shows. The parameter's name unless set.</summary>
    public string Label { get; init; } = Name;

    public string Description { get; init; } = "";
}

/// <summary>A command the client takes: its message type, and the parameters of its payload.</summary>
public record CommandCapability(
    string Command,
    string Description = "",
    List<PropertySchema>? Parameters = null)
{
    public List<PropertySchema> Parameters { get; init; } = Parameters ?? [];
}

/// <summary>
/// Everything a client announces about itself. Most apps set <see cref="ClientConfig.Commands"/> and
/// <see cref="ClientConfig.FirmwareVersion"/> instead; this is for one that changes its commands while it
/// runs (<see cref="IWbsktClient.UpdateCapabilitiesAsync"/>) or reports its own agent and OS. An empty
/// <paramref name="Agent"/>, <paramref name="Version"/> or <paramref name="OS"/> is filled in by the SDK.
/// </summary>
/// <param name="FirmwareVersion">The version of the code the client runs: its firmware on a microcontroller, the application otherwise. Not the SDK's own version, which is <paramref name="Version"/>.</param>
public record ClientCapabilities(
    string Agent,
    string Version,
    string OS,
    List<CommandCapability> Capabilities,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FirmwareVersion = null
);

internal sealed class DataTypeConverter() : JsonStringEnumConverter<DataType>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
