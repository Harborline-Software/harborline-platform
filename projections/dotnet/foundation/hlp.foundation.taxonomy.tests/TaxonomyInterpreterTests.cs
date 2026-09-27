using System.Text.Json;
using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyInterpreterTests
{
    [Fact] public void Resolves_pinned_definition_or_returns_typed_not_found()
    {
        var interpreter = Interpreter();
        Assert.IsType<ResolvedTaxonomyDefinition>(interpreter.ResolveDefinition(Coordinates));
        Assert.IsType<TaxonomyDefinitionNotFound>(interpreter.ResolveDefinition(Coordinates with { Version = "9.0.0" }));
    }

    [Fact] public void Resolves_classification_and_returns_null_for_unknown_code()
    {
        var definition = Definition(); var interpreter = Interpreter(definition);
        Assert.True(interpreter.ResolveClassification(definition, Reference("child"))!.IsActive);
        Assert.Null(interpreter.ResolveClassification(definition, Reference("missing")));
    }

    [Fact] public void Resolves_batch_in_input_order()
    {
        var definition = Definition(); var results = Interpreter(definition).ResolveClassifications(definition, [Reference("child"), Reference("missing"), Reference("root")]);
        Assert.Collection(results, first => Assert.Equal("child", first!.Node.Code), second => Assert.Null(second), third => Assert.Equal("root", third!.Node.Code));
    }

    [Fact] public void Tombstoned_classification_resolves_as_inactive()
    {
        var definition = Definition(); var result = Interpreter(definition).ResolveClassification(definition, Reference("retired"));
        Assert.NotNull(result); Assert.False(result.IsActive);
    }

    [Fact] public void Returns_direct_parent_chain_from_node_to_root()
    {
        var definition = Definition(); Assert.Equal(["child", "root"], Interpreter(definition).GetAncestors(definition, "leaf").Select(node => node.Code));
    }

    [Fact] public void Returns_descendants_in_definition_ordered_pre_order()
    {
        var definition = Definition(); Assert.Equal(["child", "leaf", "retired"], Interpreter(definition).GetDescendants(definition, "root").Select(node => node.Code));
    }

    [Fact] public void Subsumption_uses_identity_and_direct_parent_edges()
    {
        var definition = Definition(); var interpreter = Interpreter(definition);
        Assert.True(interpreter.Subsumes(definition, "root", "leaf"));
        Assert.True(interpreter.Subsumes(definition, "child", "child"));
        Assert.False(interpreter.Subsumes(definition, "leaf", "child"));
    }

    [Fact] public void Traversals_refuse_parent_cycles_and_parent_depth_beyond_the_admission_bound()
    {
        var cycle = Definition([Node("a", parent: "b"), Node("b", parent: "a")]);
        var cycleException = Assert.Throws<TaxonomyTraversalException>(() => Interpreter(cycle).GetAncestors(cycle, "a"));
        Assert.Equal("taxonomy.parent_cycle", cycleException.Code);
        var tooDeep = Definition(Enumerable.Range(0, TaxonomyDefinitionAdmission.MaximumParentTraversalDepth + 2).Select(index => Node($"n{index}", index == 0 ? null : $"n{index - 1}")).ToArray());
        Assert.Equal("taxonomy.traversal_depth_exceeded", Assert.Throws<TaxonomyTraversalException>(() => Interpreter(tooDeep).Subsumes(tooDeep, "n0", "n65")).Code);
    }

    [Fact] public void Succession_reaches_an_active_node()
    {
        var definition = Definition(); var result = Interpreter(definition).ResolveSuccession(definition, "retired");
        Assert.Equal("child", Assert.IsType<ResolvedTaxonomySuccession>(result).Node.Code);
    }

    [Fact] public void Succession_returns_typed_refusal_for_cycle_or_unknown_target()
    {
        var cycle = Definition([Node("a", status: TaxonomyNodeStatus.Tombstoned, successor: "b"), Node("b", status: TaxonomyNodeStatus.Tombstoned, successor: "a")]);
        Assert.Equal("successor_cycle", Assert.IsType<TaxonomySuccessionRefusal>(Interpreter(cycle).ResolveSuccession(cycle, "a")).Reason);
        var unknown = Definition([Node("a", status: TaxonomyNodeStatus.Tombstoned, successor: "missing")]);
        Assert.Equal("successor_unknown", Assert.IsType<TaxonomySuccessionRefusal>(Interpreter(unknown).ResolveSuccession(unknown, "a")).Reason);
    }

    [Fact] public void Expands_the_whole_scheme_including_tombstoned_nodes()
    {
        var definition = Definition(); Assert.Equal(["root", "child", "leaf", "retired"], Interpreter(definition).ExpandWholeScheme(definition));
    }

    [Fact] public void Diff_reports_added_changed_and_newly_tombstoned_nodes()
    {
        var before = Definition([Node("root"), Node("a", display: "A", description: "old"), Node("b", parent: "root", successor: "root")]);
        var after = Definition([Node("root"), Node("a", display: "A2", description: "new", status: TaxonomyNodeStatus.Tombstoned), Node("b", parent: "a", successor: "added"), Node("added")]);
        var diff = Interpreter(before).Diff(before, after);
        Assert.Equal(["added"], diff.AddedNodes.Select(node => node.Code));
        var changes = diff.ChangedNodes.ToDictionary(change => change.Code);
        Assert.Equal(TaxonomyNodeChangeFields.Display | TaxonomyNodeChangeFields.Description | TaxonomyNodeChangeFields.Status, changes["a"].Fields);
        Assert.Equal(TaxonomyNodeChangeFields.ParentCode | TaxonomyNodeChangeFields.SuccessorCode, changes["b"].Fields);
        Assert.Equal(["a"], diff.TombstonedNodes.Select(node => node.Code));
    }

    private static readonly TaxonomyDefinitionId Id = new("acme", "health", "icd");
    private static readonly TaxonomyDefinitionCoordinates Coordinates = new("tenant-a", Id, "1.0.0");
    private static TaxonomyClassificationReference Reference(string code) => new("tenant-a", Id, "1.0.0", code);
    private static TaxonomyInterpreter Interpreter(params TaxonomyDefinition[] definitions) => new(definitions.Length == 0 ? [Definition()] : definitions);
    private static TaxonomyDefinition Definition(IReadOnlyList<TaxonomyNode>? nodes = null) => new("tenant-a", Id, "1.0.0", TaxonomyGovernanceRegime.Civilian, "author", nodes ?? [Node("root"), Node("child", "root"), Node("leaf", "child"), Node("retired", "root", TaxonomyNodeStatus.Tombstoned, "child")], Envelope: new(Id.ToString(), "1.0.0", "tenant-a", TaxonomyCascadeLayer.Tenant, JsonElement.Parse("{}"), []));
    private static TaxonomyNode Node(string code, string? parent = null, TaxonomyNodeStatus status = TaxonomyNodeStatus.Active, string? successor = null, string? display = null, string? description = null) => new(code, display ?? $"Display {code}", description ?? $"Description {code}", status, [], ParentCode: parent, SuccessorCode: successor, DeprecationReason: status == TaxonomyNodeStatus.Tombstoned ? "retired" : null);
}
