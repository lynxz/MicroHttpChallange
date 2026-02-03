namespace MicroHttp.Auth;

public interface IAuthService
{
    Task<TokenResponse> LoginAsync(string username, string password);
    Task<TokenResponse> RefreshTokenAsync(string refreshToken);
    Task<bool> ValidateTokenAsync(string token);
}
