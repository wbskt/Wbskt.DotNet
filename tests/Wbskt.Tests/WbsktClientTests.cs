using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Wbskt.Internal;

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
    public async Task Capabilities_over_64_KB_are_refused_and_a_later_update_still_goes_through()
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

    [Fact]
    public async Task A_throttled_registration_carries_the_retry_after()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var api = new HttpListener();
        api.Prefixes.Add($"http://127.0.0.1:{port}/");
        api.Start();
        _ = Task.Run(async () =>
        {
            var context = await api.GetContextAsync();
            context.Response.StatusCode = 429;
            context.Response.Headers["Retry-After"] = "7";
            context.Response.Close();
        });
        await using var client = new WbsktClient(new ClientConfig($"http://127.0.0.1:{port}", "ws://127.0.0.1:1", "test", "pin"), new MemoryStorage());
        var failed = new TaskCompletionSource<Exception>();
        client.OnConnectFailed += error => failed.TrySetResult(error);

        await client.StartAsync();

        var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        error.Should().BeOfType<WbsktConnectException>().Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task Rejected_credentials_are_kept_and_stop_the_client_instead_of_registering_again()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var api = new HttpListener();
        api.Prefixes.Add($"http://127.0.0.1:{port}/");
        api.Start();
        var paths = new List<string>();
        _ = Task.Run(async () =>
        {
            while (api.IsListening)
            {
                var context = await api.GetContextAsync();
                lock (paths)
                {
                    paths.Add(context.Request.Url!.AbsolutePath);
                }

                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/problem+json";
                await context.Response.OutputStream.WriteAsync("{\"code\":\"CLIENT_UNAUTHORIZED\"}"u8.ToArray());
                context.Response.Close();
            }
        });
        var refId = Guid.NewGuid();
        var storage = new FakeWbskt.SavedStorage(refId, "old-secret");
        await using var client = new WbsktClient(new ClientConfig($"http://127.0.0.1:{port}", "ws://127.0.0.1:1", "test", "pin"), storage);
        Exception? raised = null;
        client.OnConnectFailed += error => raised = error;

        var start = () => client.StartAsync();

        (await start.Should().ThrowAsync<CredentialsRejectedException>()).Which.ClientRefId.Should().Be(refId);
        raised.Should().BeOfType<CredentialsRejectedException>();
        storage.Cleared.Should().BeFalse();
        lock (paths)
        {
            paths.Should().Equal("/api/v1/client-auth/login");
        }
    }

    [Fact]
    public async Task Starting_again_while_connected_changes_nothing_and_sends_still_go_out()
    {
        await using var server = new FakeWbskt();
        await using var client = server.NewClient();
        var connected = new TaskCompletionSource();
        client.OnConnected += () => connected.TrySetResult();

        await client.StartAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await server.WaitForAsync("capabilities");
        await client.StartAsync();
        await client.SendAsync("reading", new { celsius = 21.5 });

        var reading = await server.WaitForAsync("reading");
        reading.GetProperty("payload").GetProperty("celsius").GetDouble().Should().Be(21.5);
        client.PendingMessageCount.Should().Be(0);
        server.Sockets.Should().HaveCount(1);
        server.Logins.Should().Be(1);
    }

    [Fact]
    public async Task A_reused_payload_goes_out_with_the_values_it_had_when_it_was_sent()
    {
        await using var server = new FakeWbskt();
        await using var client = server.NewClient();
        var state = new Dictionary<string, object?>();

        // Offline, so both wait in the buffer.
        state["celsius"] = 20.0;
        await client.ReportStateAsync(state);
        state["celsius"] = 21.0;
        await client.ReportStateAsync(state);
        state["celsius"] = 99.0;
        await client.StartAsync();

        await server.WaitForCountAsync("state.report", 2);
        server.Received.Where(frame => frame.GetProperty("type").GetString() == "state.report")
            .Select(frame => frame.GetProperty("payload").GetProperty("celsius").GetDouble())
            .Should().Equal(20.0, 21.0);
    }

    [Fact]
    public async Task A_refused_registration_says_why()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var api = new HttpListener();
        api.Prefixes.Add($"http://127.0.0.1:{port}/");
        api.Start();
        _ = Task.Run(async () =>
        {
            var context = await api.GetContextAsync();
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/problem+json";
            await context.Response.OutputStream.WriteAsync("{\"title\":\"Not Found\",\"code\":\"POLICY_NOT_FOUND\",\"detail\":\"Invalid registration PIN.\"}"u8.ToArray());
            context.Response.Close();
        });
        await using var client = new WbsktClient(new ClientConfig($"http://127.0.0.1:{port}", "ws://127.0.0.1:1", "test", "pin"), new MemoryStorage());
        var failed = new TaskCompletionSource<Exception>();
        client.OnConnectFailed += error => failed.TrySetResult(error);

        await client.StartAsync();

        var refusal = (await failed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeOfType<WbsktConnectException>().Subject;
        refusal.Step.Should().Be(ConnectStep.Registration);
        refusal.StatusCode.Should().Be(HttpStatusCode.NotFound);
        refusal.Code.Should().Be("POLICY_NOT_FOUND");
        refusal.Message.Should().Be("Registration answered 404 POLICY_NOT_FOUND: Invalid registration PIN.");
    }

    [Fact]
    public async Task Credentials_that_cannot_be_saved_stop_the_client_instead_of_registering_again()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var api = new HttpListener();
        api.Prefixes.Add($"http://127.0.0.1:{port}/");
        api.Start();
        var refId = Guid.NewGuid();
        var paths = new List<string>();
        _ = Task.Run(async () =>
        {
            while (api.IsListening)
            {
                var context = await api.GetContextAsync();
                lock (paths)
                {
                    paths.Add(context.Request.Url!.AbsolutePath);
                }

                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"{{\"clientRefId\":\"{refId}\",\"secret\":\"s\"}}"));
                context.Response.Close();
            }
        });
        await using var client = new WbsktClient(new ClientConfig($"http://127.0.0.1:{port}", "ws://127.0.0.1:1", "test", "pin"), new ReadOnlyStorage());
        Exception? raised = null;
        client.OnConnectFailed += error => raised = error;

        var start = () => client.StartAsync();

        var thrown = (await start.Should().ThrowAsync<CredentialsNotSavedException>()).Which;
        thrown.ClientRefId.Should().Be(refId);
        thrown.InnerException.Should().BeOfType<UnauthorizedAccessException>();
        raised.Should().BeOfType<CredentialsNotSavedException>();
        await Task.Delay(TimeSpan.FromSeconds(2));
        lock (paths)
        {
            paths.Should().Equal("/api/v1/client-registrations/initiate");
        }
    }

    private static WbsktClient NewClient(string? pin = "pin") =>
        new(new ClientConfig("http://127.0.0.1:1", "ws://127.0.0.1:1", "test", pin), new MemoryStorage());

    private sealed class MemoryStorage : IClientStorage
    {
        public Task SaveCredentialsAsync(Guid refId, string secret) => Task.CompletedTask;

        public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync() => Task.FromResult<(Guid?, string?)>((null, null));

        public Task ClearCredentialsAsync() => Task.CompletedTask;
    }

    private sealed class ReadOnlyStorage : IClientStorage
    {
        public Task SaveCredentialsAsync(Guid refId, string secret) => throw new UnauthorizedAccessException("Access to the path '/data/wbskt/test.json' is denied.");

        public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync() => Task.FromResult<(Guid?, string?)>((null, null));

        public Task ClearCredentialsAsync() => Task.CompletedTask;
    }
}
