namespace Wbskt.Internal;

// The client's two HTTP calls before it opens the socket: register under a policy with its PIN
// (POST api/v1/client-registrations/initiate), then log in with the secret it got
// (POST api/v1/client-auth/login) for the access token the socket takes.

internal enum ClientStatus : byte
{
    Pending = 0,
    Registered = 1,
    Revoked = 2,
    Rejected = 3
}

internal record ClientRegistrationRequest(string Pin, string Name);

internal record ClientRegistrationResponse(
    Guid ClientRefId,
    string Secret,
    ClientStatus Status
);

internal record ClientLoginRequest(Guid ClientRefId, string Secret);

internal record ClientLoginResponse(string AccessToken, int ExpiresIn);

/// <summary>The <c>code</c> of a refused client login.</summary>
internal static class ClientErrorCodes
{
    /// <summary>Unknown client or wrong secret. The credentials will never work again.</summary>
    public const string Unauthorized = "CLIENT_UNAUTHORIZED";

    /// <summary>Approval is still pending. Keep the credentials and retry.</summary>
    public const string NotRegistered = "CLIENT_NOT_REGISTERED";
}
