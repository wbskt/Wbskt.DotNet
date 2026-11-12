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
    public async Task Capabilities_over_64_KB_are_refused_and_the_previous_ones_kept()
    {
        await using var client = NewClient();
        var big = new ClientCapabilities("", "", "", [new CommandCapability("big", new string('x', 70 * 1024), [])]);

        var update = () => client.UpdateCapabilitiesAsync(big);

        await update.Should().ThrowAsync<ArgumentException>().WithParameterName("capabilities");
        await client.UpdateCapabilitiesAsync(new ClientCapabilities("", "", "", []));
    }

    [Fact]
    public async Task A_state_report_over_64_KB_names_its_own_parameter()
    {
        await using var client = NewClient();

        var report = () => client.ReportStateAsync(new Dictionary<string, object?> { ["log"] = new string('x', 70 * 1024) });

        await report.Should().ThrowAsync<ArgumentException>().WithParameterName("patch");
    }

    [Fact]
    public async Task A_message_under_the_limit_is_buffered_while_offline()
    {
        await using var client = NewClient();

        await client.SendAsync("reading", new string('x', 60 * 1024));

        client.PendingMessageCount.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_first_attempt_is_raised_with_why()
    {
        await using var client = NewClient();
        var failed = new TaskCompletionSource<Exception>();
        client.OnConnectFailed += error => failed.TrySetResult(error);

        await client.StartAsync();

        (await failed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeAssignableTo<HttpRequestException>();
    }

    [Fact]
    public async Task No_credentials_and_no_pin_is_raised_as_well_as_thrown()
    {
        await using var client = NewClient(pin: null);
        Exception? raised = null;
        client.OnConnectFailed += error => raised = error;

        var start = () => client.StartAsync();

        await start.Should().ThrowAsync<RegistrationRequiredException>();
        raised.Should().BeOfType<RegistrationRequiredException>();
    }

    [Fact]
    public async Task A_connect_failed_handler_that_throws_is_reported_not_raised()
    {
        await using var client = NewClient();
        var reported = new TaskCompletionSource<Exception>();
        client.OnConnectFailed += _ => throw new InvalidOperationException("app bug");
        client.OnHandlerError += error => reported.TrySetResult(error);

        await client.StartAsync();

        (await reported.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeOfType<InvalidOperationException>();
    }

    private static WbsktClient NewClient(string? pin = "pin") =>
        new(new ClientConfig("http://127.0.0.1:1", "ws://127.0.0.1:1", "test", pin), new MemoryStorage());

    private sealed class MemoryStorage : IClientStorage
    {
        public Task SaveCredentialsAsync(Guid refId, string secret) => Task.CompletedTask;

        public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync() => Task.FromResult<(Guid?, string?)>((null, null));

        public Task ClearCredentialsAsync() => Task.CompletedTask;
    }
}
