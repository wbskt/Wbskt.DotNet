namespace Wbskt;

/// <summary>
/// The client registered but its storage could not save the new credentials, for example because the
/// data folder is read-only. The client stops rather than retry, because each retry would register
/// another client until the policy is full. Fix the storage and start the same client again: it saves the
/// credentials before it connects. If the process ended first, remove the extra client
/// (<see cref="ClientRefId"/>) in the console.
/// </summary>
public sealed class CredentialsNotSavedException(Guid clientRefId, Exception inner)
    : InvalidOperationException(
        $"Registered as client {clientRefId} but could not save its credentials: {inner.Message} " +
        "Fix the storage and start again; the client stops so that it does not register again on every retry.",
        inner)
{
    public Guid ClientRefId { get; } = clientRefId;
}
