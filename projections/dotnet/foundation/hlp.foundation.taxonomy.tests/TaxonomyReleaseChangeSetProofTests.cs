using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyReleaseChangeSetProofTests
{
    // Oracle: control 4434ea33 release-change-set-proof acceptance 2–4; interface revision 4.
    [Fact(DisplayName = "taxonomy-ck-20 taxonomy-eng-13 taxonomy-run-6: literal release lists preserve current order")]
    public void Mixed_release_lists_preserve_current_order()
    {
        var diff = new TaxonomyInterpreter([]).Diff(Previous(), Current());
        Assert.Equal(["new-z", "new-a"], diff.AddedNodes.Select(node => node.Code));
        Assert.Equal(["combined", "description", "display", "retire", "reparent", "succeed"], diff.ChangedNodes.Select(change => change.Code));
        Assert.Equal(["combined", "retire"], diff.TombstonedNodes.Select(node => node.Code));
    }

    [Fact(DisplayName = "taxonomy-ck-20 taxonomy-eng-13 taxonomy-run-6: isolated and composite literal masks retain both payloads")]
    public void Changed_fields_and_payloads_match_literal_table()
    {
        var diff = new TaxonomyInterpreter([]).Diff(Previous(), Current());
        Assert.Collection(diff.ChangedNodes,
            change => Check(change, "combined", 31,
                ("C0", "before", "Active", "root", null, null),
                ("C1", "after", "Tombstoned", "branch", "branch", "retired")),
            change => Check(change, "description", 2,
                ("Stable label", "old", "Active", null, null, null),
                ("Stable label", "new", "Active", null, null, null)),
            change => Check(change, "display", 1,
                ("D0", "Stable description", "Active", null, null, null),
                ("D1", "Stable description", "Active", null, null, null)),
            change => Check(change, "retire", 4,
                ("Stable label", "Stable description", "Active", null, null, null),
                ("Stable label", "Stable description", "Tombstoned", null, null, "retired")),
            change => Check(change, "reparent", 8,
                ("Stable label", "Stable description", "Active", "root", null, null),
                ("Stable label", "Stable description", "Active", "branch", null, null)),
            change => Check(change, "succeed", 16,
                ("Stable label", "Stable description", "Tombstoned", null, "root", "retired"),
                ("Stable label", "Stable description", "Tombstoned", null, "branch", "retired")));
    }

    [Fact(DisplayName = "taxonomy-ck-20 taxonomy-eng-13 taxonomy-run-6: identical content across versions has no changes")]
    public void Identical_content_across_versions_has_no_changes()
    {
        var previous = Previous();
        var current = Definition("2.0.0", Previous().Nodes);
        var diff = new TaxonomyInterpreter([]).Diff(previous, current);
        Assert.Empty(diff.AddedNodes);
        Assert.Empty(diff.ChangedNodes);
        Assert.Empty(diff.TombstonedNodes);
    }

    [Theory(DisplayName = "taxonomy-ck-20 taxonomy-eng-13: Diff guards each identity component with matching control")]
    [InlineData("tenant")]
    [InlineData("definition-id")]
    public void Different_identity_throws_with_matching_control(string component)
    {
        var previous = Previous();
        var current = Definition("2.0.0", Previous().Nodes);
        var interpreter = new TaxonomyInterpreter([]);
        var control = interpreter.Diff(previous, current);
        Assert.Empty(control.AddedNodes);
        Assert.Empty(control.ChangedNodes);
        Assert.Empty(control.TombstonedNodes);
        var mismatched = component == "tenant"
            ? current with { Tenant = "tenant-b" }
            : current with { DefinitionId = new("acme", "health", "other") };
        Assert.Throws<ArgumentException>(() => interpreter.Diff(previous, mismatched));
    }

    private static void Check(TaxonomyNodeChange actual, string code, int mask,
        (string Display, string Description, string Status, string? Parent, string? Successor, string? Reason) before,
        (string Display, string Description, string Status, string? Parent, string? Successor, string? Reason) after)
    {
        Assert.Equal(code, actual.Code);
        Assert.Equal(mask, (int)actual.Fields);
        Assert.Equal(code, actual.Previous.Code);
        Assert.Equal(code, actual.Current.Code);
        Assert.Equal(before, (actual.Previous.Display, actual.Previous.Description, actual.Previous.Status.ToString(),
            actual.Previous.ParentCode, actual.Previous.SuccessorCode, actual.Previous.DeprecationReason));
        Assert.Equal(after, (actual.Current.Display, actual.Current.Description, actual.Current.Status.ToString(),
            actual.Current.ParentCode, actual.Current.SuccessorCode, actual.Current.DeprecationReason));
        Assert.Empty(actual.Previous.DisplayHistoryEntries);
        Assert.Empty(actual.Current.DisplayHistoryEntries);
    }

    private static TaxonomyDefinition Previous() => Definition("1.0.0",
    [
        Node("root"), Node("branch", parent: "root"), Node("display", display: "D0"),
        Node("description", description: "old"), Node("retire"), Node("reparent", parent: "root"),
        Node("succeed", status: TaxonomyNodeStatus.Tombstoned, successor: "root", reason: "retired"),
        Node("combined", display: "C0", description: "before", parent: "root"),
        Node("old-retired", status: TaxonomyNodeStatus.Tombstoned, reason: "retired"), Node("unchanged")
    ]);

    private static TaxonomyDefinition Current() => Definition("2.0.0",
    [
        Node("new-z"), Node("root"),
        Node("combined", display: "C1", description: "after", status: TaxonomyNodeStatus.Tombstoned,
            parent: "branch", successor: "branch", reason: "retired"),
        Node("description", description: "new"), Node("branch", parent: "root"), Node("display", display: "D1"),
        Node("retire", status: TaxonomyNodeStatus.Tombstoned, reason: "retired"), Node("reparent", parent: "branch"),
        Node("succeed", status: TaxonomyNodeStatus.Tombstoned, successor: "branch", reason: "retired"),
        Node("old-retired", status: TaxonomyNodeStatus.Tombstoned, reason: "retired"), Node("unchanged"), Node("new-a")
    ]);

    private static TaxonomyDefinition Definition(string version, IReadOnlyList<TaxonomyNode> nodes)
        => new("tenant-a", new("acme", "health", "release"), version, TaxonomyGovernanceRegime.Civilian, "author", nodes);

    private static TaxonomyNode Node(string code, string display = "Stable label", string description = "Stable description",
        TaxonomyNodeStatus status = TaxonomyNodeStatus.Active, string? parent = null, string? successor = null, string? reason = null)
        => new(code, display, description, status, [], ParentCode: parent, SuccessorCode: successor, DeprecationReason: reason);
}
