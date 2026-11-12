using System.Text;
using System.Text.Json;
using Harborline.Foundation.Assistance;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Foundation.Assistance.Tests;

public sealed class CatalogueNarrowingTests
{
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);
    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
    private static AssistanceCommand Command(string id, AssistanceClassificationTier tier = AssistanceClassificationTier.Ap, string schema = "{}") =>
        new(id, [], Json(schema), new(tier, true, "security-access-control", "Human authority required."));
    private static AssistanceDefinition Binding(params AssistanceCommand[] commands) => new(
        "tenant", "binding", "1.0.0", "surface", "/surface", commands, [], ["alice"], new("provider", "model"),
        Envelope: new("binding", "1.0.0", "tenant", AssistanceCascadeLayer.Tenant, Json("{}"), "restricted", false, [], new(1, 0)));

    [Theory(DisplayName = "pilot-auth-9 pilot-ck-5: unexported catalogue command refuses in every phase")]
    [InlineData(AssistanceAdmissionPhase.Author)]
    [InlineData(AssistanceAdmissionPhase.Publish)]
    [InlineData(AssistanceAdmissionPhase.Install)]
    public void Unexported(AssistanceAdmissionPhase phase)
    {
        var registry = new FixtureCommandCatalogueRegistry();
        var binding = Binding(Command("private"));
        Assert.Equal([new AssistanceRefusal("definition.command_not_exported", "/commands/0/command_id")], AssistanceDefinitionAdmission.Validate(binding, phase, Window, registry));
        registry.Replace(registry.Resolve("private")! with { Exported = true });
        Assert.Empty(AssistanceDefinitionAdmission.Validate(binding, phase, Window, registry));
    }

    [Theory(DisplayName = "pilot-auth-4 pilot-auth-10 pilot-ck-8: catalogue tier is a floor in every phase")]
    [InlineData(AssistanceAdmissionPhase.Author)]
    [InlineData(AssistanceAdmissionPhase.Publish)]
    [InlineData(AssistanceAdmissionPhase.Install)]
    public void Tier(AssistanceAdmissionPhase phase)
    {
        var registry = new FixtureCommandCatalogueRegistry();
        foreach (var (id, lower) in new[] { ("cp", AssistanceClassificationTier.Ap), ("never", AssistanceClassificationTier.Cp) })
            Assert.Equal([new AssistanceRefusal("definition.tier_below_catalogue", "/commands/0/classification/tier")], AssistanceDefinitionAdmission.Validate(Binding(Command(id, lower)), phase, Window, registry));
        foreach (var (id, tier) in new[] { ("ap", AssistanceClassificationTier.Ap), ("ap", AssistanceClassificationTier.Cp), ("cp", AssistanceClassificationTier.Cp), ("cp", AssistanceClassificationTier.Never), ("never", AssistanceClassificationTier.Never) })
            Assert.Empty(AssistanceDefinitionAdmission.Validate(Binding(Command(id, tier)), phase, Window, registry));
    }

    // Literal schema pairs follow JSON Schema 2020-12 Validation §§6.1–6.5 and the slice's conservative rules.
    private static IEnumerable<(string Strict, string Loose)> SchemaPairs()
    {
        yield return ("{\"properties\":{\"x\":{\"not\":{\"$ref\":\"#/properties/y\"}},\"y\":{\"type\":\"string\"}}}", "{\"properties\":{\"x\":{\"not\":{\"$ref\":\"#/properties/y\"}},\"y\":{\"type\":\"string\",\"enum\":[\"a\"]}}}");
        yield return ("{\"required\":[\"id\"]}", "{\"required\":[]}");
        yield return ("{\"additionalProperties\":false}", "{\"additionalProperties\":true}");
        yield return ("{\"additionalProperties\":false}", "{}");
        yield return ("{\"additionalProperties\":false}", "{\"additionalProperties\":false,\"properties\":{\"extra\":{}}}");
        yield return ("{\"type\":\"string\"}", "{\"type\":\"number\"}");
        yield return ("{\"enum\":[1]}", "{\"enum\":[1,2]}");
        foreach (var keyword in new[] { "minimum", "minLength", "minItems" })
            yield return ($"{{\"{keyword}\":2}}", $"{{\"{keyword}\":1}}");
        foreach (var keyword in new[] { "maximum", "maxLength", "maxItems" })
        {
            yield return ($"{{\"{keyword}\":1}}", $"{{\"{keyword}\":2}}");
            yield return ($"{{\"{keyword}\":1}}", "{}");
        }
        yield return ("{\"pattern\":\"^a\"}", "{\"pattern\":\"a\"}");
        yield return ("{\"custom\":1}", "{\"custom\":2}");
        yield return ("{}", "{\"custom\":1}");
        yield return ("{\"properties\":{\"x\":{\"maximum\":1}}}", "{\"properties\":{\"x\":{\"maximum\":2}}}");
        yield return ("{\"properties\":{\"x\":{\"maximum\":1}}}", "{\"properties\":{\"x\":{},\"x\":{\"maximum\":1}}}");
        yield return ("{\"items\":{\"maxLength\":1}}", "{\"items\":{\"maxLength\":2}}");
    }

    [Theory(DisplayName = "pilot-auth-3 pilot-auth-10 pilot-ck-6: schema widening refuses in every phase")]
    [InlineData(AssistanceAdmissionPhase.Author)]
    [InlineData(AssistanceAdmissionPhase.Publish)]
    [InlineData(AssistanceAdmissionPhase.Install)]
    public void Schema(AssistanceAdmissionPhase phase)
    {
        var registry = new FixtureCommandCatalogueRegistry();
        foreach (var (strict, loose) in SchemaPairs())
        {
            registry.Replace(registry.Resolve("ap")! with { ArgsSchema = Json(strict) });
            Assert.Equal([new AssistanceRefusal("definition.args_schema_looser_than_catalogue", "/commands/0/args_schema")], AssistanceDefinitionAdmission.Validate(Binding(Command("ap", schema: loose)), phase, Window, registry));
            Assert.Empty(AssistanceDefinitionAdmission.Validate(Binding(Command("ap", schema: strict)), phase, Window, registry));
        }
        foreach (var (strict, loose) in SchemaPairs().Where(pair => !pair.Strict.Contains("pattern", StringComparison.Ordinal) && !pair.Strict.Contains("type", StringComparison.Ordinal) && !pair.Strict.Contains("custom", StringComparison.Ordinal) && !pair.Loose.Contains("custom", StringComparison.Ordinal) && !pair.Loose.Contains("extra", StringComparison.Ordinal) && !pair.Loose.Contains("\"x\":{},\"x\"", StringComparison.Ordinal)))
        {
            registry.Replace(registry.Resolve("ap")! with { ArgsSchema = Json(loose) });
            Assert.Empty(AssistanceDefinitionAdmission.Validate(Binding(Command("ap", schema: strict)), phase, Window, registry));
        }
    }

    [Theory(DisplayName = "pilot-auth-3 pilot-ck-6: rounded numeric bounds refuse in every phase")]
    [InlineData(AssistanceAdmissionPhase.Author)]
    [InlineData(AssistanceAdmissionPhase.Publish)]
    [InlineData(AssistanceAdmissionPhase.Install)]
    public void RoundedBounds(AssistanceAdmissionPhase phase)
    {
        var registry = new FixtureCommandCatalogueRegistry();
        registry.Replace(registry.Resolve("ap")! with { ArgsSchema = Json("""{"properties":{"x":{"minimum":1e-29}}}""") });
        Assert.Equal([new AssistanceRefusal("definition.args_schema_looser_than_catalogue", "/commands/0/args_schema")],
            AssistanceDefinitionAdmission.Validate(Binding(Command("ap", schema: """{"properties":{"x":{"minimum":0}}}""")), phase, Window, registry));
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("""{"maximum":1e-29}"""), Json("""{"maximum":0}""")));
    }

    [Fact(DisplayName = "pilot-auth-3 pilot-ck-6: exact decimal bounds still narrow")]
    public void ExactBounds()
    {
        Assert.True(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(
            Json("""{"properties":{"x":{"minimum":2e-28}}}"""), Json("""{"properties":{"x":{"minimum":1e-28}}}""")));
    }

    [Fact(DisplayName = "pilot-auth-3 pilot-ck-6: negated reference observes narrowed sibling")]
    public void NegatedSiblingReference()
    {
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(
            Json("""{"properties":{"x":{"not":{"$ref":"#/properties/y"}},"y":{"type":"string","enum":["a"]}}}"""),
            Json("""{"properties":{"x":{"not":{"$ref":"#/properties/y"}},"y":{"type":"string"}}}""")));
    }

    [Fact(DisplayName = "pilot-auth-3 pilot-ck-6: bounded checker fails closed and compares nested constraints")]
    public void CheckerBoundaries()
    {
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("[]"), Json("{}")));
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"maximum\":\"bad\"}"), Json("{}")));
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"x\":1,\"x\":2}"), Json("{}")));
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"required\":[1]}"), Json("{}")));
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"required\":[\"x\",\"x\"]}"), Json("{}")));
        Assert.True(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"items\":false}"), Json("{\"items\":true}")));
        Assert.True(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"properties\":{},\"additionalProperties\":false}"), Json("{\"properties\":{\"x\":{\"type\":\"string\"}},\"additionalProperties\":false}")));
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"properties\":{\"x\":{}},\"additionalProperties\":{\"type\":\"string\"}}"), Json("{\"additionalProperties\":{\"type\":\"string\"}}")));
        Assert.True(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"properties\":{\"x\":{\"type\":\"string\"}},\"additionalProperties\":false}"), Json("{\"additionalProperties\":{\"type\":\"string\"}}")));
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"unevaluatedProperties\":false,\"properties\":{\"x\":{}}}"), Json("{\"unevaluatedProperties\":false}")));
        Assert.True(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json("{\"$ref\":\"#/$defs/x\"}"), Json("{\"$ref\":\"#/$defs/x\"}")));
        var opaqueDuplicate = Json("{\"allOf\":[{\"properties\":{\"x\":{},\"x\":{}}}]}");
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(opaqueDuplicate, opaqueDuplicate));
        var wide = "{\"enum\":[" + string.Join(',', Enumerable.Range(0, 4096)) + "]}";
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json(wide), Json(wide)));
        var deep = "{}";
        for (var i = 0; i < 34; i++) deep = "{\"items\":" + deep + "}";
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json(deep), Json(deep)));
        var large = "{\"description\":\"" + new string('x', 65536) + "\"}";
        Assert.False(AssistanceArgsSchemaNarrowing.IsAtLeastAsStrict(Json(large), Json(large)));
    }

    [Theory(DisplayName = "pilot-auth-10 pilot-ck-12: recipient permissions intersect in every phase")]
    [InlineData(AssistanceAdmissionPhase.Author)]
    [InlineData(AssistanceAdmissionPhase.Publish)]
    [InlineData(AssistanceAdmissionPhase.Install)]
    public void Recipients(AssistanceAdmissionPhase phase)
    {
        var registry = new FixtureCommandCatalogueRegistry();
        var mixed = Binding(Command("ap"), Command("cp", AssistanceClassificationTier.Cp));
        Assert.Equal([new AssistanceRefusal("definition.recipient_outside_target_permission", "/recipients/1")], AssistanceDefinitionAdmission.Validate(mixed with { Recipients = ["alice", "bob"] }, phase, Window, registry));
        Assert.Empty(AssistanceDefinitionAdmission.Validate(mixed, phase, Window, registry));
        Assert.Empty(AssistanceDefinitionAdmission.Validate(Binding(Command("ap")) with { Recipients = ["alice", "bob"] }, phase, Window, registry));
        Assert.Equal([new AssistanceRefusal("definition.recipient_outside_target_permission", "/recipients/0")], AssistanceDefinitionAdmission.Validate(mixed with { Recipients = ["outsider"] }, phase, Window, registry));
        registry.Replace(registry.Resolve("ap")! with { PermittedRecipients = null! });
        Assert.Equal([new AssistanceRefusal("definition.recipient_outside_target_permission", "/recipients/0")], AssistanceDefinitionAdmission.Validate(Binding(Command("ap")), phase, Window, registry));
    }

    [Theory(DisplayName = "pilot-auth-10 pilot-ck-6: previous maximum removal refuses in every phase")]
    [InlineData(AssistanceAdmissionPhase.Author)]
    [InlineData(AssistanceAdmissionPhase.Publish)]
    [InlineData(AssistanceAdmissionPhase.Install)]
    public void PreviousMaximum(AssistanceAdmissionPhase phase)
    {
        Assert.Equal([new AssistanceRefusal("definition.args_schema_widened", "/commands/0/args_schema")], AssistanceDefinitionAdmission.Validate(Binding(Command("ap")), phase, Window, new FixtureCommandCatalogueRegistry(), Binding(Command("ap", schema: "{\"maximum\":5}"))));
    }

    [Theory(DisplayName = "pilot-auth-9 pilot-auth-10: AdmitJson collects all catalogue refusals in every phase")]
    [InlineData(AssistanceAdmissionPhase.Author)]
    [InlineData(AssistanceAdmissionPhase.Publish)]
    [InlineData(AssistanceAdmissionPhase.Install)]
    public void AllRefusals(AssistanceAdmissionPhase phase)
    {
        var registry = new FixtureCommandCatalogueRegistry();
        registry.Replace(registry.Resolve("private")! with { Tier = AssistanceClassificationTier.Cp, ArgsSchema = Json("{\"required\":[\"id\"]}") });
        var binding = Binding(Command("private")) with { Recipients = ["bob"] };
        Assert.Equal(new[] {
            new AssistanceRefusal("definition.command_not_exported", "/commands/0/command_id"),
            new AssistanceRefusal("definition.tier_below_catalogue", "/commands/0/classification/tier"),
            new AssistanceRefusal("definition.args_schema_looser_than_catalogue", "/commands/0/args_schema"),
            new AssistanceRefusal("definition.recipient_outside_target_permission", "/recipients/0")
        }, AssistanceDefinitionAdmission.AdmitJson(Encoding.UTF8.GetString(AssistanceDefinitionJson.SerializeCanonical(binding)), phase, Window, registry));
    }
}
