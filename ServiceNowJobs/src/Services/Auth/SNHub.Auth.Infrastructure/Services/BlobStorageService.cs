using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SNHub.Auth.Application.Interfaces;

namespace SNHub.Auth.Infrastructure.Services;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _client;
    private readonly AzureStorageSettings _settings;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        BlobServiceClient client,
        IOptions<AzureStorageSettings> settings,
        ILogger<AzureBlobStorageService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(_settings.ProfileImagesContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobName = $"{Guid.NewGuid()}/{fileName}";
        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(content,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: ct);

        _logger.LogInformation("Uploaded blob: {BlobName}", blobName);
        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string blobUrl, CancellationToken ct = default)
    {
        var uri = new Uri(blobUrl);
        var blobName = uri.AbsolutePath.TrimStart('/').Replace($"{_settings.ProfileImagesContainer}/", "");
        var container = _client.GetBlobContainerClient(_settings.ProfileImagesContainer);
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task<Stream> DownloadAsync(string blobUrl, CancellationToken ct = default)
    {
        var uri = new Uri(blobUrl);
        var blobName = uri.AbsolutePath.TrimStart('/').Replace($"{_settings.ProfileImagesContainer}/", "");
        var container = _client.GetBlobContainerClient(_settings.ProfileImagesContainer);
        var download = await container.GetBlobClient(blobName).DownloadAsync(ct);
        return download.Value.Content;
    }
}

public sealed class AzureStorageSettings
{
    public const string SectionName = "AzureStorage";
    public string ConnectionString { get; init; } = string.Empty;
    public string ProfileImagesContainer { get; init; } = "profile-images";
    public string CvContainer { get; init; } = "cvs";
}
