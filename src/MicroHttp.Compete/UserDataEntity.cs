using Azure;
using Azure.Data.Tables;

namespace MicroHttp.Compete;

public class UserDataEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // Email address
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public bool Success { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string BlobReference { get; set; } = string.Empty;
}
