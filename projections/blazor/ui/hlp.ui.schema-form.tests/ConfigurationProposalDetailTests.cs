using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// T-461. The Blazor half of the one Records-and-Forms example. It renders the released
/// <c>configurationProposalDetail</c> Form from the exported platform pack over the released
/// bindings in <c>conformance/hlp.blocks.builder-definitions/proposal.json</c> — the same pack and
/// the same fixture the React renderer test consumes, so both lanes complete one example rather
/// than two similar ones.
/// </summary>
public sealed class ConfigurationProposalDetailTests : BunitContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("autosaved-one-of-two")]
    [InlineData("proposed")]
    [InlineData("saved")]
    [InlineData("released")]
    [InlineData("check-invalidated-by-a-later-edit")]
    [InlineData("released-a-superseded-saved-version")]
    [InlineData("released-against-a-stale-baseline")]
    public void The_records_and_forms_example_renders_through_the_released_definition(string step)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "_shared/packs/platform/platform-pack.export.json")));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/hlp.blocks.builder-definitions/proposal.json")));
        var payload = pack.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "platform-package-ck-7").GetProperty("content").GetProperty("payload");
        var view = payload.GetProperty("configurationProposalDetail").Deserialize<SchemaFormView>(JsonOptions)!;
        var testCase = fixture.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("step").GetString() == step);
        var status = testCase.GetProperty("status").GetString()!;
        var values = testCase.GetProperty("values").EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.GetString());

        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view).Add(component => component.Values, values)
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));

        // Acceptance 6: the domain-facing vocabulary is on the surface, from the released pack.
        Assert.Contains("Proposed change", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(payload.GetProperty("configurationProposalStatuses").GetProperty(status)
            .GetProperty("values").GetProperty("en").GetString(), cut.Find("#status").TextContent);
        foreach (var pair in values) Assert.Equal(pair.Value, cut.Find($"#{pair.Key}").TextContent);
        Assert.Empty(cut.FindAll("input,select,textarea,button"));

        // Acceptance 1: the proposed change names its baseline, and the effective generation is
        // unchanged while it is edited. Only the stale case, where the tenant moved on underneath
        // the author, shows a different effective generation — and it refuses rather than releasing.
        Assert.Equal(fixture.RootElement.GetProperty("baselineDigest").GetString(), cut.Find("#baselineDigest").TextContent);
        if (step == "released-against-a-stale-baseline")
        {
            Assert.NotEqual(cut.Find("#baselineDigest").TextContent, cut.Find("#effectiveDigest").TextContent);
            Assert.Contains("configuration-baseline-stale", cut.Find("#refusals").TextContent, StringComparison.Ordinal);
        }
        else Assert.Equal(cut.Find("#baselineDigest").TextContent, cut.Find("#effectiveDigest").TextContent);

        // Acceptance 2 and 3: only a Saved version carries authorship and rationale, and only a
        // successful release shows a Released package digest.
        if (status == "proposed")
        {
            Assert.Equal("", cut.Find("#savedVersion").TextContent);
            Assert.Equal("", cut.Find("#savedBy").TextContent);
        }
        else
        {
            Assert.Equal("dana.okafor", cut.Find("#savedBy").TextContent);
            Assert.NotEqual("", cut.Find("#rationale").TextContent);
        }
        if (status == "released")
        {
            // Acceptance 5: the digest on the surface is the exported artifact's digest.
            Assert.Contains(fixture.RootElement.GetProperty("releasedPackageDigest").GetString()!,
                cut.Find("#releasedPackage").TextContent, StringComparison.Ordinal);
            Assert.Equal("", cut.Find("#refusals").TextContent);
        }
        else
        {
            Assert.Equal("", cut.Find("#releasedPackage").TextContent);
            Assert.NotEqual("Released package", cut.Find("#status").TextContent);
        }
        if (step == "check-invalidated-by-a-later-edit")
        {
            Assert.Contains("Invalidated by a later edit", cut.Find("#checkState").TextContent, StringComparison.Ordinal);
            Assert.Contains("configuration-check-invalidated", cut.Find("#refusals").TextContent, StringComparison.Ordinal);
        }
    }
}
