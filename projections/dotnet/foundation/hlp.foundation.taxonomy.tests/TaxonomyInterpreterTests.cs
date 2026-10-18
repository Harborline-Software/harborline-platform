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

    [Fact] public void A_null_or_blank_code_is_an_unknown_classification_not_a_thrown_exception()
    {
        var definition = Definition(); var interpreter = Interpreter(definition);
        Assert.Null(interpreter.ResolveClassification(definition, Reference(null!)));
        Assert.Null(interpreter.ResolveClassification(definition, Reference("")));
        Assert.Equal([null], interpreter.ResolveClassifications(definition, [Reference(null!)]));
    }

    [Theory]
    [InlineData("null-node")]
    [InlineData("null-code")]
    [InlineData("empty-code")]
    [InlineData("duplicate-code")]
    public void Malformed_node_inventories_refuse_with_the_typed_integrity_code(string scenario)
    {
        var root = Node("root");
        var nodes = scenario switch
        {
            "null-node" => new TaxonomyNode[] { root, null! },
            "null-code" => new[] { root, Node(null!) },
            "empty-code" => new[] { root, Node("") },
            "duplicate-code" => new[] { root, Node("root", display: "Another root") },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var definition = Definition(nodes);

        var refusal = Assert.Throws<TaxonomyTraversalException>(() =>
            Interpreter(definition).ResolveClassification(definition, Reference("root")));

        Assert.Equal("taxonomy.definition_malformed", refusal.Code);
    }

    [Theory]
    [InlineData("root")]
    [InlineData(" ")]
    public void Unique_nonempty_node_codes_remain_resolvable_including_opaque_whitespace(string code)
    {
        var node = Node(code);
        var definition = Definition([node]);

        var result = Assert.IsType<ResolvedTaxonomyClassification>(
            Interpreter(definition).ResolveClassification(definition, Reference(code)));

        Assert.Same(node, result.Node);
        Assert.Equal(code, result.Node.Code);
        Assert.True(result.IsActive);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("definition")]
    [InlineData("version")]
    public void Classification_references_must_pin_the_supplied_definition(string coordinate)
    {
        var node = Node("root");
        var definition = Definition([node]);
        var interpreter = Interpreter(definition);
        var matching = Reference("root");
        var mismatched = coordinate switch
        {
            "tenant" => matching with { Tenant = "tenant-b" },
            "definition" => matching with { DefinitionId = new("different-vendor", "different-domain", "different-scheme") },
            "version" => matching with { Version = "9.0.0" },
            _ => throw new ArgumentOutOfRangeException(nameof(coordinate)),
        };

        Assert.Same(node, Assert.IsType<ResolvedTaxonomyClassification>(
            interpreter.ResolveClassification(definition, matching)).Node);
        var refusal = Assert.Throws<ArgumentException>(() =>
            interpreter.ResolveClassification(definition, mismatched));

        Assert.Equal("reference", refusal.ParamName);
    }

    [Fact]
    public void Batch_null_references_preserve_positions_among_real_results()
    {
        var definition = Definition();

        var results = Interpreter(definition).ResolveClassifications(definition,
            [null!, Reference("child"), Reference("missing"), Reference("root")]);

        Assert.Collection(results,
            first => Assert.Null(first),
            second => Assert.Equal("child", Assert.IsType<ResolvedTaxonomyClassification>(second).Node.Code),
            third => Assert.Null(third),
            fourth => Assert.Equal("root", Assert.IsType<ResolvedTaxonomyClassification>(fourth).Node.Code));
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

    [Theory]
    [InlineData(0, "n0")]
    [InlineData(1, "n1")]
    [InlineData(2, "n2")]
    [InlineData(64, "n64")]
    public void Succession_reports_the_number_of_followed_edges(int expectedHops, string expectedActiveCode)
    {
        // Raw graph fixtures exercise defensive traversal without relaxing admission of tombstoned successors.
        var nodes = Enumerable.Range(0, expectedHops)
            .Select(index => Node($"n{index}", status: TaxonomyNodeStatus.Tombstoned, successor: $"n{index + 1}"))
            .Append(Node(expectedActiveCode))
            .ToArray();
        var definition = Definition(nodes);

        var result = Assert.IsType<ResolvedTaxonomySuccession>(Interpreter(definition).ResolveSuccession(definition, "n0"));

        Assert.Equal(expectedActiveCode, result.Node.Code);
        Assert.Same(nodes[^1], result.Node);
        Assert.Equal(expectedHops, result.HopCount);
    }

    [Fact]
    public void Successful_succession_retains_the_one_node_constructor_and_deconstruction()
    {
        var node = Node("active");
        var result = new ResolvedTaxonomySuccession(node);

        result.Deconstruct(out var deconstructedNode);

        Assert.Same(node, result.Node);
        Assert.Same(node, deconstructedNode);
        Assert.Equal(0, result.HopCount);
    }

    [Fact]
    public void Succession_refuses_a_sixty_fifth_edge_with_the_existing_refusal_data()
    {
        var nodes = Enumerable.Range(0, 65)
            .Select(index => Node($"n{index}", status: TaxonomyNodeStatus.Tombstoned, successor: $"n{index + 1}"))
            .Append(Node("n65"))
            .ToArray();
        var definition = Definition(nodes);

        var refusal = Assert.IsType<TaxonomySuccessionRefusal>(Interpreter(definition).ResolveSuccession(definition, "n0"));

        Assert.Equal("n0", refusal.Code);
        Assert.Equal("successor_depth_exceeded", refusal.Reason);
    }

    [Theory]
    [InlineData("unknown-start", "missing", "missing", "classification_unknown")]
    [InlineData("missing-successor", "a", "a", "successor_missing")]
    [InlineData("unknown-successor", "a", "a", "successor_unknown")]
    [InlineData("cycle", "a", "a", "successor_cycle")]
    public void Succession_preserves_the_existing_typed_refusal_data(
        string scenario, string startCode, string expectedCode, string expectedReason)
    {
        var nodes = scenario switch
        {
            "unknown-start" => new[] { Node("active") },
            "missing-successor" => new[] { Node("a", status: TaxonomyNodeStatus.Tombstoned) },
            "unknown-successor" => new[] { Node("a", status: TaxonomyNodeStatus.Tombstoned, successor: "missing") },
            "cycle" => new[]
            {
                Node("a", status: TaxonomyNodeStatus.Tombstoned, successor: "b"),
                Node("b", status: TaxonomyNodeStatus.Tombstoned, successor: "a"),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var definition = Definition(nodes);

        var refusal = Assert.IsType<TaxonomySuccessionRefusal>(Interpreter(definition).ResolveSuccession(definition, startCode));

        Assert.Equal(expectedCode, refusal.Code);
        Assert.Equal(expectedReason, refusal.Reason);
    }

    [Theory]
    [InlineData("unknown-start", "missing", "classification_unknown", null)]
    [InlineData("direct-missing", "a", "successor_missing", "a")]
    [InlineData("transitive-missing", "a", "successor_missing", "c")]
    [InlineData("unknown-successor", "a", "successor_unknown", "b")]
    [InlineData("prefixed-cycle", "a", "successor_cycle", "b")]
    public void Succession_refusals_name_the_last_resolved_hop(
        string scenario, string startCode, string expectedReason, string? expectedLastHopCode)
    {
        // Defensive raw graphs do not relax admission of tombstoned or unknown successors.
        var nodes = scenario switch
        {
            "unknown-start" => new[] { Node("active") },
            "direct-missing" => new[] { Node("a", status: TaxonomyNodeStatus.Tombstoned) },
            "transitive-missing" => new[]
            {
                Node("a", status: TaxonomyNodeStatus.Tombstoned, successor: "b"),
                Node("b", status: TaxonomyNodeStatus.Tombstoned, successor: "c"),
                Node("c", status: TaxonomyNodeStatus.Tombstoned),
            },
            "unknown-successor" => new[]
            {
                Node("a", status: TaxonomyNodeStatus.Tombstoned, successor: "b"),
                Node("b", status: TaxonomyNodeStatus.Tombstoned, successor: "missing"),
            },
            "prefixed-cycle" => new[]
            {
                Node("a", status: TaxonomyNodeStatus.Tombstoned, successor: "b"),
                Node("b", status: TaxonomyNodeStatus.Tombstoned, successor: "c"),
                Node("c", status: TaxonomyNodeStatus.Tombstoned, successor: "b"),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var definition = Definition(nodes);

        var refusal = Assert.IsType<TaxonomySuccessionRefusal>(
            Interpreter(definition).ResolveSuccession(definition, startCode));

        Assert.Equal(startCode, refusal.Code);
        Assert.Equal(expectedReason, refusal.Reason);
        Assert.Equal(expectedLastHopCode, refusal.LastHopCode);
    }

    [Theory]
    [InlineData("sixty-fifth-edge", "successor_depth_exceeded", "n64")]
    [InlineData("missing-before-depth", "successor_missing", "n64")]
    [InlineData("depth-before-lookup", "successor_depth_exceeded", "n64")]
    [InlineData("cycle-before-depth", "successor_cycle", "n0")]
    public void Succession_last_hop_preserves_the_existing_boundary_guard_order(
        string scenario, string expectedReason, string expectedLastHopCode)
    {
        var nodes = scenario switch
        {
            "sixty-fifth-edge" => Enumerable.Range(0, 65)
                .Select(index => Node($"n{index}", status: TaxonomyNodeStatus.Tombstoned, successor: $"n{index + 1}"))
                .Append(Node("n65"))
                .ToArray(),
            "missing-before-depth" => Enumerable.Range(0, 64)
                .Select(index => Node($"n{index}", status: TaxonomyNodeStatus.Tombstoned, successor: $"n{index + 1}"))
                .Append(Node("n64", status: TaxonomyNodeStatus.Tombstoned))
                .ToArray(),
            "depth-before-lookup" => Enumerable.Range(0, 64)
                .Select(index => Node($"n{index}", status: TaxonomyNodeStatus.Tombstoned, successor: $"n{index + 1}"))
                .Append(Node("n64", status: TaxonomyNodeStatus.Tombstoned, successor: "missing"))
                .ToArray(),
            "cycle-before-depth" => Enumerable.Range(0, 64)
                .Select(index => Node($"n{index}", status: TaxonomyNodeStatus.Tombstoned,
                    successor: index == 63 ? "n0" : $"n{index + 1}"))
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var definition = Definition(nodes);

        var refusal = Assert.IsType<TaxonomySuccessionRefusal>(
            Interpreter(definition).ResolveSuccession(definition, "n0"));

        Assert.Equal("n0", refusal.Code);
        Assert.Equal(expectedReason, refusal.Reason);
        Assert.Equal(expectedLastHopCode, refusal.LastHopCode);
    }

    [Fact]
    public void Succession_refusal_retains_the_two_argument_constructor_and_deconstruction()
    {
        var refusal = new TaxonomySuccessionRefusal("request", "reason");

        refusal.Deconstruct(out var code, out var reason);

        Assert.Equal("request", refusal.Code);
        Assert.Equal("reason", refusal.Reason);
        Assert.Equal("request", code);
        Assert.Equal("reason", reason);
        Assert.Null(refusal.LastHopCode);
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

    [Fact(DisplayName = "taxonomy-eng-11, taxonomy-run-5: a named collection expands its declared concepts at the pinned scheme version")]
    public void Expands_a_named_collection_at_the_pinned_scheme_version()
    {
        var definition = Definition() with { Collections = [new("active-branch", ["child", "leaf"])] };

        var expansion = Assert.IsType<ResolvedTaxonomyCollectionExpansion>(Interpreter(definition).ExpandCollection(definition, "active-branch"));

        Assert.Equal(["child", "leaf"], expansion.Codes);
    }

    [Fact(DisplayName = "taxonomy-eng-11: an unknown named collection returns a typed refusal instead of silently expanding no values")]
    public void Expanding_an_unknown_named_collection_returns_a_typed_refusal()
    {
        var definition = Definition() with { Collections = [new("active-branch", ["child"])] };

        var refusal = Assert.IsType<TaxonomyCollectionExpansionNotFound>(Interpreter(definition).ExpandCollection(definition, "unknown-branch"));

        Assert.Equal("unknown-branch", refusal.CollectionName);
    }

    [Fact(DisplayName = "DES-0024 taxonomy-eng-12: an overlay is re-expanded against the vendor's next release")]
    public void Expands_overlay_by_reference_against_the_requested_vendor_version()
    {
        var vendorId = new TaxonomyDefinitionId("vendor", "health", "scheme");
        var overlayId = new TaxonomyDefinitionId("tenant", "health", "scheme-overlay");
        var vendorV1 = Definition("vendor-registry", vendorId, "1.0.0", [Node("root")]);
        var overlay = Definition("tenant-a", overlayId, "1.0.0", [Node("tenant-extra", "root")], Overlay: new(vendorId, "1.0.0"));

        Assert.Equal(["root", "tenant-extra"], Assert.IsType<ResolvedTaxonomyOverlayExpansion>(new TaxonomyInterpreter([vendorV1, overlay]).ExpandOverlay(new("tenant-a", overlayId, "1.0.0"), "1.0.0")).Codes);

        var vendorV11 = Definition("vendor-registry", vendorId, "1.1.0", [Node("root"), Node("added", "root")]);
        Assert.Contains("added", Assert.IsType<ResolvedTaxonomyOverlayExpansion>(new TaxonomyInterpreter([vendorV1, vendorV11, overlay]).ExpandOverlay(new("tenant-a", overlayId, "1.0.0"), "1.1.0")).Codes);
    }

    [Fact(DisplayName = "eng-12: expanding an overlay whose vendor version is not installed is a named refusal, never an empty expansion")]
    public void Expands_overlay_refuses_by_name_when_the_vendor_version_is_not_installed()
    {
        var vendorId = new TaxonomyDefinitionId("vendor", "health", "scheme");
        var overlayId = new TaxonomyDefinitionId("tenant", "health", "scheme-overlay");
        var vendorV1 = Definition("vendor-registry", vendorId, "1.0.0", [Node("root")]);
        var overlay = Definition("tenant-a", overlayId, "1.0.0", [Node("tenant-extra", "root")], Overlay: new(vendorId, "1.0.0"));
        var interpreter = new TaxonomyInterpreter([vendorV1, overlay]);

        var vendorMissing = Assert.IsType<TaxonomyOverlayExpansionNotFound>(interpreter.ExpandOverlay(new("tenant-a", overlayId, "1.0.0"), "9.9.9"));
        Assert.Equal("9.9.9", vendorMissing.Coordinates.Version);

        var overlayMissing = Assert.IsType<TaxonomyOverlayExpansionNotFound>(interpreter.ExpandOverlay(new("tenant-a", overlayId, "9.9.9"), "1.0.0"));
        Assert.Equal("9.9.9", overlayMissing.Coordinates.Version);
    }

    [Fact(DisplayName = "taxonomy-auth-22: expanding an overlay that copies a vendor node is a named refusal")]
    public void Expand_refuses_overlay_node_that_copies_a_vendor_node()
    {
        var vendorId = new TaxonomyDefinitionId("vendor", "health", "scheme");
        var overlayId = new TaxonomyDefinitionId("tenant", "health", "scheme-overlay");
        var vendor = Definition("vendor-registry", vendorId, "1.0.0", [Node("shared")]);
        var overlay = Definition("tenant-a", overlayId, "1.0.0", [Node("shared")], Overlay: new(vendorId, "1.0.0"));

        var refusal = Assert.IsType<TaxonomyOverlayExpansionCopiesVendorNode>(new TaxonomyInterpreter([vendor, overlay]).ExpandOverlay(new("tenant-a", overlayId, "1.0.0"), "1.0.0"));
        Assert.Equal("shared", refusal.Code);
    }

    [Fact(DisplayName = "taxonomy-auth-10: expanding an overlay whose designation names an unknown vendor node is a named refusal")]
    public void Expand_refuses_designation_naming_an_unknown_vendor_node()
    {
        var vendorId = new TaxonomyDefinitionId("vendor", "health", "scheme");
        var overlayId = new TaxonomyDefinitionId("tenant", "health", "scheme-overlay");
        var vendor = Definition("vendor-registry", vendorId, "1.0.0", [Node("root")]);
        var overlay = Definition("tenant-a", overlayId, "1.0.0", [Node("tenant-extra", "root")], Overlay: new(vendorId, "1.0.0")) with
        {
            OverlayDesignations = [new("missing-vendor-code", "Renamed", null)],
        };

        var refusal = Assert.IsType<TaxonomyOverlayExpansionUnknownDesignation>(new TaxonomyInterpreter([vendor, overlay]).ExpandOverlay(new("tenant-a", overlayId, "1.0.0"), "1.0.0"));
        Assert.Equal("missing-vendor-code", refusal.VendorNodeCode);
    }

    [Fact(DisplayName = "eng-12: an overlay's vendor reference resolving to more than one tenant refuses rather than picking one silently")]
    public void Expand_refuses_ambiguous_vendor_registered_under_two_tenants()
    {
        var vendorId = new TaxonomyDefinitionId("vendor", "health", "scheme");
        var overlayId = new TaxonomyDefinitionId("tenant", "health", "scheme-overlay");
        var vendorUnderTenantA = Definition("tenant-a", vendorId, "1.0.0", [Node("root")]);
        var vendorUnderTenantB = Definition("tenant-b", vendorId, "1.0.0", [Node("root")]);
        var overlay = Definition("tenant-a", overlayId, "1.0.0", [Node("tenant-extra", "root")], Overlay: new(vendorId, "1.0.0"));

        var refusal = Assert.IsType<TaxonomyOverlayExpansionAmbiguousVendor>(new TaxonomyInterpreter([vendorUnderTenantA, vendorUnderTenantB, overlay]).ExpandOverlay(new("tenant-a", overlayId, "1.0.0"), "1.0.0"));
        Assert.Equal(vendorId, refusal.VendorDefinitionId);
        Assert.Equal(["tenant-a", "tenant-b"], refusal.Tenants.OrderBy(tenant => tenant, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "taxonomy-auth-10: a designation's display/description is applied over the vendor node without changing it")]
    public void Resolves_overlay_designation_display_over_the_vendor_nodes_own_value()
    {
        var vendorId = new TaxonomyDefinitionId("vendor", "health", "scheme");
        var overlayId = new TaxonomyDefinitionId("tenant", "health", "scheme-overlay");
        var vendor = Definition("vendor-registry", vendorId, "1.0.0", [Node("root", display: "Vendor label")]);
        var overlay = Definition("tenant-a", overlayId, "1.0.0", [], Overlay: new(vendorId, "1.0.0")) with
        {
            OverlayDesignations = [new("root", "Tenant label", null)],
        };

        var resolved = new TaxonomyInterpreter([vendor, overlay]).ResolveOverlayDesignations(overlay, vendor);
        Assert.Equal("Tenant label", Assert.Single(resolved).Display);
        Assert.Equal("Vendor label", vendor.Nodes[0].Display);
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
    private static TaxonomyDefinition Definition(IReadOnlyList<TaxonomyNode>? nodes = null) => new("tenant-a", Id, "1.0.0", TaxonomyGovernanceRegime.Civilian, "author", nodes ?? [Node("root"), Node("child", "root"), Node("leaf", "child"), Node("retired", "root", TaxonomyNodeStatus.Tombstoned, "child")], Envelope: new(Id.ToString(), "1.0.0", "tenant-a", TaxonomyCascadeLayer.Tenant, JsonElement.Parse("{}"), [], new(1, 0)));
    private static TaxonomyDefinition Definition(string tenant, TaxonomyDefinitionId id, string version, IReadOnlyList<TaxonomyNode> nodes, TaxonomyOverlayReference? Overlay = null) => new(tenant, id, version, TaxonomyGovernanceRegime.Civilian, "author", nodes, Envelope: new(id.ToString(), version, tenant, TaxonomyCascadeLayer.Tenant, JsonElement.Parse("{}"), [], new(1, 0)), Overlay: Overlay);
    private static TaxonomyNode Node(string code, string? parent = null, TaxonomyNodeStatus status = TaxonomyNodeStatus.Active, string? successor = null, string? display = null, string? description = null) => new(code, display ?? $"Display {code}", description ?? $"Description {code}", status, [], ParentCode: parent, SuccessorCode: successor, DeprecationReason: status == TaxonomyNodeStatus.Tombstoned ? "retired" : null);
}
