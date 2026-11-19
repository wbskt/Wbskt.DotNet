using System.Net;

namespace Wbskt;

/// <summary>Which step of connecting the server refused.</summary>
public enum ConnectStep
{
    Registration,
    Login,
}

/// <summary>
/// The server refused a registration or a login, with why: the HTTP status, the server's error
/// <see cref="Code"/> (<c>POLICY_NOT_FOUND</c>, <c>POLICY_DISABLED</c>, <c>POLICY_LIMIT_REACHED</c>,
/// <c>PLAN_CLIENT_LIMIT_REACHED</c>, <c>CLIENT_NOT_REGISTERED</c>, ...) and its message. Raised on
/// <see cref="IWbsktClient.OnConnectFailed"/>; the client keeps retrying unless it is a
/// <see cref="CredentialsRejectedException"/>. A network or TLS failure arrives as the exception
/// .NET raised for it instead.
/// </summary>
public class WbsktConnectException(ConnectStep step, HttpStatusCode status, string? code, string? detail, TimeSpan? retryAfter = null)
    : Exception(Describe(step, status, code, detail))
{
    public ConnectStep Step { get; } = step;

    public HttpStatusCode StatusCode { get; } = status;

    /// <summary>The server's error code, when it sent one.</summary>
    public string? Code { get; } = code;

    /// <summary>The server's <c>Retry-After</c> on a 429, when it sent one.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>A 429: the client waits <see cref="RetryAfter"/>, or backs off, before the next attempt.</summary>
    public bool IsThrottled => StatusCode == HttpStatusCode.TooManyRequests;

    private static string Describe(ConnectStep step, HttpStatusCode status, string? code, string? detail)
    {
        var text = $"{(step == ConnectStep.Registration ? "Registration" : "Login")} answered {(int)status}";
        if (!string.IsNullOrEmpty(code))
        {
            text += $" {code}";
        }

        return string.IsNullOrEmpty(detail) ? text + "." : $"{text}: {detail}";
    }
}
