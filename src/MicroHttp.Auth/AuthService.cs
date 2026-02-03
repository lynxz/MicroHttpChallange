using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroHttp.Auth;

public class AuthService(HttpClient httpClient, IConfiguration configuration) : IAuthService
{

    public async Task<bool> ValidateTokenAsync(string token)
    {
        var keycloakSettings = configuration.GetSection("KeycloakSettings");
        var baseUrl = keycloakSettings.GetValue<string>("BaseUrl") ?? throw new ArgumentNullException("BaseUrl is not configured");
        var realm = keycloakSettings.GetValue<string>("Realm") ?? throw new ArgumentNullException("Realm is not configured");
        var userInfoEndpoint = $"{baseUrl}/realms/{realm}/protocol/openid-connect/userinfo";

        var request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public Task<TokenResponse> LoginAsync(string username, string password) =>
        CoreToken(
            new Dictionary<string, string>
            {
                { "grant_type", "password" },
                { "username", username },
                { "password", password }
            });

    public Task<TokenResponse> RefreshTokenAsync(string refreshToken) =>
        CoreToken(
            new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            });

    private async Task<TokenResponse> CoreToken(Dictionary<string, string> requestBody)
    {
        var keycloakSettings = configuration.GetSection("KeycloakSettings");
        var baseUrl = keycloakSettings.GetValue<string>("BaseUrl") ?? throw new ArgumentNullException("BaseUrl is not configured");
        var realm = keycloakSettings.GetValue<string>("Realm") ?? throw new ArgumentNullException("Realm is not configured");
        var clientId = keycloakSettings.GetValue<string>("ClientId") ?? throw new ArgumentNullException("ClientId is not configured");
        var clientSecret = keycloakSettings.GetValue<string>("ClientSecret") ?? throw new ArgumentNullException("ClientSecret is not configured");
        var tokenEndpoint = $"{baseUrl}/realms/{realm}/protocol/openid-connect/token";

        requestBody.Add("client_id", clientId);
        requestBody.Add("client_secret", clientSecret);

        var requestContent = new FormUrlEncodedContent(requestBody);
        var response = await httpClient.PostAsync(tokenEndpoint, requestContent);

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);

            return tokenResponse!;
        }

        throw new Exception("Failed to retrieve token from Keycloak.");
    }
}


public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
}