using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class FormRuleGraphProjectorNativeTests
{
    [Fact]
    public void NullGraphIsExactPassThroughWithAnOpenSaveGate()
    {
        var view = View(Section("s1", Field("a"), Field("b")));
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "1" };
        var projection = FormRuleGraphProjector.Project(view, null, values);
        Assert.Same(view, projection.View);
        Assert.Equal("1", projection.Values["a"]);
        Assert.Empty(projection.ComputedValues);
        Assert.Null(projection.Evaluation);
        Assert.False(projection.SaveBlocked);
    }

    [Fact]
    public void HiddenFieldAndSectionAreOmittedInBaseOrder()
    {
        var view = View(Section("s1", Field("a"), Field("b")), Section("s2", Field("c")));
        var graph = Graph(_ => new SchemaFormRuleEvaluation
        {
            Visibility = new Dictionary<string, SchemaFormVisibility>(StringComparer.Ordinal)
            {
                ["field:b"] = new(Visible: false),
                ["section:s2"] = new(Visible: false),
            },
        });
        var projection = FormRuleGraphProjector.Project(view, graph);
        var section = Assert.Single(projection.View.Sections);
        Assert.Equal("s1", section.Id);
        Assert.Equal(["a"], section.Fields.Select(field => field.Name));
    }

    [Fact]
    public void NestedGroupAndCollectionItemsProjectRecursively()
    {
        var inner = new SchemaFormCollectionItem("collA", [
            new SchemaFormFieldItem("a1", Field("a1")),
            new SchemaFormFieldItem("b1", Field("b1")),
        ], SchemaFormText.From("Collection A"));
        var group = new SchemaFormGroupItem("sub", [inner], SchemaFormText.From("Sub"));
        var view = View(new SchemaFormSection
        {
            Id = "s1",
            Title = SchemaFormText.From("S1"),
            Items = [group],
        });
        var graph = Graph(_ => new SchemaFormRuleEvaluation
        {
            Visibility = new Dictionary<string, SchemaFormVisibility>(StringComparer.Ordinal)
            {
                ["field:b1"] = new(Visible: false),
            },
        });
        var projection = FormRuleGraphProjector.Project(view, graph);
        var projectedGroup = Assert.IsType<SchemaFormGroupItem>(Assert.Single(projection.View.Sections[0].Items!));
        var projectedCollection = Assert.IsType<SchemaFormCollectionItem>(Assert.Single(projectedGroup.Items));
        var nested = Assert.IsType<SchemaFormFieldItem>(Assert.Single(projectedCollection.Items));
        Assert.Equal("a1", nested.Field.Name);
    }

    [Fact]
    public void RequiredAndReadOnlyFlagsSetWithoutClearingBaseFlags()
    {
        var view = View(Section("s1", Field("a"), Field("total") with { ReadOnly = true }));
        var graph = Graph(_ => new SchemaFormRuleEvaluation
        {
            Visibility = new Dictionary<string, SchemaFormVisibility>(StringComparer.Ordinal)
            {
                ["field:a"] = new(Visible: true, Required: true, ReadOnly: true),
            },
        });
        var projection = FormRuleGraphProjector.Project(view, graph);
        var fields = projection.View.Sections[0].Fields;
        Assert.True(fields[0].Required);
        Assert.True(fields[0].ReadOnly);
        Assert.True(fields[1].ReadOnly);
    }

    [Fact]
    public void OnlyResolvedComputedValuesInjectAndUnresolvedValuesBlockSave()
    {
        var view = View(Section("s1", Field("total")));
        var resolved = Graph(_ => new SchemaFormRuleEvaluation
        {
            Values = new Dictionary<string, SchemaFormRuleValue>(StringComparer.Ordinal)
            {
                ["field:total"] = new(SchemaFormRuleValueState.Resolved, "60"),
            },
        });
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["total"] = "1" };
        var resolvedProjection = FormRuleGraphProjector.Project(view, resolved, values);
        Assert.Equal("60", resolvedProjection.Values["total"]);
        Assert.False(resolvedProjection.SaveBlocked);

        foreach (var state in new[] { SchemaFormRuleValueState.Pending, SchemaFormRuleValueState.Error })
        {
            var unresolved = Graph(_ => new SchemaFormRuleEvaluation
            {
                Values = new Dictionary<string, SchemaFormRuleValue>(StringComparer.Ordinal)
                {
                    ["field:total"] = new(state),
                },
            });
            var projection = FormRuleGraphProjector.Project(view, unresolved, values);
            Assert.Equal("1", projection.Values["total"]);
            Assert.True(projection.SaveBlocked);
        }
    }

    [Fact]
    public void PresentationOutcomesAttachToTheirTargetOnly()
    {
        var view = View(Section("s1", Field("a"), Field("b")));
        var graph = Graph(_ => new SchemaFormRuleEvaluation
        {
            Presentations = new Dictionary<string, SchemaFormPresentation>(StringComparer.Ordinal)
            {
                ["field:a"] = new(Severity: "error", Badge: SchemaFormText.From("Bad")),
            },
        });
        var projection = FormRuleGraphProjector.Project(view, graph);
        var fields = projection.View.Sections[0].Fields;
        Assert.Equal("error", fields[0].Presentation?.Severity);
        Assert.Null(fields[1].Presentation);
    }

    [Fact]
    public void ThrowingGraphPassesBaseViewThroughAndBlocksSave()
    {
        var view = View(Section("s1", Field("a")));
        var graph = Graph(_ => throw new InvalidOperationException("form-rule-graph.evaluation-failed"));
        var projection = FormRuleGraphProjector.Project(view, graph);
        Assert.Equal(["a"], projection.View.Sections[0].Fields.Select(field => field.Name));
        Assert.True(projection.SaveBlocked);
        Assert.NotNull(projection.EvaluationError);
        Assert.Null(projection.Evaluation);
    }

    [Fact]
    public void PendingAndSaveBlockedEvaluationsBlockSave()
    {
        var view = View(Section("s1", Field("a")));
        var pending = FormRuleGraphProjector.Project(view, Graph(_ => new SchemaFormRuleEvaluation { HasPending = true }));
        Assert.True(pending.SaveBlocked);
        var blocked = FormRuleGraphProjector.Project(view, Graph(_ => new SchemaFormRuleEvaluation { IsSaveBlocked = true }));
        Assert.True(blocked.SaveBlocked);
    }

    [Fact]
    public void StatelessReevaluationAnswersTheSharedDeterministicSequence()
    {
        // The same canonical sequence the React lane answers in
        // form-rule-graph.projection-equivalence.
        var view = View(Section("s1", Field("a"), Field("b"), Field("total") with { ReadOnly = true }));
        var graph = RevealGraph("show");
        var sequence = new[] { "", "show", "off" };
        var outcomes = sequence.Select(a =>
        {
            var projection = FormRuleGraphProjector.Project(
                view, graph, new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = a });
            return (
                Visible: string.Join(',', projection.View.Sections[0].Fields.Select(field => field.Name)),
                Total: projection.Values["total"]);
        }).ToArray();
        Assert.Equal(("a,total", (object?)"0"), outcomes[0]);
        Assert.Equal(("a,b,total", (object?)"4"), outcomes[1]);
        Assert.Equal(("a,total", (object?)"3"), outcomes[2]);
    }

    [Fact]
    [Trait("ModuleConformance", "hlp.ui.use-form-rule-graph")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("form-rule-graph.", fixture.RootElement.GetProperty("id").GetString());
        var view = View(Section("s1", Field("a"), Field("b"), Field("total") with { ReadOnly = true }));
        var projection = FormRuleGraphProjector.Project(
            view, RevealGraph("show"), new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "show" });
        Assert.Equal(["a", "b", "total"], projection.View.Sections[0].Fields.Select(field => field.Name));
        Assert.Equal("4", projection.Values["total"]);
        Assert.False(projection.SaveBlocked);
    }

    private static ISchemaFormRuleGraph RevealGraph(string revealValue) => Graph(instance =>
    {
        var a = instance.Fields.TryGetValue("a", out var value) ? value as string : null;
        return new SchemaFormRuleEvaluation
        {
            Visibility = new Dictionary<string, SchemaFormVisibility>(StringComparer.Ordinal)
            {
                ["field:b"] = new(Visible: a == revealValue),
            },
            Values = new Dictionary<string, SchemaFormRuleValue>(StringComparer.Ordinal)
            {
                ["field:total"] = new(SchemaFormRuleValueState.Resolved, (a?.Length ?? 0).ToString()),
            },
        };
    });

    private static ISchemaFormRuleGraph Graph(Func<SchemaFormRuleInstance, SchemaFormRuleEvaluation> evaluate) =>
        new DelegatingGraph(evaluate);

    private sealed class DelegatingGraph(Func<SchemaFormRuleInstance, SchemaFormRuleEvaluation> evaluate) : ISchemaFormRuleGraph
    {
        public SchemaFormRuleEvaluation EvaluateInstance(SchemaFormRuleInstance instance) => evaluate(instance);
    }

    private static SchemaFormField Field(string name) => new()
    {
        Name = name,
        Label = SchemaFormText.From(name.ToUpperInvariant()),
        IsSensitive = false,
        IsReadable = true,
    };

    private static SchemaFormSection Section(string id, params SchemaFormField[] fields) => new()
    {
        Id = id,
        Title = SchemaFormText.From(id.ToUpperInvariant()),
        Fields = fields,
    };

    private static SchemaFormView View(params SchemaFormSection[] sections) => new()
    {
        FormId = "f.v1",
        Version = "1.0.0",
        Sections = sections,
    };
}
