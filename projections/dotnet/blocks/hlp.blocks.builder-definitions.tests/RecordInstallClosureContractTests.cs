using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// M11 Goal 3 P-CHAR (T-615; DES-0015 records-ck-40, records-ck-41). Characterizes the Records install-closure contract
/// before the exposure carrier is decided. The exported body, the stored revision digest and the <c>sha256:</c>
/// coordinate are one value over the exact stored bytes, trailing newline included. The install gate compares a pin's
/// digest exactly. A released package names its exposed definitions but carries no version or digest for them. A Class
/// exposure never satisfies a Record Type pin. Expected digests are literals computed outside the code under test: SHA-256
/// of <c>_shared/records/record-type.asset-class.json</c> (the bytes of <c>AssetClassJson</c>) and of the exposed
/// variant spelled below, each with and without its final LF.
/// </summary>
public sealed class RecordInstallClosureContractTests
{
    private const string StoredBodyDigest = "8d8b83bd4050af00fe7d6411b1432b6470c6bfd3b8d0842929f475cb0ae613e6";
    private const string StoredBodyWithoutNewlineDigest = "7bbf3eccdb9a8e6556980f33ad871054f16061d7b5d86cde1998b1c4b3219f43";

    // The asset-class body with its envelope exposed at interface 1, as the store canonically holds it.
    private const string ExposedAssetClassJson =
        """{"class_id":"eam.equipment","envelope":{"contract":{"major":1,"minor":0},"exposes":{"interface_version":1},"package_id":"eam-core","section":"eam","tenant":"tenant-a"},"fields":[{"binding":{"constraints":{"maximum_count":1,"minimum_count":0,"read_role_ids":[],"required":true},"kind":{"kind_id":"text","parameters":{},"version":"1.0.0"}},"defaults_provenance":{"kind_id":"text","kind_version":"1.0.0"},"display_name":"Asset tag","field_key":"asset_tag","governance":{"classification":"internal","confidential":false,"masked":true,"personal_data":true}},{"binding":{"constraints":{"maximum_count":1,"minimum_count":0,"read_role_ids":[],"required":false},"kind":{"kind_id":"count","parameters":{},"version":"1.0.0"}},"defaults_provenance":{"kind_id":"count","kind_version":"1.0.0"},"display_name":"Quantity","field_key":"quantity"},{"display_name":"Notes","field_key":"notes"}],"name":"Asset Class","record_class":"master","record_type_id":"eam.asset-class"}""" + "\n";
    private const string ExposedStoredBodyDigest = "ca09327e39bb416e6a5e7025adf6e6c2ecf9967bcc2b48122ed14d1eb99e6caf";
    private const string ExposedBodyWithoutNewlineDigest = "132e1732454a8d09720b388199664f316b4c6744c80ef300ec3f9f279db1c78d";

    // A catalogue version that appears nowhere else in the released bytes, so its absence there is meaningful.
    private const string ExposedVersion = "3.1.4";

    private static readonly DefinitionContractVersion Contract = new(1, 0);
    private static readonly DateTimeOffset SavedAt = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly InstallDefinitionTarget[] ConsumerPack = [new(DefinitionKind.Records, "fleet-ops.work-order")];

