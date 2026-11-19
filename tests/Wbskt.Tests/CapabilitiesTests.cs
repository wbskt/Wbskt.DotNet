using System.Text.Json;
using Wbskt.Internal;

namespace Wbskt.Tests;

public sealed class CapabilitiesTests
{
    [Fact]
    public void A_parameter_goes_out_with_its_type_in_lower_case_and_its_name_as_label()
    {
        var command = new CommandCapability("led.set", "Turn the LED on or off", [new PropertySchema("on", DataType.Boolean, IsRequired: true)]);

        var json = JsonSerializer.Serialize(command, ProtocolJson.Options);

        Assert.Equal(
            """{"command":"led.set","description":"Turn the LED on or off","parameters":[{"name":"on","dataType":"boolean","isRequired":true,"defaultValue":null,"label":"on","description":""}]}""",
            json);
    }

    [Fact]
    public void A_command_needs_only_its_type()
    {
        var json = JsonSerializer.Serialize(new CommandCapability("reboot"), ProtocolJson.Options);

        Assert.Equal("""{"command":"reboot","description":"","parameters":[]}""", json);
    }

    [Fact]
    public void Commands_written_by_another_client_read_back()
    {
        var command = JsonSerializer.Deserialize<CommandCapability>(
            """{"command":"led.set","description":"","parameters":[{"name":"on","label":"On","dataType":"boolean","description":"","isRequired":true}]}""",
            ProtocolJson.Options)!;

        var parameter = Assert.Single(command.Parameters);
        Assert.Equal(DataType.Boolean, parameter.DataType);
        Assert.Equal("On", parameter.Label);
        Assert.True(parameter.IsRequired);
    }

    [Fact]
    public void Commands_too_big_to_announce_are_refused_when_the_client_is_built()
    {
        var config = new ClientConfig("http://127.0.0.1:1", "ws://127.0.0.1:1", "test", "pin")
        {
            Commands = [new CommandCapability("big", new string('x', 70 * 1024))],
        };

        var error = Assert.Throws<ArgumentException>(() => new WbsktClient(config, new FileClientStorage(Path.GetTempFileName())));
        Assert.Equal("config", error.ParamName);
    }
}
