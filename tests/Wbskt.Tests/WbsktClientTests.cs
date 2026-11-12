using FluentAssertions;

namespace Wbskt.Tests;

public sealed class WbsktClientTests
{
    [Fact]
    public async Task A_message_over_64_KB_is_refused_and_never_buffered()
    {
        await using var client = NewClient();

        var send = () => client.SendAsync("photo", new string('x', 70 * 1024));
        var report = () => client.ReportStateAsync(new Dictionary<string, object?> { ["log"] = new string('x', 70 * 1024) });

        await send.Should().ThrowAsync<ArgumentException>().WithMessage("*limit is 65536*");
        await report.Should().ThrowAsync<ArgumentException>();
        client.PendingMessageCount.Should().Be(0);
    }

    [Fact]
    public async Task A_message_under_the_limit_is_buffered_while_offline()
    {
        await using var client = NewClient();

        await client.SendAsync("reading", new string('x', 60 * 1024));

        client.PendingMessageCount.Should().Be(1);
    }

    private static WbsktClient NewClient() =>
        new(new ClientConfig("http://127.0.0.1:1", "ws://127.0.0.1:1", "test", "pin"), new MemoryStorage());

    private sealed class MemoryStorage : IClientStorage
    {
        public Task SaveCredentialsAsync(Guid refId, string secret) => Task.CompletedTask;

        public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync() => Task.FromResult<(Guid?, string?)>((null, null));

        public Task ClearCredentialsAsync() => Task.CompletedTask;
    }
}
