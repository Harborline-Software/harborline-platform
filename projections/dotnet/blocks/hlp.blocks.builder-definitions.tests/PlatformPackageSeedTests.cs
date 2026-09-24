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
