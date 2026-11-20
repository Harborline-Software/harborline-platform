using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    [Fact(DisplayName = "reports-auth-8 reports-auth-9: the shared literal definition is admitted")]
    public async Task AuthoredDefinitionAdmitted()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "conformance/hlp.ui.report-authoring/definition.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Shared fixture not found.");
        var json = File.ReadAllText(Path.Combine(directory.FullName, "conformance/hlp.ui.report-authoring/definition.json"));
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) } };
        var definition = JsonSerializer.Deserialize<ReportDefinition>(json, options) ?? throw new InvalidOperationException("Invalid report fixture.");
        var result = await ReportDefinitionAdmission.AdmitAsync(definition, DefinitionAdmissionPhase.Author, new(1, 0, 1), new("fixture", new FixtureCatalogue()));
        Assert.Empty(result.Refusals);
        Assert.Equal(5, definition.Parameters!.Count);
        Assert.True(Assert.IsType<BooleanReportParameterDefault>(definition.Parameters[0].Default).Value);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), Assert.IsType<DateTimeReportParameterDefault>(definition.Parameters[1].Default).Value);
        Assert.Equal(42L, Assert.IsType<IntegerReportParameterDefault>(definition.Parameters[2].Default).Value);
        Assert.Equal(2.5, Assert.IsType<FloatReportParameterDefault>(definition.Parameters[3].Default).Value);
        Assert.Equal("hello", Assert.IsType<StringReportParameterDefault>(definition.Parameters[4].Default).Value);
    }

    [Fact(DisplayName = "shared accepted parameter defaults deserialize and admit in Reports")]
    public async Task SharedDefaultsAdmitted()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "conformance/hlp.ui.report-authoring/parameter-defaults.json"))) directory = directory.Parent;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "conformance/hlp.ui.report-authoring/parameter-defaults.json")));
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) } };
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (!row.GetProperty("accepted").GetBoolean()) continue;
            var type = row.GetProperty("type").GetString();
            var kind = type == "date_time" ? "datetime" : type;
            var parameterJson = $"{{\"name\":\"p\",\"type\":\"{type}\",\"default\":{{\"kind\":\"{kind}\",\"value\":{row.GetProperty("value").GetRawText()}}}}}";
            var parameter = JsonSerializer.Deserialize<ReportParameterDefinition>(parameterJson, options)!;
            var definition = new ReportDefinition("tenant-r", "report-a", "1.0.0", [], Parameters: [parameter]);
            var result = await ReportDefinitionAdmission.AdmitAsync(definition, DefinitionAdmissionPhase.Author, new(1, 0, 1), new("fixture", new FixtureCatalogue()));
            Assert.Empty(result.Refusals);
            Assert.True(parameter.Default.IsValid, row.GetProperty("id").GetString());
        }
    }

    private sealed class FixtureCatalogue : IMeasureCatalogue
    {
        public ValueTask<MeasureDescriptor?> ResolveAsync(MeasureRef reference, System.Threading.CancellationToken cancellationToken = default) => ValueTask.FromResult<MeasureDescriptor?>(reference.Value == "finance.rent" ? new MeasureDescriptor(reference, []) : null);
        public ValueTask<MeasureResult> EvaluateAsync(MeasureRef reference, MeasureRequest request, System.Threading.CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
