using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using SNHub.CvParser.Application.Interfaces;

namespace SNHub.CvParser.Infrastructure.Services;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _client;
    private readonly ILogger<AzureBlobStorageService> _logger;
    private const string Container = "cv-documents";

    public AzureBlobStorageService(BlobServiceClient client, ILogger<AzureBlobStorageService> logger)
    { _client = client; _logger = logger; }

    public async Task<string> UploadAsync(
        Stream content, string path, string contentType, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(Container);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blob = container.GetBlobClient(path);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        _logger.LogInformation("Uploaded CV blob: {Path}", path);
        return blob.Uri.ToString();
    }

    public async Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(Container);
        var blob      = container.GetBlobClient(path);
        var download  = await blob.DownloadStreamingAsync(cancellationToken: ct);
        return download.Value.Content;
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(Container);
        await container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct);
        _logger.LogInformation("Deleted CV blob: {Path}", path);
    }
}
