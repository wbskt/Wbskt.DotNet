using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wbskt.Internal;

internal sealed class AuthClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ClientConfig _config;

    public AuthClient(ClientConfig config)
    {
        _config = config;
        // With the trailing slash, an API under a path (https://example.com/wbskt) keeps that path:
        // a relative "api/v1/..." resolves after it rather than replacing it.
        _httpClient = new HttpClient { BaseAddress = new Uri(config.BaseApiUrl.TrimEnd('/') + "/") };
    }

    public async Task<(Guid ClientRefId, string Secret)> RegisterAsync(string pin)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/client-registrations/initiate", new ClientRegistrationRequest(pin, _config.ClientName));

        if (!response.IsSuccessStatusCode)
        {
            throw await RefusedAsync(ConnectStep.Registration, response);
        }

        var result = await response.Content.ReadFromJsonAsync<ClientRegistrationResponse>();
        return result != null ? (result.ClientRefId, result.Secret) : throw new InvalidOperationException("Invalid registration response.");
    }

    public async Task<ClientToken> LoginAsync(Guid clientRefId, string secret)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/client-auth/login", new ClientLoginRequest(clientRefId, secret));

        if (!response.IsSuccessStatusCode)
        {
            var refusal = await RefusedAsync(ConnectStep.Login, response);
            throw refusal is { StatusCode: HttpStatusCode.Unauthorized, Code: ClientErrorCodes.Unauthorized }
                ? new CredentialsRejectedException(clientRefId)
                : refusal;
        }

        var result = await response.Content.ReadFromJsonAsync<ClientLoginResponse>();
        return result is { AccessToken.Length: > 0 }
            ? new ClientToken(result.AccessToken, DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn))
            : throw new InvalidOperationException("Invalid login response.");
    }

    /// <summary>The refusal with the server's Problem Details <c>code</c> and <c>detail</c> (or <c>title</c>).</summary>
    private static async Task<WbsktConnectException> RefusedAsync(ConnectStep step, HttpResponseMessage response)
    {
        string? code = null;
        string? detail = null;
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (body.RootElement.ValueKind == JsonValueKind.Object)
            {
                code = StringProperty(body.RootElement, "code");
                detail = StringProperty(body.RootElement, "detail") ?? StringProperty(body.RootElement, "title");
            }
        }
        catch (JsonException)
        {
            // Not Problem Details (a proxy's error page): the status says enough.
        }

        var retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests ? RetryAfter(response) : null;
        return new WbsktConnectException(step, response.StatusCode, code, detail, retryAfter);
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    public void Dispose() => _httpClient.Dispose();
}
