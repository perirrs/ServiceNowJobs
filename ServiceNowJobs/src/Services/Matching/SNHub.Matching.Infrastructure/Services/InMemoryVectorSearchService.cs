using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.Infrastructure.Services;

/// <summary>
/// In-memory vector search for integration tests.
/// Stores embeddings and computes cosine similarity in-memory.
/// </summary>
public sealed class InMemoryVectorSearchService : IVectorSearchService
{
    private readonly Dictionary<string, (JobSearchDocument Doc, float[] Vec)>       _jobs       = new();
    private readonly Dictionary<string, (CandidateSearchDocument Doc, float[] Vec)> _candidates = new();

    public Task UpsertJobAsync(JobSearchDocument doc, CancellationToken ct = default)
    {
        _jobs[doc.Id] = (doc, doc.Embedding);
        return Task.CompletedTask;
    }

    public Task UpsertCandidateAsync(CandidateSearchDocument doc, CancellationToken ct = default)
    {
        _candidates[doc.Id] = (doc, doc.Embedding);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, DocumentType type, CancellationToken ct = default)
    {
        if (type == DocumentType.Job) _jobs.Remove(id);
        else _candidates.Remove(id);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<(string JobId, double Score)>> SearchJobsForCandidateAsync(
        float[] candidateEmbedding, int topK = 20, CancellationToken ct = default)
    {
        IEnumerable<(string, double)> results;

        if (candidateEmbedding.Length == 0 || _jobs.Count == 0)
        {
            // Return deterministic scores for tests
            results = _jobs.Keys
                .Select((id, i) => (id, 0.9 - i * 0.05))
                .Take(topK);
        }
        else
        {
            results = _jobs
                .Select(kv => (kv.Key, CosineSimilarity(candidateEmbedding, kv.Value.Vec)))
                .OrderByDescending(x => x.Item2)
                .Take(topK);
        }

        return Task.FromResult(results);
    }

    public Task<IEnumerable<(string CandidateId, double Score)>> SearchCandidatesForJobAsync(
        float[] jobEmbedding, int topK = 20, CancellationToken ct = default)
    {
        IEnumerable<(string, double)> results;

        if (jobEmbedding.Length == 0 || _candidates.Count == 0)
        {
            results = _candidates.Keys
                .Select((id, i) => (id, 0.9 - i * 0.05))
                .Take(topK);
        }
        else
        {
            results = _candidates
                .Select(kv => (kv.Key, CosineSimilarity(jobEmbedding, kv.Value.Vec)))
                .OrderByDescending(x => x.Item2)
                .Take(topK);
        }

        return Task.FromResult(results);
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }
}
