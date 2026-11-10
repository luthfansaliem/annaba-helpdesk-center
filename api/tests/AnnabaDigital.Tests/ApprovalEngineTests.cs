using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using Xunit;
using St = AnnabaDigital.Domain.Entities.ApprovalStatus;

namespace AnnabaDigital.Tests;

/// <summary>APR-001/003: the approval rules are pure, so what a step's decisions add up to and what makes a policy valid are tested without a database.</summary>
public class ApprovalEngineTests
{
    private static ApprovalStepDef Step(string name = "Manager", string rule = "any", int people = 1, int roles = 0)
        => new(name, rule, [.. Enumerable.Range(0, people).Select(_ => Guid.NewGuid())], [.. Enumerable.Range(0, roles).Select(_ => Guid.NewGuid())]);

    [Fact]
    public void An_any_step_is_settled_by_the_first_approval_and_an_all_step_needs_everyone()
    {
        Assert.Equal(StepOutcome.Pending, ApprovalEngine.Evaluate("any", [St.Pending, St.Pending]));
        Assert.Equal(StepOutcome.Approved, ApprovalEngine.Evaluate("any", [St.Approved, St.Pending]));
        Assert.Equal(StepOutcome.Pending, ApprovalEngine.Evaluate("all", [St.Approved, St.Pending]));
        Assert.Equal(StepOutcome.Approved, ApprovalEngine.Evaluate("all", [St.Approved, St.Approved]));
    }

    [Fact]
    public void Anyones_rejection_ends_the_request_in_either_kind_of_step()
    {
        Assert.Equal(StepOutcome.Rejected, ApprovalEngine.Evaluate("any", [St.Approved, St.Rejected]));
        Assert.Equal(StepOutcome.Rejected, ApprovalEngine.Evaluate("all", [St.Approved, St.Rejected, St.Pending]));
    }

    [Fact]
    public void Skipped_and_cancelled_rows_do_not_count_and_an_empty_step_is_never_approved()
    {
        Assert.Equal(StepOutcome.Approved, ApprovalEngine.Evaluate("all", [St.Approved, St.Skipped, St.Cancelled]));
        Assert.Equal(StepOutcome.Pending, ApprovalEngine.Evaluate("all", []));
        Assert.Equal(StepOutcome.Pending, ApprovalEngine.Evaluate("any", [St.Skipped]));
        Assert.Equal(StepOutcome.Pending, ApprovalEngine.Evaluate("all", [St.Skipped, St.Cancelled]));
    }

    [Fact]
    public void A_sound_policy_has_no_errors_and_survives_storage()
    {
        var steps = new[] { Step("Line manager", "any", 2), Step("Finance", "all", 1, 1) };
        Assert.Empty(ApprovalEngine.Validate(steps));
        var back = ApprovalEngine.Deserialize(ApprovalEngine.Serialize(steps));
        Assert.Equal(2, back.Count); Assert.Equal("all", back[1].Rule); Assert.Equal(steps[0].UserIds, back[0].UserIds);
    }

    [Fact]
    public void A_broken_policy_is_refused_with_errors_that_name_the_step()
    {
        Assert.Contains("steps", ApprovalEngine.Validate([]).Keys);
        Assert.Contains("steps", ApprovalEngine.Validate(Enumerable.Range(0, ApprovalEngine.MaxSteps + 1).Select(_ => Step()).ToArray()).Keys);
        Assert.Contains("steps[0].name", ApprovalEngine.Validate([Step("  ")]).Keys);
        Assert.Contains("steps[0].rule", ApprovalEngine.Validate([Step(rule: "most")]).Keys);
        Assert.Contains("steps[0].userIds", ApprovalEngine.Validate([Step(people: 0)]).Keys);                                              // nobody named
        Assert.Contains("steps[1].userIds", ApprovalEngine.Validate([Step(), Step(people: ApprovalEngine.MaxPeoplePerStep + 1)]).Keys);
        var dup = Guid.NewGuid();
        Assert.Contains("steps[0].userIds", ApprovalEngine.Validate([new ApprovalStepDef("Twice", "any", [dup, dup], [])]).Keys);
        Assert.Contains("steps[0].userIds", ApprovalEngine.Validate([new ApprovalStepDef("Empty id", "any", [Guid.Empty], [])]).Keys);
    }
}
