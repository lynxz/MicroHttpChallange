using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MicroHttp.Compete;

public class Worker : BackgroundService
{
    private const int MaxProblemNumber = 12;
    private static readonly Regex SubmitRoutePattern = new(@"^/problems/(\d+)/submit$", RegexOptions.Compiled);
    private readonly ILogger<Worker> _logger;
    private readonly ITokenService _tokenService;
    private readonly IHttpRequestHandler _httpRequestHandler;
    private readonly IDataService _dataService;

    // Limit concurrent active connections to 10
    private readonly SemaphoreSlim _connectionSemaphore = new(10);

    public Worker(
        ILogger<Worker> logger,
        ITokenService tokenService,
        IHttpRequestHandler httpRequestHandler,
        IDataService dataService)
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

        if (token is null)
        {
            _logger.LogWarning("No authorization token provided from {remote}", remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.Unauthorized(request.Version), cancellationToken);
            return;
        }

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

        if (request.Path == "/problem")
        {
            await HandleProblemAsync(networkStream, request, email, remote, cancellationToken);
        }
        else if (request.Method == "POST" && SubmitRoutePattern.Match(request.Path) is { Success: true } match
            && int.TryParse(match.Groups[1].Value, out var problemId))
        {
            await HandleSubmitAsync(networkStream, request, email, problemId, remote, cancellationToken);
        }
        else
        {
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.NotFound(request.Version), cancellationToken);
        }

        _logger.LogInformation("Client {remote} disconnected", remote);
    }

    private async Task HandleProblemAsync(NetworkStream networkStream, HttpRequest request, string email, string remote, CancellationToken cancellationToken)
    {
        var problemNumber = await GetProblemNumberAsync(email, cancellationToken);

        if (problemNumber > MaxProblemNumber)
        {
            _logger.LogInformation("User {email} has solved all problems, congratulating from {remote}", email, remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.Ok(request.Version, "Congratulations! You have solved all the problems!"), cancellationToken);
            return;
        }

        var problemBlob = await _dataService.GetProblemBlobAsync(problemNumber, cancellationToken);
        if (problemBlob is null)
        {
            _logger.LogWarning("No blob mapping found for problem {problem} for {email} from {remote}", problemNumber, email, remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.NotFound(request.Version), cancellationToken);
            return;
        }

        _logger.LogInformation("Streaming blob {blob} for problem {problem} for {email} to {remote}", problemBlob.BlobReference, problemNumber, email, remote);
        await _dataService.StreamBlobToClientAsync(problemBlob.BlobReference, networkStream, cancellationToken);
    }

    private async Task HandleSubmitAsync(NetworkStream networkStream, HttpRequest request, string email, int problemId, string remote, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Submit received for problem {problem} from {email} at {remote}", problemId, email, remote);

        var expectedProblem = await GetProblemNumberAsync(email, cancellationToken);

        if (problemId != expectedProblem)
        {
            _logger.LogWarning("User {email} submitted problem {submitted} but expected {expected} from {remote}", email, problemId, expectedProblem, remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.BadRequest(request.Version), cancellationToken);
            return;
        }

        var headersWithoutAuth = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
        headersWithoutAuth.Remove("Authorization");
        var requestForChecksum = request with { Headers = headersWithoutAuth };
        var requestText = requestForChecksum.ToString();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestText)));

        _logger.LogInformation("Checksum for problem {problem} from {email}: {checksum}", problemId, email, checksum);

        var problemBlob = await _dataService.GetProblemBlobAsync(problemId, cancellationToken);
        if (problemBlob is null)
        {
            _logger.LogWarning("No problem blob found for problem {problem} from {remote}", problemId, remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.NotFound(request.Version), cancellationToken);
            return;
        }

        var success = string.Equals(checksum, problemBlob.AnswerHash, StringComparison.OrdinalIgnoreCase);

        var userDataEntity = new UserDataEntity
        {
            PartitionKey = email,
            RowKey = problemId.ToString(),
            ProblemNumber = problemId,
            Success = success,
            CompletedAt = success ? DateTimeOffset.UtcNow : null
        };

        await _dataService.SaveUserDataAsync(userDataEntity, cancellationToken);

        if (success)
        {
            _logger.LogInformation("Problem {problem} solved by {email} from {remote}", problemId, email, remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.Ok(request.Version, "OK"), cancellationToken);
        }
        else
        {
            _logger.LogWarning("Incorrect answer for problem {problem} from {email} at {remote}", problemId, email, remote);
            await _httpRequestHandler.WriteResponseAsync(networkStream, HttpResponses.BadRequest(request.Version), cancellationToken);
        }
    }

    private async Task<int> GetProblemNumberAsync(string email, CancellationToken cancellationToken)
    {
        var userData = await _dataService.GetLatestProblemAsync(email, cancellationToken);
        return userData switch
        {
            null => 0,
            { Success: true } => userData.ProblemNumber + 1,
            _ => userData.ProblemNumber
        };
    }
}
