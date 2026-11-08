using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

/// <summary>
/// The one ViewDefinitionDraft wire contract (T-486/engine-gaps). The expected document is written by hand from the
/// contract in interface.yaml, never captured from the serializer (T-724 ruling 121).
/// </summary>
public sealed class ViewDefinitionDraftJsonTests
{
    private const string Wire = """
        {
          "definition": {
            "envelope": {
              "identity": "work.queue",
              "version": "1.0.0",
              "tenant": "tenant-a",
              "cascadeLayer": "tenant",
              "provenance": {"source": "authoring"},
              "requires": [{"capability": "records.query", "minimumPlatformVersion": "1.0.0"}],
              "contract": {"major": 1, "minor": 0}
            },
            "schemaVersion": 1,
            "title": "Work queue",
            "recordType": "work-item",
            "ownership": "public",
            "openPermission": "work:read",
            "parameters": {
              "columns": [{"field": "title", "width": 240, "presentation": "text"}],
              "sort": [{"field": "title", "direction": "ascending"}],
              "filter": {"kind": "comparison", "field": "state", "operator": "equal", "value": "open"},
              "groupBy": "state",
              "measure": {"name": "work.count", "parameters": {"format": "integer"}}
            }
          },
          "binding": {
            "kind": "layout.table",
            "shapeRoles": {"title": "title"},
            "density": "standard"
          }
        }
        """;

