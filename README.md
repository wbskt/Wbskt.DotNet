<img src="https://raw.githubusercontent.com/wbskt/Wbskt.DotNet/master/assets/icon.png" alt="" width="64" height="64">

# Wbskt

Connect a board, script or service to [Wbskt](https://wbskt.com). It registers with a policy PIN
once, keeps the credentials it gets, holds a WebSocket open, buffers messages while offline and
reconnects on its own.

```bash
dotnet add package Wbskt --prerelease
```

This is a 0.9 beta (`--prerelease` finds it): the client protocol it speaks may still change before 1.0.

```csharp

await using var wbskt = new WbsktClient("greenhouse-sensor", "your-policy-pin");

wbskt.MessageHandler = async (message, cancellationToken) =>
{
    // The payload is a JsonElement (null when there was none): read it, or Deserialize<T>() it.
    if (message.Type == "led.set"
        && message.Payload is { ValueKind: JsonValueKind.Object } payload
        && payload.TryGetProperty("on", out var on)
        && on.ValueKind is JsonValueKind.True or JsonValueKind.False)
    {
        await wbskt.ReportStateAsync(new Dictionary<string, object?> { ["led"] = on.GetBoolean() });
    }
};

await wbskt.StartAsync();

await wbskt.SendAsync("temperature", new { celsius = 21.5 });
await wbskt.ReportStateAsync(new Dictionary<string, object?> { ["celsius"] = 21.5 });
```

The PIN comes from a registration policy in the console (Policies). The first start registers the
client and saves its credentials in `wbskt/greenhouse-sensor.json` under your local application data
folder (`~/.local/share` on Linux); later starts sign in with them.

**Your own server or storage.** Pass a `ClientConfig` with your API and socket URLs, and optionally
an `IClientStorage` of your own (a database row, a secrets store):

```csharp
var config = new ClientConfig("https://api.example.com", "wss://ws.example.com", "greenhouse-sensor", "your-policy-pin");
await using var wbskt = new WbsktClient(config, new FileClientStorage("/etc/wbskt/client.json"));
```

**Commands.** Declare the commands the client takes with `UpdateCapabilitiesAsync` and they show up
in the console's send panel. Delivery acks and expired-command refusals are sent for you.

**Offline.** `SendAsync` while disconnected queues the message (1,000 by default,
`ClientConfig.OfflineBufferSize`) and sends it in order, with its original time, on reconnect. Sends
are paced at 8 a second after a burst of 50, under the gateway's limit, so a full buffer takes about
two minutes to drain.

**Handlers.** Messages reach `MessageHandler` one at a time, in order, on the SDK's own task: each
call is awaited before the next message is handed over, and a slow handler doesn't delay the
connection's pings. Pass the token on to anything slow: it is cancelled when the client is disposed.
Don't wait inside a handler for a later message; it can't arrive until the handler returns. The payload is a `JsonElement`. A handler that throws, before or after an
`await`, doesn't drop the connection; the exception is raised on `OnHandlerError`. The
`OnMessageReceived` event still works for synchronous handlers, but don't give it an `async` lambda:
that is `async void`, and an exception after its first `await` ends the process. `OnConnected`,
`OnDisconnected` and `OnSuperseded` are guarded the same way: the client still announces itself and
reconnects.

**Not connecting?** `OnConnectFailed` is raised with the exception for every failed attempt (an
unreachable URL, TLS, or a refused registration or login, for now told apart only by its message),
while the client keeps retrying. If the saved credentials are rejected and there is no PIN to register
again, it is raised with `RegistrationRequiredException` and the client stops for good: dispose it and
create a new one with a PIN.

**Message size.** A message over 64 KB once serialised throws `ArgumentException` from `SendAsync` or
`ReportStateAsync`; send a link to anything bigger.

The wire protocol every Wbskt SDK implements is documented in
[Docs/Client.Protocol.md](https://github.com/wbskt/Wbskt/blob/master/Docs/Client.Protocol.md), with the
WebSocket specifics in [Docs/Client.Protocol.WebSocket.md](https://github.com/wbskt/Wbskt/blob/master/Docs/Client.Protocol.WebSocket.md).

## Development

```bash
dotnet build Wbskt.slnx
dotnet test Wbskt.slnx
```

[CONTRIBUTING.md](https://github.com/wbskt/Wbskt.DotNet/blob/master/CONTRIBUTING.md) has the commit and PR rules, how the shared conformance suite is
run, and how a release is cut.
