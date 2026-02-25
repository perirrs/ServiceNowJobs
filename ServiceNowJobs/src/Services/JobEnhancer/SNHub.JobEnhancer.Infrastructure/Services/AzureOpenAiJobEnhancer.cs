using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Application.Interfaces;

namespace SNHub.JobEnhancer.Infrastructure.Services;

public sealed class AzureOpenAiJobEnhancer : IJobDescriptionEnhancer
{
    private readonly ChatClient _chat;
    private readonly ILogger<AzureOpenAiJobEnhancer> _logger;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private const string SystemPrompt = """
        You are an expert technical recruiter and HR consultant specialising in the ServiceNow ecosystem.
        Your job is to analyse and improve job descriptions for ServiceNow roles.

        When given a job title, description, and optional requirements section, you must:

        1. SCORE the original description (0-100) based on:
           - Clarity: Is the role, responsibilities, and impact clearly communicated? (25 pts)
           - Specificity: Are skills, versions, and experience levels specific? (25 pts)
           - Inclusivity: Is it free from biased or exclusionary language? (25 pts)
           - Completeness: Are all key fields present (salary, location, benefits)? (25 pts)

        2. DETECT BIAS in the original text. Look for:
           - Gendered language ("ninja", "rockstar", "aggressive", "he/she")
           - Age bias ("young", "recent graduate", "digital native", "energetic team")
           - Cultural bias ("native English speaker", "Western-educated")
           - Ability bias ("must be able to lift", "fast-paced environment" without context)
           - Class bias (unpaid overtime, "passion over pay")

        3. REWRITE the title, description, and requirements to be:
           - Clear, inclusive, and specific
           - Focused on outcomes and impact, not just tasks
           - Using "you will" and "we offer" framing (candidate-centric)
           - ServiceNow-specific with proper platform terminology
           - Free of all identified bias

        4. IDENTIFY missing fields that would improve the posting:
           Choose from: "Salary range", "Remote/hybrid policy", "ServiceNow version",
           "Required certifications", "Interview process", "Team size", "Tech stack",
           "On-call requirements", "Visa sponsorship", "Benefits", "Start date"

        5. SUGGEST additional ServiceNow skills that match the role but aren't mentioned.

        Return ONLY a valid JSON object with no markdown, no explanation, no preamble:
        {
          "enhancedTitle": "string",
          "enhancedDescription": "string",
          "enhancedRequirements": "string or null",
          "scoreBefore": 0-100,
          "scoreAfter": 0-100,
          "biasIssues": [
            {
              "text": "exact phrase from original",
              "reason": "why this is biased",
              "suggestion": "inclusive alternative",
              "severity": "Low|Medium|High"
            }
          ],
          "missingFields": ["array of missing field names"],
          "improvements": [
            {
              "category": "Clarity|Specificity|Inclusivity|Structure|SEO",
              "description": "what was changed and why",
              "before": "original fragment",
              "after": "improved fragment"
            }
          ],
          "suggestedSkills": ["array of relevant ServiceNow skills not in original"]
        }
        """;

    public AzureOpenAiJobEnhancer(
        AzureOpenAIClient client,
        IConfiguration config,
        ILogger<AzureOpenAiJobEnhancer> logger)
    {
        var deployment = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
        _chat   = client.GetChatClient(deployment);
        _logger = logger;
    }

    public async Task<GptEnhancementResult> EnhanceAsync(
        string title, string description, string? requirements,
        CancellationToken ct = default)
    {
        var userContent = $"""
            Job Title: {title}

            Description:
            {description}

            {(requirements is not null ? $"Requirements:\n{requirements}" : "(No requirements section provided)")}
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(userContent)
        };

        var options = new ChatCompletionOptions
        {
            Temperature         = 0.3f, // Some creativity for rewrites, not fully deterministic
            MaxOutputTokenCount = 4000
        };

        _logger.LogInformation(
            "Sending job '{Title}' to GPT-4o for enhancement ({Chars} chars)",
            title, description.Length);

        var response = await _chat.CompleteChatAsync(messages, options, ct);
        var json     = response.Value.Content[0].Text.Trim();

        // Strip markdown fences if present
        if (json.StartsWith("```"))
        {
            var lines = json.Split('\n');
            json = string.Join('\n',
                lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
        }

        _logger.LogDebug("GPT-4o enhancement response: {Json}", json);

        return JsonSerializer.Deserialize<GptEnhancementResult>(json, _jsonOpts)
            ?? throw new InvalidOperationException("GPT-4o returned unparseable JSON.");
    }
}
