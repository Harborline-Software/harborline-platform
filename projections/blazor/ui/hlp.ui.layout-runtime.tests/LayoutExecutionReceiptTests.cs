using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// T-583 slice 4, DES-0052 layout-eng-30: the Blazor lane of the receipt and linked Access trace. It renders the same
/// shared fixture the React probe does, whose traces LayoutExecutionObservationTests derives from the fixture's reads.
/// </summary>
public sealed class LayoutExecutionReceiptTests : BunitContext
{
    private static readonly JsonElement Observation = JsonElement.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "execution-observation.json")));
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static LayoutRunReceipt Receipt(string name = "receipt") => Observation.GetProperty(name).Deserialize<LayoutRunReceipt>(Web)!;
    private static LayoutAccessTrace Trace(string name) => Observation.GetProperty("traces").GetProperty(name).Deserialize<LayoutAccessTrace>(Web)!;

    private IRenderedComponent<HarborlineLayoutExecutionReceipt> RenderReceipt(LayoutRunReceipt receipt, Func<string, Task<LayoutAccessTrace>> read)
        => Render<HarborlineLayoutExecutionReceipt>(parameters => parameters.Add(x => x.Receipt, receipt).Add(x => x.ReadAccessTrace, read));

    private static void Toggle(IRenderedComponent<HarborlineLayoutExecutionReceipt> cut) => cut.Find("details").TriggerEvent("ontoggle", EventArgs.Empty);

    [Fact(DisplayName = "layout-eng-30: renders the receipt's run identity, status, execution trace and Access's four ordered, versioned stages from the shared fixture")]
    public void RendersTheReceiptAndFourOrderedVersionedStages()
    {
        var receipt = Receipt();
        string? asked = null;
        var cut = RenderReceipt(receipt, decision => { asked = decision; return Task.FromResult(Trace("valid")); });
        Assert.Equal(receipt.RunId, cut.Find("[data-layout-run-id]").TextContent);
        Assert.Equal(receipt.Status, cut.Find("[data-layout-run-status]").TextContent);
        Assert.Equal(["submitted: succeeded", "approval: succeeded", "posted: succeeded"], cut.FindAll("[data-layout-trace-ordinal]").Select(step => step.TextContent));
        Assert.Equal(receipt.AccessDecisionId, cut.Find("[data-layout-access-decision]").TextContent);
        Toggle(cut);
        Assert.Equal(receipt.AccessDecisionId, asked);
        Assert.Equal(["act", "effective-roles", "standings", "verdict"], cut.FindAll("[data-layout-access-stage]").Select(stage => stage.GetAttribute("data-layout-access-stage")));
        Assert.Equal("Version 2", cut.Find("[data-layout-access-version]").TextContent);
        Assert.Equal("Deciding grant: grant-163@1", cut.Find("[data-layout-deciding-grant]").TextContent);
    }

    [Fact(DisplayName = "layout-eng-30: the Access trace is read only when its disclosure opens; closing triggers no read, and a failed read retries")]
    public void TheTraceIsReadOnlyWhenTheDisclosureOpens()
    {
        var reads = 0;
        var cut = RenderReceipt(Receipt(), _ => ++reads == 1 ? Task.FromException<LayoutAccessTrace>(new HttpRequestException("offline")) : Task.FromResult(Trace("valid")));
        Assert.Equal(0, reads);
        Toggle(cut);
        Assert.Equal("Unable to read the authorization trace.", cut.Find("[role=alert]").TextContent);
        cut.Find("button").Click();
        Assert.Equal(2, reads);
        Assert.Single(cut.FindAll("[data-layout-access-evidence=valid]"));
        Toggle(cut);
        Toggle(cut);
        Assert.Equal(2, reads);
    }

    [Theory(DisplayName = "layout-eng-30: missing, forbidden and malformed evidence render distinctly from absent, the other evidence and valid")]
    [InlineData("missing")]
    [InlineData("forbidden")]
    [InlineData("malformed")]
    public void EvidenceRendersDistinctly(string evidence)
    {
        var cut = RenderReceipt(Receipt(), _ => Task.FromResult(Trace(evidence)));
        Toggle(cut);
        Assert.Equal(evidence, cut.Find("[data-layout-access-evidence]").GetAttribute("data-layout-access-evidence"));
        Assert.Empty(cut.FindAll("[data-layout-access-stage], [data-layout-deciding-grant]"));
        var texts = new HashSet<string> { RenderReceipt(Receipt("absentReceipt"), _ => throw new InvalidOperationException()).Find("[data-layout-access-evidence]").TextContent };
        foreach (var other in new[] { "missing", "forbidden", "malformed" })
        {
            var view = RenderReceipt(Receipt(), _ => Task.FromResult(Trace(other)));
            Toggle(view);
            texts.Add(view.Find("[data-layout-access-evidence]").TextContent);
        }
        Assert.Equal(4, texts.Count);
    }

    [Fact(DisplayName = "layout-eng-30: no recorded decision is absence: no disclosure, no read")]
    public void NoRecordedDecisionIsAbsence()
    {
        var cut = RenderReceipt(Receipt("absentReceipt"), _ => throw new InvalidOperationException("An absent decision is never read."));
        Assert.Equal("run.index-rebuild.0002", cut.Find("[data-layout-run-id]").TextContent);
        Assert.Empty(cut.FindAll("details"));
        Assert.Single(cut.FindAll("[data-layout-access-evidence=absent]"));
    }

    [Fact(DisplayName = "layout-eng-30: mixed deciding facts render the declared grant, never the first generic deciding prefix")]
    public void MixedDecidingFactsRenderTheDeclaredGrant()
    {
        var cut = RenderReceipt(Receipt(), _ => Task.FromResult(Trace("mixed")));
        Toggle(cut);
        Assert.Equal("Deciding grant: grant-164@3", cut.Find("[data-layout-deciding-grant]").TextContent);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Harborline.Platform.slnx");
    }
}
