using System.Text.Json;
using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Domain.Entities;

namespace SNHub.JobEnhancer.Application.Mappers;

public static class EnhancementResultMapper
{
    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public static EnhancementResultDto ToDto(EnhancementResult r)
    {
        var biasIssues = SafeDeserialize<GptBiasIssue[]>(r.BiasIssuesJson) ?? [];
        var missing    = SafeDeserialize<string[]>(r.MissingFieldsJson) ?? [];
        var improvements = SafeDeserialize<GptImprovement[]>(r.ImprovementsJson) ?? [];
        var skills     = SafeDeserialize<string[]>(r.SuggestedSkillsJson) ?? [];

        return new EnhancementResultDto(
            Id:                   r.Id,
            JobId:                r.JobId,
            Status:               r.Status.ToString(),
            OriginalTitle:        r.OriginalTitle,
            OriginalDescription:  r.OriginalDescription,
            OriginalRequirements: r.OriginalRequirements,
            EnhancedTitle:        r.EnhancedTitle,
            EnhancedDescription:  r.EnhancedDescription,
            EnhancedRequirements: r.EnhancedRequirements,
            ScoreBefore:          r.ScoreBefore,
            ScoreAfter:           r.ScoreAfter,
            ScoreImprovement:     r.ScoreImprovement,
            BiasIssues:           biasIssues.Select(b => new BiasIssueDto(
                                      b.Text, b.Reason, b.Suggestion, b.Severity)).ToArray(),
            MissingFields:        missing,
            Improvements:         improvements.Select(i => new ImprovementDto(
                                      i.Category, i.Description, i.Before, i.After)).ToArray(),
            SuggestedSkills:      skills,
            IsAccepted:           r.IsAccepted,
            AcceptedAt:           r.AcceptedAt,
            CreatedAt:            r.CreatedAt);
    }

    private static T? SafeDeserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, _opts); }
        catch { return default; }
    }
}
