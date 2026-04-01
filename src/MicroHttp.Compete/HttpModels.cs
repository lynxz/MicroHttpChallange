namespace MicroHttp.Compete;

public record HttpRequest(string Method, string Path, string Version, Dictionary<string, string> Headers, byte[] Body)
{
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Method).Append(' ').Append(Path).Append(' ').Append(Version).Append("\r\n");
        foreach (var header in Headers)
        {
            sb.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }
        sb.Append("\r\n");
        if (Body.Length > 0)
        {
            sb.Append(System.Text.Encoding.UTF8.GetString(Body));
        }
        return sb.ToString();
    }
}

public record HttpResponse(string Version, int StatusCode, string ReasonPhrase, Dictionary<string, string> Headers, byte[] Body);
