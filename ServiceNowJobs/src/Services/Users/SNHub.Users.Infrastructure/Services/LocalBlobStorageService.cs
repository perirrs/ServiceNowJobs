using SNHub.Users.Application.Interfaces;

namespace SNHub.Users.Infrastructure.Services;

/// <summary>In-memory blob storage for development and integration tests.</summary>
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

    public Task DeleteAsync(string blobUrl, CancellationToken ct = default)
    {
        var path = blobUrl.Replace("https://local-storage.dev/", "");
        _store.Remove(path);
        return Task.CompletedTask;
    }
}
