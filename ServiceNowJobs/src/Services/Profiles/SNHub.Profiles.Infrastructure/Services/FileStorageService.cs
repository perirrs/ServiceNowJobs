using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using SNHub.Profiles.Application.Interfaces;

namespace SNHub.Profiles.Infrastructure.Services;

/// <summary>
/// Production file storage using Azure Blob Storage.
/// Registered via AddAzureBlobStorage() in InfrastructureExtensions.
/// For local dev: use Azurite emulator or set UseDevelopmentStorage=true.
/// </summary>
public sealed class AzureBlobFileStorageService : IFileStorageService
{
    private readonly BlobServiceClient _client;
    private readonly ILogger<AzureBlobFileStorageService> _logger;
    private const string Container = "snhub-profiles";

    public AzureBlobFileStorageService(BlobServiceClient client, ILogger<AzureBlobFileStorageService> logger)
    { _client = client; _logger = logger; }

    public async Task<string> UploadAsync(Stream content, string path, string contentType, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(Container);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blob = container.GetBlobClient(path);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        _logger.LogInformation("Uploaded blob: {Path}", path);
        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string fileUrl, CancellationToken ct = default)
    {
        var uri = new Uri(fileUrl);
        var blobName = string.Join("/", uri.Segments.Skip(2));
        var container = _client.GetBlobContainerClient(Container);
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
        _logger.LogInformation("Deleted blob: {BlobName}", blobName);
    }
}

/// <summary>
/// Local/test stub — stores files in memory. Used when Azure is not configured.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly Dictionary<string, byte[]> _store = new();

    public async Task<string> UploadAsync(Stream content, string path, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _store[path] = ms.ToArray();
        return $"https://local-storage.dev/{path}";
    }

    public Task DeleteAsync(string fileUrl, CancellationToken ct = default)
    {
        var path = new Uri(fileUrl).AbsolutePath.TrimStart('/');
        _store.Remove(path);
        return Task.CompletedTask;
    }
}
