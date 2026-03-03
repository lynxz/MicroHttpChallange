using MicroHttp.Registration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpClient<IRegistrationService, RegistrationService>( client =>
{
    client.AddAdminTokenAsync(builder.Configuration).GetAwaiter().GetResult();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();


app.MapPost("/register", async (UserRegistrationInfo userInfo, IRegistrationService registrationService) =>
{
	try
	{
		await registrationService.RegisterUserAsync(userInfo);
	}
	catch (Exception e)
	{
		System.Diagnostics.Debug.WriteLine(e);
		return Results.Problem("An error occurred while registering the user.");
    }
    return Results.Ok();
})
.WithName("PostUserRegistration");

app.Run();
