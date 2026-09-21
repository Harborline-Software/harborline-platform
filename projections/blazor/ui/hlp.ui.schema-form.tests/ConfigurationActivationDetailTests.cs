using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ConfigurationActivationDetailTests : BunitContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("released")]
    [InlineData("preparing")]
    [InlineData("refused")]
    [InlineData("effective")]
    public void Released_status_and_generation_bindings_agree_with_the_React_fixture(string status)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "_shared/packs/platform/platform-pack.export.json")));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/hlp.blocks.builder-definitions/activation.json")));
        var payload = pack.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "platform-package-ck-7").GetProperty("content").GetProperty("payload");
        var view = payload.GetProperty("configurationActivationDetail").Deserialize<SchemaFormView>(JsonOptions)!;
        var values = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("status").GetString() == status)
            .GetProperty("values").EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.GetString());
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view).Add(component => component.Values, values)
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));
        Assert.Contains("Configuration activation", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(payload.GetProperty("configurationActivationStatuses").GetProperty(status).GetProperty("values").GetProperty("en").GetString(),
            cut.Find("#status").TextContent);
        foreach (var pair in values) Assert.Equal(pair.Value, cut.Find($"#{pair.Key}").TextContent);
        Assert.Empty(cut.FindAll("input,select,textarea,button"));
        if (status == "effective")
        {
            Assert.Equal(values["candidateDigest"], values["effectiveDigest"]);
            Assert.Equal("", values["refusals"]);
        }
        else
        {
            Assert.Equal(values["expectedBaselineDigest"], values["effectiveDigest"]);
            Assert.NotEqual(values["candidateDigest"], values["effectiveDigest"]);
        }
        if (status == "refused")
        {
            Assert.NotEqual("Effective", cut.Find("#status").TextContent);
            Assert.Contains("projection-failed (forms/invoice)", cut.Find("#refusals").TextContent, StringComparison.Ordinal);
        }
    }
}
