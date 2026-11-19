using System.Text.Json;
using Wbskt.Internal;

namespace Wbskt.Tests;

public sealed class ProtocolMessageTests
{
    [Fact]
    public void Sent_time_goes_out_with_its_offset_and_is_left_out_when_unset()
    {
        var sentAt = new DateTimeOffset(2026, 10, 3, 11, 59, 0, TimeSpan.Zero);
        var json = JsonSerializer.Serialize(new ProtocolMessage("temperature", new { celsius = 4 }) { SentAt = sentAt });

        Assert.Contains("\"sentAt\":\"2026-10-03T11:59:00+00:00\"", json);
        Assert.DoesNotContain("sentAt", JsonSerializer.Serialize(new ProtocolMessage("sys.pong", new { })));
    }

    [Fact]
    public void Payload_properties_go_out_camel_cased_and_dictionary_keys_as_given()
    {
        var reading = Wire(new ProtocolMessage("temperature", new Reading(21.5, "Küche")));
        var state = Wire(new ProtocolMessage("state.report", new Dictionary<string, object?> { ["LedOn"] = true }));

        Assert.Equal(21.5, reading.GetProperty("payload").GetProperty("celsius").GetDouble());
        Assert.Equal("Küche", reading.GetProperty("payload").GetProperty("room").GetString());
        Assert.True(state.GetProperty("payload").GetProperty("LedOn").GetBoolean());
        Assert.Contains("Küche", System.Text.Encoding.UTF8.GetString(ProtocolJson.Serialize(new ProtocolMessage("t", new Reading(1, "Küche")))));
    }

    [Fact]
    public void A_command_payload_reads_into_a_record_whatever_the_case()
    {
        using var doc = JsonDocument.Parse("""{"on":true}""");
        var message = new WbsktMessage("led.set", doc.RootElement.Clone(), null);

        Assert.True(message.PayloadAs<LedSet>()!.On);
        Assert.Null(new WbsktMessage("led.set", null, null).PayloadAs<LedSet>());
    }

    private static JsonElement Wire(ProtocolMessage message) => JsonDocument.Parse(ProtocolJson.Serialize(message)).RootElement.Clone();

    private sealed record Reading(double Celsius, string Room);

    private sealed record LedSet(bool On);
}