    [Fact(DisplayName = "T-486 engine-gaps: a draft writes as the documented camelCase wire, nulls omitted")]
    public void ADraftWritesAsTheDocumentedWire()
    {
        var written = ViewDefinitionDraftJson.Serialize(Draft());

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Wire), JsonNode.Parse(written)), written);
    }

    [Fact(DisplayName = "T-486 engine-gaps: the documented wire reads into the typed draft and writes back unchanged")]
    public void TheDocumentedWireRoundTrips()
    {
        var draft = ViewDefinitionDraftJson.Deserialize(Wire);

        Assert.Equal("work.queue", draft.Definition.Key);
        Assert.Equal(ViewCascadeLayer.Tenant, draft.Definition.Envelope.CascadeLayer);
        Assert.Equal(ViewOwnershipTier.Public, draft.Definition.Ownership);
        Assert.Equal(new(1, 0), draft.Definition.Envelope.Contract);
        var filter = Assert.IsType<ViewComparisonFilter>(draft.Definition.Parameters.Filter);
        Assert.Equal(("state", ViewComparisonOperator.Equal, "open"), (filter.Field, filter.Operator, filter.Value.GetString()));
        Assert.Equal("integer", draft.Definition.Parameters.Measure!.Parameters["format"]);
        Assert.Equal("title", draft.Binding.ShapeRoles[ViewShapeRole.Title]);
        Assert.Equal(ViewDensity.Standard, draft.Binding.Density);
        Assert.Null(draft.AggregationExpression);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Wire), JsonNode.Parse(ViewDefinitionDraftJson.Serialize(draft))));
    }

    [Fact(DisplayName = "T-486 engine-gaps: a draft with no filter, grouping, measure or contract reads and writes back with those members absent")]
    public void ADraftWithoutOptionalMembersRoundTrips()
    {
        const string minimal = """
            {
              "definition": {
                "envelope": {"identity": "work.queue", "version": "1.0.0", "tenant": "tenant-a", "cascadeLayer": "tenant", "provenance": {}, "requires": []},
                "schemaVersion": 1, "title": "Work queue", "recordType": "work-item", "ownership": "public", "openPermission": "work:read",
                "parameters": {"columns": [{"field": "title", "width": 240, "presentation": "text"}], "sort": []}
              },
              "binding": {"kind": "layout.table", "shapeRoles": {"title": "title"}, "density": "standard"}
            }
            """;

        var draft = ViewDefinitionDraftJson.Deserialize(minimal);

        Assert.Null(draft.Definition.Envelope.Contract); // admission, not the wire, refuses a missing contract at /envelope/contract
        Assert.Null(draft.Definition.Parameters.Filter);
        Assert.Null(draft.Definition.Parameters.GroupBy);
        Assert.Null(draft.Definition.Parameters.Measure);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(minimal), JsonNode.Parse(ViewDefinitionDraftJson.Serialize(draft))));
    }

    [Fact(DisplayName = "T-486 engine-gaps: the inputs admission refuses travel on the wire, so the api can refuse them with their pointers")]
    public void TheInputsAdmissionRefusesTravelOnTheWire()
    {
        var wire = JsonNode.Parse(Wire)!.AsObject();
        wire["aggregationExpression"] = "sum(cost)";
        wire["rowVisibilityRule"] = "owner = current_user()";
        wire["scopeToken"] = "@me";

        var draft = ViewDefinitionDraftJson.Deserialize(wire.ToJsonString());

        Assert.Equal(("sum(cost)", "owner = current_user()", "@me"), (draft.AggregationExpression, draft.RowVisibilityRule, draft.ScopeToken));
    }

    [Theory(DisplayName = "T-486 engine-gaps: anything outside the contract refuses rather than being read leniently")]
    [InlineData("unknown top-level member", "/extra", "1")]
    [InlineData("unknown nested member", "/definition/parameters/extra", "1")]
    [InlineData("an integer for an enum", "/definition/ownership", "1")]
    [InlineData("an unknown enum spelling", "/binding/density", "\"tight\"")]
    [InlineData("an unknown filter kind", "/definition/parameters/filter/kind", "\"sql\"")]
    [InlineData("a null required string", "/definition/title", "null")]
    [InlineData("a null column", "/definition/parameters/columns", "[null]")]
    [InlineData("a null sort entry", "/definition/parameters/sort", "[null]")]
    [InlineData("a null requirement", "/definition/envelope/requires", "[null]")]
    [InlineData("a null filter inside a filter list", "/definition/parameters/filter", "{\"kind\":\"all\",\"filters\":[null]}")]
    [InlineData("a null shape-role field", "/binding/shapeRoles/title", "null")]
    [InlineData("a null measure parameter", "/definition/parameters/measure/parameters/format", "null")]
    public void AnythingOutsideTheContractRefuses(string _, string pointer, string value)
    {
        var wire = JsonNode.Parse(Wire)!;
        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parent = segments[..^1].Aggregate(wire, (node, segment) => node[segment]!);
        parent.AsObject()[segments[^1]] = JsonNode.Parse(value);

        Assert.ThrowsAny<JsonException>(() => ViewDefinitionDraftJson.Deserialize(wire.ToJsonString()));
    }

    [Fact(DisplayName = "T-486 engine-gaps: a missing required member, a repeated member and a trailing value refuse")]
    public void MissingRepeatedAndTrailingRefuse()
    {
        var missing = JsonNode.Parse(Wire)!;
        missing["definition"]!.AsObject().Remove("title");
        Assert.ThrowsAny<JsonException>(() => ViewDefinitionDraftJson.Deserialize(missing.ToJsonString()));

        var repeated = Wire.Replace("\"title\": \"Work queue\",", "\"title\": \"Work queue\", \"title\": \"Other\",", StringComparison.Ordinal);
        Assert.NotEqual(Wire, repeated);
        Assert.ThrowsAny<JsonException>(() => ViewDefinitionDraftJson.Deserialize(repeated));

        Assert.ThrowsAny<JsonException>(() => ViewDefinitionDraftJson.Deserialize(Wire + " {}"));
        Assert.ThrowsAny<JsonException>(() => ViewDefinitionDraftJson.Deserialize("null"));
    }

    private static ViewDefinitionDraft Draft() => new(
        new ViewDefinition(
            new(
                "work.queue",
                "1.0.0",
                "tenant-a",
                ViewCascadeLayer.Tenant,
                JsonSerializer.SerializeToElement(new { source = "authoring" }),
                [new("records.query", "1.0.0")],
                new(1, 0)),
            SchemaVersion: 1,
            Title: "Work queue",
            RecordType: "work-item",
            Ownership: ViewOwnershipTier.Public,
            OpenPermission: "work:read",
            Parameters: new(
                Columns: [new("title", 240)],
                Sort: [new("title", ViewSortDirection.Ascending)],
                Filter: ViewFilter.Equal("state", "open"),
                GroupBy: "state",
                Measure: new("work.count", new Dictionary<string, string> { ["format"] = "integer" }))),
        new ViewBinding("layout.table", new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }));
}
