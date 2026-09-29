using System.Text;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions.DependencyInjection;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.Taxonomy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-493 S7 (DES-0024). Taxonomy lives in the shared versioned-definition store under
/// <see cref="DefinitionKind.Taxonomy"/>, with the foundation's structural admission bound as its
/// validator, and consumers resolve it only at a pinned version.
/// </summary>
public sealed class TaxonomyDefinitionStoreTests
{
    private const string Tenant = "tenant-a";
    private static readonly TaxonomyDefinitionId Id = new("acme", "health", "icd");
    private static readonly TaxonomyDefinitionId HarborlineId = new("harborline", "geo", "iso3166");
    private static readonly DefinitionKey Key = TaxonomyDefinitionStore.KeyOf(Tenant, Id);

    [Fact]
    [Trait("Holds", "taxonomy-eng-1")]
    public async Task published_version_is_immutable_and_an_unpinned_authoritative_reference_is_unresolvable()
    {
        var (catalogue, taxonomy) = Store();
        await taxonomy.SaveDraftAsync(Definition("1.0.0"), 0, "draft-1");
        await taxonomy.PublishAsync(new(Tenant, Id, "1.0.0"), 1, "publish-1");

        var overwrite = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => taxonomy.SaveDraftAsync(Definition("1.0.0", display: "Edited"), 2, "edit").AsTask());
        Assert.Contains(overwrite.Refusals, refusal => refusal.Code == "definition.version_immutable" && refusal.Pointer == "/versionId");
        var resolved = Assert.IsType<ResolvedTaxonomyDefinition>(await taxonomy.ResolveAsync(new(Tenant, Id, "1.0.0")));
        Assert.Equal("Display root", Assert.Single(resolved.Definition.Nodes).Display);
        Assert.Equal(TaxonomyDefinitionJson.SerializeCanonical(Definition("1.0.0")), TaxonomyDefinitionJson.SerializeCanonical(resolved.Definition));
        Assert.Single(await catalogue.ListHistoryAsync(Key), revision => revision.Status == DefinitionStatus.Published);

