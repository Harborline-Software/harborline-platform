using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class BuilderDefinitionsSubstrateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"builder-definitions-{Guid.NewGuid():N}");
    private string Journal => Path.Combine(_directory, "lifecycle.json");

    // apps/carrier/src/definitions/__tests__/slug.test.ts:5-25
    [Fact] public void Slug_A1_kebab_and_v1() => Assert.Equal("tenant-intake.v1", DefinitionKeySuggester.Suggest("Tenant Intake", Set()));
    [Fact] public void Slug_A2_non_alphanumerics() => Assert.Equal("invoice-approval.v1", DefinitionKeySuggester.Suggest("  Invoice — Approval!! ", Set()));
    [Fact] public void Slug_A3_untitled() => Assert.Equal("untitled.v1", DefinitionKeySuggester.Suggest("###", Set()));
    [Fact] public void Slug_A4_collision_suffix() => Assert.Equal("tenant-intake-3.v1", DefinitionKeySuggester.Suggest("Tenant Intake", Set("tenant-intake.v1", "tenant-intake-2.v1")));
    [Fact] public void Slug_A5_exact_case_lookup() => Assert.Equal("tenant-intake-two.v1", DefinitionKeySuggester.Suggest("Tenant Intake Two", Set("tenant-intake.v1")));

    // apps/carrier/src/definitions/slug.test.ts:5-31 (separate duplicate source declarations retained)
    [Fact] public void Slug_B1_normalizes() => Assert.Equal("tenant-intake-north.v1", DefinitionKeySuggester.Suggest("  Tenant Intake — North  ", Set()));
    [Fact] public void Slug_B2_empty_and_symbols() { Assert.Equal("untitled.v1", DefinitionKeySuggester.Suggest("", Set())); Assert.Equal("untitled.v1", DefinitionKeySuggester.Suggest("---", Set())); }
    [Fact] public void Slug_B3_first_available() { Assert.Equal("tenant-intake.v1", DefinitionKeySuggester.Suggest("Tenant Intake", Set("tenant-intake-2.v1"))); Assert.Equal("tenant-intake-3.v1", DefinitionKeySuggester.Suggest("Tenant Intake", Set("tenant-intake.v1", "tenant-intake-2.v1"))); }
    [Fact] public void Slug_B4_case_sensitive() => Assert.Equal("tenant-intake.v1", DefinitionKeySuggester.Suggest("Tenant Intake", Set("TENANT-INTAKE.v1")));
    [Fact] public void Slug_B5_runtime_boundary() => Assert.Throws<ArgumentNullException>(() => DefinitionKeySuggester.Suggest(null!, Set()));

    // apps/carrier/src/definitions/__tests__/archiveStore.test.ts:10-32; durable disposition per ticket USER RULINGS (2026-08-17).
    [Fact] public async Task Archive_C1_starts_empty() { using var store = Store(); Assert.Empty(await Collect(store, "tenant-a", DefinitionKind.Forms)); }
    [Fact] public async Task Archive_C2_is_idempotent_and_restart_durable() { var key = Key("tenant-a", DefinitionKind.Forms, "tenant-intake.v1"); using (var store = Store()) { await store.ArchiveAsync(key); await store.ArchiveAsync(key); } using var restarted = Store(); Assert.True(await restarted.IsArchivedAsync(key)); Assert.Single(await Collect(restarted, "tenant-a", DefinitionKind.Forms)); }
    [Fact] public async Task Archive_C3_unarchive_is_idempotent_and_preserves_others() { var a=Key("tenant-a",DefinitionKind.Forms,"a.v1"); var b=Key("tenant-a",DefinitionKind.Forms,"b.v1"); using var store=Store(); await store.ArchiveAsync(a); await store.ArchiveAsync(b); await store.UnarchiveAsync(a); await store.UnarchiveAsync(a); Assert.Equal(["b.v1"], await Collect(store,"tenant-a",DefinitionKind.Forms)); }
    [Fact] public async Task Archive_C4_namespaces_kind_and_tenant() { using var store=Store(); await store.ArchiveAsync(Key("tenant-a",DefinitionKind.Forms,"shared-key.v1")); Assert.False(await store.IsArchivedAsync(Key("tenant-a",DefinitionKind.Workflows,"shared-key.v1"))); Assert.False(await store.IsArchivedAsync(Key("tenant-b",DefinitionKind.Forms,"shared-key.v1"))); }

    // apps/carrier/src/definitions/pilotManagerBridge.test.ts:27-70: exclusion proofs, not ports.
    [Fact] public void Bridge_D1_no_process_global_accessor() => AssertNoBridgeSurface();
    [Fact] public void Bridge_D2_no_live_handler_registration() => AssertNoBridgeSurface();
    [Fact] public void Bridge_D3_no_react_snapshot() => AssertNoBridgeSurface();
    [Fact] public void Bridge_D4_no_disposer_lifecycle() => AssertNoBridgeSurface();

    private FileJournalDefinitionLifecycleStore Store() => new(Journal);
    private static DefinitionLifecycleKey Key(string tenant, DefinitionKind kind, string key) => new(tenant, kind, key);
    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
    private static async Task<string[]> Collect(IDefinitionLifecycleStore store,string tenant,DefinitionKind kind) { var result=new List<string>(); await foreach(var key in store.ListArchivedAsync(tenant,kind)) result.Add(key); return [.. result]; }
    private static void AssertNoBridgeSurface() { var names=typeof(IDefinitionLifecycleStore).Assembly.GetExportedTypes().Select(t=>t.Name).ToArray(); Assert.DoesNotContain("ManagerBridgeHandlers",names); Assert.DoesNotContain("PilotManagerBridge",names); }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive:true); }
}
