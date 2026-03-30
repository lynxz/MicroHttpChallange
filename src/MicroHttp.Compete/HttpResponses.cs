namespace MicroHttp.Compete;

public static class HttpResponses
{
    public static HttpResponse Unauthorized(string version) => new(
        version, 401, "Unauthorized",
        new Dictionary<string, string>
        {
            ["Content-Type"] = "text/plain",
            ["Content-Length"] = "0",
            ["Connection"] = "close"
        },
        Array.Empty<byte>());

    public static HttpResponse BadRequest(string version) => new(
        version, 400, "Bad Request",
        new Dictionary<string, string>
        {
            ["Content-Type"] = "text/plain",
            ["Content-Length"] = "0",
            ["Connection"] = "close"
        },
        Array.Empty<byte>());

    public static HttpResponse NotFound(string version) => new(
        version, 404, "Not Found",
        new Dictionary<string, string>
        {
            ["Content-Type"] = "text/plain",
            ["Content-Length"] = "0",
            ["Connection"] = "close"
        },
        Array.Empty<byte>());
}
