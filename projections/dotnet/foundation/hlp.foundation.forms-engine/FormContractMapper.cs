using System.Text.Json;
using Contract = Harborline.Contracts.Forms;
using State = Harborline.Foundation.Forms.Models;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.Forms.Engine;

internal static class FormContractMapper
{
    public static Contract.RuleDefinition ToContractRule(State.RuleDefinition rule) => new()
    {
        Id = rule.Id,
        Tier = rule.Tier switch { State.RuleTier.JsonSchema => Contract.RuleTier.JsonSchema, State.RuleTier.JsonLogic => Contract.RuleTier.JsonLogic, _ => Contract.RuleTier.PowerFx },
        Scope = rule.Scope switch { State.RuleScope.Field => Contract.RuleScope.Field, State.RuleScope.Section => Contract.RuleScope.Section, State.RuleScope.Schema => Contract.RuleScope.Schema, State.RuleScope.Row => Contract.RuleScope.Row, _ => Contract.RuleScope.Table },
        ScopeTarget = rule.ScopeTarget,
        Expression = rule.Expression,
        Action = rule.Action switch
        {
            State.RuleActionKind.Visibility => Contract.RuleActionKind.Visibility,
            State.RuleActionKind.Required => Contract.RuleActionKind.Required,
            State.RuleActionKind.ReadOnly => Contract.RuleActionKind.ReadOnly,
            State.RuleActionKind.Validate => Contract.RuleActionKind.Validate,
            State.RuleActionKind.Compute => Contract.RuleActionKind.Compute,
            State.RuleActionKind.Presentation => Contract.RuleActionKind.Presentation,
            _ => Contract.RuleActionKind.Options,
        },
        ErrorMessage = rule.ErrorMessage is null ? default : ToText(rule.ErrorMessage),
        Presentation = rule.Presentation is null ? default : new Contract.PresentationHint
        {
            Severity = rule.Presentation.Severity switch
            {
                "info" => Contract.PresentationHintSeverityValue.Info,
                "warn" => Contract.PresentationHintSeverityValue.Warn,
                "error" => Contract.PresentationHintSeverityValue.Error,
                _ => default(Contract.Optional<Contract.PresentationHintSeverityValue>),
            },
            Badge = rule.Presentation.Badge is null ? default : ToText(rule.Presentation.Badge),
            StyleToken = rule.Presentation.StyleToken is null ? default : rule.Presentation.StyleToken,
        },
    };

    public static Contract.FormView ToView(
        FormExecutionScope scope,
        State.FormDefinition definition,
        JsonDocument? candidate,
        IReadOnlySet<string> sensitiveFields,
        IReadOnlySet<string> withheldFields,
        RuleEvaluationResult? rules)
    {
        var readable = FormCandidateEvaluator.ReadableFields(scope, definition);
        var projected = new Dictionary<string, Contract.FormViewField>(StringComparer.Ordinal);
        foreach (var (name, field) in definition.Overlay.Fields)
        {
            var isReadable = readable.Contains(name) && !withheldFields.Contains(name);
            var ruleState = rules?.Visibility.GetValueOrDefault($"field:{name}");
            var presentation = RulePresentationFor(name, rules);
            var value = isReadable && candidate?.RootElement.TryGetProperty(name, out var element) == true
                ? Contract.Optional<JsonElement>.Some(element.Clone())
                : default;
            projected[name] = new Contract.FormViewField
            {
                Name = name,
                Label = ToText(field.Label),
                HelpText = field.HelpText is null ? default : Contract.Optional<Contract.InternationalizedText?>.Some(ToText(field.HelpText)),
                ControlHint = field.ControlHint is null ? default : Contract.Optional<string?>.Some(field.ControlHint),
                IsSensitive = sensitiveFields.Contains(name) || field.PiiSensitivity == State.PiiSensitivity.Sensitive,
                IsReadable = isReadable,
                Value = value,
                ReadOnly = ruleState is null ? default : ruleState.ReadOnly,
                Presentation = ToPresentation(presentation),
                Rules = RulesFor(name, rules, ruleState, presentation),
            };
        }

        var sections = definition.Overlay.Sections.Select(section => new Contract.FormViewSection
        {
            Id = section.Id,
            Title = ToText(section.Title),
            Fields = section.Fields.Where(projected.ContainsKey).Select(name => projected[name]).ToArray(),
            Layout = section.Layout is null
                ? default(Contract.Optional<Contract.SectionLayout>)
                : ToLayout(section.Layout),
            FieldPlacement = section.FieldPlacement is null
                ? default(Contract.Optional<IReadOnlyDictionary<string, Contract.FieldPlacement>>)
                : section.FieldPlacement.ToDictionary(row => row.Key, row => ToPlacement(row.Value), StringComparer.Ordinal),
            Items = section.Items is null ? default : Contract.Optional<IReadOnlyList<Contract.FormViewItem>>.Some(ToItems(section.Items, projected)),
        }).ToArray();

        return new Contract.FormView
        {
            FormId = definition.Id.Value,
            Version = definition.Version.ToString(),
            Title = definition.Overlay.Title is null ? default : Contract.Optional<Contract.InternationalizedText?>.Some(ToText(definition.Overlay.Title)),
            Description = definition.Overlay.Description is null ? default : Contract.Optional<Contract.InternationalizedText?>.Some(ToText(definition.Overlay.Description)),
            Sections = sections,
        };
    }

