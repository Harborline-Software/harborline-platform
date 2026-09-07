using System.Globalization;

using Harborline.Foundation.RuleEngine.Registry;

namespace Harborline.Foundation.RuleAuthoring;

// ─────────────────────────────────────────────────────────────────────────────
//  The named-rule REGISTRY (ADR 0146 D5) at the narrowed Harborline seam. The pinned Harborline App
//  client was an HONEST INTERIM — a per-tenant-device localStorage catalog awaiting node routes
//  that never existed at the pin. Harborline replaces that interim with a STORE PORT: the
//  registry's caller-observable semantics (create-refuses-existing-key, draft-open lifecycle,
//  append-only monotonic published versions with downgrade refusal, duplicate, archive-not-delete,
//  stable listing) are identical, while persistence is injected — InMemoryRuleCatalogStore for
//  tests/dev, a durable host adapter behind the same port in production. Version semantics remain
//  the shipped engine's (RuleVersion watermark).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One published (immutable) version of a named rule.</summary>
public sealed record StoredRuleVersion(string Version, RuleDraft Draft, string PublishedAt);

/// <summary>The full persisted record for one named rule.</summary>
public sealed record StoredRule
{
    public required string RuleKey { get; init; }

    /// <summary>The human display label (the authoring name).</summary>
    public required string Name { get; init; }

    public required RuleSkinType SkinType { get; init; }

    /// <summary>The current WORKING draft (may carry unpublished edits past the latest published
    /// version).</summary>
    public required RuleDraft Draft { get; init; }

    /// <summary>Published versions, append-only, monotonic (never mutated in place — the S-8
    /// watermark forbids downgrade).</summary>
    public required IReadOnlyList<StoredRuleVersion> Versions { get; init; }

    /// <summary>True when <see cref="Draft"/> carries edits not yet published (a draft-open state).</summary>
    public required bool HasUnpublishedDraft { get; init; }

    public required bool Archived { get; init; }

    public required string UpdatedAt { get; init; }
}

/// <summary>The lifecycle status shown in the list (design §1.1).</summary>
public enum RuleStatus
{
    Published = 0,
    DraftOpen = 1,
    Draft = 2,
}

/// <summary>A front-door list row (design §1.1). <paramref name="PinnedConsumers"/> is
/// display-only (D7 pins are consumer-side).</summary>
public sealed record NamedRuleSummary(
    string RuleKey,
    string Name,
    RuleSkinType SkinType,
    string? PublishedVersion,
    string? DraftVersion,
    RuleStatus Status,
    int PinnedConsumers,
    bool Archived,
    string UpdatedAt);

/// <summary>
/// The persistence PORT the catalog composes — the Harborline seam the pinned localStorage interim
/// becomes. Implementations must return/accept whole records; the catalog owns every registry
/// semantic above this line.
/// </summary>
public interface IRuleCatalogStore
{
    Task<StoredRule?> ReadAsync(string ruleKey);

    Task WriteAsync(StoredRule rule);

    Task<IReadOnlyList<StoredRule>> ListAsync();
}

/// <summary>The in-process store — a faithful test/dev double of a durable adapter. The TS twin
/// structuredClones on every port call because its records are mutable; these records are immutable
/// (records over read-only lists), so sharing references is isolation-equivalent.</summary>
public sealed class InMemoryRuleCatalogStore : IRuleCatalogStore
{
    private readonly Dictionary<string, StoredRule> _rules = new(StringComparer.Ordinal);

    public Task<StoredRule?> ReadAsync(string ruleKey)
        => Task.FromResult(_rules.TryGetValue(ruleKey, out var rule) ? rule : null);

    public Task WriteAsync(StoredRule rule)
    {
        _rules[rule.RuleKey] = rule;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredRule>> ListAsync()
        => Task.FromResult<IReadOnlyList<StoredRule>>(_rules.Values.ToList());
}

/// <summary>
/// The named-rule catalog over an injected store port — the registry's public surface. The method
/// shapes mirror the pinned client one-for-one so callers (and the ported tests) carry over
/// unchanged apart from construction.
/// </summary>
public sealed class RuleCatalog
{
    private readonly IRuleCatalogStore _store;
    private readonly TimeProvider _clock;

