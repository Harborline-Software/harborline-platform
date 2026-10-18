using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class LayoutBindingKindDecodeTests
{
    private static readonly IReadOnlyDictionary<string, string> Host =
        new Dictionary<string, string> { ["platform.layout"] = "1.0.0" };

    // Independent authored JSON: these fixtures do not round-trip the production serializer to
    // manufacture their discriminator, envelope or expected refusal identities.
    private const string Document = """
        {"envelope":{"identity":"surface.binding","version":"1.0.0","tenant":"tenant-a",
        "cascade_layer":"domain_package","provenance":{},"retention_class":"standard",
        "legal_hold":false,"requires":[{"capability":"platform.layout","minimum_platform_version":"1.0.0"}],
        "contract":{"major":1,"minor":0}},"schema_version":1,"medium":"screen","default_intent":"observe",
        "blocks":BLOCKS,"page_layouts":[],"page_masters":[],"page_runs":[],"drill_through_targets":[]}
        """;

    [Theory]
    [InlineData("record_field")]
    [InlineData("query")]
    [InlineData("measure")]
    [InlineData("template")]
    [InlineData("static")]
    [InlineData("text")]
    public void EveryDeclaredVariantDecodesAtRootChildAndGrandchild(string kind)
    {
        foreach (var depth in new[] { 0, 1, 2 })
        {
            var definition = Decode(Payload(AtDepth(Binding(kind), depth)));
            var block = Assert.Single(definition.Blocks);
            for (var index = 0; index < depth; index++) block = Assert.Single(block.Children);
            switch (kind)
            {
                case "record_field":
                    Assert.Equal("orders.customer", Assert.IsType<LayoutRecordFieldBinding>(block.Binding).FieldPath);
                    break;
                case "query":
                    Assert.Equal("view.orders", Assert.IsType<LayoutQueryBinding>(block.Binding).ViewDefinitionId);
                    break;
                case "measure":
                    Assert.Equal("orders.total", Assert.IsType<LayoutMeasureBinding>(block.Binding).MeasurePath);
                    break;
                case "template":
                    var template = Assert.IsType<LayoutTemplateBinding>(block.Binding);
                    Assert.Equal(("template.invoice", "1.0.0"), (template.TemplateDefinitionId, template.TemplateVersion));
                    break;
                case "static":
                    Assert.Equal("control", Assert.IsType<LayoutStaticBinding>(block.Binding).Content.GetString());
                    break;
                case "text":
                    Assert.Equal("control", Assert.Single(Assert.IsType<LayoutTextBinding>(block.Binding).Runs).Text);
                    break;
            }
        }
    }

    [Theory]
    [InlineData("record_field")]
    [InlineData("query")]
    [InlineData("measure")]
    [InlineData("template")]
    [InlineData("static")]
    [InlineData("text")]
    public void MissingExactTagInEveryVariantAndTypedPositionRefusesTheEntry(string kind)
    {
        var binding = JsonNode.Parse(Binding(kind))!.AsObject();
        Assert.True(binding.Remove("binding_kind"));
        foreach (var depth in new[] { 0, 1, 2 })
        {
            var payload = Payload(AtDepth(binding.ToJsonString(), depth));
            Assert.Throws<JsonException>(() => Decode(payload));
            AssertBodyInvalid(payload);
        }
    }

    [Fact]
    public void ValidRawAndCanonicalControlsPublishAndInstall()
    {
        var payload = Payload(Block(Binding("static")));
        var definition = Decode(payload);
        LayoutDefinitionAdmission.ValidateForPublish(definition, LayoutTestAccess.GrantsAll);
        LayoutPackHostAdmission.Admit([Entry("surface.binding", payload)], Host);

        var canonical = LayoutDefinitionJson.SerializeCanonical(definition);
        Assert.Equal("control", Assert.IsType<LayoutStaticBinding>(Assert.Single(LayoutDefinitionJson.Deserialize(canonical).Blocks).Binding).Content.GetString());
        LayoutPackHostAdmission.Admit([new("surface.binding", "1.0.0", PlatformPackageContent.PresentJson(canonical))], Host);
    }

    [Fact]
    public void MixedCaseOrdinaryNamesKeepValidBindingsAndCannotHideAMissingTag()
    {
        var valid = MixedCase(Payload(AtDepth(Binding("static"), 2)));
        Assert.Equal("control", Assert.IsType<LayoutStaticBinding>(Decode(valid).Blocks[0].Children[0].Children[0].Binding).Content.GetString());
        LayoutPackHostAdmission.Admit([Entry("surface.binding", valid)], Host);

        var missing = MixedCase(Payload(AtDepth("{\"content\":\"PAYLOAD_MARKER\"}", 2)));
        AssertBodyInvalid(missing);
        AssertBodyInvalid(Payload(Block("{\"Binding_Kind\":\"static\",\"content\":\"control\"}")));
    }

    [Fact]
    public void LaterSiblingAndDeepBranchBindingsAreAlsoChecked()
    {
        var valid = Block(Binding("static"), id: "first");
        var deep = AtDepth("{\"content\":\"PAYLOAD_MARKER\"}", 8);
        AssertBodyInvalid(Payload(valid + "," + deep));
        AssertBodyInvalid(Payload(Block(Binding("static"), "[" + valid + "," + deep + "]", "branch")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateBindingOccurrencesAreReadInBothOrders(bool missingFirst)
    {
        var first = missingFirst ? "{\"content\":\"missing\"}" : Binding("static");
        var second = missingFirst ? Binding("static") : "{\"content\":\"missing\"}";
        var block = Block(first).Replace("\"children\":", "\"Binding\":" + second + ",\"children\":", StringComparison.Ordinal);
        AssertBodyInvalid(Payload(block));

        var valid = Block(Binding("static")).Replace("\"children\":", "\"Binding\":{\"binding_kind\":\"static\",\"content\":\"last\"},\"children\":", StringComparison.Ordinal);
        Assert.Equal("last", Assert.IsType<LayoutStaticBinding>(Decode(Payload(valid)).Blocks[0].Binding).Content.GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateRootAndChildrenArraysCannotHideAnEarlierOrLaterMissingTag(bool missingFirst)
    {
        var good = Block(Binding("static"));
        var bad = Block("{\"content\":\"missing\"}");
        var first = missingFirst ? bad : good;
        var second = missingFirst ? good : bad;
        var duplicateRoots = Payload(first).Replace("\"page_layouts\":", "\"Blocks\":[" + second + "],\"page_layouts\":", StringComparison.Ordinal);
        AssertBodyInvalid(duplicateRoots);
        var duplicateChildren = Block(Binding("static"), "[" + first + "]");
        duplicateChildren = duplicateChildren[..^1] + ",\"Children\":[" + second + "]}";
        AssertBodyInvalid(Payload(duplicateChildren));

        var validRoots = Payload(good).Replace("\"page_layouts\":", "\"Blocks\":[" + Block(Binding("static"), id: "last") + "],\"page_layouts\":", StringComparison.Ordinal);
        Assert.Equal("last", Assert.Single(Decode(validRoots).Blocks).Id);
        var validChildren = Block(Binding("static"), "[" + good + "]");
        validChildren = validChildren[..^1] + ",\"Children\":[" + Block(Binding("static"), id: "last") + "]}";
        Assert.Equal("last", Assert.Single(Decode(Payload(validChildren)).Blocks[0].Children).Id);
    }

    [Fact]
    public void OpaqueJsonValuesAndLiteralTextAreNotTypedBindingPositions()
    {
        const string opaque = """{"binding":{},"children":[{"binding":{}}],"blocks":[{"binding":{}}]}""";
        var root = JsonNode.Parse(Payload(Block(Binding("static"))))!.AsObject();
        root["envelope"]!["provenance"] = JsonNode.Parse(opaque);
        var block = root["blocks"]![0]!;
        block["binding"]!["content"] = JsonNode.Parse(opaque);
        block["capture"] = JsonNode.Parse("{\"required\":null,\"validation_rules\":[],\"control\":{\"id\":\"opaque\",\"parameters\":" + opaque + "}}");
        block["default_selection"] = JsonNode.Parse(opaque);
        block["live_selection"] = JsonNode.Parse(opaque);

        var definition = Decode(root.ToJsonString());
        var decoded = Assert.Single(definition.Blocks);
        Assert.Equal(JsonValueKind.Object, definition.Envelope.Provenance.GetProperty("binding").ValueKind);
        Assert.Equal(JsonValueKind.Object, Assert.IsType<LayoutStaticBinding>(decoded.Binding).Content.GetProperty("binding").ValueKind);
        Assert.Equal(JsonValueKind.Object, decoded.Capture!.Control!.Parameters!.Value.GetProperty("binding").ValueKind);
        Assert.Equal(JsonValueKind.Object, decoded.DefaultSelection!.Value.GetProperty("binding").ValueKind);
        Assert.Equal(JsonValueKind.Object, decoded.LiveSelection!.Value.GetProperty("binding").ValueKind);

        var text = Decode(Payload(Block("{\"binding_kind\":\"text\",\"runs\":[{\"text\":\"{binding:{},children:[{}]}\"}]}")));
        Assert.Equal("{binding:{},children:[{}]}", Assert.Single(Assert.IsType<LayoutTextBinding>(text.Blocks[0].Binding).Runs).Text);
        // Live selection/capture are decoder-only controls, not a claim that semantic install admits them.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullAndAbsentBindingStillReachTheExistingSemanticRefusal(bool omitted)
    {
        var root = JsonNode.Parse(Payload(Block("null")))!;
        if (omitted) Assert.True(root["blocks"]![0]!.AsObject().Remove("binding"));
        var payload = root.ToJsonString();
        Assert.Null(Decode(payload).Blocks[0].Binding);

        var refused = Assert.Throws<DefinitionRefusalException>(() => LayoutPackHostAdmission.Admit([Entry("surface.binding", payload)], Host));
        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal([new DefinitionRefusal("layout.binding.invalid", "/blocks/0/binding", "surface.binding@1.0.0")], refused.Refusals);
    }

    [Fact]
    public void StrictUnknownMemberTrailingDocumentAndDepthBehaviorRemains()
    {
        var valid = Payload(Block(Binding("static")));
        AssertBodyInvalid(valid[..^1] + ",\"unknown\":true}");
        Assert.Throws<JsonException>(() => Decode(valid + " {}"));
        Assert.Throws<JsonException>(() => Decode(valid.Replace("\"provenance\":{}", "\"provenance\":" + new string('[', 65) + "0" + new string(']', 65), StringComparison.Ordinal)));
        AssertBodyInvalid(Payload(Block("{\"binding_kind\":\"unregistered\"}")));
        var withinDepth = valid.Replace("\"provenance\":{}", "\"provenance\":" + new string('[', 20) + "0" + new string(']', 20), StringComparison.Ordinal);
        Assert.Equal("control", Assert.IsType<LayoutStaticBinding>(Decode(withinDepth).Blocks[0].Binding).Content.GetString());
    }

    [Fact]
    public void EveryBadEntrySkipsLaterChecksWhileOtherEntryRefusalsAccumulate()
    {
        var bad = JsonNode.Parse(Payload(Block("{\"content\":\"PAYLOAD_MARKER\"}")))!;
        bad["envelope"]!["requires"] = new JsonArray();
        bad["blocks"]![0]!["kind"] = "layout.unregistered";
        var otherBad = Payload(AtDepth("{\"field_path\":\"PAYLOAD_MARKER\"}", 2));
        var stripped = JsonNode.Parse(Payload(Block(Binding("static"))))!;
        stripped["envelope"]!["requires"] = new JsonArray();

        var refused = Assert.Throws<DefinitionRefusalException>(() => LayoutPackHostAdmission.Admit(
            [Entry("surface.first", bad.ToJsonString()), Entry("surface.second", otherBad), Entry("surface.stripped", stripped.ToJsonString())], Host));

        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal(
            [new DefinitionRefusal("layout.definition.body_invalid", "", "surface.first@1.0.0"),
             new DefinitionRefusal("layout.definition.body_invalid", "", "surface.second@1.0.0"),
             new DefinitionRefusal("layout.pack.capability_undeclared", "/envelope/requires", "surface.stripped@1.0.0")],
            refused.Refusals);
        Assert.DoesNotContain("PAYLOAD_MARKER", JsonSerializer.Serialize(new { refused.Stage, refused.Refusals }));
    }

    [Fact]
    public void AnUnrelatedNotSupportedExceptionFromHostCapabilitiesPropagatesUnchanged()
    {
        var failure = new NotSupportedException("operational-marker");
        var observed = Assert.Throws<NotSupportedException>(() => LayoutPackHostAdmission.Admit(
            [Entry("surface.binding", Payload(Block(Binding("static"))))], new FailingCapabilities(failure)));

        Assert.Same(failure, observed);
    }

    private static LayoutDefinition Decode(string payload) => LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(payload));

    private static LayoutDefinitionPackageEntry Entry(string id, string payload)
        => new(id, "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(payload)));

    private static void AssertBodyInvalid(string payload)
    {
        var refused = Assert.Throws<DefinitionRefusalException>(() => LayoutPackHostAdmission.Admit([Entry("surface.binding", payload)], Host));
        Assert.Equal(DefinitionAdmissionPhase.Install, refused.Stage);
        Assert.Equal([new DefinitionRefusal("layout.definition.body_invalid", "", "surface.binding@1.0.0")], refused.Refusals);
    }

    private static string Payload(string blocks) => Document.Replace("BLOCKS", "[" + blocks + "]", StringComparison.Ordinal);

    private static string Block(string binding, string children = "[]", string id = "root")
        => "{\"id\":\"" + id + "\",\"kind\":\"layout.text\",\"binding\":" + binding + ",\"children\":" + children + ",\"container\":{\"kind\":\"stack\"}}";

    private static string AtDepth(string binding, int depth)
    {
        var block = Block(binding, id: "leaf");
        for (var index = 0; index < depth; index++) block = Block(Binding("static"), "[" + block + "]", "parent-" + index);
        return block;
    }

    private static string MixedCase(string payload) => payload
        .Replace("\"blocks\":", "\"BlOcKs\":", StringComparison.Ordinal)
        .Replace("\"children\":", "\"ChIlDrEn\":", StringComparison.Ordinal)
        .Replace("\"binding\":", "\"BiNdInG\":", StringComparison.Ordinal);

    private static string Binding(string kind) => kind switch
    {
        "record_field" => """{"binding_kind":"record_field","field_path":"orders.customer"}""",
        "query" => """{"binding_kind":"query","view_definition_id":"view.orders"}""",
        "measure" => """{"binding_kind":"measure","measure_path":"orders.total"}""",
        "template" => """{"binding_kind":"template","template_definition_id":"template.invoice","template_version":"1.0.0"}""",
        "static" => """{"binding_kind":"static","content":"control"}""",
        "text" => """{"binding_kind":"text","runs":[{"text":"control"}]}""",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class FailingCapabilities(NotSupportedException failure) : IReadOnlyDictionary<string, string>
    {
        public int Count => 0;
        public IEnumerable<string> Keys => [];
        public IEnumerable<string> Values => [];
        public string this[string key] => throw failure;
        public bool ContainsKey(string key) => throw failure;
        public bool TryGetValue(string key, out string value) => throw failure;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
