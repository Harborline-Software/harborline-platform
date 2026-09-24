using System.Globalization;
using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class FieldEditorCardinalityTests
{
    public static TheoryData<ValueDomainSourceKind, int, string> EditorCases => new()
    {
        { ValueDomainSourceKind.LiteralSet, 0, "None" },
        { ValueDomainSourceKind.TaxonomyScheme, 0, "None" },
        { ValueDomainSourceKind.RecordQuery, 0, "None" },
        { ValueDomainSourceKind.LiteralSet, 1, "SingleValue" },
        { ValueDomainSourceKind.TaxonomyScheme, 1, "SingleValue" },
        { ValueDomainSourceKind.RecordQuery, 1, "SingleValue" },
        { ValueDomainSourceKind.LiteralSet, 2, "RadioGroup" },
        { ValueDomainSourceKind.TaxonomyScheme, 2, "RadioGroup" },
        { ValueDomainSourceKind.RecordQuery, 2, "RadioGroup" },
        { ValueDomainSourceKind.LiteralSet, 5, "RadioGroup" },
        { ValueDomainSourceKind.TaxonomyScheme, 5, "RadioGroup" },
        { ValueDomainSourceKind.RecordQuery, 5, "RadioGroup" },
        { ValueDomainSourceKind.LiteralSet, 6, "ChoiceList" },
        { ValueDomainSourceKind.TaxonomyScheme, 6, "TaxonomyPicker" },
        { ValueDomainSourceKind.RecordQuery, 6, "RecordPicker" },
    };

    [Theory]
    [MemberData(nameof(EditorCases))]
    public async Task Editor_policy_uses_authorized_count_consistently_across_operations(
        ValueDomainSourceKind source, int count, string expected)
    {
        var fixture = new DomainFixture();
        string[] values = ["a", "b", "c", "d", "e", "f", "hidden"];
        var scheme = new TaxonomySchemeReference("status", "1");
        fixture.Schemes[scheme] = values.Select(value => DomainFixture.Member(value)).ToArray();
        fixture.Records["status"] = values.Select(value => DomainFixture.Member(value)).ToArray();
        fixture.CanRead = (_, member) => values.Take(count).Contains(member.Value, StringComparer.Ordinal);
        var domain = source switch
        {
            ValueDomainSourceKind.LiteralSet => new ValueDomainDefinition(LiteralValues: values),
            ValueDomainSourceKind.TaxonomyScheme => new(TaxonomyScheme: scheme),
            _ => new(RecordQuery: new("status", "true")),
        };
        var floor = new FieldConstraintDefinition(false, 0, 1, [], domain);
        var runtime = fixture.Runtime();
        var resolved = await runtime.ResolveAsync(domain, DomainFixture.Scope, "/status");
        var intersection = await runtime.IntersectAsync([floor], DomainFixture.Scope, "/status");
        var narrowed = await runtime.NarrowAsync(floor, floor with
        {
            ValueDomain = new(LiteralValues: values),
        }, DomainFixture.Scope, "/status");

        Assert.Equal(expected, resolved.Editor.ToString());
        Assert.Equal(expected, intersection.Editor.ToString());
        Assert.Equal(expected, narrowed.Editor.ToString());
        Assert.Equal(values.Take(count), resolved.Values);
        Assert.Equal(resolved.Values, intersection.Values);
        Assert.Equal(resolved.Values, narrowed.Values);
        Assert.Contains(narrowed.Sources, attribution => attribution.SourceKind == source
            && attribution.RecordQuery == domain.RecordQuery && attribution.TaxonomyScheme == domain.TaxonomyScheme);
    }

    [Theory]
    [InlineData(ValueDomainSourceKind.LiteralSet)]
    [InlineData(ValueDomainSourceKind.TaxonomyScheme)]
    [InlineData(ValueDomainSourceKind.RecordQuery)]
    public async Task Three_readable_values_choose_radios_for_every_source(ValueDomainSourceKind source)
    {
        var fixture = new DomainFixture();
        string[] values = ["open", "closed", "pending"];
        var scheme = new TaxonomySchemeReference("status", "1");
        fixture.Schemes[scheme] = values.Select(value => DomainFixture.Member(value)).ToArray();
        fixture.Records["status"] = values.Select(value => DomainFixture.Member(value)).ToArray();
        var domain = source switch
        {
            ValueDomainSourceKind.LiteralSet => new ValueDomainDefinition(LiteralValues: values),
            ValueDomainSourceKind.TaxonomyScheme => new(TaxonomyScheme: scheme),
            _ => new(RecordQuery: new("status", "true")),
        };

        var resolved = await fixture.Runtime().ResolveAsync(domain, DomainFixture.Scope, "/status");

        Assert.Equal("RadioGroup", resolved.Editor.ToString());
        Assert.Equal(source, resolved.SourceKind);
        Assert.Equal(values, resolved.Values);
    }

    [Theory]
    [InlineData(false, "RecordPicker", 40000)]
    [InlineData(true, "RadioGroup", 3)]
    public async Task Large_query_chooses_editor_from_readable_membership(bool restrict, string editor, int count)
    {
        var fixture = new DomainFixture();
        fixture.Records["postcode"] = Enumerable.Range(0, 40000)
            .Select(index => DomainFixture.Member(index.ToString("D5", CultureInfo.InvariantCulture))).ToArray();
        if (restrict) fixture.CanRead = (_, member) => member.Value is "00000" or "00001" or "00002";

        var resolved = await fixture.Runtime().ResolveAsync(new(RecordQuery: new("postcode", "true")),
            DomainFixture.Scope, "/postcode");

        Assert.Equal(editor, resolved.Editor.ToString());
        Assert.Equal(count, resolved.Values.Count);
        Assert.Equal("true", resolved.Predicate);
    }
}
