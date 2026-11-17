using System.Text.Json.Serialization;

namespace Wbskt;

// The payload of the "capabilities" message (MessageTypes.Capabilities). The SDK sends these and the
// management host reads them, so property names are the contract.

public record PropertySchema(
    string Name,
    string Label,
    string DataType, // "string", "number", "boolean", "object"
    string Description,
    bool IsRequired = false,
    string? DefaultValue = null
);

public record CommandCapability(
    string Command,
    string Description,
    List<PropertySchema> Parameters
);

/// <param name="FirmwareVersion">The version of the code the client runs: its firmware on a microcontroller, the application otherwise. Not the SDK's own version, which is <paramref name="Version"/>.</param>
public record ClientCapabilities(
    string Agent,
    string Version,
    string OS,
    List<CommandCapability> Capabilities,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FirmwareVersion = null
);
