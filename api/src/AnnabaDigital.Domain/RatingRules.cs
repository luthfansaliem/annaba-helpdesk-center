using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

/// <summary>TKT-009. When a request can be rated, and what counts as satisfied or low.</summary>
public static class RatingRules
{
    public const int MinScore = 1, MaxScore = 5, LowAtOrBelow = 2, SatisfiedFrom = 4, CommentMax = 1000;
    /// <summary>The requester may rate (or change the rating) for this long after the request was resolved.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    /// <summary>The requester of a resolved or closed request, within the window; never planned work (a maintenance visit has no one to satisfy).</summary>
    public static bool CanRate(TicketStatus status, DateTimeOffset? resolvedAt, DateTimeOffset now, bool isRequester, bool planned)
        => isRequester && !planned && status is TicketStatus.Resolved or TicketStatus.Closed && resolvedAt is { } r && now - r <= Window;

    public static bool IsLow(int score) => score <= LowAtOrBelow;
    public static bool IsSatisfied(int score) => score >= SatisfiedFrom;
}
