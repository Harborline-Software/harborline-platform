using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine;
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

        public async Task<RulePublishCommitResult> AllocatePublishedVersionAsync(
            string ruleKey,
            string requestId,
            RuleDraft draft,
            string bodyHash,
            string publishedAt)
        {
            if (FailNextWrite is { } e) { FailNextWrite = null; throw e; }
            var result = await _inner.AllocatePublishedVersionAsync(
                ruleKey, requestId, draft, bodyHash, publishedAt);
            if (result.Disposition == RulePublishCommitDisposition.Committed) Writes++;
            return result;
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

        var unresolved = await PublishAdmission.PublishRuleAsync(_catalog, "route", RuleSeeds.BlankTableDraft(), "unresolved");
        Assert.Equal(PublishOutcome.Failure(SkinCodes.NoMatchUnresolved, "no-match is unresolved"), unresolved);

        var resolved = await PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable(), "resolved");
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
            () => PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable(), "store-failure"));
        Assert.Equal("store offline", e.Message);
        // nothing was committed past the failed write
        var stored = await _catalog.LoadRuleAsync("route");
        Assert.NotNull(stored);
        Assert.Empty(stored.Versions);
    }

    [Fact]
    public async Task RejectsUnresolvedNoMatchPostureBeforeCompileOrCatalogAccess()
    {
        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "route", RuleSeeds.BlankTableDraft(), "unresolved");

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

        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "formula", draft, "undeclared");

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
        _store.FailNextWrite = new InvalidCastException("unexpected");

        var e = await Assert.ThrowsAsync<InvalidCastException>(
            () => PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable(), "unexpected"));
        Assert.Equal("unexpected", e.Message);
    }

    [Fact]
    public async Task ReturnsStableMissingRuleRejectionWithoutMintingAVersion()
    {
        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "never-created", ResolvedTable(), "missing");
        Assert.Equal(
            PublishOutcome.Failure(PublishAdmission.NotFoundCode, "rule 'never-created' not found"),
            outcome);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task RejectsAFormulaOverTheCompilerBoundBeforeCatalogAccess()
    {
        static FormulaExpr AddTree(int depth) => depth == 0
            ? new FormulaExpr.Literal("1", ColumnValueType.Number)
            : new FormulaExpr.Binary(ArithOps.Add, AddTree(depth - 1), AddTree(depth - 1));
        var draft = RuleSeeds.BlankFormulaDraft() with { Expression = AddTree(8) };

        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "bounded", draft, "over-bound");

        Assert.False(outcome.Ok);
        Assert.Equal(RuleEngineCodes.CompileAstTooLarge, outcome.Code);
        Assert.Contains("bounded", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(0, _store.Reads);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ConcurrentPublishersReceiveDistinctConsecutiveVersions()
    {
        await _catalog.CreateRuleAsync("route", "Route", RuleSkinType.Table, ResolvedTable());

        var outcomes = await Task.WhenAll(
            PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable(), "publisher-a"),
            PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable(), "publisher-b"));

        Assert.All(outcomes, outcome => Assert.True(outcome.Ok));
        Assert.Equal(new[] { "1.0.0", "1.0.1" }, outcomes.Select(outcome => outcome.Version).Order().ToArray());
    }

    [Fact]
    public async Task ReplayedRequestReturnsItsFirstVersionWithoutAppending()
    {
        await _catalog.CreateRuleAsync("route", "Route", RuleSkinType.Table, ResolvedTable());

        var first = await PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable(), "request-1");
        var replay = await PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable(), "request-1");

        Assert.Equal(first.Version, replay.Version);
        Assert.Single((await _catalog.LoadRuleAsync("route"))!.Versions);
    }

    [Fact]
    public void TreatsEveryPublishAsAControlChange()
    {
        Assert.True(PublishAdmission.PublishIsControlChange(RuleSeeds.BlankTableDraft()));
        Assert.True(PublishAdmission.PublishIsControlChange(RuleSeeds.BlankFormulaDraft()));
    }
}
