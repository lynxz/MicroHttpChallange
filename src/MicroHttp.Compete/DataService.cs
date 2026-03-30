using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace MicroHttp.Compete;

public interface IDataService
{
    Task<UserDataEntity?> GetUserDataAsync(string email, CancellationToken cancellationToken);
    Task StreamBlobToClientAsync(string blobReference, Stream outputStream, CancellationToken cancellationToken);
}

public class DataService : IDataService
{
    private readonly TableClient _tableClient;
    private readonly BlobContainerClient _blobContainerClient;

    public DataService(TableClient tableClient, BlobContainerClient blobContainerClient)
    {
        _tableClient = tableClient;
        _blobContainerClient = blobContainerClient;
    }

    public async Task<UserDataEntity?> GetUserDataAsync(string email, CancellationToken cancellationToken)
    {
        var queryResults = _tableClient.QueryAsync<UserDataEntity>(
            e => e.PartitionKey == email && !e.Success,
            maxPerPage: 1,
            cancellationToken: cancellationToken);

        await foreach (var entity in queryResults)
        {
            return entity;
        }

        return null;
    }

    public async Task StreamBlobToClientAsync(string blobReference, Stream outputStream, CancellationToken cancellationToken)
    {
        var blobClient = _blobContainerClient.GetBlobClient(blobReference);
        await blobClient.DownloadToAsync(outputStream, cancellationToken);
    }
}
