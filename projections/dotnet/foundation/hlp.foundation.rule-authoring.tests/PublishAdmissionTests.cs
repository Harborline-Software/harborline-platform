using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>
/// The publish admission fence (design §5.2 / ADR 0146 D7) — the .NET twin of the TS
/// <c>admission.test.ts</c>: compile-first fail-closed rejection with stable <c>rule.skin.*</c>
/// codes, catalog untouched on any rejection, S-8 version minting only after the fence passes.
/// The TS suite proves catalog-untouched with method spies; here the SAME observable is proven
/// one seam lower — a counting/failure-injecting store port (an untouched store is an untouched
/// catalog).
/// </summary>
public sealed class PublishAdmissionTests
{
    /// <summary>A store-port probe: counts reads/writes and injects failures on demand.</summary>
    private sealed class ProbeStore : IRuleCatalogStore
    {
        private readonly InMemoryRuleCatalogStore _inner = new();

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public Exception? FailNextRead { get; set; }

        public Exception? FailNextWrite { get; set; }

        public Task<StoredRule?> ReadAsync(string ruleKey)
        {
            if (FailNextRead is { } e) { FailNextRead = null; throw e; }
            Reads++;
            return _inner.ReadAsync(ruleKey);
        }

        public Task WriteAsync(StoredRule rule)
        {
            if (FailNextWrite is { } e) { FailNextWrite = null; throw e; }
            Writes++;
            return _inner.WriteAsync(rule);
        }

        public Task<IReadOnlyList<StoredRule>> ListAsync() => _inner.ListAsync();
    }

    private readonly ProbeStore _store = new();
    private readonly RuleCatalog _catalog;

    public PublishAdmissionTests()
    {
        _catalog = new RuleCatalog(_store);
    }

    private static DecisionTableDraft ResolvedTable()
    {
        var draft = RuleSeeds.BlankTableDraft();
        string col = draft.Columns[0].Id;
        return draft with
        {
            Rows = new[]
            {
                new TableRow("r1",
                    new Dictionary<string, TableCell> { [col] = new TableCell.Range("0", "100") },
                    "low",
                    0),
            },
            NoMatch = new NoMatchPosture.Default("high"),
        };
    }

    [Fact]
    public async Task PublishesTableOnlyAfterItsNoMatchPostureResolves()
    {
        await _catalog.CreateRuleAsync("route", "Route", RuleSkinType.Table, RuleSeeds.BlankTableDraft());

        var unresolved = await PublishAdmission.PublishRuleAsync(_catalog, "route", RuleSeeds.BlankTableDraft());
        Assert.Equal(PublishOutcome.Failure(SkinCodes.NoMatchUnresolved, "no-match is unresolved"), unresolved);

        var resolved = await PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable());
        Assert.True(resolved.Ok);
        Assert.Equal("1.0.0", resolved.Version);
        var stored = await _catalog.LoadRuleAsync("route");
        Assert.NotNull(stored);
        Assert.Single(stored.Versions);
    }

    [Fact]
    public async Task WaitsForThePublishedVersionCommitAndPropagatesItsRejection()
    {
        await _catalog.CreateRuleAsync("route", "Route", RuleSkinType.Table, ResolvedTable());
        _store.FailNextWrite = new InvalidOperationException("store offline");

        var e = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable()));
        Assert.Equal("store offline", e.Message);
        // nothing was committed past the failed write
        var stored = await _catalog.LoadRuleAsync("route");
        Assert.NotNull(stored);
        Assert.Empty(stored.Versions);
    }

    [Fact]
    public async Task RejectsUnresolvedNoMatchPostureBeforeCompileOrCatalogAccess()
    {
        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "route", RuleSeeds.BlankTableDraft());

        Assert.False(outcome.Ok);
        Assert.Equal(SkinCodes.NoMatchUnresolved, outcome.Code);
        Assert.Equal(0, _store.Reads);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ReturnsTypedCompileRejectionWithoutTouchingTheCatalog()
    {
        // A formula reading an undeclared input — the shipped compiler's stable rejection.
        var draft = RuleSeeds.BlankFormulaDraft() with
        {
            Inputs = Array.Empty<FormulaInputDecl>(),
            Expression = new FormulaExpr.Ref("undeclared"),
        };

        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "formula", draft);

        Assert.False(outcome.Ok);
        Assert.Equal(SkinCodes.FormulaUndeclaredRef, outcome.Code);
        Assert.Equal(0, _store.Reads);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task PropagatesAnUnexpectedCatalogErrorUnchanged()
    {
        // Anything that is not a RuleCompilationException re-throws unchanged — poison the catalog
        // load, which sits on the same unexpected-error contract as the TS twin's TypeError case.
        await _catalog.CreateRuleAsync("route", "Route", RuleSkinType.Table, ResolvedTable());
        _store.FailNextRead = new InvalidCastException("unexpected");

        var e = await Assert.ThrowsAsync<InvalidCastException>(
            () => PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable()));
        Assert.Equal("unexpected", e.Message);
    }

    [Fact]
    public async Task ReturnsStableMissingRuleRejectionWithoutMintingAVersion()
    {
        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "never-created", ResolvedTable());
        Assert.Equal(
            PublishOutcome.Failure(PublishAdmission.NotFoundCode, "rule 'never-created' not found"),
            outcome);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public void TreatsEveryPublishAsAControlChange()
    {
        Assert.True(PublishAdmission.PublishIsControlChange(RuleSeeds.BlankTableDraft()));
        Assert.True(PublishAdmission.PublishIsControlChange(RuleSeeds.BlankFormulaDraft()));
    }
}
