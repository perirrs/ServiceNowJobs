using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Enums;

namespace SNHub.Applications.Infrastructure.Services;

/// <summary>
/// Stub implementation used until the Subscriptions microservice is built (Step 17).
/// Returns Free plan for all candidates — enforces the 5 applications/month limit.
///
/// REPLACE with HttpSubscriptionService in Step 17 by swapping the DI registration
/// in InfrastructureExtensions. No other code changes needed.
/// </summary>
public sealed class StubSubscriptionService : ISubscriptionService
{
    public Task<CandidatePlan> GetCandidatePlanAsync(Guid candidateId, CancellationToken ct = default)
        => Task.FromResult(CandidatePlan.Free);
}
