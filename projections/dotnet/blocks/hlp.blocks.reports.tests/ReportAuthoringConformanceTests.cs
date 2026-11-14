using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.UIAdapters.Blazor.Components.ReportAuthoring;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Harborline.Blocks.MeasureCatalogue;
using Harborline.Blocks.BuilderDefinitions;

using Harborline.Blocks.Reports.Definitions;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

public sealed class ReportAuthoringConformanceTests
{
    [Fact(DisplayName = "reports-auth-8 reports-auth-9: the shared authored definition is admitted")]
    public async Task AuthoredDefinitionAdmitted()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "conformance/hlp.ui.report-authoring/definition.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Shared fixture not found.");
        var json = File.ReadAllText(Path.Combine(directory.FullName, "conformance/hlp.ui.report-authoring/definition.json"));
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) } };
        var draft = JsonSerializer.Deserialize<ReportDraft>(json, options) ?? throw new InvalidOperationException("Invalid authoring fixture.");
        var authoredJson = ReportDraftCodec.Serialize(draft);
        var definition = JsonSerializer.Deserialize<ReportDefinition>(authoredJson, options) ?? throw new InvalidOperationException("Invalid authored report.");
        var result = await ReportDefinitionAdmission.AdmitAsync(definition, DefinitionAdmissionPhase.Author, new(1, 0, 1), new("fixture", new FixtureCatalogue()));
        Assert.Empty(result.Refusals);
        Assert.Equal(5, definition.Parameters!.Count);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(authoredJson)));
    }

    private sealed class FixtureCatalogue : IMeasureCatalogue
    {
        public ValueTask<MeasureDescriptor?> ResolveAsync(MeasureRef reference, System.Threading.CancellationToken cancellationToken = default) => ValueTask.FromResult<MeasureDescriptor?>(reference.Value == "finance.rent" ? new MeasureDescriptor(reference, []) : null);
        public ValueTask<MeasureResult> EvaluateAsync(MeasureRef reference, MeasureRequest request, System.Threading.CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
