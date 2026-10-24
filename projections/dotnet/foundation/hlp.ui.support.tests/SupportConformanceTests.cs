using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Harborline.Contracts.Forms;
using Harborline.Foundation;
using Harborline.Foundation.Builder;
using Harborline.Foundation.Forms.UI;
using Harborline.Foundation.Localization;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

public sealed class AspectLensConformanceTests
{
    [Fact]
    public void NativeContractPreservesHostCallbacksAndFailClosedProvenance()
    {
        var selected = new List<string>();
        var model = new CanvasModel(
            [new("root", "section", "Root", 0, null), new("child", "field", "Child", 1, "root")],
            null,
            selected.Add);
        model.Select("child");
        Assert.Equal(["child"], selected);
        Assert.Equal(10, Enum.GetValues<LensTone>().Length);
        Assert.Equal(2, Enum.GetValues<AspectLensKind>().Length);
        Assert.Equal(5, Enum.GetValues<ProvenanceSource>().Length);
        var unresolved = new ProvenanceInfo(ProvenanceSource.Unknown, [ProvenanceSource.Unknown], false, true, false);
        Assert.True(unresolved.Locked);
        Assert.False(unresolved.Resolved);
    }

    [Theory]
    [Trait("ModuleConformance", "hlp.ui.aspect-lens")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.aspect-lens", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            using var fixture = Fixture.Read("aspect-lens.");
            if (fixture is null) return;
            var id = fixture.RootElement.GetProperty("id").GetString();
            Assert.Contains(id, Fixture.CaseIds("hlp.ui.aspect-lens"));
            Assert.Equal(9, typeof(CanvasNode).Assembly.GetTypes().Count(type => type.Namespace == "Harborline.Foundation.Builder" &&
                type.Name is "CanvasNode" or "CanvasModel" or "LensTone" or "AspectState" or "AspectEdge" or "AspectLens" or "ProvenanceSource" or "ProvenanceInfo" or "ProvenanceResolver"));
        });
    }
}

public sealed class CssClassComposerConformanceTests
{
    [Fact]
    public void NativeContractMatchesRepresentativeClsxAndTailwindMergeCases()
    {
        Assert.Equal("button active", CssClassComposer.Combine("button", false, null, "active", ""));
        Assert.Equal("root child leaf", CssClassComposer.Combine("root", new object?[] { "child", new[] { "leaf" } }));
        Assert.Equal("enabled selected", CssClassComposer.Combine(new[] { new KeyValuePair<string, bool>("enabled", true), new("disabled", false), new("selected", true) }));
        Assert.Equal("p-4 text-lg", CssClassComposer.Combine("p-2", "text-sm", "p-4", "text-lg"));
        Assert.Equal("hover:bg-blue-500 focus:bg-green-500", CssClassComposer.Combine("hover:bg-red-500", "hover:bg-blue-500", "focus:bg-green-500"));
        Assert.Equal("p-1 md:p-6 lg:p-8", CssClassComposer.Combine("md:p-2", "p-1", "md:p-6", "lg:p-8"));
        Assert.Equal("w-[2rem] h-[12px]", CssClassComposer.Combine("w-[12px]", "w-[2rem]", "h-[12px]"));
        Assert.Equal("p-4! p-6", CssClassComposer.Combine("p-2", "p-4!", "p-6"));
        Assert.Equal("-mt-4 mx-1", CssClassComposer.Combine("mt-2", "-mt-4", "mx-1"));
        Assert.Equal("block custom custom", CssClassComposer.Combine("block", "block", "custom", "custom"));
    }

    [Theory]
    [Trait("ModuleConformance", "hlp.ui.cn")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.cn", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            using var fixture = Fixture.Read("cn.");
            if (fixture is null) return;
            var id = fixture.RootElement.GetProperty("id").GetString();
            Assert.Contains(id, Fixture.CaseIds("hlp.ui.cn"));
            var expected = id switch
            {
                "cn.strings" => CssClassComposer.Combine("button", "primary"),
                "cn.nested-arrays" => CssClassComposer.Combine("root", new object?[] { "child", new[] { "leaf" } }),
                "cn.object-map" => CssClassComposer.Combine(new[] { new KeyValuePair<string, bool>("enabled", true), new("disabled", false), new("selected", true) }),
                "cn.tailwind-conflict" => CssClassComposer.Combine("p-2", "text-sm", "p-4", "text-lg"),
                "cn.modifier-conflict" => CssClassComposer.Combine("hover:bg-red-500", "hover:bg-blue-500", "focus:bg-green-500"),
                "cn.responsive-conflict" => CssClassComposer.Combine("md:p-2", "p-1", "md:p-6", "lg:p-8"),
                "cn.arbitrary-value-conflict" => CssClassComposer.Combine("w-[12px]", "w-[2rem]", "h-[12px]"),
                "cn.important-modifier" => CssClassComposer.Combine("p-2", "p-4!", "p-6"),
                "cn.negative-utility" => CssClassComposer.Combine("mt-2", "-mt-4", "mx-1"),
                "cn.duplicate" => CssClassComposer.Combine("block", "block", "custom", "custom"),
                "cn.arbitrary-token" => CssClassComposer.Combine("custom-token", "tenant-theme", "data-[open=true]:block"),
                "cn.empty" => CssClassComposer.Combine(false, null, ""),
                "cn.projection-equivalence" => CssClassComposer.Combine("rtl:ms-2", "rtl:ms-4", "forced-colors:border", "theme-token"),
                _ => "button active",
            };
            var expectedNode = fixture.RootElement.GetProperty("expected");
            var fixtureValue = expectedNode.ValueKind == JsonValueKind.String ? expectedNode.GetString() : expectedNode.GetProperty("value").GetString();
            Assert.Equal(fixtureValue, expected);
        });
    }
}

