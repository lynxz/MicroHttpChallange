using MicroHttp.Competition;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = $"{builder.Configuration["KeycloakSettings:BaseUrl"]}/realms/{builder.Configuration["KeycloakSettings:Realm"]}",

        ValidateAudience = true,
        ValidAudience = "account",

        ValidateIssuerSigningKey = true,
        ValidateLifetime = false,

        IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
        {
            // Retrieve the public keys from Keycloak
            var keyUri = $"{parameters.ValidIssuer}/protocol/openid-connect/certs";;
            using var httpClient = new HttpClient();
            var response = httpClient.GetStringAsync(keyUri).Result;
            var keys = new JsonWebKeySet(response);
            return keys?.GetSigningKeys();;
        }
    };

    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
});
builder.Services.AddAuthorization();

builder.Services.AddScoped<IProblemService, ProblemService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/problem", (IProblemService problemService, HttpContext context) =>
{
    var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value;
    if (userEmail == null)
    {
        return Results.Unauthorized();
    }

    var problem = problemService.GetProblem(userEmail);
    return Results.File(problem, "application/octet-stream");
}).RequireAuthorization();

app.Run();

