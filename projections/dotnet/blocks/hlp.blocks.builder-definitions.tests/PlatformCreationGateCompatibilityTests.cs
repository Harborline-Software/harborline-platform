using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// Plan W3 under decision K6 (approved 2026-10-10): the released platform package revision 1.0.0 keeps its bytes and
/// signatures, including the ck-14 default spelled <c>"form-only"</c>; that exact spelling is read by a version-specific
/// reader for 1.0.0 only, every new revision spells <c>form_only</c>, unknown values fail, and an absent value fails
/// because no published revision defines a default for it. Oracles are literals.
/// </summary>
public sealed class PlatformCreationGateCompatibilityTests
{
    [Fact]
    [Trait("Holds", "platform-package-ck-14")]
    public void The_released_export_keeps_its_legacy_spelling_and_reads_as_form_only()
    {
        var export = PlatformPackageSeed.LoadCheckedInExport();

        Assert.Contains("\"creationGate\":\"form-only\"", Encoding.UTF8.GetString(export), StringComparison.Ordinal);
        Assert.DoesNotContain("\"creationGate\":\"form_only\"", Encoding.UTF8.GetString(export), StringComparison.Ordinal);
        Assert.Equal("1.0.0", PlatformPackageSeed.Manifest.Revision);
        Assert.True(PlatformPackageSeed.VerifyCheckedInExport());
        Assert.Equal(export, PlatformPackageSeed.Export());
        Assert.Equal(RecordCreationGate.FormOnly, PlatformCreationGateCompatibility.ReadCreationGate(export));
        Assert.Equal(RecordCreationGate.FormOnly, PlatformPackageSeed.CreationGate);
    }

    [Theory]
    [InlineData("1.0.0", "form-only")]
    [InlineData("1.0.1", "form_only")]
    [InlineData("1.1.0", "form_only")]
    [InlineData("2.0.0", "form_only")]
    public void The_seed_writes_the_released_spelling_only_for_the_released_revision(string revision, string spelling) =>
        Assert.Equal(spelling, PlatformCreationGateCompatibility.SeedSpelling(revision));

    [Theory]
    [InlineData("1.1.0", "form_only", "FormOnly")]
    [InlineData("1.1.0", "role", "Role")]
    [InlineData("1.1.0", "engine", "Engine")]
    [InlineData("2.0.0", "form_only", "FormOnly")]
    public void A_new_revision_reads_the_exact_literal_set(string revision, string spelling, string gate) =>
        Assert.Equal(Enum.Parse<RecordCreationGate>(gate),
            PlatformCreationGateCompatibility.ReadCreationGate(Export(revision, JsonValue.Create(spelling))));

    [Theory]
    [InlineData("1.0.0", "form_only")]
    [InlineData("1.0.0", "role")]
    [InlineData("1.0.0", "engine")]
    [InlineData("1.0.0", "FORM-ONLY")]
    [InlineData("1.0.0", "form-only ")]
    [InlineData("1.1.0", "form-only")]
    [InlineData("1.1.0", "Form_Only")]
    [InlineData("1.1.0", "FORM_ONLY")]
    [InlineData("1.1.0", "Role")]
    [InlineData("1.1.0", "nobody")]
    [InlineData("1.1.0", "")]
    public void A_spelling_outside_its_revisions_set_fails(string revision, string spelling)
    {
        var refused = Assert.Throws<InvalidDataException>(() =>
            PlatformCreationGateCompatibility.ReadCreationGate(Export(revision, JsonValue.Create(spelling))));
        Assert.Equal("platform-package-creation-gate-malformed", refused.Message);
    }

    public static TheoryData<string> MalformedShapes =>
        ["absent", "null", "number", "array", "version-mismatch", "no-revision", "duplicate-ck-14", "no-ck-14", "not-json"];

    [Theory]
    [MemberData(nameof(MalformedShapes))]
    public void An_absent_or_malformed_default_fails_for_every_revision(string shape)
    {
        foreach (var revision in new[] { "1.0.0", "1.1.0" })
        {
            var export = shape switch
            {
                "absent" => Export(revision, null, remove: true),
                "null" => Export(revision, null),
                "number" => Export(revision, JsonValue.Create(0)),
                "array" => Export(revision, new JsonArray(JsonValue.Create("form_only"))),
                "version-mismatch" => Export(revision, JsonValue.Create(PlatformCreationGateCompatibility.SeedSpelling(revision)), packageVersion: "9.9.9"),
                "no-revision" => Relabelled(revision, root => root.AsObject().Remove("revision")),
                "duplicate-ck-14" => Relabelled(revision, root => root["items"]!.AsArray().Add(Item(root, "platform-package-ck-14")!.DeepClone())),
                "no-ck-14" => Relabelled(revision, root => root["items"]!.AsArray().Remove(Item(root, "platform-package-ck-14"))),
                _ => Encoding.UTF8.GetBytes("{\"revision\":\"" + revision + "\",\"items\":["),
            };

            var refused = Assert.Throws<InvalidDataException>(() => PlatformCreationGateCompatibility.ReadCreationGate(export));
            Assert.Equal("platform-package-creation-gate-malformed", refused.Message);
        }
    }

    // The checked-in export re-labelled as revision with the ck-14 creationGate replaced (or removed); bytes need not be canonical.
    private static byte[] Export(string revision, JsonNode? creationGate, bool remove = false, string? packageVersion = null) =>
        Mutate(root =>
        {
            root["revision"] = revision;
            Item(root, "platform-package-ck-1")!["content"]!["payload"]!["version"] = packageVersion ?? revision;
            var defaults = Item(root, "platform-package-ck-14")!["content"]!["payload"]!.AsObject();
            if (remove) defaults.Remove("creationGate");
            else defaults["creationGate"] = creationGate;
        });

    // The export labelled as revision with that revision's own valid spelling, then changed: every shape is exercised for
    // both revisions on input that differs only by its revision.
    private static byte[] Relabelled(string revision, Action<JsonNode> change) =>
        Mutate(root =>
        {
            root["revision"] = revision;
            Item(root, "platform-package-ck-1")!["content"]!["payload"]!["version"] = revision;
            Item(root, "platform-package-ck-14")!["content"]!["payload"]!["creationGate"] = PlatformCreationGateCompatibility.SeedSpelling(revision);
            change(root);
        });

    private static byte[] Mutate(Action<JsonNode> change)
    {
        var root = JsonNode.Parse(PlatformPackageSeed.LoadCheckedInExport())!;
        change(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static JsonNode? Item(JsonNode root, string id) =>
        root["items"]!.AsArray().First(item => item!["id"]!.GetValue<string>() == id);
}
