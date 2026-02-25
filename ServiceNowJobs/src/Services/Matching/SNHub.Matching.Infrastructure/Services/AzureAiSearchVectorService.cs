using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.Infrastructure.Services;

public sealed class AzureAiSearchVectorService : IVectorSearchService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient      _jobsClient;
    private readonly SearchClient      _candidatesClient;
    private readonly IEmbeddingRecordRepository _embeddingRepo;
    private readonly ILogger<AzureAiSearchVectorService> _logger;

    private const string JobsIndex       = "snhub-jobs";
    private const string CandidatesIndex = "snhub-candidates";
    private const string EmbeddingField  = "embedding";
    private const int    Dimensions      = 1536;

    public AzureAiSearchVectorService(
        SearchIndexClient indexClient,
        IEmbeddingRecordRepository embeddingRepo,
        IConfiguration config,
        ILogger<AzureAiSearchVectorService> logger)
    {
        _indexClient      = indexClient;
        _embeddingRepo    = embeddingRepo;
        _logger           = logger;

        var endpoint = config["AzureAISearch:Endpoint"]
            ?? throw new InvalidOperationException("AzureAISearch:Endpoint required.");
        var key = config["AzureAISearch:ApiKey"]
            ?? throw new InvalidOperationException("AzureAISearch:ApiKey required.");

        var credential    = new AzureKeyCredential(key);
        _jobsClient       = new SearchClient(new Uri(endpoint), JobsIndex, credential);
        _candidatesClient = new SearchClient(new Uri(endpoint), CandidatesIndex, credential);
    }

    // ── Index management ──────────────────────────────────────────────────────

    public async Task EnsureIndexesExistAsync(CancellationToken ct = default)
    {
        await EnsureJobsIndexAsync(ct);
        await EnsureCandidatesIndexAsync(ct);
    }

    private async Task EnsureJobsIndexAsync(CancellationToken ct)
    {
        var fields = new List<SearchField>
        {
            new SimpleField("id",              SearchFieldDataType.String)  { IsKey = true },
            new SearchableField("title")       { IsFilterable = true },
            new SearchableField("description"),
            new SearchableField("requirements"),
            new SimpleField("companyName",     SearchFieldDataType.String)  { IsFilterable = true },
            new SimpleField("location",        SearchFieldDataType.String)  { IsFilterable = true },
            new SimpleField("country",         SearchFieldDataType.String)  { IsFilterable = true },
            new SimpleField("workMode",        SearchFieldDataType.String)  { IsFilterable = true },
            new SimpleField("experienceLevel", SearchFieldDataType.String)  { IsFilterable = true },
            new SimpleField("jobType",         SearchFieldDataType.String)  { IsFilterable = true },
            new SimpleField("salaryMin",       SearchFieldDataType.Double)  { IsFilterable = true },
            new SimpleField("salaryMax",       SearchFieldDataType.Double)  { IsFilterable = true },
            new SimpleField("salaryCurrency",  SearchFieldDataType.String),
            new SimpleField("isSalaryVisible", SearchFieldDataType.Boolean),
            new SearchField("skills",          SearchFieldDataType.Collection(SearchFieldDataType.String))
                { IsSearchable = true, IsFilterable = true },
            new SearchField("serviceNowVersions", SearchFieldDataType.Collection(SearchFieldDataType.String))
                { IsFilterable = true },
            new SimpleField("createdAt",       SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new VectorSearchField(EmbeddingField, Dimensions, "default-hnsw")
        };

        var index = new SearchIndex(JobsIndex, fields)
        {
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("default-hnsw") }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
        _logger.LogInformation("Ensured Azure AI Search index: {Index}", JobsIndex);
    }

    private async Task EnsureCandidatesIndexAsync(CancellationToken ct)
    {
        var fields = new List<SearchField>
        {
            new SimpleField("id",                SearchFieldDataType.String) { IsKey = true },
            new SearchableField("fullName"),
            new SearchableField("headline"),
            new SearchableField("summary"),
            new SimpleField("currentRole",       SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("location",          SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("country",           SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("yearsOfExperience", SearchFieldDataType.Int32)  { IsFilterable = true, IsSortable = true },
            new SimpleField("experienceLevel",   SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("availability",      SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("openToRemote",      SearchFieldDataType.Boolean){ IsFilterable = true },
            new SearchField("skills", SearchFieldDataType.Collection(SearchFieldDataType.String))
                { IsSearchable = true, IsFilterable = true },
            new SearchField("certifications", SearchFieldDataType.Collection(SearchFieldDataType.String))
                { IsFilterable = true },
            new SearchField("serviceNowVersions", SearchFieldDataType.Collection(SearchFieldDataType.String))
                { IsFilterable = true },
            new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new VectorSearchField(EmbeddingField, Dimensions, "default-hnsw")
        };

        var index = new SearchIndex(CandidatesIndex, fields)
        {
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("default-hnsw") }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
        _logger.LogInformation("Ensured Azure AI Search index: {Index}", CandidatesIndex);
    }

    // ── Upsert ────────────────────────────────────────────────────────────────

    public async Task UpsertJobAsync(JobSearchDocument doc, CancellationToken ct = default)
    {
        var batch = IndexDocumentsBatch.MergeOrUpload([ToSearchDoc(doc)]);
        await _jobsClient.IndexDocumentsAsync(batch, cancellationToken: ct);
        _logger.LogDebug("Upserted job {Id} to search index", doc.Id);
    }

    public async Task UpsertCandidateAsync(CandidateSearchDocument doc, CancellationToken ct = default)
    {
        var batch = IndexDocumentsBatch.MergeOrUpload([ToSearchDoc(doc)]);
        await _candidatesClient.IndexDocumentsAsync(batch, cancellationToken: ct);
        _logger.LogDebug("Upserted candidate {Id} to search index", doc.Id);
    }

    public async Task DeleteAsync(string id, DocumentType type, CancellationToken ct = default)
    {
        var client = type == DocumentType.Job ? _jobsClient : _candidatesClient;
        await client.DeleteDocumentsAsync("id", [id], cancellationToken: ct);
        _logger.LogInformation("Deleted {Type} {Id} from search index", type, id);
    }

    // ── Vector search ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<(string JobId, double Score)>> SearchJobsForCandidateAsync(
        float[] candidateEmbedding, int topK = 20, CancellationToken ct = default)
    {
        var options = new SearchOptions
        {
            VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizedQuery(candidateEmbedding)
                {
                    Fields = { EmbeddingField },
                    KNearestNeighborsCount = topK
                }}
            },
            Filter = "isActive eq true",
            Size = topK
        };

        var results = await _jobsClient.SearchAsync<SearchDocument>(null, options, ct);
        var matches = new List<(string, double)>();
        await foreach (var result in results.Value.GetResultsAsync())
            matches.Add((result.Document["id"]!.ToString()!, result.Score ?? 0));

        return matches;
    }

    public async Task<IEnumerable<(string CandidateId, double Score)>> SearchCandidatesForJobAsync(
        float[] jobEmbedding, int topK = 20, CancellationToken ct = default)
    {
        var options = new SearchOptions
        {
            VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizedQuery(jobEmbedding)
                {
                    Fields = { EmbeddingField },
                    KNearestNeighborsCount = topK
                }}
            },
            Filter = "availability ne 'NotAvailable'",
            Size = topK
        };

        var results = await _candidatesClient.SearchAsync<SearchDocument>(null, options, ct);
        var matches = new List<(string, double)>();
        await foreach (var result in results.Value.GetResultsAsync())
            matches.Add((result.Document["id"]!.ToString()!, result.Score ?? 0));

        return matches;
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static SearchDocument ToSearchDoc(JobSearchDocument d)
    {
        var doc = new SearchDocument();
        doc["id"]              = d.Id;
        doc["title"]           = d.Title;
        doc["description"]     = d.Description;
        doc["requirements"]    = d.Requirements;
        doc["companyName"]     = d.CompanyName;
        doc["location"]        = d.Location;
        doc["country"]         = d.Country;
        doc["workMode"]        = d.WorkMode;
        doc["experienceLevel"] = d.ExperienceLevel;
        doc["jobType"]         = d.JobType;
        doc["salaryMin"]       = d.SalaryMin;
        doc["salaryMax"]       = d.SalaryMax;
        doc["salaryCurrency"]  = d.SalaryCurrency;
        doc["isSalaryVisible"] = d.IsSalaryVisible;
        doc["skills"]          = d.Skills;
        doc["serviceNowVersions"] = d.ServiceNowVersions;
        doc["embedding"]       = d.Embedding;
        doc["createdAt"]       = d.CreatedAt;
        return doc;
    }

    private static SearchDocument ToSearchDoc(CandidateSearchDocument d)
    {
        var doc = new SearchDocument();
        doc["id"]                = d.Id;
        doc["fullName"]          = d.FullName;
        doc["headline"]          = d.Headline;
        doc["summary"]           = d.Summary;
        doc["currentRole"]       = d.CurrentRole;
        doc["location"]          = d.Location;
        doc["country"]           = d.Country;
        doc["yearsOfExperience"] = d.YearsOfExperience;
        doc["experienceLevel"]   = d.ExperienceLevel;
        doc["availability"]      = d.Availability;
        doc["openToRemote"]      = d.OpenToRemote;
        doc["skills"]            = d.Skills;
        doc["certifications"]    = d.Certifications;
        doc["serviceNowVersions"]= d.ServiceNowVersions;
        doc["embedding"]         = d.Embedding;
        doc["updatedAt"]         = d.UpdatedAt;
        return doc;
    }
}
