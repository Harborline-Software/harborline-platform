using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SchemaFormTests : BunitContext
{
    [Fact(DisplayName = "schema-form.host-readonly-unavailable")]
    public void Read_only_structured_values_fail_closed_without_stringification()
    {
        var structured = new UnexpectedReadOnlyValue();
        var view = Form([Section("details", [
            Field("unknown", "Unknown") with { ControlHint = "future-object", ValueKind = "object" },
            Field("text", "Text"),
            Field("secret", "Secret") with { IsSensitive = true },
        ])]);
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.Values, new Dictionary<string, object?> { ["unknown"] = structured, ["text"] = structured, ["secret"] = structured })
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));
        Assert.Equal(2, cut.FindAll("[data-error-code='schema-form.unavailable-value']").Count);
        Assert.Equal(new SchemaFormStrings().Redacted, cut.Find("#secret").TextContent);
        Assert.Equal(0, structured.Stringifications);
        Assert.Empty(cut.FindAll("input,select,textarea,button"));
    }

    private sealed class UnexpectedReadOnlyValue
    {
        public int Stringifications { get; private set; }
        public override string ToString() { Stringifications++; return "must-not-render"; }
    }

    [Fact]
    public void Read_only_renders_every_visible_builtin_hint_and_keeps_hidden_nonvisual()
    {
        var visible = new (string Hint, string Name, object? Value)[]
        {
            ("text", "vesselName", "MV North Star"), ("textarea", "inspectionScope", "Annual hull and machinery inspection."),
            ("number", "grossTonnage", 18425.5d), ("integer", "crewCount", 24), ("select", "inspectionType", "annual"),
            ("multiselect", "systemsReviewed", new[] { "navigation", "fire" }), ("checkbox", "documentsVerified", true),
            ("boolean", "masterAttested", true), ("boolean-toggle", "followUpRequired", false), ("date", "inspectionDate", "2026-09-16"),
            ("datetime", "inspectionStarted", "2026-09-16T09:30:00-04:00"), ("time", "highTide", "14:45"),
            ("currency", "estimatedCost", 48750.5d), ("percentage", "completion", 92.5d), ("phone", "agentPhone", "+1 410 555 0142"),
            ("email", "agentEmail", "port.agent@example.test"), ("url", "certificateUrl", "https://records.example.test/inspections/HLI-2048"),
            ("readonly", "applicationStatus", "Approved for certificate issuance"),
        };
        var fields = visible.Select(item => Field(item.Name, item.Name) with
        {
            ControlHint = item.Hint,
            Options = item.Hint switch
            {
                "select" => [new("annual", Text("Annual safety inspection"))],
                "multiselect" => [new("navigation", Text("Navigation")), new("fire", Text("Fire suppression"))],
                _ => [],
            },
        }).Append(Field("transportToken", "Transport token") with { ControlHint = "hidden" }).ToArray();
        var values = visible.ToDictionary(item => item.Name, item => item.Value);
        values["transportToken"] = "kept";
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, Form([Section("inspection", fields)]))
            .Add(component => component.Values, values)
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));

        var expected = new Dictionary<string, string>
        {
            ["vesselName"] = "MV North Star", ["inspectionScope"] = "Annual hull and machinery inspection.", ["grossTonnage"] = "18425.5",
            ["crewCount"] = "24", ["inspectionType"] = "Annual safety inspection", ["systemsReviewed"] = "Navigation, Fire suppression",
            ["documentsVerified"] = "true", ["masterAttested"] = "true", ["followUpRequired"] = "false", ["inspectionDate"] = "2026-09-16",
            ["inspectionStarted"] = "2026-09-16T09:30:00-04:00", ["highTide"] = "14:45", ["estimatedCost"] = "48750.5",
            ["completion"] = "92.5", ["agentPhone"] = "+1 410 555 0142", ["agentEmail"] = "port.agent@example.test",
            ["certificateUrl"] = "https://records.example.test/inspections/HLI-2048", ["applicationStatus"] = "Approved for certificate issuance",
        };
        foreach (var pair in expected) Assert.Equal(pair.Value, cut.Find($"#{pair.Key}").TextContent);
        Assert.Equal("kept", cut.Find("input[type='hidden'][name='transportToken']").GetAttribute("value"));
        Assert.DoesNotContain("Transport token", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input:not([type='hidden']),select,textarea,button[type='submit']"));
    }

    [Fact]
    public void Read_only_multiselect_refuses_undeclared_and_structured_values_without_stringification()
    {
        var structured = new UnexpectedReadOnlyValue();
        var fields = new[]
        {
            Field("undeclared", "Undeclared") with { ControlHint = "multiselect", Options = [new("navigation", Text("Navigation"))] },
            Field("structured", "Structured") with { ControlHint = "multiselect", Options = [new("navigation", Text("Navigation"))] },
        };
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, Form([Section("inspection", fields)]))
            .Add(component => component.Values, new Dictionary<string, object?> { ["undeclared"] = new[] { "fire" }, ["structured"] = new[] { structured } })
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));
        Assert.Equal(2, cut.FindAll("[data-error-code='schema-form.unavailable-value']").Count);
        Assert.Equal(0, structured.Stringifications);
    }

    [Fact]
    public void Sensitive_and_unreadable_hidden_fields_are_suppressed_before_hidden_serialization()
    {
        var structured = new UnexpectedReadOnlyValue();
        var fields = new[]
        {
            Field("sensitive", "Sensitive") with { ControlHint = "hidden", IsSensitive = true },
            Field("unreadable", "Unreadable") with { ControlHint = "hidden", IsReadable = false },
            Field("structured", "Structured") with { ControlHint = "hidden" },
        };
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, Form([Section("details", fields)]))
            .Add(component => component.Values, new Dictionary<string, object?> { ["sensitive"] = structured, ["unreadable"] = "secret", ["structured"] = structured })
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));

        Assert.DoesNotContain("Sensitive", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Unreadable", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Structured", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("input[type='hidden']"));
        Assert.Equal(string.Empty, cut.Find("input[type='hidden'][name='structured']").GetAttribute("value"));
        Assert.Equal(0, structured.Stringifications);
    }

    [Fact]
    public void Read_only_multiselect_uses_first_matching_label_for_duplicate_option_values()
    {
        var options = new[]
        {
            new SchemaFormOption("navigation", Text("Navigation")),
            new SchemaFormOption("navigation", Text("Duplicate")),
        };
        var field = Field("systems", "Systems") with { ControlHint = "multiselect", Options = options };
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, Form([Section("inspection", [field])]))
            .Add(component => component.Values, new Dictionary<string, object?> { ["systems"] = new[] { "navigation" } })
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));

        Assert.Equal("Navigation", cut.Find("#systems").TextContent);
    }

    [Fact(DisplayName = "schema-form.host-readonly")]
    public void Read_only_outputs_have_no_submit_and_reject_programmatic_submission()
    {
        var submissions = 0;
        var changes = 0;
        var actions = 0;
        var values = new Dictionary<string, object?> { ["title"] = "Published" };
        var graph = new TestRuleGraph(_ => Evaluation(values: new Dictionary<string, SchemaFormRuleValue>
            { ["field:title"] = new(SchemaFormRuleValueState.Resolved, "Computed") }));
        var view = Form([Section("details", [Field("title", "Title")]), Section("actions", [],
            [new SchemaFormActionItem("open", new(SchemaFormActionKind.OpenUrl, Text("Open"), Url: "https://example.test"))])]);
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.Values, values)
            .Add(component => component.RuleGraph, graph)
            .Add(component => component.ReadOnly, true)
            .Add(component => component.OnBlockAction, _ => actions++)
            .Add(component => component.OnValuesChange, _ => changes++)
            .Add(component => component.OnSubmit, _ => { submissions++; return ValueTask.FromResult<SchemaFormValidationResult?>(null); }));
        Assert.Equal("Computed", cut.Find("output").TextContent);
        Assert.Empty(cut.FindAll("input,select,textarea,button[type=submit]"));
        cut.Find("button").Click();
        Assert.Equal(0, actions);
        cut.Find("form").Submit();
        Assert.Equal(0, submissions);
        Assert.Equal(0, changes);
        Assert.Equal("Published", values["title"]);
        cut.Render(parameters => parameters.Add(component => component.ReadOnly, false).Add(component => component.RuleGraph, null));
        Assert.Equal("Published", cut.Find("input").GetAttribute("value"));
        Assert.Single(cut.FindAll("button[type=submit]"));
    }
    [Fact(DisplayName = "schema-form.section-order")]
    public void SchemaFormSectionOrder()
    {
        var cut = RenderForm(Form([
            Section("Second", [Field("b", "B"), Field("a", "A")]),
            Section("First", [Field("d", "D"), Field("c", "C")]),
        ]));
        Assert.Equal(["Second", "First"], cut.FindAll("[data-schemaform-section]").Select(node => node.GetAttribute("data-schemaform-section")));
        Assert.Equal(["b", "a", "d", "c"], cut.FindAll("input[type='text']").Select(node => node.GetAttribute("name")));
    }

    [Fact(DisplayName = "schema-form.hint-registry")]
    public void SchemaFormHintRegistry()
    {
        var renders = 0;
        SchemaFormControlRenderer custom = args => builder =>
        {
            renders++;
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "aria-label", "Custom");
            builder.AddAttribute(2, "value", args.StringValue);
            builder.CloseElement();
        };
        var cut = RenderForm(
            Form([Section("s", [Field("x", "X") with { ControlHint = "bespoke" }])]),
            new Dictionary<string, object?> { ["x"] = "value" },
            controls: new Dictionary<string, SchemaFormControlRenderer> { ["bespoke"] = custom });
        Assert.Equal("value", cut.Find("[aria-label='Custom']").GetAttribute("value"));
        Assert.Equal(1, renders);
    }

    [Fact(DisplayName = "schema-form.hint-fallback")]
    public void SchemaFormHintFallback()
    {
        var cut = RenderForm(
            Form([Section("s", [Field("x", "Fallback") with { ControlHint = "future-string" }])]),
            new Dictionary<string, object?> { ["x"] = "fallback" });
        Assert.Equal("fallback", cut.Find("input[name='x']").GetAttribute("value"));
    }

    [Fact(DisplayName = "schema-form.unavailable-value")]
    public void SchemaFormUnavailableValue()
    {
        var structured = new Dictionary<string, object?> { ["nested"] = true };
        var cut = RenderForm(
            Form([Section("s", [
                Field("x", "Unknown") with { ControlHint = "future-object", ValueKind = "object" },
                Field("y", "Text object"),
            ])]),
            new Dictionary<string, object?> { ["x"] = structured, ["y"] = structured });
        Assert.Equal(2, cut.FindAll($"[data-error-code='{SchemaFormErrorCodes.UnavailableValue}']").Count);
        Assert.All(cut.FindAll("[data-error-code]"), node =>
            Assert.Equal("schema-form.unavailable-value", node.GetAttribute("data-error-code")));
        Assert.DoesNotContain("[object Object]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "schema-form.rule-visibility")]
    public void SchemaFormRuleVisibility()
    {
        var graph = new TestRuleGraph(_ => Evaluation(visibility: new Dictionary<string, SchemaFormVisibility>
        {
            ["field:hidden"] = new(false),
            ["section:gone"] = new(false),
        }));
        var cut = RenderForm(Form([
            Section("shown", [Field("visible", "Visible"), Field("hidden", "Hidden")]),
            Section("gone", [Field("gone-field", "Gone")]),
        ]), graph: graph);
        Assert.Single(cut.FindAll("input[name='visible']"));
        Assert.Empty(cut.FindAll("input[name='hidden']"));
        Assert.Empty(cut.FindAll("[data-schemaform-section='gone']"));
    }

    [Fact(DisplayName = "schema-form.rule-required")]
    public void SchemaFormRuleRequired()
    {
        var graph = new TestRuleGraph(_ => Evaluation(visibility: new Dictionary<string, SchemaFormVisibility>
        {
            ["field:name"] = new(true, Required: true),
        }));
        var cut = RenderForm(Form([Section("s", [Field("name", "Name")])]), graph: graph);
        Assert.NotNull(cut.Find("input[name='name']").GetAttribute("required"));
        Assert.Equal("true", cut.Find(".hl-form-field").GetAttribute("data-hl-required"));
        Assert.Equal("true", cut.Find(".hl-form-field__required").GetAttribute("aria-hidden"));
    }

    [Fact(DisplayName = "schema-form.rule-readonly")]
    public void SchemaFormRuleReadonly()
    {
        var graph = new TestRuleGraph(_ => Evaluation(visibility: new Dictionary<string, SchemaFormVisibility>
        {
            ["field:name"] = new(true, ReadOnly: true),
        }));
        var cut = RenderForm(Form([Section("s", [Field("name", "Name")])]),
            new Dictionary<string, object?> { ["name"] = "fixed" }, graph: graph);
        Assert.NotNull(cut.Find("input[name='name']").GetAttribute("disabled"));
        Assert.Equal("true", cut.Find(".hl-form-field").GetAttribute("data-read-only"));
    }

    [Fact(DisplayName = "schema-form.rule-computed")]
    public void SchemaFormRuleComputed()
    {
        var graph = new TestRuleGraph(instance => Evaluation(values:
            Equals(instance.Fields["enable"], "yes")
                ? new Dictionary<string, SchemaFormRuleValue>
                {
                    ["field:total"] = new(SchemaFormRuleValueState.Resolved, 42),
                }
                : null));
        var cut = RenderForm(Form([Section("s", [Field("enable", "Enable"), Field("total", "Total")])]),
            new Dictionary<string, object?> { ["enable"] = "yes", ["total"] = 7 }, graph: graph);
        Assert.Equal("42", cut.Find("input[name='total']").GetAttribute("value"));
        cut.Find("input[name='enable']").Input("no");
        cut.WaitForAssertion(() => Assert.Equal("7", cut.Find("input[name='total']").GetAttribute("value")));
    }

    [Fact(DisplayName = "schema-form.rule-presentation")]
    public void SchemaFormRulePresentation()
    {
        var graph = new TestRuleGraph(_ => Evaluation(presentations: new Dictionary<string, SchemaFormPresentation>
        {
            ["field:name"] = new(Text("Review"), "warn", "attention"),
        }));
        var cut = RenderForm(Form([Section("s", [Field("name", "Name")])]), graph: graph);
        Assert.Equal("warn", cut.Find(".hl-schema-form__presentation").GetAttribute("data-severity"));
        Assert.Equal("Review", cut.Find(".hl-schema-form__presentation").TextContent);
        Assert.Equal("attention", cut.Find(".hl-form-field").GetAttribute("data-presentation-style-token"));
    }

    [Fact(DisplayName = "schema-form.submit-blocked")]
    public void SchemaFormSubmitBlocked()
    {
        var submissions = 0;
        var pending = new TestRuleGraph(_ => Evaluation(
            values: new Dictionary<string, SchemaFormRuleValue>
            {
                ["field:name"] = new(SchemaFormRuleValueState.Pending),
            },
            hasPending: true));
        var cut = RenderForm(Form([Section("s", [Field("name", "Name")])]), graph: pending,
            submit: _ => { submissions++; return ValueTask.FromResult<SchemaFormValidationResult?>(null); });
        Assert.NotNull(cut.Find("button[type='submit']").GetAttribute("disabled"));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll($"[data-error-code='{SchemaFormErrorCodes.SubmitBlocked}']")));
        Assert.Equal("schema-form.submit-blocked", cut.Find("[data-error-code]").GetAttribute("data-error-code"));
        Assert.Contains(JSInterop.Invocations.Identifiers, identifier => identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, submissions);

        var errored = new TestRuleGraph(_ => throw new InvalidOperationException("rule failure"));
        var second = RenderForm(Form([Section("s", [Field("name", "Name")])]), graph: errored,
            submit: _ => { submissions++; return ValueTask.FromResult<SchemaFormValidationResult?>(null); });
        second.Find("form").Submit();
        second.WaitForAssertion(() => Assert.Single(second.FindAll($"[data-error-code='{SchemaFormErrorCodes.SubmitBlocked}']")));
        Assert.Equal(0, submissions);
    }

    [Fact(DisplayName = "schema-form.validation-inline")]
    public void SchemaFormValidationInline()
    {
        var field = Field("name", "Localized label") with { HelpText = Text("Localized hint") };
        var cut = RenderForm(Form([Section("s", [field])]),
            submit: _ => ValueTask.FromResult<SchemaFormValidationResult?>(Invalid(new SchemaFormValidationError("/name", "Caller-localized inline error"))));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal("Caller-localized inline error", cut.Find("#name-error").TextContent));
        Assert.Equal("name-hint name-error", cut.Find("input[name='name']").GetAttribute("aria-describedby"));
        Assert.Contains("Localized label", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Localized hint", cut.Markup, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "schema-form.validation-summary")]
    public void SchemaFormValidationSummary()
    {
        var cut = RenderForm(Form([Section("s", [Field("name", "Name")])]),
            submit: _ => ValueTask.FromResult<SchemaFormValidationResult?>(Invalid(
                new("/group/name", "Nested error"), new("", "Form error"))));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Nested error", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal));
        Assert.Contains("Form error", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#name-error"));
    }

    [Fact(DisplayName = "schema-form.summary-focus")]
    public void SchemaFormSummaryFocus()
    {
        var cut = RenderForm(Form([Section("s", [Field("name", "Name")])]),
            submit: _ => ValueTask.FromResult<SchemaFormValidationResult?>(Invalid(new SchemaFormValidationError("", "Return to errors"))));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role='alert'][tabindex='-1']")));
        Assert.Equal("-1", cut.Find("[role='alert']").GetAttribute("tabindex"));
        Assert.Contains(JSInterop.Invocations.Identifiers, identifier => identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "schema-form.nested-group-set")]
    public void SchemaFormNestedGroupSet()
    {
        var groupSibling = new Dictionary<string, object?> { ["bytes"] = new[] { "same" } };
        var rootSibling = new Dictionary<string, object?> { ["untouched"] = true };
        IReadOnlyDictionary<string, object?>? changed = null;
        var rowSibling = new Dictionary<string, object?> { ["bytes"] = new[] { "same-row" } };
        var siblingRow = new Dictionary<string, object?> { ["child"] = "other" };
        var item = new SchemaFormGroupItem("group",
        [
            new SchemaFormFieldItem("child", Field("child", "Child")),
            new SchemaFormCollectionItem(
                "rows",
                [
                    new SchemaFormFieldItem("item", Field("item", "Item")),
                    new SchemaFormFieldItem("rowSibling", Field("rowSibling", "Row sibling") with { ControlHint = "hidden" }),
                ],
                Cardinality: new(0, 3)),
        ], Text("Group"));
        var cut = RenderForm(
            Form([Section("s", [], [item])]),
            new Dictionary<string, object?>
            {
                ["group"] = new Dictionary<string, object?>
                {
                    ["child"] = "before",
                    ["sibling"] = groupSibling,
                    ["rows"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["item"] = "before", ["rowSibling"] = rowSibling },
                        siblingRow,
                    },
                },
                ["rootSibling"] = rootSibling,
            },
            valuesChanged: candidate => changed = candidate);
        cut.Find("input[name='child']").Input("after");
        cut.WaitForAssertion(() => Assert.NotNull(changed));
        var group = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(changed!["group"]);
        Assert.Equal("after", group["child"]);
        Assert.Same(groupSibling, group["sibling"]);
        Assert.Same(rootSibling, changed["rootSibling"]);

        changed = null;
        cut.Find("input[name='item']").Input("after");
        cut.WaitForAssertion(() => Assert.NotNull(changed));
        group = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(changed!["group"]);
        var rows = Assert.IsAssignableFrom<System.Collections.IList>(group["rows"]);
        var firstRow = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(rows[0]);
        Assert.Equal("after", firstRow["item"]);
        Assert.Same(rowSibling, firstRow["rowSibling"]);
        Assert.Same(siblingRow, rows[1]);
        Assert.Same(groupSibling, group["sibling"]);
        Assert.Same(rootSibling, changed["rootSibling"]);
    }

    [Fact(DisplayName = "schema-form.collection-cardinality")]
    public void SchemaFormCollectionCardinality()
    {
        var lengths = new List<int>();
        var collection = new SchemaFormCollectionItem(
            "lines",
            [new SchemaFormFieldItem("value", Field("value", "Value"))],
            Text("Lines"),
            new(1, 2));
        var cut = RenderForm(
            Form([Section("s", [], [collection])]),
            new Dictionary<string, object?>
            {
                ["lines"] = new List<object?> { new Dictionary<string, object?> { ["value"] = "one" } },
            },
            valuesChanged: candidate => lengths.Add(((System.Collections.IEnumerable)candidate["lines"]!).Cast<object?>().Count()));
        Assert.NotNull(cut.Find("button[aria-label='Remove Item 1']").GetAttribute("disabled"));
        Assert.Null(cut.Find(".hl-schema-form__add").GetAttribute("disabled"));
        cut.Find(".hl-schema-form__add").Click();
        Assert.Equal(2, cut.FindAll("[data-testid='collection-lines-instance']").Count);
        Assert.NotNull(cut.Find(".hl-schema-form__add").GetAttribute("disabled"));
        cut.Find("button[aria-label='Remove Item 1']").Click();
        Assert.Single(cut.FindAll("[data-testid='collection-lines-instance']"));
        Assert.NotNull(cut.Find("button[aria-label='Remove Item 1']").GetAttribute("disabled"));
        Assert.Equal([2, 1], lengths);
    }

    [Fact(DisplayName = "schema-form.candidate-complete")]
    public void SchemaFormCandidateComplete()
    {
        IReadOnlyDictionary<string, object?>? submitted = null;
        var nativeHidden = new Dictionary<string, object?> { ["structured"] = true };
        var graph = new TestRuleGraph(_ => Evaluation(visibility: new Dictionary<string, SchemaFormVisibility>
        {
            ["field:ruleHidden"] = new(false),
        }));
        var cut = RenderForm(
            Form([Section("s", [
                Field("visible", "Visible"),
                Field("ruleHidden", "Rule hidden"),
                Field("nativeHidden", "Native hidden") with { ControlHint = "hidden", ValueKind = "object" },
            ])]),
            new Dictionary<string, object?> { ["visible"] = "v", ["ruleHidden"] = "r", ["nativeHidden"] = nativeHidden },
            graph: graph,
            submit: candidate =>
            {
                submitted = candidate;
                return ValueTask.FromResult<SchemaFormValidationResult?>(null);
            });
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.NotNull(submitted));
        Assert.Equal("v", submitted!["visible"]);
        Assert.Equal("r", submitted["ruleHidden"]);
        Assert.Same(nativeHidden, submitted["nativeHidden"]);
    }

    [Fact(DisplayName = "schema-form.projection-equivalence")]
    public void SchemaFormProjectionEquivalence()
    {
        Assert.Equal(
            ["boolean", "boolean-toggle", "checkbox", "currency", "date", "datetime", "email", "hidden",
             "integer", "multiselect", "number", "percentage", "phone", "readonly", "select", "text",
             "textarea", "time", "url"],
            SchemaFormControls.BuiltInHints.Order(StringComparer.Ordinal));
        IReadOnlyDictionary<string, object?>? submitted = null;
        var view = Form([Section("Main", [
            Field("status", "Status") with
            {
                ControlHint = "select",
                Options = [new("new", Text("New")), new("done", Text("Done"))],
            },
            Field("notes", "Notes") with { ControlHint = "textarea" },
            Field("enabled", "Enabled") with { ControlHint = "boolean-toggle" },
        ])]);
        var cut = RenderForm(view,
            new Dictionary<string, object?> { ["status"] = "new", ["notes"] = "Ported", ["enabled"] = true },
            strings: new() { Submit = "Save" },
            submit: candidate =>
            {
                submitted = candidate;
                return ValueTask.FromResult<SchemaFormValidationResult?>(SchemaFormValidationResult.Valid);
            });
        Assert.Equal(["status", "notes", "enabled"], cut.FindAll(".hl-form-field").Select(node => node.QuerySelector("select,textarea,[role='switch']")?.GetAttribute("name") ?? node.QuerySelector("[role='combobox']")?.GetAttribute("name") ?? "enabled"));
        Assert.Contains("New", cut.Find("[role='combobox']").TextContent, StringComparison.Ordinal);
        Assert.Equal("Ported", cut.Find("textarea").GetAttribute("value"));
        Assert.Equal("true", cut.Find("[role='switch']").GetAttribute("aria-checked"));
        Assert.Equal("Save", cut.Find("button[type='submit']").TextContent.Trim());
        Assert.Equal(["Status", "Notes", "Enabled"], cut.FindAll(".hl-form-field__label-text").Select(node => node.TextContent));
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(submitted));
        Assert.Equal("new", submitted!["status"]);
        Assert.Equal("Ported", submitted["notes"]);
        Assert.Equal(true, submitted["enabled"]);

        var localeCut = RenderForm(
            Form([Section("numeric", [
                Field("money", "Currency") with
                {
                    ControlHint = "currency",
                    Config = new Dictionary<string, object?> { ["currencyCode"] = "EUR" },
                },
                Field("percent", "Percentage") with { ControlHint = "percentage" },
            ])]),
            new Dictionary<string, object?> { ["money"] = 1234.5, ["percent"] = 50 },
            localeChain: ["de-DE"]);
        Assert.Contains("1.234,50", localeCut.Find("input[name='money']").GetAttribute("value"), StringComparison.Ordinal);
        var percentage = localeCut.FindComponents<HarborlineNumericTextBox>().Single(component => component.Instance.Name == "percent");
        Assert.Equal(0, percentage.Instance.Min);
        Assert.Equal(100, percentage.Instance.Max);
    }

    [Fact]
    public void OneKeystrokeRendersOnlyChangedField()
    {
        var counts = Enumerable.Range(0, 500).ToDictionary(index => $"field-{index}", _ => 0, StringComparer.Ordinal);
        var fields = Enumerable.Range(0, 500).Select(index => Field($"field-{index}", $"Field {index}")).ToArray();
        SchemaFormControlRenderer instrumentedText = args => builder =>
        {
            counts[args.Field.Name]++;
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "name", args.Field.Name);
            builder.AddAttribute(2, "type", "text");
            builder.AddAttribute(3, "value", args.StringValue);
            builder.AddAttribute(4, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(
                this, value => args.ValueChanged.InvokeAsync(value.Value?.ToString() ?? string.Empty)));
            builder.CloseElement();
        };
        var cut = RenderForm(
            Form([Section("large", fields)]),
            new Dictionary<string, object?> { ["field-0"] = string.Empty },
            graph: new TestRuleGraph(_ => Evaluation()),
            controls: new Dictionary<string, SchemaFormControlRenderer> { ["text"] = instrumentedText });
        var before = counts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        cut.Find("input[name='field-0']").Input("next");
        cut.WaitForAssertion(() => Assert.True(counts["field-0"] > before["field-0"]));
        Assert.All(Enumerable.Range(1, 499), index => Assert.Equal(before[$"field-{index}"], counts[$"field-{index}"]));
    }

    [Fact]
    public void ControlledValuesRemainHostOwned()
    {
        IReadOnlyDictionary<string, object?>? proposed = null;
        var hostValues = new Dictionary<string, object?> { ["name"] = "before" };
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, Form([Section("s", [Field("name", "Name")])]))
            .Add(component => component.Values, hostValues)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null))
            .Add(component => component.OnValuesChange, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(
                this, candidate => proposed = candidate)));
        cut.Find("input[name='name']").Input("after");
        cut.WaitForAssertion(() => Assert.NotNull(proposed));
        Assert.Equal("after", proposed!["name"]);
        Assert.Equal("before", cut.Find("input[name='name']").GetAttribute("value"));
    }

    [Fact]
    public void RuleRehideRestoresFocusToSubmit()
    {
        var graph = new TestRuleGraph(instance => Evaluation(visibility:
            Equals(instance.Fields["trigger"], "hide")
                ? new Dictionary<string, SchemaFormVisibility> { ["field:trigger"] = new(false) }
                : null));
        var cut = RenderForm(
            Form([Section("s", [Field("trigger", "Trigger"), Field("other", "Other")])]),
            new Dictionary<string, object?> { ["trigger"] = string.Empty },
            graph: graph);
        cut.Find("input[name='trigger']").FocusIn();
        cut.Find("input[name='trigger']").Input("hide");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("input[name='trigger']")));
        Assert.Contains(JSInterop.Invocations.Identifiers, identifier => identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ControlledRuleRehideRestoresFocusToSubmit()
    {
        var graph = new TestRuleGraph(instance => Evaluation(visibility:
            Equals(instance.Fields["trigger"], "hide")
                ? new Dictionary<string, SchemaFormVisibility> { ["field:trigger"] = new(false) }
                : null));
        IReadOnlyDictionary<string, object?>? proposed = null;
        var hostValues = new Dictionary<string, object?> { ["trigger"] = string.Empty };
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, Form([Section("s", [Field("trigger", "Trigger"), Field("other", "Other")])]))
            .Add(component => component.Values, hostValues)
            .Add(component => component.RuleGraph, graph)
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null))
            .Add(component => component.OnValuesChange, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(
                this, candidate => proposed = candidate)));
        cut.Find("input[name='trigger']").FocusIn();
        cut.Find("input[name='trigger']").Input("hide");
        cut.WaitForAssertion(() => Assert.NotNull(proposed));
        cut.Render(parameters => parameters.Add(component => component.Values, proposed));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("input[name='trigger']")));
        Assert.Contains(JSInterop.Invocations.Identifiers, identifier => identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.schema-form")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("schema-form.", fixture.RootElement.GetProperty("id").GetString());
        var cut = Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, Form([Section("shared", [Field("fixture-field", "Shared fixture")])]))
            .Add(component => component.InitialValues, new Dictionary<string, object?> { ["fixture-field"] = "rendered" })
            .Add(component => component.OnSubmit, _ => ValueTask.FromResult<SchemaFormValidationResult?>(null)));
        Assert.Equal("Shared fixture", cut.Find("label").TextContent);
        Assert.Equal("rendered", cut.Find("input[name='fixture-field']").GetAttribute("value"));
    }

    private IRenderedComponent<HarborlineSchemaForm> RenderForm(
        SchemaFormView view,
        IReadOnlyDictionary<string, object?>? initialValues = null,
        ISchemaFormRuleGraph? graph = null,
        Func<IReadOnlyDictionary<string, object?>, ValueTask<SchemaFormValidationResult?>>? submit = null,
        IReadOnlyDictionary<string, SchemaFormControlRenderer>? controls = null,
        SchemaFormStrings? strings = null,
        IReadOnlyList<string>? localeChain = null,
        Action<IReadOnlyDictionary<string, object?>>? valuesChanged = null) =>
        Render<HarborlineSchemaForm>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.InitialValues, initialValues)
            .Add(component => component.RuleGraph, graph)
            .Add(component => component.OnSubmit, submit ?? (_ => ValueTask.FromResult<SchemaFormValidationResult?>(null)))
            .Add(component => component.Controls, controls)
            .Add(component => component.Strings, strings)
            .Add(component => component.LocaleChain, localeChain ?? [])
            .Add(component => component.OnValuesChange, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(
                this, candidate => valuesChanged?.Invoke(candidate))));

    private static SchemaFormText Text(string value) => SchemaFormText.From(value);

    private static SchemaFormField Field(string name, string label) => new()
    {
        Name = name,
        Label = Text(label),
        IsReadable = true,
    };

    private static SchemaFormSection Section(
        string id,
        IReadOnlyList<SchemaFormField> fields,
        IReadOnlyList<SchemaFormItem>? items = null) => new()
    {
        Id = id,
        Title = Text(id),
        Fields = fields,
        Items = items,
    };

    private static SchemaFormView Form(IReadOnlyList<SchemaFormSection> sections) => new()
    {
        FormId = "fixture",
        Version = "1.0.0",
        Title = Text("Fixture form"),
        Sections = sections,
    };

    private static SchemaFormRuleEvaluation Evaluation(
        IReadOnlyDictionary<string, SchemaFormVisibility>? visibility = null,
        IReadOnlyDictionary<string, SchemaFormRuleValue>? values = null,
        IReadOnlyDictionary<string, SchemaFormPresentation>? presentations = null,
        bool hasPending = false,
        bool isSaveBlocked = false) => new()
    {
        Visibility = visibility ?? new Dictionary<string, SchemaFormVisibility>(),
        Values = values ?? new Dictionary<string, SchemaFormRuleValue>(),
        Presentations = presentations ?? new Dictionary<string, SchemaFormPresentation>(),
        HasPending = hasPending,
        IsSaveBlocked = isSaveBlocked,
    };

    private static SchemaFormValidationResult Invalid(params SchemaFormValidationError[] errors) => new(false, errors);

    private sealed class TestRuleGraph(Func<SchemaFormRuleInstance, SchemaFormRuleEvaluation> evaluate) : ISchemaFormRuleGraph
    {
        public SchemaFormRuleEvaluation EvaluateInstance(SchemaFormRuleInstance instance) => evaluate(instance);
    }
}
