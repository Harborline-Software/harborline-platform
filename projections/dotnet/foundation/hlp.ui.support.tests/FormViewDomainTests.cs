using System.Text.Json;
using Harborline.Contracts.Forms;
using Harborline.Foundation.Forms.UI;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

public sealed class FormViewDomainTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Canonical_membership_and_redaction_bound_renderer_options(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        AssertFixture(document.RootElement);
    }

    public static IEnumerable<object[]> Cases()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Repository root was not found.");
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "conformance", "hlp.ui.form-view", "fixtures.yaml")));
        var cases = fixtures.RootElement.GetProperty("cases").EnumerateArray()
            .Where(value => value.GetProperty("id").GetString()!.StartsWith("form-view.domain-", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(new[]
        {
            "form-view.domain-permitted", "form-view.domain-empty", "form-view.domain-unreadable",
            "form-view.domain-sensitive", "form-view.domain-redacted-host-options",
        }, cases.Select(value => value.GetProperty("id").GetString()));
        foreach (var value in cases) yield return [value.GetRawText()];
    }

    internal static void AssertFixture(JsonElement fixture)
    {
        var input = fixture.GetProperty("input");
        var expected = fixture.GetProperty("expected");
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var field = input.GetProperty("field").Deserialize<FormViewField>(jsonOptions)!;
        var before = JsonSerializer.Serialize(field, jsonOptions);
        var hints = new FormViewRendererHints(
            Options: input.GetProperty("hostOptions").EnumerateArray()
                .Select(value => new FormViewOption(value.GetString()!, value.GetString())).ToArray(),
            ReadOnly: false);

        var result = FormViewBinding.Normalize(field, hints);

        Assert.NotNull(result.Options);
        Assert.Equal(expected.GetProperty("options").EnumerateArray().Select(value => value.GetString()),
            result.Options.Select(option => option.Value));
        if (expected.TryGetProperty("permittedValues", out var permitted))
        {
            Assert.True(result.Canonical.PermittedValues.HasValue);
            Assert.Equal(permitted.EnumerateArray().Select(value => value.GetString()),
                result.Canonical.PermittedValues.Value);
        }
        else Assert.False(result.Canonical.PermittedValues.HasValue);
        Assert.Equal(field.ControlHint, result.Canonical.ControlHint);
        Assert.Equal(before, JsonSerializer.Serialize(field, jsonOptions));
        if (expected.GetProperty("redacted").GetBoolean())
        {
            Assert.False(result.Canonical.Value.HasValue);
            Assert.True(result.ReadOnly);
            Assert.DoesNotContain("secret", JsonSerializer.Serialize(result, jsonOptions), StringComparison.Ordinal);
        }
    }
}
