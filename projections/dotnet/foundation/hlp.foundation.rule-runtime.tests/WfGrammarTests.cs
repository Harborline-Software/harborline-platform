using System.Linq;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Model;


using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// WF-KEY grammar extension (ADR 0140) — the integrity-tier mirror of the
/// @harborline-software/rule-engine `wf-grammar.test.ts`. Proves the net-new `wf.` / `timer.`
/// process-context prefixes lower CANONICALLY (no <c>field.</c> rewrite) and are NOT
/// extracted as record-field dependencies, so a workflow guard reads form data AND
/// process state through ONE lowering — keeping a single addressing grammar across
/// forms + workflows. Additive: form rules are byte-identical (the unchanged cases
/// in <see cref="RuleEngineUnitTests"/> still pass).
/// </summary>
public sealed class WfGrammarTests
{
    private static CompiledRule CompileOne(string expr)
    {
        var rule = RuleDefinitionFactory.Create("g", RuleTier.JsonLogic, RuleScope.Schema, "", expr, RuleActionKind.Validate);
        return RuleCompiler.Compile(new[] { rule }).Rules.Single();
    }

    /// <summary>Reads the var path out of a single-key <c>{"var": ...}</c> node.</summary>
    private static string VarPath(JsonNode? node)
    {
        var v = node!["var"];
        return v is JsonArray a ? a[0]!.GetValue<string>() : v!.GetValue<string>();
    }

    [Theory]
    [InlineData("wf.state")]
    [InlineData("wf.actor")]
    [InlineData("wf.iteration")]
    [InlineData("timer.sla-clock")]
    public void Lowers_process_context_prefix_canonically(string path)
    {
        var compiled = CompileOne($"{{\"var\":\"{path}\"}}");
        Assert.Equal(path, VarPath(compiled.Ast));
    }

    [Fact]
    public void Still_rewrites_bare_and_field_names_to_field_scope()
    {
        Assert.Equal("field.amount", VarPath(CompileOne("{\"var\":\"amount\"}").Ast));
        Assert.Equal("field.amount", VarPath(CompileOne("{\"var\":\"field.amount\"}").Ast));
    }

    [Fact]
    public void Mixed_guard_reads_form_data_and_process_state_through_one_lowering()
    {
        // field.amount > 5000 and wf.iteration < 3 — the design's worked example.
        const string expr =
            "{\"and\":[{\">\":[{\"var\":\"field.amount\"},5000]},{\"<\":[{\"var\":\"wf.iteration\"},3]}]}";
        var compiled = CompileOne(expr);
        var and = compiled.Ast!["and"]!.AsArray();
        Assert.Equal("field.amount", VarPath(and[0]![">"]!.AsArray()[0]));
        Assert.Equal("wf.iteration", VarPath(and[1]!["<"]!.AsArray()[0]));
    }

    [Fact]
    public void Does_not_extract_process_context_as_field_dependencies()
    {
        const string expr =
            "{\"and\":[{\">\":[{\"var\":\"field.amount\"},5000]},{\"<\":[{\"var\":\"wf.iteration\"},3]},{\">\":[{\"var\":\"timer.sla-clock\"},0]}]}";
        var compiled = CompileOne(expr);
        // Only the real form field participates in the dependency graph.
        var fieldRefs = compiled.References.OfType<FieldRef>().Select(r => r.Name).ToArray();
        Assert.Equal(new[] { "amount" }, fieldRefs);
        Assert.DoesNotContain(compiled.References, r => r is FieldRef f && (f.Name.StartsWith("wf.") || f.Name.StartsWith("timer.")));
    }
}
