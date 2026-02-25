namespace SNHub.Applications.Domain.Enums;

/// <summary>
/// Subscription plan tiers for candidates.
/// Monthly application limits are enforced at the Applications service level.
/// The actual plan assignment lives in the Subscriptions service (Step 17).
/// </summary>
public enum CandidatePlan
{
    Free       = 0,  // 5 applications / month
    Lite       = 1,  // 20 applications / month
    Pro        = 2,  // Unlimited
    Enterprise = 3   // Unlimited + priority placement
}

public static class CandidatePlanLimits
{
    /// <summary>
    /// Returns the monthly application limit for a plan.
    /// int.MaxValue means unlimited.
    /// </summary>
    public static int MonthlyApplicationLimit(this CandidatePlan plan) => plan switch
    {
        CandidatePlan.Free       => 5,
        CandidatePlan.Lite       => 20,
        CandidatePlan.Pro        => int.MaxValue,
        CandidatePlan.Enterprise => int.MaxValue,
        _                        => 5
    };

    public static bool IsUnlimited(this CandidatePlan plan) =>
        plan >= CandidatePlan.Pro;
}
