namespace Wbskt;

/// <summary>
/// Login refused the stored credentials (<c>CLIENT_UNAUTHORIZED</c>): the client was deleted or its
/// secret rotated in the console. The client keeps the credentials and stops, because registering again
/// would bring a deleted device back as a new client. Save a rotated secret beside the same
/// <see cref="ClientRefId"/>, or clear the credentials to register again with the PIN.
/// </summary>
public sealed class CredentialsRejectedException(Guid clientRefId)
    : InvalidOperationException(
        $"Login refused the credentials of client {clientRefId} (CLIENT_UNAUTHORIZED): it was deleted or its " +
        "secret rotated in the console. Save the new secret with the same client id, or clear the stored " +
        "credentials to register again with the PIN.")
{
    public Guid ClientRefId { get; } = clientRefId;
}
