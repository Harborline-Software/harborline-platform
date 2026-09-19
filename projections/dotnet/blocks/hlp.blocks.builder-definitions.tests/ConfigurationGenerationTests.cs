using System.Text.Json;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class ConfigurationGenerationTests
{
    [Fact]
    public void Producer_matches_the_pinned_cross_lane_read_fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/hlp.blocks.builder-definitions/generation.json")));
        var input = fixture.RootElement.GetProperty("input").Deserialize<ResolvedConfiguration>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var generation = ConfigurationGeneration.Resolve(input);
        Assert.Equal(fixture.RootElement.GetProperty("digest").GetString(), generation.Digest);
        var values = ConfigurationGenerationDetail.Bind(generation);
        foreach (var property in fixture.RootElement.GetProperty("values").EnumerateObject())
            Assert.Equal(property.Value.GetString(), values[property.Name]);
    }

    private static ConfigurationReference Ref(string key, char digest = 'a') => new(key, "1.0.0", new string(digest, 64));
    private static ResolvedConfiguration Input() => new("tenant-a", ["a", "b"],
        [new(Ref("a"), [Ref("form"), Ref("rule")], ["c"]),
         new(Ref("b", 'b'), [Ref("form", 'b')], ["c"]), new(Ref("c", 'c'), [Ref("shared")], [])],
        [new("form", "a"), new("rule", "a"), new("shared", "c")], Ref("platform"), [Ref("review"), Ref("retention")]);

    [Fact]
    public void Equivalent_resolutions_ignore_all_enumeration_order_and_culture()
    {
        var originalInput = Input();
        var input = originalInput with { Packages = [originalInput.Packages[0] with { Dependencies = ["c", "b"] }, originalInput.Packages[1], originalInput.Packages[2]] };
        var reordered = input with
        {
            ActivePackageKeys = input.ActivePackageKeys.Reverse().ToArray(),
            Packages = input.Packages.Reverse().Select(package => package with
            {
                Content = package.Content.Reverse().ToArray(), Dependencies = package.Dependencies.Reverse().ToArray(),
            }).ToArray(),
            Ownership = input.Ownership.Reverse().ToArray(), Policies = input.Policies.Reverse().ToArray(),
        };
        var expected = ConfigurationGeneration.Resolve(input);
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("tr-TR");
            var actual = ConfigurationGeneration.Resolve(reordered);
            Assert.Equal(expected.Digest, actual.Digest);
            Assert.Equal(expected.References.GetRawText(), actual.References.GetRawText());
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("package")]
    [InlineData("revision")]
    [InlineData("content")]
    [InlineData("dependency-content")]
    [InlineData("dependency-edge")]
    [InlineData("owner")]
    [InlineData("contract")]
    [InlineData("policy")]
    [InlineData("roots")]
    [InlineData("tenant")]
    public void Every_governed_dimension_changes_the_complete_digest(string dimension)
    {
        var input = Input();
        var packages = input.Packages.ToArray();
        var changed = dimension switch
        {
            "owner" => input with { Ownership = [new("form", "b"), new("rule", "a"), new("shared", "c")] },
            "contract" => input with { PlatformContract = Ref("platform", 'f') },
            "policy" => input with { Policies = [Ref("review", 'f'), Ref("retention")] },
            "roots" => input with { ActivePackageKeys = ["a", "b", "c"] },
            "tenant" => input with { TenantKey = "tenant-b" },
            _ => input with { Packages = packages },
        };
        if (dimension == "package") packages[1] = packages[1] with { Reference = Ref("b", 'f') };
        if (dimension == "revision") packages[1] = packages[1] with { Reference = Ref("b", 'b') with { Revision = "2.0.0" } };
        if (dimension == "content") packages[1] = packages[1] with { Content = [Ref("form", 'f')] };
        if (dimension == "dependency-content") packages[2] = packages[2] with { Content = [Ref("shared", 'f')] };
        if (dimension == "dependency-edge") packages[1] = packages[1] with { Dependencies = [] };
        Assert.NotEqual(ConfigurationGeneration.Resolve(input).Digest, ConfigurationGeneration.Resolve(changed).Digest);
    }

    [Fact]
    public void Hashing_one_package_cannot_identify_the_effective_generation()
    {
        var input = Input();
        var changed = input with { Policies = [Ref("review", 'f')] };
        Assert.Equal(input.Packages[0], changed.Packages[0]);
        Assert.NotEqual(ConfigurationGeneration.Resolve(input).Digest, ConfigurationGeneration.Resolve(changed).Digest);
        Assert.NotEqual(input.Packages[0].Reference.Digest, ConfigurationGeneration.Resolve(input).Digest);
        Assert.Throws<ArgumentException>(() => ConfigurationGeneration.Resolve(input with { Packages = [input.Packages[0]] }));
    }

    [Fact]
    public void Incomplete_ambiguous_or_invalid_references_are_refused()
    {
        var input = Input();
        var invalid = new[]
        {
            input with { ActivePackageKeys = [] },
            input with { ActivePackageKeys = ["missing"] },
            input with { ActivePackageKeys = ["a", "a"] },
            input with { Packages = [input.Packages[0], input.Packages[1]] },
            input with { Packages = [.. input.Packages, new(Ref("unused"), [], [])] },
            input with { Packages = [.. input.Packages, input.Packages[0]] },
            input with { Packages = [input.Packages[0] with { Content = [Ref("form"), Ref("form", 'b')] }, input.Packages[1], input.Packages[2]] },
            input with { Packages = [input.Packages[0] with { Dependencies = ["c", "c"] }, input.Packages[1], input.Packages[2]] },
            input with { Ownership = [new("form", "c"), new("rule", "a"), new("shared", "c")] },
            input with { Ownership = [new("form", "a")] },
            input with { Ownership = [.. input.Ownership, new("form", "b")] },
            input with { PlatformContract = Ref("platform") with { Digest = "not-a-digest" } },
            input with { Policies = [Ref("policy") with { Revision = "" }] },
            input with { Policies = [Ref("policy"), Ref("policy")] },
        };
        foreach (var candidate in invalid) Assert.Throws<ArgumentException>(() => ConfigurationGeneration.Resolve(candidate));
    }

    [Fact]
    public void Read_document_and_detail_are_detached_public_reference_snapshots()
    {
        var packages = Input().Packages.ToArray();
        var content = packages[0].Content.ToArray();
        var dependencies = packages[0].Dependencies.ToArray();
        packages[0] = packages[0] with { Content = content, Dependencies = dependencies };
        var generation = ConfigurationGeneration.Resolve(Input() with { Packages = packages });
        var before = JsonSerializer.Serialize(generation);
        packages[0] = new(Ref("changed"), [], []);
        content[0] = Ref("changed");
        dependencies[0] = "changed";
        Assert.Equal(before, JsonSerializer.Serialize(generation));
        var values = ConfigurationGenerationDetail.Bind(generation);
        Assert.Equal(generation.Digest, values["generationDigest"]);
        Assert.Equal(generation.References.GetProperty("packages").GetRawText(), values["packages"]);
        Assert.DoesNotContain("payload", before, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", before, StringComparison.OrdinalIgnoreCase);
        var definition = ConfigurationGenerationDetail.Definition;
        Assert.Equal("platform.detail.configuration-generation", definition.GetProperty("formId").GetString());
        Assert.Equal(values.Keys.Order(), definition.GetProperty("sections")[0].GetProperty("fields")
            .EnumerateArray().Select(field => field.GetProperty("name").GetString()!).Order());
        Assert.All(definition.GetProperty("sections")[0].GetProperty("fields").EnumerateArray(),
            field => Assert.True(field.GetProperty("readOnly").GetBoolean()));
    }
}
