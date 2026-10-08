using System.Text;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class PlatformPackageSeedTests
{
    [Fact]
    public void Export_is_deterministic_and_preserves_bootstrap_order()
    {
        var manifest = Manifest(
            Entry("platform-package", PlatformSeedStage.PackageRecord, "{\"name\":\"Platform\"}"),
            Entry("record-types", PlatformSeedStage.SystemRecordTypes, "{\"sealed\":true}", "platform-package"));

        var first = PlatformPackageExporter.Export(manifest);
        var second = PlatformPackageExporter.Export(manifest);

        Assert.Equal(first, second);
        using var exported = JsonDocument.Parse(first);
        Assert.Empty(exported.RootElement.GetProperty("closure").GetProperty("dependencies").EnumerateArray());
        Assert.Equal("sha256", exported.RootElement.GetProperty("digest").GetProperty("algorithm").GetString());
        Assert.Equal(64, exported.RootElement.GetProperty("digest").GetProperty("value").GetString()!.Length);
        Assert.Equal(["platform-package", "record-types"], exported.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    // ck-2 S7: the manifest's closure is exported as declared dependencies in ordinal key order, so
    // the bytes and their digest do not depend on the order a producer listed them in.
    [Fact]
    public void Export_writes_the_declared_closure_in_ordinal_key_order()
    {
        var items = new[] { Entry("package", PlatformSeedStage.PackageRecord, "{}") };
        var manifest = new PlatformPackageManifest(1, "tenant.release", "1.0.0", items,
            [new("payroll", "3.0.0"), new("finance", "1.2.0")]);
        var reordered = new PlatformPackageManifest(1, "tenant.release", "1.0.0", items,
            [new("finance", "1.2.0"), new("payroll", "3.0.0")]);

        var exported = PlatformPackageExporter.Export(manifest);
        Assert.Equal(exported, PlatformPackageExporter.Export(reordered));
        using var document = JsonDocument.Parse(exported);
        Assert.Equal(["finance@1.2.0", "payroll@3.0.0"], document.RootElement.GetProperty("closure").GetProperty("dependencies")
            .EnumerateArray().Select(item => $"{item.GetProperty("key").GetString()}@{item.GetProperty("version").GetString()}"));
        Assert.Equal(["finance", "payroll"], manifest.Dependencies.Select(dependency => dependency.Key));
        Assert.NotEqual(exported, PlatformPackageExporter.Export(Manifest(items)));
    }

    // One version per key per closure (D2), and a package is never its own dependency (D3).
    [Theory]
    [InlineData("", "1.0.0", "platform-package-dependency-key-required")]
    [InlineData("finance", " ", "platform-package-dependency-version-required")]
    [InlineData("tenant.release", "1.0.0", "platform-package-dependency-self")]
    [InlineData("finance", "2.0.0", "platform-package-dependency-duplicate")]
    public void A_malformed_closure_refuses_by_name(string key, string version, string code)
    {
        var exception = Assert.Throws<ArgumentException>(() => new PlatformPackageManifest(1, "tenant.release", "1.0.0",
            [Entry("package", PlatformSeedStage.PackageRecord, "{}")], [new("finance", "1.0.0"), new(key, version)]));
        Assert.StartsWith(code, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_without_identity_items_or_a_closure_refuses()
    {
        PlatformPackageItem[] items = [Entry("package", PlatformSeedStage.PackageRecord, "{}")];
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlatformPackageManifest(0, "p", "1.0.0", items, []));
        Assert.StartsWith("platform-package-key-required", Assert.Throws<ArgumentException>(() => new PlatformPackageManifest(1, " ", "1.0.0", items, [])).Message, StringComparison.Ordinal);
        Assert.StartsWith("platform-package-revision-required", Assert.Throws<ArgumentException>(() => new PlatformPackageManifest(1, "p", " ", items, [])).Message, StringComparison.Ordinal);
        Assert.Equal("items", Assert.Throws<ArgumentNullException>(() => new PlatformPackageManifest(1, "p", "1.0.0", null!, [])).ParamName);
        Assert.Equal("dependencies", Assert.Throws<ArgumentNullException>(() => new PlatformPackageManifest(1, "p", "1.0.0", items, null!)).ParamName);
    }

    // ADR-0028 / T-396: an exposure is exported after the closure as the api manifest's exposes and interfaceVersion,
    // in ordinal order whatever order the producer listed it in; a manifest exposing nothing exports the bytes it did.
    [Fact]
    public void Export_writes_the_exposure_in_ordinal_order_and_omits_it_when_absent()
    {
        PlatformPackageItem[] items = [Entry("package", PlatformSeedStage.PackageRecord, "{}")];
        var exposed = new PlatformPackageManifest(1, "tenant.release", "1.0.0", items, [],
            new PlatformPackageExposure(3, ["records/payslip", "records/invoice"]));
        var reordered = new PlatformPackageManifest(1, "tenant.release", "1.0.0", items, [],
            new PlatformPackageExposure(3, ["records/invoice", "records/payslip"]));

        var exported = PlatformPackageExporter.Export(exposed);
        Assert.Equal(exported, PlatformPackageExporter.Export(reordered));
        Assert.Equal(["records/invoice", "records/payslip"], exposed.Exposure!.Definitions);
        using var document = JsonDocument.Parse(exported);
        Assert.Equal(["schemaVersion", "packageKey", "revision", "closure", "exposes", "interfaceVersion", "items", "digest"],
            document.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.Equal(["records/invoice", "records/payslip"], document.RootElement.GetProperty("exposes").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(3, document.RootElement.GetProperty("interfaceVersion").GetInt32());

        var plain = new PlatformPackageManifest(1, "tenant.release", "1.0.0", items, []);
        Assert.Null(plain.Exposure);
        Assert.Equal(PlatformPackageExporter.Export(plain),
            PlatformPackageExporter.Export(new PlatformPackageManifest(1, "tenant.release", "1.0.0", items, [], exposure: null)));
        Assert.NotEqual(exported, PlatformPackageExporter.Export(plain));
    }

    // A consumer requires one positive interface version, and each exposed definition is named once.
    [Theory]
    [InlineData(1, new string[0])]
    [InlineData(1, new[] { "records/invoice", " " })]
    [InlineData(1, new[] { "records/invoice", "records/invoice" })]
    public void A_malformed_exposure_refuses_by_name(int interfaceVersion, string[] definitions)
    {
        var exception = Assert.Throws<ArgumentException>(() => new PlatformPackageManifest(1, "tenant.release", "1.0.0",
            [Entry("package", PlatformSeedStage.PackageRecord, "{}")], [], new PlatformPackageExposure(interfaceVersion, definitions)));
        Assert.StartsWith("platform-package-exposure-malformed", exception.Message, StringComparison.Ordinal);
        Assert.Equal("exposure", exception.ParamName);
    }

    [Fact]
    public void An_exposure_below_interface_version_one_or_without_definitions_refuses()
    {
        PlatformPackageItem[] items = [Entry("package", PlatformSeedStage.PackageRecord, "{}")];
        Assert.Equal("exposure", Assert.Throws<ArgumentOutOfRangeException>(() => new PlatformPackageManifest(1, "p", "1.0.0", items, [],
            new PlatformPackageExposure(0, ["records/invoice"]))).ParamName);
        Assert.Equal("exposure", Assert.Throws<ArgumentNullException>(() => new PlatformPackageManifest(1, "p", "1.0.0", items, [],
            new PlatformPackageExposure(1, null!))).ParamName);
    }

    [Fact]
    public void Content_classification_can_represent_an_unresolved_absence_without_a_payload()
    {
        var content = PlatformPackageContent.Unresolved("DES-0007.audit-entry-retention");

        Assert.Equal(PlatformPackageContentClassification.Unresolved, content.Classification);
        Assert.Equal("DES-0007.audit-entry-retention", content.Reference);
        Assert.Null(content.MediaType);
        Assert.True(content.Payload.IsEmpty);
    }

    [Fact]
    public async Task Replay_writes_an_empty_target_in_manifest_order()
    {
        var target = new RecordingTarget(isEmpty: true);
        var manifest = Manifest(
            Entry("package", PlatformSeedStage.PackageRecord, "{}"),
            Entry("types", PlatformSeedStage.SystemRecordTypes, "{}", "package"),
            Entry("workspace", PlatformSeedStage.Workspace, "{}", "types"));

        var result = await PlatformPackageReplayer.ReplayAsync(manifest, target);

        Assert.True(result.Succeeded);
        Assert.Null(result.RefusalCode);
        Assert.Equal(["package", "types", "workspace"], target.Writes);
    }

    [Fact]
    public async Task Replay_refuses_a_non_empty_target_before_writing()
    {
        var target = new RecordingTarget(isEmpty: false);

        var result = await PlatformPackageReplayer.ReplayAsync(Manifest(Entry("package", PlatformSeedStage.PackageRecord, "{}")), target);

        Assert.Equal("platform-seed-target-not-empty", result.RefusalCode);
        Assert.Empty(target.Writes);
    }

    [Fact]
    public async Task Replay_refuses_an_empty_manifest_before_calling_the_target()
    {
        var target = new RecordingTarget(isEmpty: true);

        var result = await PlatformPackageReplayer.ReplayAsync(Manifest(), target);

        Assert.Equal("platform-seed-manifest-empty", result.RefusalCode);
        Assert.Equal(0, target.ApplyAttempts);
    }

    [Fact]
    public async Task Replay_refuses_a_manifest_that_does_not_begin_with_the_package_record()
    {
        var target = new RecordingTarget(isEmpty: true);

        var result = await PlatformPackageReplayer.ReplayAsync(
            Manifest(Entry("types", PlatformSeedStage.SystemRecordTypes, "{}")), target);

        Assert.Equal("platform-seed-package-record-first", result.RefusalCode);
        Assert.Equal("types", result.ItemId);
        Assert.Equal(0, target.ApplyAttempts);
    }

    [Fact]
    public async Task Replay_refuses_more_than_one_package_record()
    {
        var target = new RecordingTarget(isEmpty: true);
        var manifest = Manifest(
            Entry("package", PlatformSeedStage.PackageRecord, "{}"),
            Entry("other-package", PlatformSeedStage.PackageRecord, "{}"));

        var result = await PlatformPackageReplayer.ReplayAsync(manifest, target);

        Assert.Equal("platform-seed-package-record-count-invalid", result.RefusalCode);
        Assert.Equal("other-package", result.ItemId);
        Assert.Equal(0, target.ApplyAttempts);
    }

    [Fact]
    public async Task Replay_refuses_reordered_stages_before_writing()
    {
        var target = new RecordingTarget(isEmpty: true);
        var manifest = Manifest(
            Entry("package", PlatformSeedStage.PackageRecord, "{}"),
            Entry("workspace", PlatformSeedStage.Workspace, "{}"),
            Entry("types", PlatformSeedStage.SystemRecordTypes, "{}"));

        var result = await PlatformPackageReplayer.ReplayAsync(manifest, target);

        Assert.Equal("platform-seed-stage-order-invalid", result.RefusalCode);
        Assert.Equal("types", result.ItemId);
        Assert.Empty(target.Writes);
    }

    [Fact]
    public async Task Replay_refuses_an_unknown_bootstrap_stage_before_writing()
    {
        var target = new RecordingTarget(isEmpty: true);
        var manifest = Manifest(
            Entry("package", PlatformSeedStage.PackageRecord, "{}"),
            Entry("unknown", (PlatformSeedStage)99, "{}"));

        var result = await PlatformPackageReplayer.ReplayAsync(manifest, target);

        Assert.Equal("platform-seed-stage-unknown", result.RefusalCode);
        Assert.Equal("unknown", result.ItemId);
        Assert.Equal(0, target.ApplyAttempts);
    }

    [Fact]
    public async Task Replay_refuses_duplicate_item_ids_before_writing()
    {
        var target = new RecordingTarget(isEmpty: true);
        var manifest = Manifest(
            Entry("package", PlatformSeedStage.PackageRecord, "{}"),
            Entry("package", PlatformSeedStage.PackageRecord, "{}"));

        var result = await PlatformPackageReplayer.ReplayAsync(manifest, target);

        Assert.Equal("platform-seed-item-duplicate", result.RefusalCode);
        Assert.Equal("package", result.ItemId);
        Assert.Empty(target.Writes);
    }

    [Theory]
    [InlineData("missing", "platform-seed-dependency-missing")]
    [InlineData("later", "platform-seed-dependency-not-replayed")]
    public async Task Replay_refuses_missing_or_forward_dependencies_before_writing(string dependency, string expectedCode)
    {
        var target = new RecordingTarget(isEmpty: true);
        var items = dependency == "missing"
            ? new[] { Entry("package", PlatformSeedStage.PackageRecord, "{}", "unknown") }
            : new[]
            {
                Entry("package", PlatformSeedStage.PackageRecord, "{}", "types"),
                Entry("types", PlatformSeedStage.SystemRecordTypes, "{}"),
            };

        var result = await PlatformPackageReplayer.ReplayAsync(Manifest(items), target);

        Assert.Equal(expectedCode, result.RefusalCode);
        Assert.Equal("package", result.ItemId);
        Assert.Empty(target.Writes);
    }

    [Fact]
    public async Task Replay_refuses_unresolved_content_without_inventing_the_policy()
    {
        var target = new RecordingTarget(isEmpty: true);
        var unresolved = new PlatformPackageItem(
            "audit-retention",
            PlatformSeedStage.SealedDefinitions,
            [],
            PlatformPackageContent.Unresolved("DES-0007.audit-entry-retention"));

        var result = await PlatformPackageReplayer.ReplayAsync(
            Manifest(Entry("package", PlatformSeedStage.PackageRecord, "{}"), unresolved), target);

        Assert.Equal("platform-seed-content-unresolved", result.RefusalCode);
        Assert.Equal("audit-retention", result.ItemId);
        Assert.Empty(target.Writes);
    }

    private static PlatformPackageManifest Manifest(params PlatformPackageItem[] items) =>
        new(1, "harborline.platform", "2026.09.16", items);

    private static PlatformPackageItem Entry(string id, PlatformSeedStage stage, string json, params string[] dependencies) =>
        new(id, stage, dependencies, PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(json)));

    private sealed class RecordingTarget(bool isEmpty) : IPlatformPackageReplayTarget
    {
        public List<string> Writes { get; } = [];
        public int ApplyAttempts { get; private set; }

        public ValueTask<bool> TryApplyToEmptyAsync(IReadOnlyList<PlatformPackageItem> items, CancellationToken cancellationToken = default)
        {
            ApplyAttempts++;
            if (!isEmpty) return ValueTask.FromResult(false);
            Writes.AddRange(items.Select(item => item.Id));
            return ValueTask.FromResult(true);
        }
    }
}
