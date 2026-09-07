using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>
/// Registry-flow + publish-fence proof (design R1/R5 gates) — the .NET twin of the TS
/// <c>catalog-registry.test.ts</c>: create → save draft → publish through the admission fence →
/// version mint (S-8 monotonic) → duplicate → archive. The publish fence rejects an unresolved
/// decision table with the stable <c>rule.skin.no_match_unresolved</c> code (never a silent
/// commit).
/// </summary>
public sealed class CatalogRegistryTests
{
    private readonly RuleCatalog _catalog = new(new InMemoryRuleCatalogStore());

    private static DecisionTableDraft ResolvedTable()
    {
        var d = RuleSeeds.BlankTableDraft();
        string col = d.Columns[0].Id;
        return d with
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
    public async Task CreatesListsAndOpensNamedRuleInDraftStatus()
    {
        await _catalog.CreateRuleAsync("invoice-route", "Invoice route", RuleSkinType.Table, RuleSeeds.BlankTableDraft());
        var list = await _catalog.ListRulesAsync();
        Assert.Contains("invoice-route", list.Select(r => r.RuleKey));
        Assert.Equal(RuleStatus.Draft, list.Single(r => r.RuleKey == "invoice-route").Status);
        Assert.NotNull(await _catalog.LoadRuleAsync("invoice-route"));
    }

    [Fact]
    public async Task RefusesDuplicateKeyNeverClobbers()
    {
        await _catalog.CreateRuleAsync("k", "K", RuleSkinType.Formula, RuleSeeds.BlankFormulaDraft());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _catalog.CreateRuleAsync("k", "K2", RuleSkinType.Formula, RuleSeeds.BlankFormulaDraft()));
    }

    [Fact]
    public async Task PublishesResolvedTableThroughAdmissionFenceAndMintsSequentialVersions()
    {
        await _catalog.CreateRuleAsync("route", "Route", RuleSkinType.Table, ResolvedTable());
        var first = await PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable());
        Assert.True(first.Ok);
        Assert.Equal("1.0.0", first.Version);
        var summary = (await _catalog.ListRulesAsync()).Single(r => r.RuleKey == "route");
        Assert.Equal(RuleStatus.Published, summary.Status);
        Assert.Equal("1.0.0", summary.PublishedVersion);

        // an edit → new draft → publish mints the next patch version
        await _catalog.SaveDraftAsync("route", ResolvedTable());
        var second = await PublishAdmission.PublishRuleAsync(_catalog, "route", ResolvedTable());
        Assert.True(second.Ok);
        Assert.Equal("1.0.1", second.Version);
        summary = (await _catalog.ListRulesAsync()).Single(r => r.RuleKey == "route");
        Assert.Equal("1.0.1", summary.PublishedVersion);
    }

    [Fact]
    public async Task PublishFenceRejectsUnresolvedNoMatchTableWithoutSilentCommit()
    {
        var unresolved = ResolvedTable() with { NoMatch = new NoMatchPosture.Default("") };
        await _catalog.CreateRuleAsync("bad", "Bad", RuleSkinType.Table, unresolved);
        var outcome = await PublishAdmission.PublishRuleAsync(_catalog, "bad", unresolved);
        Assert.False(outcome.Ok);
        Assert.Equal(SkinCodes.NoMatchUnresolved, outcome.Code);
        // nothing was committed
        var stored = await _catalog.LoadRuleAsync("bad");
        Assert.NotNull(stored);
        Assert.Empty(stored.Versions);
    }

    [Fact]
    public async Task DuplicatesAndArchives()
    {
        await _catalog.CreateRuleAsync("orig", "Orig", RuleSkinType.Table, ResolvedTable());
        await _catalog.DuplicateRuleAsync("orig", "orig-copy", "Orig copy");
        Assert.Contains("orig-copy", (await _catalog.ListRulesAsync()).Select(r => r.RuleKey));
        await _catalog.SetArchivedAsync("orig-copy", true);
        Assert.True((await _catalog.ListRulesAsync()).Single(r => r.RuleKey == "orig-copy").Archived);
    }
}
