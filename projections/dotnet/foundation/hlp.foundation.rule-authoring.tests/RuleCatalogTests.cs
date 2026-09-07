using System.Globalization;

using Harborline.Foundation.RuleAuthoring;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>The .NET twin of the TS <c>catalog.test.ts</c> — the store-port catalog semantics
/// under an injected fixed clock (the vitest fake-timer cases).</summary>
public sealed class RuleCatalogTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
            = DateTimeOffset.Parse("2026-07-16T00:00:00Z", CultureInfo.InvariantCulture);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly FixedTimeProvider _clock = new();
    private readonly RuleCatalog _catalog;

    public RuleCatalogTests()
    {
        _catalog = new RuleCatalog(new InMemoryRuleCatalogStore(), _clock);
    }

    [Fact]
    public async Task RoundTripsCreateDraftDuplicateAndArchiveThroughTheStorePort()
    {
        var initialDraft = RuleSeeds.BlankFormulaDraft();
        var created = await _catalog.CreateRuleAsync("invoice-total", "Invoice total", RuleSkinType.Formula, initialDraft);

        Assert.Equal("2026-07-16T00:00:00.000Z", created.UpdatedAt);
        Assert.Equal(created, await _catalog.LoadRuleAsync("invoice-total"));

        var summary = Assert.Single(await _catalog.ListRulesAsync());
        Assert.Equal("invoice-total", summary.RuleKey);
        Assert.Null(summary.PublishedVersion);
        Assert.Equal("1.0.0", summary.DraftVersion);
        Assert.Equal(RuleStatus.Draft, summary.Status);

        var savedDraft = initialDraft with { ScopeTarget = "saved-total" };
        _clock.Now = DateTimeOffset.Parse("2026-07-16T00:01:00Z", CultureInfo.InvariantCulture);
        await _catalog.SaveDraftAsync("invoice-total", savedDraft);
        var reloaded = await _catalog.LoadRuleAsync("invoice-total");
        Assert.NotNull(reloaded);
        Assert.Equal(savedDraft, reloaded.Draft);
        Assert.True(reloaded.HasUnpublishedDraft);
        Assert.Equal("2026-07-16T00:01:00.000Z", reloaded.UpdatedAt);

        await _catalog.DuplicateRuleAsync("invoice-total", "invoice-total-copy", "Invoice total copy");
        await _catalog.SetArchivedAsync("invoice-total-copy", true);
        var copy = await _catalog.LoadRuleAsync("invoice-total-copy");
        Assert.NotNull(copy);
        Assert.Equal("Invoice total copy", copy.Name);
        Assert.Equal(savedDraft, copy.Draft);
        Assert.True(copy.Archived);
    }

    // The pinned 'degrades safely when browser storage is unavailable' case is the localStorage
    // interim's own failure semantics — replaced at the store port (the ledger's
    // replaced-at-new-seam rulesClient rows); the port has no browser storage.

    [Fact]
    public async Task RejectsPublishedVersionDowngradeWithoutChangingStoredHistory()
    {
        var draft = RuleSeeds.BlankFormulaDraft();
        await _catalog.CreateRuleAsync("watermarked", "Watermarked", RuleSkinType.Formula, draft);
        await _catalog.CommitPublishedVersionAsync("watermarked", "2.4.9", draft);
        await _catalog.CommitPublishedVersionAsync("watermarked", "2.5.0", draft);

        var e = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _catalog.CommitPublishedVersionAsync("watermarked", "2.4.10", draft));
        Assert.Equal("rule publish refused: 2.4.10 downgrades 2.5.0", e.Message);

        var stored = await _catalog.LoadRuleAsync("watermarked");
        Assert.NotNull(stored);
        Assert.Equal(new[] { "2.4.9", "2.5.0" }, stored.Versions.Select(v => v.Version).ToArray());
        Assert.Equal("2.5.1", RuleCatalog.NextVersion(stored));
    }

    [Fact]
    public async Task PreservesTheDocumentedNullNoOpAndErrorPaths()
    {
        var draft = RuleSeeds.BlankFormulaDraft();
        Assert.Null(await _catalog.LoadRuleAsync("missing"));
        await _catalog.SetArchivedAsync("missing", true); // documented no-op

        var save = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _catalog.SaveDraftAsync("missing", draft));
        Assert.Equal("rule save failed: 'missing' not found", save.Message);

        var publish = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _catalog.CommitPublishedVersionAsync("missing", "1.0.0", draft));
        Assert.Equal("rule publish failed: 'missing' not found", publish.Message);

        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _catalog.DuplicateRuleAsync("missing", "copy", "Copy"));
        Assert.Equal("rule duplicate failed: 'missing' not found", duplicate.Message);

        await _catalog.CreateRuleAsync("existing", "Existing", RuleSkinType.Formula, draft);
        var create = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _catalog.CreateRuleAsync("existing", "Replacement", RuleSkinType.Formula, draft));
        Assert.Equal("rule create failed: key 'existing' already exists", create.Message);
    }

    [Fact]
    public void MintRuleKeySlugsAndAvoidsCollisions()
    {
        var none = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal("invoice-route", RuleCatalog.MintRuleKey("  Invoice Route!  ", none));
        Assert.Equal("rule", RuleCatalog.MintRuleKey("!!!", none));
        var taken = new HashSet<string>(StringComparer.Ordinal) { "invoice-route", "invoice-route-2" };
        Assert.Equal("invoice-route-3", RuleCatalog.MintRuleKey("Invoice Route", taken));
    }
}
