using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MicroHttp.Compete;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    // Limit concurrent active connections to 10
    private readonly SemaphoreSlim _connectionSemaphore = new(10);

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
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
                _ = Task.Run(async () =>
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
                }, CancellationToken.None);
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

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("Accepted client {remote}", remote);

        using var networkStream = client.GetStream();
        var buffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Connection reset or closed
                break;
            }

            if (bytesRead == 0) // remote closed
                break;

            // Log received data as UTF8, trim control characters for logging
            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            var display = text.Replace("\r", "\\r").Replace("\n", "\\n");
            _logger.LogInformation("Received {count} bytes from {remote}: {data}", bytesRead, remote, display);
        }

        _logger.LogInformation("Client {remote} disconnected", remote);
    }
}
