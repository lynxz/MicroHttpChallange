using System.Net.Sockets;
using System.Text;

namespace MicroHttp.Compete;

public interface IHttpRequestHandler
{
    Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken);
    Task WriteResponseAsync(NetworkStream stream, HttpResponse response, CancellationToken cancellationToken);
}

public class HttpRequestHandler : IHttpRequestHandler
{
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    public async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var totalRead = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (totalRead >= buffer.Length)
                return null;

            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }

            if (bytesRead == 0)
                return null;

            totalRead += bytesRead;

            var headerEnd = buffer.AsSpan(0, totalRead).IndexOf(HeaderTerminator);
            if (headerEnd >= 0)
            {
                var headerLength = headerEnd + HeaderTerminator.Length;
                var text = Encoding.UTF8.GetString(buffer, 0, headerLength);
                return ParseHttpRequest(text);
            }
        }

        return null;
    }

    public async Task WriteResponseAsync(NetworkStream stream, HttpResponse response, CancellationToken cancellationToken)
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

        var requestLineParts = lines[0].Split(' ', 3);
        if (requestLineParts.Length != 3)
            return null;

        var method = requestLineParts[0];
        var path = requestLineParts[1];
        var version = requestLineParts[2];

        if (!version.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            return null;

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
