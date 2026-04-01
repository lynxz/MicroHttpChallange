using Azure;
using Azure.Data.Tables;

namespace MicroHttp.Compete;

public class ProblemBlobEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "problem";
    public string RowKey { get; set; } = string.Empty; // Problem number as string
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string BlobReference { get; set; } = string.Empty;
    public string AnswerHash { get; set; } = string.Empty;
    public int ProblemNumber { get; set; }
}
