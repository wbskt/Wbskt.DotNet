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
}
