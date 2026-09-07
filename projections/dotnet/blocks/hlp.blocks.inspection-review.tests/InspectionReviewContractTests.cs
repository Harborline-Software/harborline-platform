using System.Text.Json;
using Harborline.Contracts.Workflow;
using Harborline.Foundation.Forms.Models;
using Xunit;

#pragma warning disable CS1591

namespace Harborline.Blocks.InspectionReview.Tests;

public sealed class InspectionReviewContractTests
{
    [Fact]
    public async Task SharedConditionFixtureMatchesFrozenClassifier()
    {
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures.yaml")));
        Assert.Equal("hlp.blocks.inspection-review", fixture.RootElement.GetProperty("moduleId").GetString());
        var binding = Binding();
        foreach (var row in fixture.RootElement.GetProperty("conditionCases").EnumerateArray())
        {
            var actual = InspectionReviewProcess.Classify(binding, row.GetProperty("score").GetInt32());
            Assert.Equal(row.GetProperty("expected").GetString(), actual.ToString());
        }
    }

    [Fact]
    public void ReviewIdentityIsDeterministicOpaqueAndSubmissionBound()
    {
        var first = InspectionReviewProcess.DeriveReviewId("forminst:one");
        Assert.Equal(first, InspectionReviewProcess.DeriveReviewId("forminst:one"));
        Assert.NotEqual(first, InspectionReviewProcess.DeriveReviewId("forminst:two"));
        Assert.StartsWith("ir1:", first, StringComparison.Ordinal);
        Assert.DoesNotContain("forminst", first, StringComparison.Ordinal);
    }

    [Fact]
    public void FrozenWorkflowIsHumanGatedAndAdmissionGreen()
    {
        var workflow = InspectionReviewProcess.BuildWorkflow(
            "tenant-a",
            Binding(),
            DateTimeOffset.Parse("2026-08-09T00:00:00Z"));
        var admission = WorkflowAdmissionValidator.Validate(
            workflow,
            new ImmutableWorkflowAuthorityResolver(new Dictionary<string, ActionClassification>()));

        Assert.True(admission.IsValid, string.Join(Environment.NewLine, admission.Violations));
        Assert.Equal(InspectionReviewProcess.ReviewState, workflow.InitialState);
        Assert.Equal([InspectionReviewProcess.ApproveOutcome, InspectionReviewProcess.ReworkOutcome],
            workflow.Transitions.Select(row => row.Id).ToArray());
        Assert.All(workflow.Triggers, trigger => Assert.Equal(WorkflowTriggerKind.HumanAction, trigger.Kind));
    }

    [Fact]
    public void BindingFailsClosedForAmbiguousSubjectOrThreshold()
    {
        Assert.Throws<ArgumentException>(() => (Binding() with { ConditionPointer = "condition" }).Validate());
        Assert.Throws<ArgumentException>(() => (Binding() with { SubjectPointer = "/asset" }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Binding() with { ReviewRequiredAtOrBelow = 5 }).Validate());
    }

    private static InspectionReviewBinding Binding() => new()
    {
        FormId = new FormDefinitionId("equipment-inspection.v1"),
        FormVersion = new SemanticVersion(1, 0, 0),
        ConditionPointer = "/conditionRating",
        SubjectSource = InspectionSubjectSource.CaseReference,
        MinimumScore = 1,
        MaximumScore = 5,
        ReviewRequiredAtOrBelow = 2,
    };
}
