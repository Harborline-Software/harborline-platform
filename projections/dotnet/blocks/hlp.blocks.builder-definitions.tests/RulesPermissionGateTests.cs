using System.Text.Json.Nodes;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Registry;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>Host-supplied Access verdicts over the Rules capability names (test double for the host gate).</summary>
internal static class RulesGrants
{
    public static RulesCapabilityCheck All { get; } = (_, _) => ValueTask.FromResult(true);

    public static RulesCapabilityCheck Only(params string[] granted)
        => (permission, _) => ValueTask.FromResult(granted.Contains(permission, StringComparer.Ordinal));
}

/// <summary>T-591 (owner ruling Q14): the author, publish and author-floor gates at the Rules operation boundary.</summary>
public sealed class RulesPermissionGateTests
{
    private static readonly DefinitionKey Key = new("tenant-a", DefinitionKind.Rules, "amount-rule");

    private static (InMemoryVersionedDefinitionStore Store, RuleDefinitionCatalog Catalog) Catalog(RulesCapabilityCheck grants, string directory)
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission> { [DefinitionKind.Rules] = RuleDefinitionCatalog.Admit });
        return (store, new RuleDefinitionCatalog(store, new FileJournalDefinitionLifecycleStore(Path.Combine(directory, "lifecycle.json")), grants));
    }

    [Fact(DisplayName = "DES-0018 §6 rules:author and rules:publish: authoring needs rules:author and publishing needs rules:publish, each refused at its own stage before any shared history write")]
    public async Task Author_and_publish_are_gated_by_their_own_capability()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rules-gates-{Guid.NewGuid():N}");
        try
        {
            var (store, denied) = Catalog(RulesGrants.Only(RulesPermissions.Publish), directory);
            var author = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await denied.SaveDraftJsonAsync(RuleDefinitionCatalogTests.Source(), "v1", 0, "r1"));
            Assert.Equal((DefinitionAdmissionPhase.Author, RulesPermissions.DeniedCode), (author.Stage, Assert.Single(author.Refusals).Code));
            Assert.Empty(await store.ListHistoryAsync(Key));

            var (authorOnlyStore, authorOnly) = Catalog(RulesGrants.Only(RulesPermissions.Author), directory + "-a");
            var draft = await authorOnly.SaveDraftJsonAsync(RuleDefinitionCatalogTests.Source(), "v1", 0, "r1");
            var publish = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await authorOnly.PublishAsync(Key, "v1", draft.Revision, "r2"));
            Assert.Equal((DefinitionAdmissionPhase.Publish, RulesPermissions.DeniedCode), (publish.Stage, Assert.Single(publish.Refusals).Code));
            Assert.Null(await authorOnlyStore.GetPublishedHeadAsync(Key));

            var (_, both) = Catalog(RulesGrants.Only(RulesPermissions.Author, RulesPermissions.Publish), directory + "-b");
            var saved = await both.SaveDraftJsonAsync(RuleDefinitionCatalogTests.Source(), "v1", 0, "r1");
            Assert.Equal(DefinitionStatus.Published, (await both.PublishAsync(Key, "v1", saved.Revision, "r2")).Status);
        }
        finally
        {
            foreach (var path in new[] { directory, directory + "-a", directory + "-b" }) if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact(DisplayName = "rules-auth-8 / rules-auth-33: sealed floor writability and floor authoring read the rules:author-floor capability; rules:author alone does not grant it")]
    public async Task Floor_authoring_reads_the_author_floor_capability()
    {
        var seed = JsonNode.Parse("""{"safetyFloors":{"retention":3}}""")!;
        var raised = JsonNode.Parse("""{"safetyFloors":{"retention":5}}""")!;

        var authorOnly = RulesGrants.Only(RulesPermissions.Author);
        Assert.False(Assert.Single(await SafetyFloorAuthoring.DescribeAsync(seed, authorOnly)).Writable);
        Assert.Equal(SafetyFloorAuthoring.AuthorFloorDenied, Assert.Single((await SafetyFloorAuthoring.AuthorAsync(seed, raised, authorOnly)).Refusals).Code);

        var floor = RulesGrants.Only(RulesPermissions.AuthorFloor);
        Assert.True(Assert.Single(await SafetyFloorAuthoring.DescribeAsync(seed, floor)).Writable);
        Assert.Empty((await SafetyFloorAuthoring.AuthorAsync(seed, raised, floor)).Refusals);
    }

    [Fact(DisplayName = "rules-auth-31: refuses saving, restoring or archiving a rule without rules:author, each before any shared history write")]
    public async Task Save_restore_and_archive_are_gated_by_author_capability()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rules-auth-31-{Guid.NewGuid():N}");
        try
        {
            var (store, denied) = Catalog(RulesGrants.Only(RulesPermissions.Publish), directory);

            var saveRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async ()
                => await denied.SaveDraftJsonAsync(RuleDefinitionCatalogTests.Source(), "v1", 0, "r1"));
            Assert.Equal((DefinitionAdmissionPhase.Author, RulesPermissions.DeniedCode), (saveRefusal.Stage, Assert.Single(saveRefusal.Refusals).Code));
            Assert.Empty(await store.ListHistoryAsync(Key));

            var restoreRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async ()
                => await denied.RestoreAsDraftAsync(Key, "v1", "v2", "2.0.0", 0, "r3"));
            Assert.Equal((DefinitionAdmissionPhase.Author, RulesPermissions.DeniedCode), (restoreRefusal.Stage, Assert.Single(restoreRefusal.Refusals).Code));
            Assert.Empty(await store.ListHistoryAsync(Key));

            var archiveRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async ()
                => await denied.ArchiveAsync(Key));
            Assert.Equal((DefinitionAdmissionPhase.Author, RulesPermissions.DeniedCode), (archiveRefusal.Stage, Assert.Single(archiveRefusal.Refusals).Code));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "rules-auth-32: refuses publishing and release materialisation without rules:publish, each at its own stage before anything is written")]
    public async Task Publish_and_materialize_release_are_gated_by_publish_capability()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rules-auth-32-{Guid.NewGuid():N}");
        try
        {
            var (store, authorOnly) = Catalog(RulesGrants.Only(RulesPermissions.Author), directory);
            var draft = await authorOnly.SaveDraftJsonAsync(RuleDefinitionCatalogTests.Source(), "v1", 0, "r1");

            var publishRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async ()
                => await authorOnly.PublishAsync(Key, "v1", draft.Revision, "r2"));
            Assert.Equal((DefinitionAdmissionPhase.Publish, RulesPermissions.DeniedCode), (publishRefusal.Stage, Assert.Single(publishRefusal.Refusals).Code));
            Assert.Null(await store.GetPublishedHeadAsync(Key));

            var materializeRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async ()
                => await authorOnly.MaterializeReleaseAsync([new(Key, RuleVersionPolicy.Latest)]));
            Assert.Equal((DefinitionAdmissionPhase.Publish, RulesPermissions.DeniedCode), (materializeRefusal.Stage, Assert.Single(materializeRefusal.Refusals).Code));
            Assert.Null(await store.GetPublishedHeadAsync(Key));

            var (_, both) = Catalog(RulesGrants.Only(RulesPermissions.Author, RulesPermissions.Publish), directory + "-b");
            var saved = await both.SaveDraftJsonAsync(RuleDefinitionCatalogTests.Source(), "v1", 0, "r1");
            await both.PublishAsync(Key, "v1", saved.Revision, "r2");
            var released = await both.MaterializeReleaseAsync([new(Key, RuleVersionPolicy.Latest)]);
            Assert.Single(released.Bindings);
        }
        finally
        {
            foreach (var path in new[] { directory, directory + "-b" }) if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
