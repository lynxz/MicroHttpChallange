using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace MicroHttp.Compete;

public interface IDataService
{
    Task<UserDataEntity?> GetLatestProblemAsync(string email, CancellationToken cancellationToken);
    Task<ProblemBlobEntity?> GetProblemBlobAsync(int problemNumber, CancellationToken cancellationToken);
    Task SaveUserDataAsync(UserDataEntity entity, CancellationToken cancellationToken);
    Task StreamBlobToClientAsync(string blobReference, Stream outputStream, CancellationToken cancellationToken);
}

public class DataService : IDataService
{
    private readonly TableClient _tableClient;
    private readonly TableClient _problemBlobTableClient;
    private readonly BlobContainerClient _blobContainerClient;

    public DataService(TableClient tableClient, [FromKeyedServices("problemBlob")] TableClient problemBlobTableClient, BlobContainerClient blobContainerClient)
    {
        _tableClient = tableClient;
        _problemBlobTableClient = problemBlobTableClient;
        _blobContainerClient = blobContainerClient;
    }

    public async Task<UserDataEntity?> GetLatestProblemAsync(string email, CancellationToken cancellationToken)
    {
        var queryResults = _tableClient.QueryAsync<UserDataEntity>(
            e => e.PartitionKey == email,
            cancellationToken: cancellationToken);

        UserDataEntity? latest = null;
        await foreach (var entity in queryResults)
        {
            if (latest is null || entity.ProblemNumber > latest.ProblemNumber)
                latest = entity;
        }

        return latest;
    }

    public async Task<ProblemBlobEntity?> GetProblemBlobAsync(int problemNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _problemBlobTableClient.GetEntityAsync<ProblemBlobEntity>(
                "problem",
                problemNumber.ToString(),
                cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SaveUserDataAsync(UserDataEntity entity, CancellationToken cancellationToken)
    {
        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task StreamBlobToClientAsync(string blobReference, Stream outputStream, CancellationToken cancellationToken)
    {
        var blobClient = _blobContainerClient.GetBlobClient(blobReference);
        await blobClient.DownloadToAsync(outputStream, cancellationToken);
    }
}
