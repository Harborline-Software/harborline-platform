using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// T-463. The Blazor half of the one Records-and-Rules example. It renders the released
/// <c>verificationRunDetail</c> Form from the exported platform pack over the released bindings in
/// <c>conformance/hlp.blocks.builder-definitions/verification.json</c> — the same pack and the same
/// fixture the React renderer test consumes, so both lanes run one canonical suite.
/// </summary>
public sealed class VerificationRunDetailTests : BunitContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DirectoryInfo Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        return root!;
    }

    private static JsonDocument Fixture() => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(Root().FullName, "conformance/hlp.blocks.builder-definitions/verification.json")));

    [Theory]
    [InlineData("passed")]
    [InlineData("business-rule-defect")]
    [InlineData("authorization-defect")]
    public void The_records_and_rules_run_renders_through_the_released_definition(string step)
    {
        using var pack = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Root().FullName, "_shared/packs/platform/platform-pack.export.json")));
        using var fixture = Fixture();
        var payload = pack.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "platform-package-ck-7").GetProperty("content").GetProperty("payload");
        var view = payload.GetProperty("verificationRunDetail").Deserialize<SchemaFormView>(JsonOptions)!;
        var run = fixture.RootElement.GetProperty("runs").EnumerateArray()
            .Single(item => item.GetProperty("step").GetString() == step);
        var status = run.GetProperty("status").GetString()!;
        var values = run.GetProperty("values").EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.GetString());

        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view).Add(component => component.Values, values)
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));

        // Acceptance 6: the vocabulary is the released one, from the pack, not authored here.
        Assert.Contains("Verification run", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(payload.GetProperty("verificationRunStatuses").GetProperty(status)
            .GetProperty("values").GetProperty("en").GetString(), cut.Find("#status").TextContent);
        foreach (var pair in values) Assert.Equal(pair.Value, cut.Find($"#{pair.Key}").TextContent);
        Assert.Empty(cut.FindAll("input,select,textarea,button"));

        // Acceptance 5: the receipt on the surface binds candidate, baseline, suite, fixtures and engines.
        Assert.Equal(fixture.RootElement.GetProperty("candidateDigest").GetString(), cut.Find("#candidateDigest").TextContent);
        Assert.Equal(fixture.RootElement.GetProperty("baselineDigest").GetString(), cut.Find("#baselineDigest").TextContent);
        Assert.Contains(fixture.RootElement.GetProperty("suiteDigest").GetString()!, cut.Find("#suite").TextContent, StringComparison.Ordinal);
        Assert.Contains(fixture.RootElement.GetProperty("catalogueDigest").GetString()!, cut.Find("#engines").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, cut.Find("#fixtures").TextContent.Split('\n').Length);

        // Acceptance 2: the declared inputs are on the surface, so a reader can see what was controlled.
        foreach (var declared in new[] { "Europe/London", "en-GB", "seed=t-463-invoice", "ordering=ordinal-by-key" })
            Assert.Contains(declared, cut.Find("#determinism").TextContent, StringComparison.Ordinal);

        // Acceptance 7: each defect fails its own claim and not the other one.
        var failures = cut.Find("#failures").TextContent;
        if (step == "passed")
        {
            Assert.Equal("", failures);
            Assert.DoesNotContain("Failed", cut.Find("#cases").TextContent, StringComparison.Ordinal);
        }
        else if (step == "business-rule-defect")
        {
            Assert.Contains("invoice-total[ten-at-one-hundred] record.number:/total: expected 1000, actual 110", failures, StringComparison.Ordinal);
            Assert.DoesNotContain("approval-authority", failures, StringComparison.Ordinal);
            Assert.Contains("approval-authority — Only an approver may create an invoice already marked approved.: Passed",
                cut.Find("#cases").TextContent, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("approval-authority authorization.decision: expected \"refused\", actual \"allowed\"", failures, StringComparison.Ordinal);
            Assert.DoesNotContain("invoice-total", failures, StringComparison.Ordinal);
        }
    }

    // Acceptance 4: an empty case is refused when it is authored, so no run of it reaches a surface.
    [Fact]
    public void A_case_that_asserts_nothing_is_refused_before_it_can_be_run()
    {
        using var fixture = Fixture();
        var refusals = fixture.RootElement.GetProperty("emptyCase").GetProperty("refusals").EnumerateArray().ToArray();
        Assert.Single(refusals);
        Assert.Equal("verification-assertion-required", refusals[0].GetProperty("code").GetString());
        var cases = fixture.RootElement.GetProperty("suite").GetProperty("cases").EnumerateArray().ToArray();
        Assert.DoesNotContain(cases, item => item.GetProperty("caseId").GetString()
            == fixture.RootElement.GetProperty("emptyCase").GetProperty("caseId").GetString());
        Assert.All(cases, item => Assert.NotEmpty(item.GetProperty("assertions").EnumerateArray()));
    }
}
