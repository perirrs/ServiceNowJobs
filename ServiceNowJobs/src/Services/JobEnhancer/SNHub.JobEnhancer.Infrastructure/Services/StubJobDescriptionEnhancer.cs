using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Application.Interfaces;

namespace SNHub.JobEnhancer.Infrastructure.Services;

/// <summary>
/// Deterministic enhancer for integration tests and local development.
/// Returns realistic-looking results without calling Azure OpenAI.
/// Bias issues and improvements are derived from simple keyword scanning
/// so the output varies meaningfully based on input.
/// </summary>
public sealed class StubJobDescriptionEnhancer : IJobDescriptionEnhancer
{
    public Task<GptEnhancementResult> EnhanceAsync(
        string title, string description, string? requirements,
        CancellationToken ct = default)
    {
        var biasIssues = DetectBias(description, requirements);
        var scoreBefore = CalculateScore(description, requirements, biasIssues.Length);
        var scoreAfter  = Math.Min(100, scoreBefore + 18);

        var result = new GptEnhancementResult
        {
            EnhancedTitle = $"{title} — ServiceNow Specialist",
            EnhancedDescription = description
                .Replace("rockstar", "experienced professional")
                .Replace("ninja", "expert")
                .Replace("he/she", "they")
                + "\n\nWe are committed to an inclusive hiring process. " +
                  "All qualified applicants will receive consideration for employment.",
            EnhancedRequirements = requirements is null ? null
                : requirements + "\n\nNice to have: Experience with ServiceNow ATF and Performance Analytics.",
            ScoreBefore     = scoreBefore,
            ScoreAfter      = scoreAfter,
            BiasIssues      = biasIssues,
            MissingFields   = DetectMissingFields(description),
            Improvements    =
            [
                new GptImprovement
                {
                    Category    = "Inclusivity",
                    Description = "Replaced gendered/exclusionary terms with neutral language",
                    Before      = "We need a rockstar developer",
                    After       = "We're looking for an experienced ServiceNow developer"
                },
                new GptImprovement
                {
                    Category    = "Specificity",
                    Description = "Added ServiceNow platform context",
                    Before      = "Experience with the platform",
                    After       = "Experience with ServiceNow ITSM, HRSD, and Flow Designer"
                }
            ],
            SuggestedSkills = ["ATF", "Performance Analytics", "Integration Hub", "Service Portal"]
        };

        return Task.FromResult(result);
    }

    private static GptBiasIssue[] DetectBias(string description, string? requirements)
    {
        var combined = $"{description} {requirements}".ToLower();
        var issues   = new List<GptBiasIssue>();

        if (combined.Contains("rockstar") || combined.Contains("ninja"))
            issues.Add(new GptBiasIssue
            {
                Text       = "rockstar/ninja developer",
                Reason     = "Gendered, exclusionary tech culture jargon that discourages diverse applicants",
                Suggestion = "experienced developer / senior engineer",
                Severity   = "High"
            });

        if (combined.Contains("he/she") || combined.Contains("his/her"))
            issues.Add(new GptBiasIssue
            {
                Text       = "he/she",
                Reason     = "Binary gender assumption; excludes non-binary candidates",
                Suggestion = "they/them",
                Severity   = "Medium"
            });

        if (combined.Contains("young") || combined.Contains("energetic"))
            issues.Add(new GptBiasIssue
            {
                Text       = "young/energetic",
                Reason     = "Age-biased language that may violate employment discrimination law",
                Suggestion = "motivated / enthusiastic",
                Severity   = "High"
            });

        if (combined.Contains("native english"))
            issues.Add(new GptBiasIssue
            {
                Text       = "native English speaker",
                Reason     = "Nationality/origin bias; 'fluent English' is sufficient and inclusive",
                Suggestion = "Strong written and verbal English communication skills",
                Severity   = "High"
            });

        return issues.ToArray();
    }

    private static string[] DetectMissingFields(string description)
    {
        var desc    = description.ToLower();
        var missing = new List<string>();
        if (!desc.Contains("salary") && !desc.Contains("£") && !desc.Contains("$"))
            missing.Add("Salary range");
        if (!desc.Contains("remote") && !desc.Contains("hybrid") && !desc.Contains("on-site"))
            missing.Add("Remote/hybrid policy");
        if (!desc.Contains("certif"))
            missing.Add("Required certifications");
        if (!desc.Contains("interview"))
            missing.Add("Interview process");
        return missing.ToArray();
    }

    private static int CalculateScore(string desc, string? req, int biasCount)
    {
        var score = 60;
        if (desc.Length > 300) score += 10;
        if (req is not null)   score += 5;
        if (desc.ToLower().Contains("servicenow")) score += 5;
        score -= biasCount * 8;
        return Math.Clamp(score, 20, 85);
    }
}
