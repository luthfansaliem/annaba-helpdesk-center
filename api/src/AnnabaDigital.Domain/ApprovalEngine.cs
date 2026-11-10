using System.Text.Json;
using System.Text.Json.Serialization;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

/// <summary>One step of an approval policy: who may decide, and whether one of them is enough or all must agree.</summary>
public sealed record ApprovalStepDef(string Name, string Rule, Guid[] UserIds, Guid[] RoleIds, bool RequesterManager = false);

public enum StepOutcome { Pending, Approved, Rejected }

/// <summary>
/// The approval rules (APR-001/003), pure: what a step's decisions add up to, how a policy is checked, and how it is stored. Anyone's rejection ends the request;
/// an <c>any</c> step is settled by the first approval; an <c>all</c> step needs every approver still standing (skipped or cancelled rows don't count).
/// </summary>
public static class ApprovalEngine
{
    public const string Any = "any", All = "all";
    public const int MaxSteps = 5, MaxPeoplePerStep = 20, MaxRolesPerStep = 5;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string Serialize(IReadOnlyList<ApprovalStepDef> steps) => JsonSerializer.Serialize(steps, Json);
    public static IReadOnlyList<ApprovalStepDef> Deserialize(string json) => JsonSerializer.Deserialize<List<ApprovalStepDef>>(json, Json) ?? [];

    /// <summary>Errors keyed like <c>steps[1].userIds</c>; empty when the policy is sound.</summary>
    public static Dictionary<string, string> Validate(IReadOnlyList<ApprovalStepDef>? steps)
    {
        var errors = new Dictionary<string, string>();
        steps ??= [];
        if (steps.Count is 0 or > MaxSteps) { errors["steps"] = $"Use 1–{MaxSteps} steps."; return errors; }
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i]; var p = $"steps[{i}]";
            if (s is null) { errors[p] = "Empty step."; continue; }
            if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Trim().Length > 60) errors[$"{p}.name"] = "Name the step in 1–60 characters.";
            if (s.Rule is not (Any or All)) errors[$"{p}.rule"] = "Choose whether any one or all of them must approve.";
            var users = s.UserIds ?? []; var roles = s.RoleIds ?? [];
            if (users.Length + roles.Length == 0 && !s.RequesterManager) errors[$"{p}.userIds"] = "Name at least one person or role, or choose the requester's manager.";
            else if (users.Length > MaxPeoplePerStep || roles.Length > MaxRolesPerStep) errors[$"{p}.userIds"] = $"Use at most {MaxPeoplePerStep} people and {MaxRolesPerStep} roles per step.";
            else if (users.Distinct().Count() != users.Length || roles.Distinct().Count() != roles.Length || users.Contains(Guid.Empty) || roles.Contains(Guid.Empty)) errors[$"{p}.userIds"] = "Each person and role can be named once.";
        }
        return errors;
    }

    /// <summary>What a step's approvals add up to. Rejected beats everything; an empty step is never approved.</summary>
    public static StepOutcome Evaluate(string rule, IReadOnlyCollection<ApprovalStatus> statuses)
    {
        if (statuses.Contains(ApprovalStatus.Rejected)) return StepOutcome.Rejected;
        var counting = statuses.Where(s => s is ApprovalStatus.Pending or ApprovalStatus.Approved or ApprovalStatus.Waiting).ToList();
        if (rule == Any) return counting.Contains(ApprovalStatus.Approved) ? StepOutcome.Approved : StepOutcome.Pending;
        return counting.Count > 0 && counting.All(s => s == ApprovalStatus.Approved) ? StepOutcome.Approved : StepOutcome.Pending;
    }
}