    public static Contract.InternationalizedText ToText(State.InternationalizedText text) => new()
    {
        DefaultLocale = text.DefaultLocale,
        Values = new Dictionary<string, string>(text.Values, StringComparer.Ordinal),
    };

    private static PresentationOutcome? RulePresentationFor(string field, RuleEvaluationResult? rules)
    {
        return rules?.ByRule.Values.LastOrDefault(
            row => row.Target.Key == $"field:{field}" && row.Presentation is not null)?.Presentation;
    }

    private static Contract.Optional<Contract.PresentationOutcome> ToPresentation(PresentationOutcome? outcome)
    {
        if (outcome is null) return default;
        return new Contract.PresentationOutcome
        {
            Severity = outcome.Severity is null ? default : Contract.Optional<Contract.Severity?>.Some(outcome.Severity.Value switch
            {
                Severity.Info => Contract.Severity.Info,
                Severity.Warn => Contract.Severity.Warn,
                _ => Contract.Severity.Error,
            }),
            Badge = outcome.Badge is null ? default : outcome.Badge,
            StyleToken = outcome.StyleToken is null ? default : outcome.StyleToken,
        };
    }

    private static Contract.Optional<Contract.FormViewFieldRules> RulesFor(
        string field,
        RuleEvaluationResult? rules,
        VisibilityState? visibility,
        PresentationOutcome? presentation)
    {
        if (rules is null) return default;
        var hasComputed = rules.Values.TryGetValue($"field:{field}", out var computed);
        if (visibility is null && !hasComputed && presentation is null) return default;
        return new Contract.FormViewFieldRules
        {
            Visible = visibility?.Visible ?? true,
            Required = visibility?.Required ?? false,
            ReadOnly = visibility?.ReadOnly ?? false,
            Computed = computed is { State: ValueState.Resolved }
                ? Contract.Optional<JsonElement>.Some(JsonSerializer.SerializeToElement(computed.Value))
                : default,
            PresentationSeverity = presentation?.Severity is null
                ? default
                : Contract.Optional<Contract.Severity?>.Some(presentation.Severity.Value switch
                {
                    Severity.Info => Contract.Severity.Info,
                    Severity.Warn => Contract.Severity.Warn,
                    _ => Contract.Severity.Error,
                }),
            PresentationBadge = presentation?.Badge is null ? default : presentation.Badge,
            PresentationStyleToken = presentation?.StyleToken is null ? default : presentation.StyleToken,
        };
    }

    private static Contract.SectionLayout ToLayout(State.SectionLayout layout) => new()
    {
        Kind = layout.Kind switch { State.SectionLayoutKind.Stack => Contract.SectionLayoutKind.Stack, State.SectionLayoutKind.Flex => Contract.SectionLayoutKind.Flex, _ => Contract.SectionLayoutKind.Grid },
        Direction = layout.Direction == State.FlexDirection.Row ? Contract.FlexDirection.Row : Contract.FlexDirection.Column,
        Wrap = layout.Wrap == State.FlexWrap.Wrap ? Contract.FlexWrap.Wrap : Contract.FlexWrap.Nowrap,
        Columns = layout.Columns,
        Gap = new Contract.LayoutGap(layout.Gap),
        CollapseBelow = layout.CollapseBelow switch { "sm" => Contract.LayoutBreakpoint.Sm, "md" => Contract.LayoutBreakpoint.Md, "lg" => Contract.LayoutBreakpoint.Lg, _ => default(Contract.Optional<Contract.LayoutBreakpoint>) },
        Density = layout.Density switch { "comfortable" => Contract.LayoutDensity.Comfortable, "compact" => Contract.LayoutDensity.Compact, _ => default(Contract.Optional<Contract.LayoutDensity>) },
        Align = ToAlign(layout.Align),
    };

