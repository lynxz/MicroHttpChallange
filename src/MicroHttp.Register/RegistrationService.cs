namespace MicroHttp.Registration;

public class RegistrationService(HttpClient httpClient, IConfiguration configuration)
{
    
    public async Task RegisterUserAsync(UserRegistrationInfo registrationInfo)
    {
        var keycloakSettings = configuration.GetSection("KeycloakSettings");
        var baseUrl = keycloakSettings.GetValue<string>("BaseUrl") ?? throw new ArgumentNullException("BaseUrl is not configured");
        var realm = keycloakSettings.GetValue<string>("Realm") ?? throw new ArgumentNullException("Realm is not configured");
        var usersEndpoint = $"{baseUrl}/admin/realms/{realm}/users";

        var userPayload = new
        {
            registrationInfo.Username,
            registrationInfo.Email,
            emailVerified = true,
            enabled = true,
            credentials = new[]
            {
                new
                {
                    type = "password",
                    value = registrationInfo.Password,
                    temporary = false
                }
            }
        };

        var requestContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(userPayload), System.Text.Encoding.UTF8, "application/json");
        var responses = await httpClient.PostAsync(usersEndpoint, requestContent);
        responses.EnsureSuccessStatusCode();
    }


}

public record UserRegistrationInfo(string Username, string Email, string Password);