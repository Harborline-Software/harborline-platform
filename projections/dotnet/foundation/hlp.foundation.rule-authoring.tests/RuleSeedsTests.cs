using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>The .NET twin of the TS <c>seeds.test.ts</c> + the id/dispatch cases of
/// <c>model.test.ts</c> (the TS <c>makeLocalId</c> spy tests become deterministic shape checks —
/// .NET has no Math.random seam to pin).</summary>
public sealed class RuleSeedsTests
{
    [Fact]
    public void BlankTableDraftCreatesOneAuthorableNumericAmountColumnAndUnresolvedDefaultRow()
    {
        var draft = RuleSeeds.BlankTableDraft();
        var column = draft.Columns[0];

        Assert.Equal(RuleScope.Field, draft.Scope);
        Assert.Equal("outcome", draft.ScopeTarget);
        Assert.Equal(RuleActionKind.Compute, draft.OutputType);
        Assert.Equal(HitPolicy.FirstMatch, draft.HitPolicy);
        Assert.Equal(new NoMatchPosture.Default(""), draft.NoMatch);
        Assert.Equal("amount", column.Input);
        Assert.Equal(ColumnValueType.Number, column.ValueType);

        var row = Assert.Single(draft.Rows);
        Assert.Equal("", row.Output);
        Assert.Equal(0, row.Priority);
        var cell = Assert.Single(row.Cells);
        Assert.Equal(column.Id, cell.Key);
        Assert.Equal(new TableCell.Range("0", ""), cell.Value);
    }

    [Fact]
    public void BlankFormulaDraftCreatesAuthorableFormulaWithNoInputsOrExpression()
    {
        var draft = RuleSeeds.BlankFormulaDraft();
        Assert.Equal(RuleScope.Field, draft.Scope);
        Assert.Equal("outcome", draft.ScopeTarget);
        Assert.Equal(RuleActionKind.Compute, draft.OutputType);
        Assert.Empty(draft.Inputs);
        Assert.Null(draft.Expression);
    }

    [Fact]
    public void BlankDraftForDispatchesEachTypedSkinToItsBlankSeed()
    {
        Assert.IsType<DecisionTableDraft>(RuleSeeds.BlankDraftFor(RuleSkinType.Table));
        Assert.IsType<FormulaDraft>(RuleSeeds.BlankDraftFor(RuleSkinType.Formula));
    }

    [Fact]
    public void BlankDraftForCharacterizesRuntimeSkinOutsideTypedPair()
    {
        // characterizes current behavior — undocumented (the TS twin lands on the formula seed)
        Assert.IsType<FormulaDraft>(RuleSeeds.BlankDraftFor((RuleSkinType)999));
        Assert.IsType<FormulaDraft>(RuleSeeds.BlankDraftFor(RuleSkinType.Condition));
    }

    [Fact]
    public void MakeLocalIdUsesSuppliedPrefixAndBase36Suffix()
    {
        string id = RuleSeeds.MakeLocalId("column");
        Assert.StartsWith("column-", id, StringComparison.Ordinal);
        string suffix = id["column-".Length..];
        Assert.Equal(7, suffix.Length);
        Assert.All(suffix, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'z'));
    }

    [Fact]
    public void MakeLocalIdRetainsEmptyTypedPrefix()
    {
        string id = RuleSeeds.MakeLocalId("");
        Assert.StartsWith("-", id, StringComparison.Ordinal);
        Assert.Equal(8, id.Length);
    }
}
