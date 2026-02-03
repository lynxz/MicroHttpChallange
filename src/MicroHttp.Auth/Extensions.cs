using MicroHttp.Auth;

internal static class Extensions
{
    public static void MapEndpoints(this WebApplication app)
    {
        app.MapPost("/login", async (LoginRequest loginRequest, IAuthService authService) =>
        {
            try
            {
                var token = await authService.LoginAsync(loginRequest.Username, loginRequest.Password);
                return Results.Ok(token);
            }
            catch (Exception)
            {
                return Results.Unauthorized();
            }
        });

        app.MapPost("/refresh", async (RefreshTokenRequest refreshTokenRequest, IAuthService authService) =>
        {
            try
            {
                var token = await authService.RefreshTokenAsync(refreshTokenRequest.RefreshToken);
                return Results.Ok(token);
            }
            catch (Exception)
            {
                return Results.Unauthorized();
            }
        });

        app.MapGet("/validate", async (string token, IAuthService authService) =>
        {
            var isValid = await authService.ValidateTokenAsync(token);
            return isValid ? Results.Ok() : Results.Unauthorized();
        });
    }
}

public record LoginRequest(string Username, string Password);

public record RefreshTokenRequest(string RefreshToken);