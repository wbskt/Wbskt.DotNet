using System.Text.Json;
using Wbskt.Internal;

namespace Wbskt.Tests;

public sealed class ProtocolMessageTests
{
    [Fact]
    public void Sent_time_round_trips_and_is_left_out_when_unset()
    {
        var sentAt = new DateTimeOffset(2026, 10, 3, 11, 59, 0, TimeSpan.Zero);
        var json = JsonSerializer.Serialize(new ProtocolMessage("temperature", new { celsius = 4 }) { SentAt = sentAt });

        Assert.Equal(sentAt, JsonSerializer.Deserialize<ProtocolMessage>(json)!.SentAt);
        Assert.DoesNotContain("sentAt", JsonSerializer.Serialize(new ProtocolMessage("sys.pong", new { })));
    }

    [Theory]
    [InlineData("{\"type\":\"t\",\"payload\":{}}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":null}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":\"yesterday-ish\"}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":1759492800}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":{\"at\":1}}")]
    public void A_missing_or_unreadable_sent_time_does_not_reject_the_message(string json)
    {
        var message = JsonSerializer.Deserialize<ProtocolMessage>(json)!;

        Assert.Equal("t", message.Type);
        Assert.Null(message.SentAt);
    }

    [Fact]
    public void A_sent_time_without_an_offset_is_read_as_utc()
    {
        var message = JsonSerializer.Deserialize<ProtocolMessage>("{\"type\":\"t\",\"payload\":{},\"sentAt\":\"2026-10-03T11:00:00\"}")!;

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 11, 0, 0, TimeSpan.Zero), message.SentAt);
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
