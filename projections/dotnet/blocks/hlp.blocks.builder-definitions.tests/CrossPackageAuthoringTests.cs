using System.Text.Json.Nodes;
using Harborline.Foundation.RuleAuthoring;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-591 (owner ruling Q12): the AUTHORING-ONLY cross-package reference check. Publication and installation
/// gates belong to T-615; nothing here is evidence for either.
/// </summary>
public sealed class CrossPackageAuthoringTests
{
    private static readonly CrossPackageEndpoint Source = new("pkg.fleet", "rule.overdue", "1.0.0", new string('a', 64));
    private static readonly CrossPackageEndpoint Balanced = new("pkg.ledger", "calc.balanced", "2.1.0", new string('b', 64));
    private static readonly CrossPackageEndpoint Period = new("pkg.ledger", "calc.period-open", "1.0.0", new string('c', 64));
    private static readonly CrossPackageEndpoint Hidden = new("pkg.ledger", "calc.internal", "1.0.0", new string('d', 64));
    private static readonly PackageExposure Ledger = new("pkg.ledger", [Balanced, Period]);

    private static RuleDefinitionEnvelope Consumer(params string[] requires)
        => new("rule.overdue", "1.0.0", "tenant-a", "tenant", [], requires);

    private static CrossPackageReference Reference(string pointer, CrossPackageEndpoint target) => new(pointer, Source, target);

    [Fact(DisplayName = "rules-auth-30 (authoring-only): a reference the consumer requires and the producer exposes at that version is permitted")]
    public void Declared_and_exposed_reference_is_permitted()
    {
        var report = RuleCrossPackageAuthoring.Check(Consumer("pkg.ledger"), [Reference("/draft/calculations/0", Balanced)], [Ledger]);
        Assert.Equal(DefinitionAdmissionPhase.Author, report.Stage);
        Assert.Empty(report.Refusals);
    }

    [Fact(DisplayName = "rules-auth-30 (authoring-only): a reference whose package the consumer envelope does not require is a named refusal")]
    public void Missing_requires_is_refused()
    {
        var refusal = Assert.Single(RuleCrossPackageAuthoring.Check(Consumer(), [Reference("/draft/calculations/0", Balanced)], [Ledger]).Refusals);
        Assert.Equal(new DefinitionRefusal(RuleCrossPackageAuthoring.DependencyUndeclared, "/draft/calculations/0", "pkg.ledger/calc.balanced@2.1.0"), refusal);
    }

    [Fact(DisplayName = "rules-auth-30 (authoring-only): a declared dependency the producer does not expose is a named refusal that does not reveal the target")]
    public void Declared_but_unexposed_is_refused_without_target()
    {
        var refusal = Assert.Single(RuleCrossPackageAuthoring.Check(Consumer("pkg.ledger"), [Reference("/draft/calculations/0", Hidden)], [Ledger]).Refusals);
        Assert.Equal(new DefinitionRefusal(RuleCrossPackageAuthoring.NotExposed, "/draft/calculations/0"), refusal);
        Assert.Null(refusal.Target);
    }

    [Fact(DisplayName = "rules-auth-30 (authoring-only): several refusals share one stage with distinct pointers and targets, the target present only where the producer exposes it; a same-package reference is not an edge")]
    public void Several_refusals_one_stage_distinct_pointers_and_targets()
    {
        var stale = Balanced with { Version = "2.0.0" };
        var report = RuleCrossPackageAuthoring.Check(Consumer("pkg.other"),
            [Reference("/a", Balanced), Reference("/b", Period), Reference("/c", Hidden), Reference("/s", Source with { DefinitionId = "rule.sibling" })],
            [Ledger]);
        Assert.Equal(DefinitionAdmissionPhase.Author, report.Stage);
        Assert.Equal(
            [new DefinitionRefusal(RuleCrossPackageAuthoring.DependencyUndeclared, "/a", "pkg.ledger/calc.balanced@2.1.0"),
             new DefinitionRefusal(RuleCrossPackageAuthoring.DependencyUndeclared, "/b", "pkg.ledger/calc.period-open@1.0.0"),
             new DefinitionRefusal(RuleCrossPackageAuthoring.DependencyUndeclared, "/c"),
             new DefinitionRefusal(RuleCrossPackageAuthoring.NotExposed, "/c")],
            report.Refusals);

        var incompatible = RuleCrossPackageAuthoring.Check(Consumer("pkg.ledger"), [Reference("/x", stale), Reference("/y", Period)], [Ledger]);
        Assert.Equal([new DefinitionRefusal(RuleCrossPackageAuthoring.ExposureIncompatible, "/x", "pkg.ledger/calc.balanced@2.1.0")], incompatible.Refusals);
    }
}
