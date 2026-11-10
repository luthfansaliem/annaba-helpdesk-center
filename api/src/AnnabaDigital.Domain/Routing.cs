using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

/// <summary>What a ticket looks like to a rule: its service, the service's category (and that category's parent), the place and everything above it, and its priority.</summary>
public sealed record MatchFacts(Guid ServiceId, IReadOnlySet<Guid> CategoryChain, IReadOnlySet<Guid> LocationChain, TicketPriority Priority);

/// <summary>The "if" part shared by SLA policies and assignment rules. An empty condition matches anything; all given conditions must hold.</summary>
public readonly record struct Conditions(Guid? ServiceId, Guid? CategoryId, Guid? LocationId, TicketPriority? Priority)
{
    public bool Matches(MatchFacts f)
        => (ServiceId is null || ServiceId == f.ServiceId)
        && (CategoryId is null || f.CategoryChain.Contains(CategoryId.Value))
        && (LocationId is null || f.LocationChain.Contains(LocationId.Value))
        && (Priority is null || Priority == f.Priority);

    /// <summary>How many conditions are set (for showing "matches everything" in the admin screens).</summary>
    public int Count => (ServiceId is null ? 0 : 1) + (CategoryId is null ? 0 : 1) + (LocationId is null ? 0 : 1) + (Priority is null ? 0 : 1);
}

/// <summary>A person a ticket could be handed to, with the facts the strategies use.</summary>
public sealed record Candidate(Guid UserId, int OpenCount, DateTimeOffset? LastAssignedAt);

/// <summary>Pure routing logic: which rule applies, and which person a strategy picks. No database, no clock.</summary>
public static class Routing
{
    /// <summary>First item (in the order given) whose conditions match, or <c>null</c>. Callers pass items already sorted by position.</summary>
    public static T? FirstMatch<T>(IEnumerable<T> ordered, Func<T, Conditions> conditions, MatchFacts facts) where T : class
        => ordered.FirstOrDefault(x => conditions(x).Matches(facts));

    /// <summary>
    /// Round robin = whoever was given a ticket longest ago (never-assigned first; equal times fall to the lighter load); least loaded = fewest open tickets, then longest ago. Remaining ties fall to the lowest id so the
    /// outcome is deterministic. <c>None</c> picks nobody (the ticket waits in the team's queue).
    /// </summary>
    public static Guid? Pick(AssignStrategy strategy, IReadOnlyList<Candidate> candidates)
    {
        if (candidates.Count == 0 || strategy == AssignStrategy.None) return null;
        var ordered = strategy == AssignStrategy.LeastLoaded
            ? candidates.OrderBy(c => c.OpenCount).ThenBy(c => c.LastAssignedAt ?? DateTimeOffset.MinValue).ThenBy(c => c.UserId)
            : candidates.OrderBy(c => c.LastAssignedAt ?? DateTimeOffset.MinValue).ThenBy(c => c.OpenCount).ThenBy(c => c.UserId);
        return ordered.First().UserId;
    }
}
