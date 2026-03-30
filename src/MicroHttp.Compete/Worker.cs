using System.Net;
using System.Net.Sockets;

namespace MicroHttp.Compete;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ITokenService _tokenService;
    private readonly IHttpRequestHandler _httpRequestHandler;
    private readonly IDataService _dataService;

    // Limit concurrent active connections to 10
    private readonly SemaphoreSlim _connectionSemaphore = new(10);

    public Worker(ILogger<Worker> logger, ITokenService tokenService, IHttpRequestHandler httpRequestHandler, IDataService dataService)
    {
        _logger = logger;
        _tokenService = tokenService;
        _httpRequestHandler = httpRequestHandler;
        _dataService = dataService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 7222);
        listener.Start();
        _logger.LogInformation("TCP listener started on {endpoint}", listener.LocalEndpoint);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    // Accept a client; this will throw if the token is cancelled via WaitAsync
                    client = await listener.AcceptTcpClientAsync().WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // Wait until we have capacity for up to 10 concurrent connections
                try
                {
                    await _connectionSemaphore.WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    client.Close();
                    break;
                }

                // Handle the client in the background and release the semaphore when done
                _ = HandleAndCleanupAsync(client, stoppingToken);
            }
        }
        finally
        {
            try
            {
                listener.Stop();
            }
            catch { }
        }

        _logger.LogInformation("Worker stopping");
    }

    private async Task HandleAndCleanupAsync(TcpClient client, CancellationToken stoppingToken)
    {
        try
        {
            await HandleClientAsync(client, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while handling client");
        }
        finally
        {
            try { client.Close(); } catch { }
            _connectionSemaphore.Release();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("Accepted client {remote}", remote);

        using var networkStream = client.GetStream();

        var request = await _httpRequestHandler.ReadRequestAsync(networkStream, cancellationToken);
        if (request is null)
        {
            _logger.LogWarning("Malformed or incomplete HTTP request from {remote}", remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.BadRequest("HTTP/1.1"), cancellationToken);
            return;
        }

        _logger.LogInformation("{method} {path} {version} from {remote}", request.Method, request.Path, request.Version, remote);
        foreach (var header in request.Headers)
        {
            _logger.LogInformation("  {name}: {value}", header.Key, header.Value);
        }

        var token = request.Headers.TryGetValue("Authorization", out var authHeader) && authHeader.StartsWith("Bearer ")
            ? authHeader.Substring("Bearer ".Length)
            : null;

        if (token is not null)
        {
            var isValid = await _tokenService.ValidateTokenAsync(token, cancellationToken);
            if (!isValid)
            {
                _logger.LogWarning("Token validation failed for {remote}", remote);
                await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.Unauthorized(request.Version), cancellationToken);
                return;
            }

            _logger.LogInformation("Token validated successfully for {remote}", remote);

            var email = _tokenService.GetEmailFromToken(token);
            if (email is null)
            {
                _logger.LogWarning("No email claim found in token for {remote}", remote);
                await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.BadRequest(request.Version), cancellationToken);
                return;
            }

            var userData = await _dataService.GetUserDataAsync(email, cancellationToken);
            if (userData is null)
            {
                _logger.LogWarning("No pending user data found for {email} from {remote}", email, remote);
                await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.NotFound(request.Version), cancellationToken);
                return;
            }

            _logger.LogInformation("Streaming blob {blob} for {email} to {remote}", userData.BlobReference, email, remote);
            await _dataService.StreamBlobToClientAsync(userData.BlobReference, networkStream, cancellationToken);
        }

        _logger.LogInformation("Client {remote} disconnected", remote);
    }
}
