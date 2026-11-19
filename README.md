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
using Wbskt;

await using var wbskt = new WbsktClient("greenhouse-sensor", "your-policy-pin");

wbskt.MessageHandler = async (message, cancellationToken) =>
{
    if (message.Type == "led.set" && message.PayloadAs<LedSet>() is { } led)
    {
        await wbskt.ReportStateAsync(new Dictionary<string, object?> { ["led"] = led.On });
    }
};

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

await wbskt.StartAsync();

// Keep the program running: when it ends, the client is disposed and goes offline.
while (!stop.IsCancellationRequested)
{
    await wbskt.SendAsync("temperature", new Reading(21.5)); // {"celsius":21.5}
    await wbskt.ReportStateAsync(new Dictionary<string, object?> { ["celsius"] = 21.5 });
    await Task.Delay(TimeSpan.FromSeconds(10), stop.Token).ContinueWith(_ => { });
}

record LedSet(bool On);
record Reading(double Celsius);
```

`StartAsync` returns once the first connection attempt has finished; the client then runs in the
background for as long as it isn't disposed, so the program has to keep running (here until Ctrl+C).
A service does the same in its hosted service's `ExecuteAsync`.

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

**Property names.** Payloads go out with camelCase property names (`Celsius` is sent as `celsius`),
as the Python and Arduino clients send them, so a workflow reading `payload.celsius` works for every
client. Dictionary keys go out exactly as given. Earlier betas sent property names as declared.

**Offline.** `SendAsync` while disconnected queues the message (1,000 by default,
`ClientConfig.OfflineBufferSize`) and sends it in order, with its original time, on reconnect. Sends
are paced at 8 a second after a burst of 50, under the gateway's limit, so a full buffer takes about
two minutes to drain. A full buffer drops its oldest message for each new one; `DroppedMessageCount` says
how many have gone that way.

**Stopping.** `StopAsync` closes the connection normally and stops reconnecting; the queue and the
handlers stay, and `StartAsync` connects again. `IsConnected` says whether the client is connected
with its capabilities sent, so a message sent now goes straight out. Disposing closes normally too,
then drops anything still queued: check `PendingMessageCount` before disposing if that matters. Any
call after disposing throws `ObjectDisposedException`.

**Handlers.** Messages reach `MessageHandler` one at a time, in order, on the SDK's own task: each
call is awaited before the next message is handed over, and a slow handler doesn't delay the
connection's pings. The payload is a `JsonElement`; `PayloadAs<T>()` reads it into your own type,
matching property names ignoring case. Pass the token on to anything slow: it is
cancelled when the client is disposed, and no further message is handed over after that. Don't wait
inside a handler for a later message; it can't arrive until the handler returns. A handler that
throws, before or after an `await`, doesn't drop the connection; the exception is raised on
`OnHandlerError`. The `OnMessageReceived` event still works for synchronous handlers, but don't give
it an `async` lambda: that is `async void`, and an exception after its first `await` ends the
process. `OnConnected`, `OnDisconnected` and `OnSuperseded` are guarded the same way: the client
still announces itself and reconnects.

**Not connecting?** `OnConnectFailed` is raised with the exception for every failed attempt while the
client keeps retrying. A refused registration or login is a `WbsktConnectException` with the `Step`,
the `StatusCode`, the server's `Code` and its message, for example
`Registration answered 404 POLICY_NOT_FOUND: Invalid registration PIN.`; a 429 also carries
`RetryAfter`. An unreachable URL or a TLS error arrives as the `HttpRequestException` or
`WebSocketException` .NET raised. With no saved credentials and no PIN it is raised with
`RegistrationRequiredException`, and the client stops for good: dispose it and create a new one with a PIN.

| `Code` | Why, and what to do |
|---|---|
| `POLICY_NOT_FOUND` (404) | The PIN matches no policy. Check it in the console (Policies). |
| `POLICY_DISABLED` (403) | The policy is turned off. Turn it on; the client retries. |
| `POLICY_LIMIT_REACHED` (400), `PLAN_CLIENT_LIMIT_REACHED` (409) | The policy or the plan is full. Remove a client you no longer use. |
| `CLIENT_NOT_REGISTERED` (401) | Waiting for approval, or revoked or rejected. Approve it in the console; the client retries. |
| `CLIENT_UNAUTHORIZED` (401) | Deleted or secret rotated: a `CredentialsRejectedException`, below. |

**Secret rotated or client deleted.** When someone rotates the client's secret or deletes the client in
the console, login refuses the saved credentials. `StartAsync` throws (or `OnConnectFailed` is raised
with) `CredentialsRejectedException`, and the client stops for good and keeps its credentials file. It
never registers again by itself: a deleted device would come back as a new client, and a rotated one
would stay offline beside a duplicate. To install a rotated secret, save it beside the same client id
and start again:

```csharp
var storage = FileClientStorage.ForClient("greenhouse-sensor");
var (clientRefId, _) = await storage.LoadCredentialsAsync();
await storage.SaveCredentialsAsync(clientRefId!.Value, "the-new-secret");
```

To register again on purpose (the client was deleted, or you point it at a fresh install), call
`ClearCredentialsAsync()` on the storage or delete the file; the next start registers with the PIN.

**Credentials can't be saved.** If the client registers but its storage can't write the credentials
(a read-only folder, a folder owned by another user), `StartAsync` throws (or `OnConnectFailed` is
raised with) `CredentialsNotSavedException`, naming the new client and the storage error, and the client
stops. It doesn't retry, because every retry would register another client until the policy was full.
Fix the folder and start again, and remove the extra client in the console.

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
