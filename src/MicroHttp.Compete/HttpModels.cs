namespace MicroHttp.Compete;

public record HttpRequest(string Method, string Path, string Version, Dictionary<string, string> Headers);
public record HttpResponse(string Version, int StatusCode, string ReasonPhrase, Dictionary<string, string> Headers, byte[] Body);
