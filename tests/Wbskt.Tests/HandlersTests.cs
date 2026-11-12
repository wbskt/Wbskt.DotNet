using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Wbskt.Internal;

namespace Wbskt.Tests;

public sealed class HandlersTests
{
    [Fact]
    public void A_handler_that_throws_reaches_the_error_handler()
    {
        Exception? seen = null;

        Handlers.Raise(() => throw new InvalidOperationException("boom"), error => seen = error);

        seen.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void An_error_handler_that_throws_is_ignored()
    {
        var raise = () => Handlers.Raise(() => throw new InvalidOperationException("boom"), _ => throw new InvalidOperationException("again"));

        raise.Should().NotThrow();
    }

    [Fact]
    public async Task A_connected_handler_that_throws_leaves_the_connection_read()
    {
        using var server = new LoopbackServer();
        await using var socket = new SocketClient(server.Url);
        Exception? seen = null;
        socket.OnConnected += () => throw new InvalidOperationException("app bug");
        socket.OnHandlerError += error => seen = error;

        var accepting = server.AcceptAsync();
        await socket.ConnectAsync("token");
        using var serverSide = await accepting;
        await serverSide.SendAsync(Encoding.UTF8.GetBytes("""{"type":"sys.ping","payload":{"timestamp":"2026-11-12T10:00:00Z","id":"p1"}}"""),
            WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = await serverSide.ReceiveAsync(buffer, timeout.Token);

        seen.Should().BeOfType<InvalidOperationException>();
        Encoding.UTF8.GetString(buffer, 0, read.Count).Should().Contain("sys.pong").And.Contain("p1");
    }

    /// <summary>A WebSocket server on a free loopback port that accepts any token.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();

        public LoopbackServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"ws://127.0.0.1:{port}";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
        }

        public string Url { get; }

        public async Task<WebSocket> AcceptAsync()
        {
            var context = await _listener.GetContextAsync();
            var accepted = await context.AcceptWebSocketAsync(WebSocketBinding.Subprotocol);
            return accepted.WebSocket;
        }

        public void Dispose() => _listener.Close();
    }
}
