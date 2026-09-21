using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ConfigurationGenerationDetailTests : BunitContext
{
    [Fact]
    public void Released_complete_generation_detail_agrees_with_the_React_fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "_shared/packs/platform/platform-pack.export.json")));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/hlp.blocks.builder-definitions/generation.json")));
        var definition = pack.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "platform-package-ck-7")
            .GetProperty("content").GetProperty("payload").GetProperty("configurationGenerationDetail");
        var view = definition.Deserialize<SchemaFormView>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var values = fixture.RootElement.GetProperty("values").EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.GetString());
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.Values, values)
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));
        Assert.Equal(fixture.RootElement.GetProperty("digest").GetString(), cut.Find("#generationDigest").TextContent);
        Assert.Contains("Effective configuration generation", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Individual package versions are constituent references.", cut.Markup, StringComparison.Ordinal);
        foreach (var pair in values) Assert.Equal(pair.Value, cut.Find($"#{pair.Key}").TextContent);
        Assert.Empty(cut.FindAll("input,select,textarea,button"));
    }
}
