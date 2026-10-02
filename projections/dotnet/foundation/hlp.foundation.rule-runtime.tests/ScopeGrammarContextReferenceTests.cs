using Harborline.Foundation.RuleEngine.Compilation;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// T-1022: the .NET twin of the TS tier's <c>wf-grammar.test.ts</c> cases "does NOT extract wf./timer. as field/row
/// deps" and "does NOT extract candidate. as a field/row dep". Declared context prefixes are external context, not
/// cells, so a lowered <c>wf.</c>, <c>timer.</c> or <c>candidate.</c> var path registers no field or row reference.
/// </summary>
public sealed class ScopeGrammarContextReferenceTests
{
    private static readonly LowerContext SchemaContext = new(RuleScope.Schema, "", null);

    [Fact]
    public void Wf_and_timer_paths_are_not_extracted_as_field_or_row_references()
    {
        var ast = ScopeGrammar.Lower("""
            {"and":[
              {">":[{"var":"field.amount"},5000]},
              {"<":[{"var":"wf.iteration"},3]},
              {">":[{"var":"timer.sla-clock"},0]}
            ]}
            """, SchemaContext, "guard-1");

        // Only the real form field participates in the dependency graph.
        Assert.Equal([new FieldRef("amount")], ScopeGrammar.ExtractRefs(ast, "guard-1"));
    }

    [Theory]
    [InlineData("wf.iteration")]
    [InlineData("timer.sla-clock")]
    [InlineData("candidate.status")]
    public void A_context_path_alone_registers_no_reference(string path)
    {
        var ast = ScopeGrammar.Lower($$"""{"var":"{{path}}"}""", SchemaContext, "g");

        Assert.Empty(ScopeGrammar.ExtractRefs(ast, "g"));
    }
}