public sealed class DefaultStringsConformanceTests
{
    [Fact]
    public void NativeCatalogHasExactDigestAndInterpolation()
    {
        Assert.Equal(732, HarborlineDefaultStrings.Values.Count);
        Assert.Equal("Loading", HarborlineDefaultStrings.Get("common.loading"));
        Assert.Equal("Step 2 of {total}", HarborlineDefaultStrings.Interpolate("Step {current} of {total}", new Dictionary<string, object?> { ["current"] = 2 }));
        Assert.Equal("abe7a3d3cd49ff3d2ab9ad75df1638774d84f1dae90d945dfb68042cd800a5c9", CatalogDigest());
    }

    // Revision 2 appends 19 platform keys; revision 1 is every other entry, by identity and in catalog order.
    private static readonly string[] Revision2Keys = ["forms.switch.off", "forms.switch.on", "navigation.notifications.all", "navigation.notifications.bell.pending.one", "navigation.notifications.bell.pending.other", "navigation.notifications.bell.unread.one", "navigation.notifications.bell.unread.other", "navigation.notifications.confirm", "navigation.notifications.deny", "navigation.notifications.dismiss", "navigation.notifications.empty", "navigation.notifications.groups", "navigation.notifications.markAllRead", "navigation.notifications.title", "navigation.notifications.unread", "navigation.notifications.viewAll", "navigation.userMenu.panel", "navigation.userMenu.signOut", "navigation.userMenu.trigger"];

    [Fact]
    public void RevisionOneIsPreservedExactly()
    {
        var revision1 = HarborlineDefaultStrings.Values.Where(pair => !Revision2Keys.Contains(pair.Key)).ToArray();
        Assert.Equal(713, revision1.Length);
        Assert.Equal("73c7cec0b07f0058d0a48784e76fe8aa86f8e421f85716ff2ea9cc53975c3f7c", Digest(revision1));
        Assert.Equal(Revision2Keys, HarborlineDefaultStrings.Values.Keys.Skip(713).ToArray());
    }

    [Theory]
    [Trait("ModuleConformance", "hlp.ui.default-strings")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.default-strings", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            using var fixture = Fixture.Read("default-strings.");
            if (fixture is null) return;
            var id = fixture.RootElement.GetProperty("id").GetString();
            Assert.Contains(id, Fixture.CaseIds("hlp.ui.default-strings"));
            switch (id)
            {
                case "default-strings.keyset": Assert.Equal(fixture.RootElement.GetProperty("expected").GetProperty("count").GetInt32(), HarborlineDefaultStrings.Values.Count); break;
                case "default-strings.digest": Assert.Equal(fixture.RootElement.GetProperty("expected").GetProperty("sha256").GetString(), CatalogDigest()); break;
                case "default-strings.revision-1-subset":
                {
                    var revision1 = HarborlineDefaultStrings.Values.Where(pair => !Revision2Keys.Contains(pair.Key)).ToArray();
                    var expected = fixture.RootElement.GetProperty("expected");
                    Assert.Equal(expected.GetProperty("count").GetInt32(), revision1.Length);
                    Assert.Equal(expected.GetProperty("first").GetString(), revision1[0].Key);
                    Assert.Equal(expected.GetProperty("last").GetString(), revision1[^1].Key);
                    Assert.Equal(expected.GetProperty("sha256").GetString(), Digest(revision1));
                    break;
                }
                case "default-strings.revision-2-additions":
                {
                    var expected = fixture.RootElement.GetProperty("expected").GetProperty("entries").EnumerateArray().Select(entry => (entry[0].GetString(), entry[1].GetString())).ToArray();
                    Assert.Equal(expected, HarborlineDefaultStrings.Values.Where(pair => Revision2Keys.Contains(pair.Key)).Select(pair => ((string?)pair.Key, (string?)pair.Value)).ToArray());
                    break;
                }
                case "default-strings.english-fallback": Assert.Equal("Loading", HarborlineDefaultStrings.Get("common.loading")); break;
                case "default-strings.interpolate-string": Assert.Equal("Remove Invoice", HarborlineDefaultStrings.Interpolate("Remove {label}", new Dictionary<string, object?> { ["label"] = "Invoice" })); break;
                case "default-strings.interpolate-number": Assert.Equal("Page 12", HarborlineDefaultStrings.Interpolate("Page {page}", new Dictionary<string, object?> { ["page"] = 12 })); break;
                case "default-strings.missing-variable": Assert.Equal("Step 2 of {total}", HarborlineDefaultStrings.Interpolate("Step {current} of {total}", new Dictionary<string, object?> { ["current"] = 2 })); break;
                case "default-strings.no-variables": Assert.Equal("Loading", HarborlineDefaultStrings.Interpolate("Loading")); break;
                default: Assert.NotEmpty(HarborlineDefaultStrings.Values); break;
            }
        });
    }

    private static string CatalogDigest() => Digest(HarborlineDefaultStrings.Values);

    // The original revision-1 serialization: [key, value] rows, relaxed escaping, SHA-256, lowercase hex.
    private static readonly JsonSerializerOptions DigestOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Digest(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var rows = entries.Select(pair => new object[] { pair.Key, pair.Value });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows, DigestOptions))));
    }
}

