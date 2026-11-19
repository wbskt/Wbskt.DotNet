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

    /// <summary>
    /// Registration answers no retry can fix (protocol §1): a wrong or missing PIN, or a name the platform
    /// can't store. The others (a disabled or full policy, a full plan) can clear while the client waits.
    /// </summary>
    public static readonly IReadOnlySet<string> FinalRegistration = new HashSet<string>(StringComparer.Ordinal)
    {
        "POLICY_NOT_FOUND",
        "REGISTRATION_PIN_REQUIRED",
        "CLIENT_NAME_INVALID",
    };
}
