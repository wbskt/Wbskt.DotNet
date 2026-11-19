using System.Net;
using System.Net.Sockets;

namespace Wbskt.Internal;

/// <summary>
/// How often the client checks that the link still carries traffic, and how long it waits for an answer
/// before it drops the connection and reconnects. 20 s and 20 s, as the Python client's WebSocket library
/// and the ESP32 library's heartbeat do.
/// </summary>
internal sealed record Liveness(TimeSpan Interval, TimeSpan Timeout)
{
    public static readonly Liveness Default = new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
}

#if !NET9_0_OR_GREATER
/// <summary>An HTTP handler whose sockets probe an idle link with TCP keep-alive (net8.0 only).</summary>
internal static class LivenessInvoker
{
    private const int KeepAliveProbes = 3;

    // TCP_USER_TIMEOUT: how long sent data may go unacknowledged before Linux drops the connection.
    private const SocketOptionName TcpUserTimeout = (SocketOptionName)18;

    private static readonly object Gate = new();
    private static readonly Dictionary<Liveness, HttpMessageInvoker> Invokers = [];

    public static HttpMessageInvoker Get(Liveness liveness)
    {
        lock (Gate)
        {
            if (!Invokers.TryGetValue(liveness, out var invoker))
            {
                invoker = new HttpMessageInvoker(new SocketsHttpHandler { ConnectCallback = (context, ct) => ConnectAsync(context, liveness, ct) });
                Invokers[liveness] = invoker;
            }

            return invoker;
        }
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, Liveness liveness, CancellationToken ct)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, ct);
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                Probe(socket, liveness);
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException error)
            {
                socket.Dispose();
                last = error;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    private static void Probe(Socket socket, Liveness liveness)
    {
        var probeEvery = (int)Math.Max(1, liveness.Timeout.TotalSeconds / KeepAliveProbes);
        TrySet(socket, SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);
        TrySet(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)Math.Max(1, liveness.Interval.TotalSeconds));
        TrySet(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, probeEvery);
        TrySet(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, KeepAliveProbes);
        if (OperatingSystem.IsLinux())
        {
            TrySet(socket, SocketOptionLevel.Tcp, TcpUserTimeout, (int)(liveness.Interval + liveness.Timeout).TotalMilliseconds);
        }
    }

    /// <summary>Best effort: a platform or sandbox without the option still connects, only without the probe.</summary>
    private static void TrySet(Socket socket, SocketOptionLevel level, SocketOptionName name, int value)
    {
        try
        {
            socket.SetSocketOption(level, name, value);
        }
        catch (SocketException)
        {
        }
    }
}
#endif
