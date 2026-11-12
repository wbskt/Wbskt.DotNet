using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Wbskt.Internal;

namespace Wbskt.Tests;

public sealed class SocketClientTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_connected_handler_that_throws_leaves_the_connection_reading()
    {
        using var server = new LoopbackServer();
        await using var socket = new SocketClient(server.Url);
        Exception? seen = null;
        socket.OnConnected += () => throw new InvalidOperationException("app bug");
        socket.OnHandlerError += error => seen = error;

        using var serverSide = await ConnectAsync(server, socket);
        await SendAsync(serverSide, """{"type":"sys.ping","payload":{"timestamp":"2026-11-12T10:00:00Z","id":"p1"}}""");

        seen.Should().BeOfType<InvalidOperationException>();
        (await ReceiveAsync(serverSide)).Should().Contain("sys.pong").And.Contain("p1");
    }

    [Fact]
    public async Task A_disconnected_handler_that_throws_still_lets_the_client_connect_again()
    {
        using var server = new LoopbackServer();
        await using var socket = new SocketClient(server.Url);
        var disconnected = new TaskCompletionSource();
        Exception? seen = null;
        socket.OnDisconnected += () =>
        {
            disconnected.TrySetResult();
            throw new InvalidOperationException("app bug");
        };
        socket.OnHandlerError += error => seen = error;

        using (var first = await ConnectAsync(server, socket))
        {
            await first.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            await disconnected.Task.WaitAsync(Wait);
        }

        using var second = await ConnectAsync(server, socket);
        socket.IsConnected.Should().BeTrue();
        seen.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task What_the_connected_handler_sends_goes_out_before_any_answer_to_the_gateway()
    {
        using var server = new LoopbackServer();
        await using var socket = new SocketClient(server.Url);
        var order = new List<string>();
        socket.OnConnected += () =>
        {
            order.Add("connected");

            // As WbsktClient announces its capabilities: started here, finished on the send lock.
            _ = socket.SendAsync(new ProtocolMessage(MessageTypes.Capabilities, new { }));
        };
        socket.OnMessageReceived += (type, _, _) => order.Add(type);

        // The gateway pings the moment it accepts, before reading anything.
        var accepting = server.AcceptAsync();
        var connecting = socket.ConnectAsync("token");
        using var serverSide = await accepting.WaitAsync(Wait);
        await SendAsync(serverSide, """{"type":"sys.ping","payload":{"timestamp":"2026-11-12T10:00:00Z","id":"p1"}}""");
        await SendAsync(serverSide, """{"type":"led.set","payload":{}}""");
        await connecting.WaitAsync(Wait);

        (await ReceiveAsync(serverSide)).Should().Contain("\"capabilities\"");
        (await ReceiveAsync(serverSide)).Should().Contain("sys.pong");
        await WaitUntilAsync(() => order.Count == 2);
        order.Should().Equal("connected", "led.set");
    }

    [Fact]
    public async Task An_async_handler_that_throws_after_awaiting_is_reported_and_the_next_message_still_arrives()
    {
        using var server = new LoopbackServer();
        await using var socket = new SocketClient(server.Url);
        var handled = new List<string>();
        Exception? seen = null;
        socket.MessageHandler = async message =>
        {
            await Task.Yield();
            handled.Add($"{message.Type}:{message.Payload?.GetProperty("on").GetBoolean()}");
            if (message.Type == "led.set")
            {
                throw new InvalidOperationException("app bug");
            }
        };
        socket.OnHandlerError += error => seen = error;

        using var serverSide = await ConnectAsync(server, socket);
        await SendAsync(serverSide, """{"type":"led.set","payload":{"on":true}}""");
        await SendAsync(serverSide, """{"type":"led.get","payload":{"on":false}}""");

        await WaitUntilAsync(() => handled.Count == 2);
        handled.Should().Equal("led.set:True", "led.get:False");
        seen.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task An_async_handler_finishes_one_message_before_the_next_starts()
    {
        using var server = new LoopbackServer();
        await using var socket = new SocketClient(server.Url);
        var order = new List<string>();
        socket.MessageHandler = async message =>
        {
            order.Add($"start {message.Type}");
            await Task.Delay(50);
            order.Add($"end {message.Type}");
        };

        using var serverSide = await ConnectAsync(server, socket);
        await SendAsync(serverSide, """{"type":"a"}""");
        await SendAsync(serverSide, """{"type":"b"}""");

        await WaitUntilAsync(() => order.Count == 4);
        order.Should().Equal("start a", "end a", "start b", "end b");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<WebSocket> ConnectAsync(LoopbackServer server, SocketClient socket)
    {
        var accepting = server.AcceptAsync();
        await socket.ConnectAsync("token").WaitAsync(Wait);
        return await accepting.WaitAsync(Wait);
    }

    private static Task SendAsync(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<string> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(Wait);
        var read = await socket.ReceiveAsync(buffer, timeout.Token);
        return Encoding.UTF8.GetString(buffer, 0, read.Count);
    }

    /// <summary>
    /// A WebSocket server on a free loopback port that accepts any token. HttpListener on 127.0.0.1 needs
    /// a URL ACL on Windows; CI runs on Linux.
    /// </summary>
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
