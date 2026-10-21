using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;

internal static class RecordsExposurePackageProbe
{
    // Literal specimen and external hashes from the records-ck-40/41 stored-byte contract fixture.
    private const string Body =
        """{"class_id":"eam.equipment","envelope":{"contract":{"major":1,"minor":0},"exposes":{"interface_version":1},"package_id":"eam-core","section":"eam","tenant":"tenant-a"},"fields":[{"binding":{"constraints":{"maximum_count":1,"minimum_count":0,"read_role_ids":[],"required":true},"kind":{"kind_id":"text","parameters":{},"version":"1.0.0"}},"defaults_provenance":{"kind_id":"text","kind_version":"1.0.0"},"display_name":"Asset tag","field_key":"asset_tag","governance":{"classification":"internal","confidential":false,"masked":true,"personal_data":true}},{"binding":{"constraints":{"maximum_count":1,"minimum_count":0,"read_role_ids":[],"required":false},"kind":{"kind_id":"count","parameters":{},"version":"1.0.0"}},"defaults_provenance":{"kind_id":"count","kind_version":"1.0.0"},"display_name":"Quantity","field_key":"quantity"},{"display_name":"Notes","field_key":"notes"}],"name":"Asset Class","record_class":"master","record_type_id":"eam.asset-class"}""" + "\n";
    private const string StoredDigest = "ca09327e39bb416e6a5e7025adf6e6c2ecf9967bcc2b48122ed14d1eb99e6caf";
    private const string WithoutNewlineDigest = "132e1732454a8d09720b388199664f316b4c6744c80ef300ec3f9f279db1c78d";
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);

    internal static async Task VerifyAsync()
    {
        var validator = new RecordsIntentValidator();
        var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(Window, validator),
        });
        var key = new DefinitionKey("tenant-a", DefinitionKind.Records, "eam.asset-class");
        // Publish raw to preserve the specimen's bytes; unit tests own authoring-boundary serialization.
        await catalogue.SaveDraftAsync(new(key, "exposed-version", "3.1.4", Body), 0, "exposure-draft");
        var published = await catalogue.PublishAsync(key, "exposed-version", 1, "exposure-publish");
        var exported = RecordTypeDefinitionPackageExporter.Export(published);
        var payload = exported.Content.Payload.ToArray();
        Require(exported.DefinitionId == "eam.asset-class" && exported.Version == "3.1.4"
            && Encoding.UTF8.GetString(payload) == Body && payload[^1] == (byte)'\n'
            && Convert.ToHexStringLower(SHA256.HashData(payload)) == StoredDigest && published.Digest == StoredDigest
            && Convert.ToHexStringLower(SHA256.HashData(payload.AsSpan(0, payload.Length - 1))) == WithoutNewlineDigest,
            "The packaged catalogue/export boundary changed the exact stored bytes, final LF or literal digest.");

        var baseline = ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-a", ["eam-core"],
            [new(new("eam-core", "0.9.0", new string('a', 64)),
                [new("records/eam.asset-class", "0.9.0", new string('b', 64))], [])],
            [new("records/eam.asset-class", "eam-core")], new("platform", "1.0.0", new string('d', 64)), []));
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("consumer-exposure", baseline),
            new("records/eam.asset-class", "eam-core", Body, "AssetTypeDefinition"));
        var saved = ConfigurationProposal.Save(state, 1, "consumer-author", "Expose the published Records revision.",
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var check = new ProposedChangeCheck("consumer-exposure", saved.Digest, "consumer-check");
        var asked = new List<string>();
        var bound = ConfigurationProposal.Release(state, saved, check, baseline, "eam-core", "7.0.0", requested =>
        {
            asked.Add(requested);
            return published;
        });
        Require(bound.Refusal is null && bound.Released is not null && asked.SequenceEqual(["records/eam.asset-class"]),
            "The public release did not resolve exactly the exposed Records key.");
        using var document = JsonDocument.Parse(bound.Released!.Document);
        var root = document.RootElement;
        var carrier = root.GetProperty("exposedDefinitions").EnumerateArray().Single();
        Require(root.GetProperty("revision").GetString() == "7.0.0" && root.GetProperty("interfaceVersion").GetInt32() == 1
            && carrier.GetProperty("definitionKey").GetString() == "records/eam.asset-class"
            && carrier.GetProperty("version").GetString() == "3.1.4"
            && carrier.GetProperty("digest").GetString() == "sha256:" + StoredDigest,
            "Release substituted the package version or reserialized-body digest for the published catalogue facts.");
        var closure = new RecordsInstallClosure([new(DefinitionKind.Records, "fleet-ops.work-order")],
            [new("eam-core", DefinitionKind.Records, "eam.asset-class", carrier.GetProperty("version").GetString()!,
                carrier.GetProperty("digest").GetString()!, root.GetProperty("interfaceVersion").GetInt32())]);
        var kinds = new FieldKindRuntime(new FieldKindRegistry(Array.Empty<AdmittedFieldKind>()));
        var installGate = new RecordTypeDefinitionStore(catalogue,
            new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission()),
            new RecordFieldDefaults(kinds), new InMemorySchemaRegistry(fieldKindRuntime: kinds), Window);
        var admitted = await installGate.AdmitInstallAsync("tenant-b", Consumer(StoredDigest), closure);
        Require(admitted.Stage == DefinitionAdmissionPhase.Install && admitted.Refusals.Count == 0,
            "An exact consumer pin did not admit against the carried published exposure.");
        Refusal(await installGate.AdmitInstallAsync("tenant-b", Consumer(WithoutNewlineDigest), closure),
            "records.reference.closure_changed");

        var unbound = ConfigurationProposal.Release(state, saved, check, baseline, "eam-core", "7.0.0");
        Require(unbound.Refusal is null && unbound.Released is not null, "The resolver-free release unexpectedly refused.");
        using var unboundDocument = JsonDocument.Parse(unbound.Released!.Document);
        var unboundRoot = unboundDocument.RootElement;
        var hasUnboundDefinitions = unboundRoot.TryGetProperty("exposedDefinitions", out var unboundDefinitions);
        var unboundExposures = hasUnboundDefinitions
            ? unboundDefinitions.EnumerateArray().Select(definition => new PinnedExposure(
                unboundRoot.GetProperty("packageKey").GetString()!, DefinitionKind.Records,
                definition.GetProperty("definitionKey").GetString()!["records/".Length..],
                definition.GetProperty("version").GetString()!, definition.GetProperty("digest").GetString()!,
                unboundRoot.GetProperty("interfaceVersion").GetInt32())).ToArray()
            : Array.Empty<PinnedExposure>();
        Require(!hasUnboundDefinitions
            && unboundRoot.GetProperty("exposes").EnumerateArray().Single().GetString() == "records/eam.asset-class",
            "A resolver-free release invented a bound version/digest carrier.");
        Refusal(await installGate.AdmitInstallAsync("tenant-b", Consumer(StoredDigest),
            new RecordsInstallClosure([new(DefinitionKind.Records, "fleet-ops.work-order")], unboundExposures)),
            "records.reference.closure_missing");

        var wrongIdentity = published with
        {
            Document = published.Document with { Key = key with { DefinitionId = "eam.another-type" } },
        };
        var refused = ConfigurationProposal.Release(state, saved, check, baseline, "eam-core", "7.0.0", _ => wrongIdentity);
        Require(refused.Released is null && refused.Refusal is { Code: "configuration-release-exposure-unpublished", Target: "records/eam.asset-class" },
            "Release bound a revision catalogued under another body's identity.");
        Refusal(await installGate.AdmitInstallAsync("tenant-b", Consumer(StoredDigest), closure with
        {
            Exposures = [closure.Exposures.Single() with { Kind = DefinitionKind.Classes }],
        }), "records.reference.closure_missing");
        Console.WriteLine("RECORDS_EXPOSURE_PACKAGE_PASS: stored LF/digest, bound published version, exact pin, changed digest, unbound release, mismatched identity and Class non-substitution");
    }

    private static RecordTypeDefinitionPackageEntry Consumer(string digest)
    {
        var body = new RecordTypeDocument(new("tenant-b", "fleet-ops", new(1, 0), "fleet-ops", [new("eam-core@1")]),
            "Work Order", "fleet-ops.work-order",
            [new("asset", "Asset", Reference: new("eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block,
                Pin: new("eam-core", "eam.asset-class", "3.1.4", "sha256:" + digest, 1)))],
            ClassId: "eam.equipment", RecordClass: RecordClass.Transactional);
        return new("fleet-ops.work-order", "1.0.0", PlatformPackageContent.PresentJson(RecordTypeDefinitionJson.SerializeCanonical(body)));
    }

    private static void Refusal(DefinitionRefusalReport report, string code)
        => Require(report.Stage == DefinitionAdmissionPhase.Install && report.Refusals.Count == 1
            && report.Refusals[0].Code == code && report.Refusals[0].Pointer == "/fields/0/reference/pin",
            "The package install gate did not return the literal closure refusal at its pin.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
