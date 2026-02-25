using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using SNHub.Matching.Application.Interfaces;

namespace SNHub.Matching.Infrastructure.Services;

public sealed class AzureOpenAiEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<AzureOpenAiEmbeddingService> _logger;

    public AzureOpenAiEmbeddingService(
        AzureOpenAIClient openAi,
        IConfiguration config,
        ILogger<AzureOpenAiEmbeddingService> logger)
    {
        var model = config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";
        _client = openAi.GetEmbeddingClient(model);
        _logger = logger;
    }

    public async Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
    {
        // Truncate to ~8000 tokens (roughly 32k chars) for safety
        var truncated = text.Length > 32000 ? text[..32000] : text;

        _logger.LogDebug("Generating embedding for {Chars} chars", truncated.Length);

        var response = await _client.GenerateEmbeddingAsync(truncated, cancellationToken: ct);
        return response.Value.ToFloats().ToArray();
    }
}
