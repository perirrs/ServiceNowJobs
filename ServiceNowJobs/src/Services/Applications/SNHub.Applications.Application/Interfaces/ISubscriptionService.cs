using SNHub.Applications.Domain.Enums;

namespace SNHub.Applications.Application.Interfaces;

/// <summary>
/// Abstraction over the Subscriptions microservice (built in Step 17).
/// The Applications service calls this to enforce monthly application limits
/// without directly depending on the Subscriptions service database.
///
/// Current implementation: StubSubscriptionService (always returns Free plan).
/// Step 17 implementation: HttpSubscriptionService (calls Subscriptions API via internal HTTP).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Returns the current candidate's subscription plan.</summary>
    Task<CandidatePlan> GetCandidatePlanAsync(Guid candidateId, CancellationToken ct = default);
}
