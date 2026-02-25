using SNHub.Matching.Application.Interfaces;

namespace SNHub.Matching.Infrastructure.Services;

/// <summary>
/// Returns a deterministic pseudo-random embedding vector for tests/local dev.
/// The vector is seeded from the text hash so identical inputs produce identical vectors.
/// </summary>
public sealed class StubEmbeddingService : IEmbeddingService
{
    private const int Dimensions = 1536; // same as text-embedding-3-small

    public Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
    {
        var rng     = new Random(text.GetHashCode());
        var vector  = new float[Dimensions];
        double sumSq = 0;

        for (int i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(rng.NextDouble() * 2 - 1);
            sumSq    += vector[i] * vector[i];
        }

        // Normalise to unit vector (required for cosine similarity)
        var norm = (float)Math.Sqrt(sumSq);
        for (int i = 0; i < Dimensions; i++)
            vector[i] /= norm;

        return Task.FromResult(vector);
    }
}