    public RuleCatalog(IRuleCatalogStore store, TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>JS <c>Date.prototype.toISOString</c> (millisecond precision, <c>Z</c> suffix) —
    /// the timestamp shape the TS twin persists.</summary>
    private string NowIso()
        => _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>List the tenant's named rules (design §1.1 — the front-door source of truth).</summary>
    public async Task<IReadOnlyList<NamedRuleSummary>> ListRulesAsync()
    {
        var rules = await _store.ListAsync().ConfigureAwait(false);
        return rules.Select(Summarize).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Load one named rule (its working draft + versions). Returns null if absent.</summary>
    public Task<StoredRule?> LoadRuleAsync(string ruleKey) => _store.ReadAsync(ruleKey);

    /// <summary>Mint a fresh named rule from a blank draft. Refuses an existing key (never
    /// clobbers).</summary>
    public async Task<StoredRule> CreateRuleAsync(string ruleKey, string name, RuleSkinType skinType, RuleDraft draft)
    {
        var existing = await _store.ReadAsync(ruleKey).ConfigureAwait(false);
        if (existing is not null)
            throw new InvalidOperationException($"rule create failed: key '{ruleKey}' already exists");
        var rule = new StoredRule
        {
            RuleKey = ruleKey,
            Name = name,
            SkinType = skinType,
            Draft = draft,
            Versions = Array.Empty<StoredRuleVersion>(),
            HasUnpublishedDraft = true,
            Archived = false,
            UpdatedAt = NowIso(),
        };
        await _store.WriteAsync(rule).ConfigureAwait(false);
        return rule;
    }

    /// <summary>Persist the working draft (marks an unpublished-draft state). Does NOT publish.</summary>
    public async Task SaveDraftAsync(string ruleKey, RuleDraft draft)
    {
        var rule = await _store.ReadAsync(ruleKey).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"rule save failed: '{ruleKey}' not found");
        await _store.WriteAsync(rule with
        {
            Draft = draft,
            HasUnpublishedDraft = true,
            UpdatedAt = NowIso(),
        }).ConfigureAwait(false);
    }

    /// <summary>Append a published version (called by the admission fence AFTER the compile passes
    /// — never directly by a UI). Advances the working state to "published, no pending draft".</summary>
    public async Task CommitPublishedVersionAsync(string ruleKey, string version, RuleDraft draft)
    {
        var rule = await _store.ReadAsync(ruleKey).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"rule publish failed: '{ruleKey}' not found");
        // S-8 monotonic guard: refuse a downgrade (never mutate history in place).
        string? cur = HighestPublished(rule);
        if (cur is not null && RuleVersion.IsDowngrade(cur, version))
            throw new InvalidOperationException($"rule publish refused: {version} downgrades {cur}");
        var versions = new List<StoredRuleVersion>(rule.Versions)
        {
            new(version, draft, NowIso()),
        };
        await _store.WriteAsync(rule with
        {
            Versions = versions,
            HasUnpublishedDraft = false,
            UpdatedAt = NowIso(),
        }).ConfigureAwait(false);
    }

    /// <summary>Duplicate a rule under a new key/name (forks the working draft).</summary>
    public async Task<StoredRule> DuplicateRuleAsync(string sourceKey, string newKey, string newName)
    {
        var source = await LoadRuleAsync(sourceKey).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"rule duplicate failed: '{sourceKey}' not found");
        return await CreateRuleAsync(newKey, newName, source.SkinType, source.Draft).ConfigureAwait(false);
    }

    /// <summary>Archive / unarchive (list-visibility only — never a hard delete).</summary>
    public async Task SetArchivedAsync(string ruleKey, bool archived)
    {
        var rule = await _store.ReadAsync(ruleKey).ConfigureAwait(false);
        if (rule is null) return;
        await _store.WriteAsync(rule with { Archived = archived, UpdatedAt = NowIso() }).ConfigureAwait(false);
    }

    // ── version + summary helpers (the TS module-level functions) ────────────

    /// <summary>Highest published version by the S-8 watermark, or null.</summary>
    private static string? HighestPublished(StoredRule rule)
    {
        string? best = null;
        foreach (var v in rule.Versions)
        {
            if (best is null || RuleVersion.Compare(v.Version, best) > 0) best = v.Version;
        }
        return best;
    }

    /// <summary>The next version after the highest published (patch bump; <c>1.0.0</c> for the
    /// first).</summary>
    public static string NextVersion(StoredRule rule)
    {
        string? cur = HighestPublished(rule);
        if (cur is null) return "1.0.0";
        var parts = cur.Split('.');
        int maj = ParseOr0(parts.Length > 0 ? parts[0] : "");
        int min = ParseOr0(parts.Length > 1 ? parts[1] : "");
        int patch = ParseOr0(parts.Length > 2 ? parts[2] : "");
        return FormattableString.Invariant($"{maj}.{min}.{patch + 1}");
    }

    /// <summary>TS <c>Number(n) || 0</c> over a version segment — any unparseable segment is 0.</summary>
    private static int ParseOr0(string segment)
        => int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;

    private static NamedRuleSummary Summarize(StoredRule rule)
    {
        string? published = HighestPublished(rule);
        var status = rule.HasUnpublishedDraft
            ? (published is not null ? RuleStatus.DraftOpen : RuleStatus.Draft)
            : RuleStatus.Published;
        return new NamedRuleSummary(
            RuleKey: rule.RuleKey,
            Name: rule.Name,
            SkinType: rule.SkinType,
            PublishedVersion: published,
            DraftVersion: rule.HasUnpublishedDraft ? NextVersion(rule) : null,
            Status: status,
            PinnedConsumers: 0,
            Archived: rule.Archived,
            UpdatedAt: rule.UpdatedAt);
    }

    /// <summary>Mint a stable, collision-free rule key from a display name (slug; rule versions
    /// are the registry's job, so NO <c>.v1</c> suffix).</summary>
    public static string MintRuleKey(string name, IReadOnlySet<string> existing)
    {
        var slug = new System.Text.StringBuilder();
        bool pendingDash = false;
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && slug.Length > 0) slug.Append('-');
                pendingDash = false;
                slug.Append(c);
            }
            else
            {
                pendingDash = true;
            }
        }
        string baseKey = slug.Length > 0 ? slug.ToString() : "rule";
        string candidate = baseKey;
        int n = 2;
        while (existing.Contains(candidate))
        {
            candidate = FormattableString.Invariant($"{baseKey}-{n}");
            n += 1;
        }
        return candidate;
    }
}