    private static Contract.FieldPlacement ToPlacement(State.FieldPlacement placement) => new()
    {
        ColSpan = placement.ColSpan,
        Grow = placement.Grow,
        Width = ToWidth(placement.Width),
        Align = ToAlign(placement.Align),
    };

    private static Contract.Optional<Contract.LayoutAlign> ToAlign(string? value) => value switch
    {
        "start" => Contract.LayoutAlign.Start,
        "center" => Contract.LayoutAlign.Center,
        "end" => Contract.LayoutAlign.End,
        "stretch" => Contract.LayoutAlign.Stretch,
        _ => default,
    };

    private static IReadOnlyList<Contract.FormViewItem> ToItems(
        IReadOnlyList<State.FormItem> items,
        IReadOnlyDictionary<string, Contract.FormViewField> fields)
    {
        var result = new List<Contract.FormViewItem>();
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case State.FormItemKind.Field when fields.TryGetValue(item.Key, out var field):
                    result.Add(new Contract.FieldFormViewItem { Key = item.Key, Field = field });
                    break;
                case State.FormItemKind.Group:
                    result.Add(new Contract.GroupFormViewItem
                    {
                        Key = item.Key, Items = ToItems(item.Items ?? Array.Empty<State.FormItem>(), fields),
                        Title = item.Title is null ? default : ToText(item.Title),
                        Layout = item.Layout is null
                            ? default(Contract.Optional<Contract.SectionLayout>)
                            : ToLayout(item.Layout),
                        Placement = item.Placement is null
                            ? default(Contract.Optional<IReadOnlyDictionary<string, Contract.FieldPlacement>>)
                            : item.Placement.ToDictionary(row => row.Key, row => ToPlacement(row.Value), StringComparer.Ordinal),
                    });
                    break;
                case State.FormItemKind.Collection:
                    result.Add(new Contract.CollectionFormViewItem
                    {
                        Key = item.Key, Items = ToItems(item.Items ?? Array.Empty<State.FormItem>(), fields),
                        Title = item.Title is null ? default : ToText(item.Title),
                        Cardinality = item.Cardinality is null ? default : new Contract.Cardinality { Min = item.Cardinality.Min, Max = Contract.Optional<decimal?>.Some(item.Cardinality.Max) },
                        Table = item.Table is null ? default : ToTable(item.Table),
                    });
                    break;
                case State.FormItemKind.Content:
                    result.Add(new Contract.ContentFormViewItem { Key = item.Key, Content = (item.Content ?? []).Select(ToContent).ToArray() });
                    break;
                case State.FormItemKind.Action when item.Action is not null:
                    result.Add(new Contract.ActionFormViewItem { Key = item.Key, Action = ToAction(item.Action) });
                    break;
            }
        }
        return result;
    }

    private static Contract.ContentNode ToContent(State.ContentNode node) => node.Kind == State.ContentNodeKinds.Heading
        ? new Contract.HeadingContentNode { Text = ToText(node.Text), Level = node.Level is null ? default : node.Level.Value }
        : new Contract.ParagraphContentNode { Text = ToText(node.Text) };

    private static Contract.FormActionConfig ToAction(State.FormActionConfig action) => new()
    {
        Kind = action.Kind == State.FormActionKinds.OpenUrl ? Contract.FormActionKind.OpenUrl : Contract.FormActionKind.ScrollToSection,
        Label = ToText(action.Label),
        Url = action.Url is null ? default : action.Url,
        SectionId = action.SectionId is null ? default : action.SectionId,
    };

    private static Contract.CollectionTableConfig ToTable(State.CollectionTableConfig table) => new()
    {
        Columns = table.Columns is null
            ? default
            : table.Columns.ToDictionary(
                row => row.Key,
                row => new Contract.CollectionColumn { Width = ToWidth(row.Value.Width), Align = ToAlign(row.Value.Align) },
                StringComparer.Ordinal),
        Totals = table.Totals is null ? default : table.Totals.ToArray(),
    };

    private static Contract.Optional<Contract.FieldWidth> ToWidth(string? width) => width switch
    {
        "auto" => Contract.FieldWidth.Auto,
        "1/4" => Contract.FieldWidth.OneQuarter,
        "1/3" => Contract.FieldWidth.OneThird,
        "1/2" => Contract.FieldWidth.OneHalf,
        "2/3" => Contract.FieldWidth.TwoThirds,
        "3/4" => Contract.FieldWidth.ThreeQuarters,
        "full" => Contract.FieldWidth.Full,
        _ => default,
    };
}
