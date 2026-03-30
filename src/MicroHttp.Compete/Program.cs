using Azure.Data.Tables;
using Azure.Storage.Blobs;
using MicroHttp.Compete;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IHttpRequestHandler, HttpRequestHandler>();
builder.Services.AddHttpClient<ITokenService, TokenService>(client =>
{
    var baseAddress = builder.Configuration["TokenService:BaseAddress"] ?? "http://localhost:5209";
    client.BaseAddress = new Uri(baseAddress);
});

var storageConnectionString = builder.Configuration["AzureStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
var tableName = builder.Configuration["AzureStorage:TableName"] ?? "userdata";
var blobContainerName = builder.Configuration["AzureStorage:BlobContainerName"] ?? "userdata";

builder.Services.AddSingleton(new TableClient(storageConnectionString, tableName));
builder.Services.AddSingleton(new BlobContainerClient(storageConnectionString, blobContainerName));
builder.Services.AddSingleton<IDataService, DataService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
