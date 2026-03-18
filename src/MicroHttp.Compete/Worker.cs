using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MicroHttp.Compete;

public record HttpRequest(string Method, string Path, string Version, Dictionary<string, string> Headers);
public record HttpResponse(string Version, int StatusCode, string ReasonPhrase, Dictionary<string, string> Headers, byte[] Body);

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly HttpClient _httpClient = new();

    // Limit concurrent active connections to 10
    private readonly SemaphoreSlim _connectionSemaphore = new(10);

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _httpClient.BaseAddress = new Uri("http://localhost:5209");
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

    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("Accepted client {remote}", remote);

        using var networkStream = client.GetStream();
        var buffer = new byte[8192];
        var totalRead = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (totalRead >= buffer.Length)
            {
                _logger.LogWarning("Buffer full without finding header terminator from {remote}", remote);
                break;
            }

            int bytesRead;
            try
            {
                bytesRead = await networkStream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }

            if (bytesRead == 0)
                break;

            totalRead += bytesRead;

            // Check if we've received the full header terminator
            var headerEnd = buffer.AsSpan(0, totalRead).IndexOf(HeaderTerminator);
            if (headerEnd >= 0)
            {
                var headerLength = headerEnd + HeaderTerminator.Length;
                var text = Encoding.UTF8.GetString(buffer, 0, headerLength);

                var request = ParseHttpRequest(text);
                if (request is null)
                {
                    _logger.LogWarning("Malformed HTTP request from {remote}", remote);
                    break;
                }

                _logger.LogInformation("{method} {path} {version} from {remote}", request.Method, request.Path, request.Version, remote);
                foreach (var header in request.Headers)
                {
                    _logger.LogInformation("  {name}: {value}", header.Key, header.Value);
                }

                var token = request.Headers.TryGetValue("Authorization", out var authHeader) && authHeader.StartsWith("Bearer ") ? authHeader.Substring("Bearer ".Length) : null;

                if (token is not null)
                {
                    var validationResponse = await _httpClient.GetAsync("/validate?token=" + token);  // Handle authorization header
                    if (validationResponse.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Token validated successfully for {remote}", remote);
                        // Send 200 OK response
                        var responseBody = "OK"u8.ToArray();
                        var response = new HttpResponse(
                            request.Version,
                            200,
                            "OK",
                            new Dictionary<string, string>
                            {
                                ["Content-Type"] = "text/plain",
                                ["Content-Length"] = responseBody.Length.ToString(),
                                ["Connection"] = "close"
                            },
                            responseBody);

                        await WriteResponseAsync(networkStream, response, cancellationToken);
                    }
                    else
                    {
                        var errorContent = await validationResponse.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogWarning("Token validation failed for {remote}: {status} {error}", remote, validationResponse.StatusCode, errorContent);
                        var unauthorizedResponse = new HttpResponse(
                            request.Version,
                            401,
                            "Unauthorized",
                            new Dictionary<string, string>
                            {
                                ["Content-Type"] = "text/plain",
                                ["Content-Length"] = "0",
                                ["Connection"] = "close"
                            },
                            Array.Empty<byte>());

                        await WriteResponseAsync(networkStream, unauthorizedResponse, cancellationToken);
                        return;
                    }
                }
                break;
            }
        }

        _logger.LogInformation("Client {remote} disconnected", remote);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, HttpResponse response, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append($"{response.Version} {response.StatusCode} {response.ReasonPhrase}\r\n");
        foreach (var header in response.Headers)
        {
            sb.Append($"{header.Key}: {header.Value}\r\n");
        }
        sb.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes, cancellationToken);
        if (response.Body.Length > 0)
        {
            await stream.WriteAsync(response.Body, cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
    }

    private static HttpRequest? ParseHttpRequest(string rawHeaders)
    {
        var lines = rawHeaders.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
            return null;

        // Parse request line: "METHOD /path HTTP/1.1"
        var requestLineParts = lines[0].Split(' ', 3);
        if (requestLineParts.Length != 3)
            return null;

        var method = requestLineParts[0];
        var path = requestLineParts[1];
        var version = requestLineParts[2];

        if (!version.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            return null;

        // Parse headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line))
                break;

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var name = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();
            headers[name] = value;
        }

        return new HttpRequest(method, path, version, headers);
    }
}
