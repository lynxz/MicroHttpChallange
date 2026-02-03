using System.Text.Json.Serialization;

namespace MicroHttp.Registration;

internal static class Extensions
{
    public static async Task<HttpClient> AddAdminTokenAsync(this HttpClient client, IConfiguration configuration)
    {
        var keycloakSettings = configuration.GetSection("KeycloakSettings");
        var baseUrl = keycloakSettings.GetValue<string>("BaseUrl") ?? throw new ArgumentNullException("BaseUrl is not configured");
        var realm = keycloakSettings.GetValue<string>("Realm") ?? throw new ArgumentNullException("Realm is not configured");
        var clientId = keycloakSettings.GetValue<string>("ClientId") ?? throw new ArgumentNullException("ClientId is not configured");
        var clientSecret = keycloakSettings.GetValue<string>("ClientSecret") ?? throw new ArgumentNullException("ClientSecret is not configured");
        var userId = keycloakSettings.GetValue<string>("UserId") ?? throw new ArgumentNullException("UserId is not configured");
        var password = keycloakSettings.GetValue<string>("Password") ?? throw new ArgumentNullException("Password is not configured");
        var tokenEndpoint = $"{baseUrl}/realms/{realm}/protocol/openid-connect/token";
        
        var adminToken = await GetAdminTokenAsync(tokenEndpoint, clientId, clientSecret, userId, password);
        if (!string.IsNullOrEmpty(adminToken))
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {adminToken}");
        }

        return client;
    }

    private static async Task<string> GetAdminTokenAsync(string tokenEndpoint, string clientId, string clientSecret, string userId, string password)
    {
        using var httpClient = new HttpClient();
        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "password" },
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "username", userId },
            { "password", password }
        };
        var content = new FormUrlEncodedContent(parameters);
        var response = await httpClient.PostAsync(tokenEndpoint, content);
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(responseContent);
        return tokenResponse?.AccessToken ?? throw new InvalidOperationException("Failed to retrieve access token.");
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