    // (a) records-ck-40: one digest, over the stored bytes as stored.
    [Fact]
    [Trait("Holds", "records-ck-40")]
    public async Task the_exported_payload_hashes_to_the_stored_revision_digest_and_its_sha256_coordinate()
    {
        var (_, published) = await PublishedAssetClass("1.0.0");

        var entry = RecordTypeDefinitionPackageExporter.Export(published);
        var payload = entry.Content.Payload.ToArray();

        Assert.Equal("eam.asset-class", entry.DefinitionId);
        Assert.Equal("1.0.0", entry.Version);
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(payload));
        Assert.Equal((byte)'\n', payload[^1]);
        Assert.Equal(StoredBodyDigest, Convert.ToHexStringLower(SHA256.HashData(payload)));
        Assert.Equal(StoredBodyDigest, published.Digest);
        Assert.Equal("sha256:" + StoredBodyDigest, CatalogueFieldSource.DigestOf(published));
    }

    // (b) records-ck-40/41: a digest of any other representation, here the body less its final LF, is a mismatch.
    [Fact]
    [Trait("Holds", "records-ck-40")]
    [Trait("Holds", "records-ck-41")]
    public async Task a_pin_whose_digest_omits_the_bodys_trailing_newline_refuses_closure_changed()
    {
        var (host, published) = await PublishedAssetClass("1.0.0");
        var payload = RecordTypeDefinitionPackageExporter.Export(published).Content.Payload.ToArray();
        Assert.Equal(StoredBodyWithoutNewlineDigest, Convert.ToHexStringLower(SHA256.HashData(payload[..^1])));
        // The closure declares the exposure exactly as the producer's catalogue holds it.
        RecordsInstallClosure closure = new(ConsumerPack,
            [new("eam-core", DefinitionKind.Records, "eam.asset-class", "1.0.0", "sha256:" + StoredBodyDigest, 1)]);

        var refused = await host.Records.AdmitInstallAsync("tenant-b", Consumer("sha256:" + StoredBodyWithoutNewlineDigest), closure);
        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal([("records.reference.closure_changed", "/fields/0/reference/pin")], Pairs(refused));

        var admitted = await host.Records.AdmitInstallAsync("tenant-b", Consumer("sha256:" + StoredBodyDigest), closure);
        Assert.Equal(DefinitionAdmissionPhase.Install, admitted.Stage);
        Assert.Empty(admitted.Refusals);
    }

    // (c) MARKER: this test fails by design when ConfigurationProposal.Release starts carrying each exposed definition's
    // catalogue version and sha256: store digest (Goal 3 P1, Option A). Replace it with P-REL and P-BYTES then.
    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task marker_until_the_exposure_carrier_lands_a_release_names_an_exposed_record_type_without_its_version_or_digest()
    {
        var (_, published) = await PublishedAssetClass(ExposedVersion, new(1));
        var entry = RecordTypeDefinitionPackageExporter.Export(published);
        Assert.Equal(ExposedVersion, entry.Version);
        Assert.Equal(ExposedStoredBodyDigest, published.Digest);
        Assert.Equal(ExposedAssetClassJson, Encoding.UTF8.GetString(entry.Content.Payload.Span));

        var baseline = ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-a", ["eam-core"],
            [new(new("eam-core", "0.9.0", new string('a', 64)), [new("records/eam.asset-class", "0.9.0", new string('b', 64))], [])],
            [new("records/eam.asset-class", "eam-core")], new("platform", "1.0.0", new string('d', 64)), []));
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("records/eam.asset-class", "eam-core", ExposedAssetClassJson, "AssetTypeDefinition"));
        var saved = ConfigurationProposal.Save(state, 1, "dana.okafor", "Expose the asset class to other packages.", SavedAt);
        var result = ConfigurationProposal.Release(state, saved, new ProposedChangeCheck("proposal-1", saved.Digest, "receipt-1"),
            baseline, "eam-core", "7.0.0");

        Assert.Null(result.Refusal);
        var released = result.Released;
        Assert.NotNull(released);
        using var document = JsonDocument.Parse(released.Document);
        var root = document.RootElement;
        Assert.Equal(["schemaVersion", "packageKey", "revision", "closure", "exposes", "interfaceVersion", "items", "digest"],
            root.EnumerateObject().Select(member => member.Name));
        // The exposure is a key list and one interface version: no per-definition object to hold a version or digest.
        Assert.Equal(["records/eam.asset-class"], root.GetProperty("exposes").EnumerateArray().Select(item => item.GetString()));
        Assert.All(root.GetProperty("exposes").EnumerateArray(), item => Assert.Equal(JsonValueKind.String, item.ValueKind));
        Assert.Equal(1, root.GetProperty("interfaceVersion").GetInt32());
        var payload = root.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "eam-core.definition-1")
            .GetProperty("content").GetProperty("payload");
        Assert.Equal(["definitionKey", "packageKey", "contentKind", "body"], payload.EnumerateObject().Select(member => member.Name));

        var bytes = Encoding.UTF8.GetString(released.Document.Span);
        Assert.DoesNotContain(ExposedVersion, bytes, StringComparison.Ordinal);
        Assert.DoesNotContain(ExposedStoredBodyDigest, bytes, StringComparison.Ordinal);
        // The carried body is re-serialized without the stored LF, so hashing it yields a digest the gate refuses (b).
        var carried = payload.GetProperty("body").GetRawText();
        Assert.Equal(ExposedAssetClassJson.TrimEnd('\n'), carried);
        Assert.Equal(ExposedBodyWithoutNewlineDigest, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(carried))));
    }

    // (d) records-ck-41 with T-724 ruling 51: the closure is keyed by (package, kind, id); a Class exposure with every other
    // member equal to the Record Type pin is absent for that pin, and only a Records exposure satisfies it.
    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_class_exposure_never_satisfies_a_record_type_pin()
    {
        var host = Host();
        PinnedExposure records = new("eam-core", DefinitionKind.Records, "eam.asset-class", "1.0.0", "sha256:" + StoredBodyDigest, 1);
        var asClass = records with { Kind = DefinitionKind.Classes };
        var entry = Consumer("sha256:" + StoredBodyDigest);

        var classOnly = await host.Records.AdmitInstallAsync("tenant-b", entry, new(ConsumerPack, [asClass]));
        Assert.Equal([("records.reference.closure_missing", "/fields/0/reference/pin")], Pairs(classOnly));

        var classFirst = await host.Records.AdmitInstallAsync("tenant-b", entry, new(ConsumerPack, [asClass, records]));
        Assert.Empty(classFirst.Refusals);
    }

    private static async Task<(TestHost Host, DefinitionRevision Published)> PublishedAssetClass(string version,
        RecordsExposure? exposes = null)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass(version) with { Exposes = exposes }, "create");
        var publication = await host.Records.PublishAsync(Tenant, "eam.asset-class", version, 1, "publish");
        return (host, publication.Revision);
    }

    // A fleet-ops Record Type with one cross-package reference to eam.asset-class, declared at eam-core interface 1.
    private static RecordTypeDefinitionPackageEntry Consumer(string digest)
    {
        var document = new RecordTypeDocument(new(Tenant, "fleet-ops", Contract, "fleet-ops", [new("eam-core@1")]),
            "Work Order", "fleet-ops.work-order",
            [new("asset", "asset", Reference: new("eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block,
                Pin: new("eam-core", "eam.asset-class", "1.0.0", digest, 1)))],
            ClassId: "eam.equipment", RecordClass: RecordClass.Transactional);
        return new(document.RecordTypeId, "1.0.0", PlatformPackageContent.PresentJson(RecordTypeDefinitionJson.SerializeCanonical(document)));
    }

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalReport report)
        => report.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();
}