public sealed class FormViewConformanceTests
{
    [Fact]
    public void NativeBindingUsesCanonicalContractsAndExactLocaleResolution()
    {
        var text = new InternationalizedText { DefaultLocale = "en", Values = new Dictionary<string, string> { ["en"] = "Amount", ["ar"] = "المبلغ" } };
        Assert.Equal("المبلغ", FormViewText.Resolve(text, ["ar-AE"]));
        Assert.Equal("Amount", FormViewText.Resolve(text, ["fr-CA"]));
        Assert.Equal("Field", FormViewText.Resolve(null, ["fr-CA"], "Field"));

        var field = new FormViewField
        {
            Name = "amount",
            Label = text,
            IsSensitive = true,
            IsReadable = false,
            Value = Optional<JsonElement>.Some(JsonDocument.Parse("\"must-not-leak\"").RootElement.Clone()),
            Rules = new FormViewFieldRules { Visible = true, Required = true, ReadOnly = false },
        };
        var normalized = FormViewBinding.Normalize(field, new(
            Required: false,
            ValueKind: "decimal-string",
            ReadOnly: false,
            Presentation: new PresentationOutcome { StyleToken = Optional<string>.Some("hint") }));
        Assert.True(normalized.ReadOnly);
        Assert.True(normalized.Required);
        Assert.Equal("decimal-string", normalized.ValueKind);
        Assert.False(normalized.Canonical.Value.HasValue);
        Assert.True(normalized.Presentation?.StyleToken.HasValue);

        var rulePresentation = field with
        {
            IsSensitive = false,
            IsReadable = true,
            Value = Optional<JsonElement>.Some(JsonDocument.Parse("\"approved\"").RootElement.Clone()),
            Rules = new FormViewFieldRules
            {
                Visible = true,
                Required = false,
                ReadOnly = false,
                PresentationSeverity = Optional<Severity?>.Some(Severity.Warn),
            },
        };
        var merged = FormViewBinding.Normalize(rulePresentation, new(
            ReadOnly: true,
            Presentation: new PresentationOutcome { StyleToken = Optional<string>.Some("hint") }));
        Assert.False(merged.ReadOnly);
        Assert.Equal(Severity.Warn, merged.Presentation?.Severity.Value);
        Assert.Equal("hint", merged.Presentation?.StyleToken.Value);
        Assert.True(merged.Canonical.Value.HasValue);
    }

    [Theory]
    [Trait("ModuleConformance", "hlp.ui.form-view")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.form-view", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            using var fixture = Fixture.Read("form-view.");
            if (fixture is null) return;
            var id = fixture.RootElement.GetProperty("id").GetString();
            Assert.Contains(id, Fixture.CaseIds("hlp.ui.form-view"));
            if (id!.StartsWith("form-view.domain-", StringComparison.Ordinal))
            {
                FormViewDomainTests.AssertFixture(fixture.RootElement);
                return;
            }
            var text = new InternationalizedText { DefaultLocale = "en", Values = new Dictionary<string, string> { ["en"] = "Amount", ["fr-CA"] = "Montant", ["ar"] = "المبلغ" } };
            if (id == "form-view.locale-exact") Assert.Equal("Montant", FormViewText.Resolve(text, ["fr-CA"]));
            if (id == "form-view.locale-primary") Assert.Equal("المبلغ", FormViewText.Resolve(text, ["ar-AE"]));
            if (id == "form-view.locale-fallback") Assert.Equal("Field", FormViewText.Resolve(null, ["es-MX"], "Field"));
            Assert.Equal("Harborline.Contracts", typeof(FormView).Assembly.GetName().Name);
            Assert.Equal("Harborline.Foundation", typeof(FormViewBinding).Assembly.GetName().Name);
        });
    }
}

internal static class Fixture
{
    internal static JsonDocument? Read(string prefix)
    {
        var raw = SharedFixtureBatch.Current;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var document = JsonDocument.Parse(raw);
        var id = document.RootElement.GetProperty("id").GetString();
        if (id?.StartsWith(prefix, StringComparison.Ordinal) != true)
        {
            document.Dispose();
            return null;
        }
        return document;
    }

    internal static IReadOnlyList<string> CaseIds(string moduleId)
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "conformance", moduleId, "fixtures.yaml")));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(row => row.GetProperty("id").GetString()!).ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