        // An Authoritative scheme is never tenant-authored, so the shared store cannot hold one as a draft.
        var authoritative = Definition("1.0.0", id: HarborlineId) with { Governance = TaxonomyGovernanceRegime.Authoritative, Owner = TaxonomyDefinitionAdmission.HarborlineActorId };
        var authored = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => taxonomy.SaveDraftAsync(authoritative, 0, "authoritative").AsTask());
        Assert.Contains(authored.Refusals, refusal => refusal.Code == "definition.governance_not_authorable" && refusal.Pointer == "/governance");
        Assert.Empty(await catalogue.ListHistoryAsync(TaxonomyDefinitionStore.KeyOf(Tenant, HarborlineId)));

        // A reference that names no exact version (absent, blank, a label or a range) never resolves to a head.
        foreach (var unpinned in new[] { null, "", " ", "latest", "1", "1.0", "^1.0.0", "1.x.0" })
        {
            var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
                () => taxonomy.ResolveAsync(new(Tenant, HarborlineId, unpinned!)).AsTask());
            Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
            Assert.Equal([("definition.reference_unpinned", "/version")], refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
            await Assert.ThrowsAsync<DefinitionRefusalException>(() => taxonomy.ResolveAsync(new(Tenant, Id, unpinned!)).AsTask());
        }
    }

    [Fact]
    [Trait("Holds", "taxonomy-eng-1")]
    public async Task a_pinned_version_resolves_exactly_and_a_missing_or_draft_version_refuses()
    {
        var (_, taxonomy) = Store();
        await taxonomy.SaveDraftAsync(Definition("1.0.0"), 0, "d1");
        await taxonomy.PublishAsync(new(Tenant, Id, "1.0.0"), 1, "p1");
        await taxonomy.SaveDraftAsync(Definition("1.1.0", display: "Newer"), 2, "d2");
        await taxonomy.PublishAsync(new(Tenant, Id, "1.1.0"), 3, "p2");
        await taxonomy.SaveDraftAsync(Definition("2.0.0"), 4, "d3");

        var pinned = Assert.IsType<ResolvedTaxonomyDefinition>(await taxonomy.ResolveAsync(new(Tenant, Id, "1.0.0")));
        Assert.Equal("1.0.0", pinned.Definition.Version);
        Assert.Equal("Display root", pinned.Definition.Nodes[0].Display);
        var newer = Assert.IsType<ResolvedTaxonomyDefinition>(await taxonomy.ResolveAsync(new(Tenant, Id, "1.1.0")));
        Assert.Equal("Newer", newer.Definition.Nodes[0].Display);

        foreach (var missing in new TaxonomyDefinitionCoordinates[]
        {
            new(Tenant, Id, "2.0.0"),       // a draft is never production-resolvable
            new(Tenant, Id, "3.0.0"),       // never saved
            new("tenant-b", Id, "1.0.0"),   // another tenant's namespace
            new(Tenant, new("acme", "health", "other"), "1.0.0"),
        })
            Assert.Equal(new TaxonomyDefinitionNotFound(missing), await taxonomy.ResolveAsync(missing));
    }

    [Fact]
    [Trait("Holds", "taxonomy-ck-22")]
    public async Task publish_refuses_an_empty_or_envelope_less_scheme_and_publishes_nothing()
    {
        var (catalogue, taxonomy) = Store();
        await taxonomy.SaveDraftAsync(Definition("1.0.0") with { Nodes = [] }, 0, "empty");
        var empty = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => taxonomy.PublishAsync(new(Tenant, Id, "1.0.0"), 1, "publish-empty").AsTask());
        Assert.Contains(empty.Refusals, refusal => refusal.Code == "definition.no_terms" && refusal.Pointer == "/nodes");

        await taxonomy.SaveDraftAsync(Definition("1.0.0") with { Envelope = null }, 1, "envelope-less");
        var bare = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => taxonomy.PublishAsync(new(Tenant, Id, "1.0.0"), 2, "publish-bare").AsTask());
        Assert.Contains(bare.Refusals, refusal => refusal.Code == "definition.envelope_required" && refusal.Pointer == "/envelope");
        Assert.Null(await catalogue.GetPublishedHeadAsync(Key));
        Assert.Equal(new TaxonomyDefinitionNotFound(new(Tenant, Id, "1.0.0")), await taxonomy.ResolveAsync(new(Tenant, Id, "1.0.0")));

        await taxonomy.SaveDraftAsync(Definition("1.0.0"), 2, "corrected");
        await taxonomy.PublishAsync(new(Tenant, Id, "1.0.0"), 3, "publish-corrected");
        Assert.IsType<ResolvedTaxonomyDefinition>(await taxonomy.ResolveAsync(new(Tenant, Id, "1.0.0")));
    }

    [Theory]
    [InlineData("tenant-b", "acme.health.icd", "1.0.0", "/tenant")]
    [InlineData(Tenant, "acme.health.other", "1.0.0", "/definition_id")]
    [InlineData(Tenant, "not-a-taxonomy-id", "1.0.0", "/definition_id")]
    [InlineData(Tenant, "acme.health.icd", "1.0.1", "/version")]
    [Trait("Holds", "taxonomy-auth-11")]
    public async Task publish_refuses_catalogue_coordinates_that_disagree_with_the_body(
        string tenant, string definitionId, string version, string pointer)
    {
        var (catalogue, _) = Store();
        var key = new DefinitionKey(tenant, DefinitionKind.Taxonomy, definitionId);
        await catalogue.SaveDraftAsync(new(key, version, version, Body(Definition("1.0.0"))), 0, "draft");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.PublishAsync(key, version, 1, "publish").AsTask());

        Assert.Contains(refused.Refusals, refusal => refusal.Code == "definition.catalogue_mismatch" && refusal.Pointer == pointer);
        Assert.Null(await catalogue.GetPublishedHeadAsync(key));
    }

    [Fact]
    public async Task a_body_that_is_not_a_taxonomy_refuses_at_author_with_the_members_code()
    {
        var (catalogue, _) = Store();
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => catalogue.SaveDraftAsync(new(Key, "1.0.0", "1.0.0", "[]"), 0, "array").AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Author, refused.Stage);
        Assert.Equal([("definition.settings_not_object", "/")], refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
        Assert.Empty(await catalogue.ListHistoryAsync(Key));
    }

    [Fact]
    public void Taxonomy_is_its_own_catalogue_namespace_and_the_key_is_the_dotted_id()
    {
        Assert.Equal(14, (int)DefinitionKind.Taxonomy);
        Assert.Equal(new DefinitionKey(Tenant, DefinitionKind.Taxonomy, "acme.health.icd"), Key);
    }

    [Fact]
    public void Arguments_are_checked_rather_than_admitting_without_a_window_or_store()
    {
        Assert.Equal("window", Assert.Throws<ArgumentNullException>(() => TaxonomyDefinitionStore.Admission(null!)).ParamName);
        Assert.Equal("store", Assert.Throws<ArgumentNullException>(() => new TaxonomyDefinitionStore(null!)).ParamName);
        Assert.Equal("id", Assert.Throws<ArgumentNullException>(() => TaxonomyDefinitionStore.KeyOf(Tenant, null!)).ParamName);
        var (_, taxonomy) = Store();
        Assert.Equal("definition", Assert.Throws<ArgumentNullException>(() => taxonomy.SaveDraftAsync(null!, 0, "r")).ParamName);
        Assert.Equal("coordinates", Assert.Throws<ArgumentNullException>(() => taxonomy.PublishAsync(null!, 0, "r")).ParamName);
        Assert.Equal("coordinates", Assert.Throws<ArgumentNullException>(() => taxonomy.ResolveAsync(null!)).ParamName);
    }

    [Fact]
    public async Task Di_registers_one_taxonomy_store_over_the_hosts_shared_store()
    {
        var shared = Catalogue();
        var services = new ServiceCollection()
            .AddSingleton<IVersionedDefinitionStore>(shared)
            .AddTaxonomyDefinitionStore()
            .AddTaxonomyDefinitionStore();
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TaxonomyDefinitionStore));
        using var provider = services.BuildServiceProvider();

        var taxonomy = provider.GetRequiredService<TaxonomyDefinitionStore>();
        Assert.Same(taxonomy, provider.GetRequiredService<TaxonomyDefinitionStore>());
        await taxonomy.SaveDraftAsync(Definition("1.0.0"), 0, "draft");
        Assert.Single(await shared.ListHistoryAsync(Key));
        Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => TaxonomyDefinitionStoreServiceCollectionExtensions.AddTaxonomyDefinitionStore(null!)).ParamName);
    }

    [Fact]
    public async Task A_published_body_that_disagrees_with_its_pin_is_surfaced_not_served()
    {
        var taxonomy = new TaxonomyDefinitionStore(new OneRevisionStore(Body(Definition("1.1.0"))));
        var corrupt = await Assert.ThrowsAsync<JsonException>(() => taxonomy.ResolveAsync(new(Tenant, Id, "1.0.0")).AsTask());
        Assert.Equal("The published taxonomy body disagrees with its catalogue coordinates.", corrupt.Message);
        Assert.IsType<ResolvedTaxonomyDefinition>(await new TaxonomyDefinitionStore(new OneRevisionStore(Body(Definition("1.0.0"))))
            .ResolveAsync(new(Tenant, Id, "1.0.0")));
    }

    /// <summary>A store that answers every pin with one published body, standing in for a corrupted durable adapter.</summary>
    private sealed class OneRevisionStore(string body) : IVersionedDefinitionStore
    {
        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<DefinitionRevision?>(new(new(binding.Key, binding.VersionId, binding.VersionId, body), 1, DefinitionStatus.Published, ""));
        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static InMemoryVersionedDefinitionStore Catalogue() => new(new Dictionary<DefinitionKind, DefinitionAdmission>
    {
        [DefinitionKind.Taxonomy] = TaxonomyDefinitionStore.Admission(PlatformPackageSeed.ContractWindow),
    });

    private static (InMemoryVersionedDefinitionStore Catalogue, TaxonomyDefinitionStore Taxonomy) Store()
    {
        var catalogue = Catalogue();
        return (catalogue, new TaxonomyDefinitionStore(catalogue));
    }

    private static string Body(TaxonomyDefinition definition) => Encoding.UTF8.GetString(TaxonomyDefinitionJson.SerializeCanonical(definition));

    private static TaxonomyDefinition Definition(string version, string display = "Display root", TaxonomyDefinitionId? id = null)
    {
        var definitionId = id ?? Id;
        return new(Tenant, definitionId, version, TaxonomyGovernanceRegime.Enterprise, "author",
            [new("root", display, "Description root", TaxonomyNodeStatus.Active, [new(display, "Description root", DateTimeOffset.UnixEpoch)])],
            Envelope: new(definitionId.ToString(), version, Tenant, TaxonomyCascadeLayer.Tenant,
                JsonSerializer.SerializeToElement(new { source = "tenant" }), [], new(1, 0)));
    }
}
