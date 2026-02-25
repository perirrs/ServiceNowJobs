using SNHub.CvParser.Application.Interfaces;

namespace SNHub.CvParser.Infrastructure.Services;

/// <summary>In-memory blob storage for integration tests and local development.</summary>
public sealed class LocalBlobStorageService : IBlobStorageService
{
    private readonly Dictionary<string, byte[]> _store = new();

    public async Task<string> UploadAsync(
        Stream content, string path, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _store[path] = ms.ToArray();
        return $"https://local-storage.dev/{path}";
    }

    public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        if (_store.TryGetValue(path, out var bytes))
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        throw new FileNotFoundException($"Blob not found: {path}");
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        _store.Remove(path);
        return Task.CompletedTask;
    }
}
