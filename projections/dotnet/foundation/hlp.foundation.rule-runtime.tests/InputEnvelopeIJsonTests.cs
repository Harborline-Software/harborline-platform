using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Graph;
using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// T-1143: the rule context is refused at the input envelope when it is not I-JSON in the two respects the owner ruled on
/// 2026-11-07 (RFC 7493 §2.1 lone surrogates, raw or escaped; §2.3 repeated member names). The cases and their expected
/// outcomes are the shared fixture the TypeScript tier also reads, written from RFC 7493 rather than from either tier.
/// </summary>
public sealed partial class InputEnvelopeIJsonTests
{
    public static TheoryData<string, string, string?> Cases()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(FunctionRegisterTests.RepositoryRoot(),
            "conformance", "hlp.foundation.rule-runtime", "input-envelope-ijson.json")))!;
        var data = new TheoryData<string, string, string?>();
        foreach (var row in fixture["cases"]!.AsArray())
            data.Add(row!["name"]!.GetValue<string>(), row["text"]!.GetValue<string>(), row["refusal"]?.GetValue<string>());
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_rule_instance_text_is_refused_at_the_envelope_exactly_when_it_is_not_i_json(string name, string text, string? refusal)
    {
        _ = name;
        var json = RawUnits(text);
        if (refusal is null)
        {
            Assert.NotNull(RuleInstance.FromJsonText(json));
            return;
        }

        var refused = Assert.Throws<ArgumentException>(() => RuleInstance.FromJsonText(json));
        Assert.StartsWith("Rule context is not I-JSON: " + refusal, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_rule_context_text_is_refused_at_the_envelope_exactly_when_it_is_not_i_json(string name, string text, string? refusal)
    {
        _ = name;
        var json = "{\"root\":" + RawUnits(text) + "}";
        if (refusal is null)
        {
            Assert.NotNull(RuleContextSnapshot.FromJsonText(json));
            return;
        }

        var refused = Assert.Throws<ArgumentException>(() => RuleContextSnapshot.FromJsonText(json));
        Assert.StartsWith("Rule context is not I-JSON: " + refusal, refused.Message, StringComparison.Ordinal);
    }

    // The fixture writes a raw UTF-16 code unit as <U+D800>, because a JSON file cannot hold a lone one itself.
    private static string RawUnits(string text) => RawUnit().Replace(text,
        match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    [GeneratedRegex("<U\\+([0-9A-F]{4})>")]
    private static partial Regex RawUnit();
}
