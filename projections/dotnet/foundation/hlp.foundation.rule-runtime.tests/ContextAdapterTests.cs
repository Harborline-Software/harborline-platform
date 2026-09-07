using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// The ADR 0146 D3 context-adapter seam. Verifies the public contract
/// (<see cref="IContextAdapter"/> / <see cref="IValueResolver"/> / <see cref="RefValue"/> /
/// <see cref="RuleEvalScope"/>) and the two first (migrated) implementations — forms
/// (<see cref="FormContextAdapter"/>) and workflow-guard (<see cref="ContextBagAdapter"/>).
/// Behaviour-neutrality of the wider migration is covered by the existing conformance /
/// unit / guard suites, which now obtain their resolvers through this seam; these tests pin
/// the seam contract itself and that an arbitrary (Wave-2-style) consumer can implement it.
/// </summary>
public sealed class ContextAdapterTests
{
    [Fact]
    public void RuleEvalScope_Root_is_the_default_top_level_scope()
    {
        Assert.Equal(default, RuleEvalScope.Root);
        Assert.Null(RuleEvalScope.Root.RowSection);
        Assert.Null(RuleEvalScope.Root.RowId);
        Assert.Equal(new RuleEvalScope("items", "r1"), new RuleEvalScope("items", "r1"));
    }

    [Fact]
    public void ContextBagAdapter_resolver_reads_field_paths_and_bad_references_row()
    {
        var bag = new Dictionary<string, JsonNode?> { ["a"] = JsonValue.Create(7) };
        var resolver = new ContextBagAdapter(bag).CreateResolver(RuleEvalScope.Root);

        Assert.Equal(7, resolver.ResolveVar("field.a").Value!.GetValue<int>());
        Assert.Equal(7, resolver.ResolveVar("a").Value!.GetValue<int>()); // bare name = field
        Assert.Equal(ValueState.Resolved, resolver.ResolveVar("missing").State);
        Assert.Null(resolver.ResolveVar("missing").Value); // absent = resolved-null (open-world)
        Assert.Equal(ValueState.Error, resolver.ResolveVar("row.x").State); // flat bag has no rows
        Assert.Equal(ValueState.Error, resolver.ResolveAgg("sum", "items", "amount").State);
    }

    [Fact]
    public void FormContextAdapter_resolver_honours_row_scope()
    {
        var instance = new RuleInstance();
        instance.Fields["total"] = JsonValue.Create(5);
        instance.Tables["items"] = new List<RuleRow>
        {
            new("r1", new Dictionary<string, JsonNode?> { ["qty"] = JsonValue.Create(9) }),
        };
        var adapter = new FormContextAdapter(new Dictionary<string, ComputedValue>(), instance);

        // Top-level field resolves at any scope.
        Assert.Equal(5, adapter.CreateResolver(RuleEvalScope.Root).ResolveVar("field.total").Value!.GetValue<int>());

        // A `row.` reference resolves against the scoped row.
        var rowResolver = adapter.CreateResolver(new RuleEvalScope("items", "r1"));
        Assert.Equal(9, rowResolver.ResolveVar("row.qty").Value!.GetValue<int>());

        // A `row.` reference at the top-level (rowless) scope is a bad reference — same as pre-seam.
        Assert.Equal(ValueState.Error, adapter.CreateResolver(RuleEvalScope.Root).ResolveVar("row.qty").State);
    }

    [Fact]
    public void An_arbitrary_consumer_can_implement_the_public_seam()
    {
        // A minimal external pillar adapter written against ONLY the public seam types — the
        // shape Wave 2's document/event/dataset adapters take (own the seam, scatter the impls).
        IContextAdapter adapter = new EchoPillarAdapter(new Dictionary<string, JsonNode?>
        {
            ["name"] = JsonValue.Create("acme"),
        });
        var resolver = adapter.CreateResolver(RuleEvalScope.Root);

        Assert.Equal("acme", resolver.ResolveVar("field.name").Value!.GetValue<string>());
        Assert.Equal(ValueState.Error, resolver.ResolveVar("field.absent").State);
    }

    private sealed class EchoPillarAdapter : IContextAdapter
    {
        private readonly IReadOnlyDictionary<string, JsonNode?> _data;
        public EchoPillarAdapter(IReadOnlyDictionary<string, JsonNode?> data) => _data = data;
        public IValueResolver CreateResolver(RuleEvalScope scope) => new EchoResolver(_data);
    }

    private sealed class EchoResolver : IValueResolver
    {
        private readonly IReadOnlyDictionary<string, JsonNode?> _data;
        public EchoResolver(IReadOnlyDictionary<string, JsonNode?> data) => _data = data;

        public RefValue ResolveVar(string path)
        {
            var name = path.StartsWith("field.", StringComparison.Ordinal) ? path["field.".Length..] : path;
            return _data.TryGetValue(name, out var v)
                ? RefValue.Resolved(v)
                : RefValue.OfError(RuleError.Of("test.absent", "path", path));
        }

        public RefValue ResolveAgg(string fn, string section, string col)
            => RefValue.OfError(RuleError.Of("test.no_agg"));
    }
}
