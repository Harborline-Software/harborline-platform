using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SchemaFormPerformanceTests : BunitContext
{
    [Fact]
    public async Task TwoHundredFiftySixFieldsAndNinetySixReplacementsStayStructurallyExact()
    {
        const int fieldCount = 256;
        const int updateCycles = 96;
        var fields = Enumerable.Range(0, fieldCount).Select(index => new SchemaFormField
        {
            Name = $"field-{index}",
            Label = SchemaFormText.From($"Field {index}"),
            IsReadable = true,
        }).ToArray();
        var view = new SchemaFormView
        {
            FormId = "fixture",
            Version = "1.0.0",
            Title = SchemaFormText.From("Fixture form"),
            Sections =
            [
                new SchemaFormSection
                {
                    Id = "large",
                    Title = SchemaFormText.From("large"),
                    Fields = fields,
                },
            ],
        };
        IReadOnlyDictionary<string, object?> ValuesForCycle(int cycle) =>
            Enumerable.Range(0, fieldCount).ToDictionary(
                index => $"field-{index}",
                index => (object?)(index == 0 ? $"{cycle}-0" : $"stable-{index}"),
                StringComparer.Ordinal);
        var callbackCycles = new List<int>();
        IReadOnlyDictionary<string, object?>? submitted = null;
        var latestValues = ValuesForCycle(-1);
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.Values, latestValues)
            .Add(component => component.OnSubmit, values =>
            {
                callbackCycles.Add(-1);
                submitted = values;
                return ValueTask.FromResult<SchemaFormValidationResult?>(null);
            }));

        for (var cycle = 0; cycle < updateCycles; cycle++)
        {
            var currentCycle = cycle;
            latestValues = ValuesForCycle(currentCycle);
            cut.Render(parameters => parameters
                .Add(component => component.View, view)
                .Add(component => component.Values, latestValues)
                .Add(component => component.OnSubmit, values =>
                {
                    callbackCycles.Add(currentCycle);
                    submitted = values;
                    return ValueTask.FromResult<SchemaFormValidationResult?>(null);
                }));

            // Invariant: exact-field-count
            Assert.Equal(fieldCount, cut.FindAll(".hl-form-field").Count);

            // Invariant: latest-value-visible
            Assert.Equal($"{currentCycle}-0", cut.Find("input[name='field-0']").GetAttribute("value"));

            // Invariant: no-stale-callbacks
            Assert.Empty(callbackCycles);
        }

        await cut.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);

        Assert.Equal([updateCycles - 1], callbackCycles);
        Assert.NotNull(submitted);
        Assert.Equal($"{updateCycles - 1}-0", submitted["field-0"]);
    }
}
