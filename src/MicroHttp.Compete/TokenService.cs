using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MicroHttp.Compete;

public interface ITokenService
{
    Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken);
    string? GetEmailFromToken(string token);
}

public class TokenService : ITokenService
{
    private readonly HttpClient _httpClient;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public TokenService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/validate?token={token}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public string? GetEmailFromToken(string token)
    {
        if (!_tokenHandler.CanReadToken(token))
            return null;

        var jwt = _tokenHandler.ReadJwtToken(token);
        return jwt.Claims.FirstOrDefault(c => c.Type is "email" or ClaimTypes.Email)?.Value;
    }
}
