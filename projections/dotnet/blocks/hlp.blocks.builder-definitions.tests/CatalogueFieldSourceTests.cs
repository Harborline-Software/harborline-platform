using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Definitions;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 slice 5 (DES-0015 records-ck-40, ADR 0095 ruling 5). The Records catalogue field source reads one field of
/// an exact published Record Type revision, only when its <c>sha256:</c> digest is the store's digest of that stored
/// body. The expected digest is a literal, computed independently over the fixture bytes.
/// </summary>
public sealed class CatalogueFieldSourceTests
{
    // sha256 of _shared/records/record-type.asset-class.json, the body the asset-class type is stored with.
    private const string StoredDigest = "sha256:4eab2f5deb063f03f4ac0e64bd7c4d91f32a1d26d7b94ebbfad7b99b07a2a738";

    [Fact]
    [Trait("Holds", "records-ck-40")]
    public async Task a_pinned_coordinate_resolves_its_field_from_the_exact_published_revision()
    {
        var host = await Published();

        var resolved = await new CatalogueFieldSource(host.Catalogue).ResolveAsync(Tenant, Coordinate());

        Assert.Equal("asset_tag", resolved.Field.FieldKey);
        Assert.Equal("Asset tag", resolved.Field.DisplayName);
        Assert.Equal(("1.0.0", DefinitionStatus.Published), (resolved.Revision.Document.VersionId, resolved.Revision.Status));
        Assert.Equal(StoredDigest, CatalogueFieldSource.DigestOf(resolved.Revision));
        Assert.Equal(StoredDigest, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "_shared", "records", "record-type.asset-class.json")))));
    }

    [Fact]
    [Trait("Holds", "records-ck-40")]
    public async Task a_digest_of_any_other_representation_is_a_mismatch_never_an_equivalent()
    {
        var host = await Published();
        // The same definition serialized another way (indented): equal content, different bytes.
        var indented = "sha256:aab3c81b490b9be30fef169a5e434502b564b8f57187a9b94a00e87f77892fb1";

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => new CatalogueFieldSource(host.Catalogue).ResolveAsync(Tenant, Coordinate() with { BodyDigest = indented }).AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
        Assert.Equal([("records.field_source.digest_mismatch", "/body_digest")], Pairs(refused));
    }

    [Fact]
    [Trait("Holds", "records-ck-40")]
    public async Task a_draft_an_unknown_version_another_tenant_or_an_unknown_field_does_not_resolve()
    {
        var host = await Published();
        await host.Records.RestoreAsDraftAsync(Tenant, "eam.asset-class", "1.0.0", "1.1.0", 2, "restore");
        var source = new CatalogueFieldSource(host.Catalogue);

        foreach (var (tenant, coordinate, code, pointer) in new[]
        {
            (Tenant, Coordinate() with { VersionId = "1.1.0" }, "records.field_source.version_unavailable", "/version_id"),
            (Tenant, Coordinate() with { VersionId = "9.9.9" }, "records.field_source.version_unavailable", "/version_id"),
            ("tenant-b", Coordinate(), "records.field_source.version_unavailable", "/version_id"),
            (Tenant, Coordinate() with { FieldKey = "serial" }, "records.field_source.field_unknown", "/field_key"),
        })
        {
            var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => source.ResolveAsync(tenant, coordinate).AsTask());
            Assert.Equal([(code, pointer)], Pairs(refused));
        }
    }

    [Theory]
    [Trait("Holds", "records-ck-40")]
    [InlineData("forms.catalogue-field-source", 1, "records.field_source.unsupported_capability", "/capability_id")]
    [InlineData("records.catalogue-field-source", 2, "records.field_source.unsupported_capability", "/schema_version")]
    public async Task an_unsupported_capability_or_schema_version_refuses(string capability, int schema, string code, string pointer)
    {
        var host = await Published();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => new CatalogueFieldSource(host.Catalogue)
            .ResolveAsync(Tenant, Coordinate() with { CapabilityId = capability, SchemaVersion = schema }).AsTask());

        Assert.Equal([(code, pointer)], Pairs(refused));
    }

    public static TheoryData<string, CatalogueFieldCoordinate, string, string> Malformed() => new()
    {
        { "kind", Coordinate() with { Kind = DefinitionKind.Forms }, "records.field_source.unsupported_kind", "/kind" },
        { "id", Coordinate() with { DefinitionId = " " }, "records.field_source.coordinate_invalid", "/definition_id" },
        { "version", Coordinate() with { VersionId = "latest" }, "records.field_source.coordinate_invalid", "/version_id" },
        { "field", Coordinate() with { FieldKey = "" }, "records.field_source.coordinate_invalid", "/field_key" },
        { "unprefixed digest", Coordinate() with { BodyDigest = StoredDigest[7..] }, "records.field_source.digest_invalid", "/body_digest" },
        { "uppercase digest", Coordinate() with { BodyDigest = StoredDigest.ToUpperInvariant().Replace("SHA256:", "sha256:", StringComparison.Ordinal) }, "records.field_source.digest_invalid", "/body_digest" },
        { "short digest", Coordinate() with { BodyDigest = StoredDigest[..70] }, "records.field_source.digest_invalid", "/body_digest" },
        { "other algorithm", Coordinate() with { BodyDigest = "sha512:" + StoredDigest[7..] }, "records.field_source.digest_invalid", "/body_digest" },
        { "tenant with a pack", Coordinate() with { Provenance = new("tenant", "eam-pack", "1.0.0") }, "records.field_source.provenance_invalid", "/provenance" },
        { "pack without a key", Coordinate() with { Provenance = new("pack", null, "1.0.0") }, "records.field_source.provenance_invalid", "/provenance" },
        { "platform unpinned", Coordinate() with { Provenance = new("platform", "platform", "1.x") }, "records.field_source.provenance_invalid", "/provenance" },
        { "unknown origin", Coordinate() with { Provenance = new("vendor") }, "records.field_source.provenance_invalid", "/provenance" },
    };

    [Theory]
    [Trait("Holds", "records-ck-40")]
    [MemberData(nameof(Malformed))]
    public async Task a_malformed_coordinate_refuses_before_any_read(string _, CatalogueFieldCoordinate coordinate, string code, string pointer)
    {
        var host = await Published();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => new CatalogueFieldSource(host.Catalogue).ResolveAsync(Tenant, coordinate).AsTask());

        Assert.Equal([(code, pointer)], Pairs(refused));
    }

    [Theory]
    [Trait("Holds", "records-ck-40")]
    [InlineData("pack", "eam-pack", "2.1.0")]
    [InlineData("platform", "platform", "1.0.0")]
    public async Task a_package_claim_cannot_resolve_a_tenant_authored_revision(
        string kind, string packKey, string packVersion)
    {
        var host = await Published();
        var source = new CatalogueFieldSource(host.Catalogue);

        // Oracle: publishing tenant content does not establish ownership by any package, regardless of
        // whether the coordinate names a syntactically valid exact package version.
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => source.ResolveAsync(Tenant,
            Coordinate() with { Provenance = new(kind, packKey, packVersion) }).AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
        Assert.Equal([("records.field_source.provenance_unverified", "/provenance")], Pairs(refused));
        Assert.Equal("asset_tag", (await source.ResolveAsync(Tenant, Coordinate())).Field.FieldKey);
    }

    [Theory]
    [Trait("Holds", "records-ck-40")]
    [InlineData("pack")]
    [InlineData("platform")]
    public async Task an_unverified_package_claim_refuses_before_catalogue_resolution(string kind)
    {
        var host = Host(); // No revision exists: version lookup would instead refuse version_unavailable.

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => new CatalogueFieldSource(host.Catalogue)
            .ResolveAsync(Tenant, Coordinate() with { Provenance = new(kind, "uninstalled", "9.9.9") }).AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
        Assert.Equal([("records.field_source.provenance_unverified", "/provenance")], Pairs(refused));
    }

    [Fact]
    public void the_source_refuses_a_missing_store_or_coordinate()
    {
        Assert.Equal("store", Assert.Throws<ArgumentNullException>(() => new CatalogueFieldSource(null!)).ParamName);
        Assert.Equal("revision", Assert.Throws<ArgumentNullException>(() => CatalogueFieldSource.DigestOf(null!)).ParamName);
    }

    private static async Task<TestHost> Published()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        return host;
    }

    private static CatalogueFieldCoordinate Coordinate() => new("records.catalogue-field-source", 1, DefinitionKind.Records,
        "eam.asset-class", "1.0.0", "asset_tag", StoredDigest, new("tenant"));

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refused)
        => refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The platform repository root was not found above the test output.");
    }
}
