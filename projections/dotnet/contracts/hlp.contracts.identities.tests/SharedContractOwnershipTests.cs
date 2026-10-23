using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Contracts.Forms;
using Harborline.Contracts.Presentation;
using Harborline.Contracts.Rules;
using Harborline.Contracts.Serialization;
using Harborline.Contracts.Text;
using Harborline.Contracts.Workflow;
using Xunit;

namespace Harborline.Contracts.Tests;

public sealed class SharedContractOwnershipTests
{
    [Theory]
    [InlineData(typeof(RuleDefinition))]
    [InlineData(typeof(InternationalizedText))]
    [InlineData(typeof(PresentationHint))]
    public void Moved_properties_match_the_canonical_manifest(Type type)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        const string relativePath = "projections/typescript/contracts/hlp.contracts.forms/projection-manifest.json";
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, relativePath))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, relativePath)));
        var expected = manifest.RootElement.GetProperty("model").GetProperty("declarations").GetProperty(type.Name).GetProperty("properties");
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(expected.EnumerateObject().Count(), properties.Length);
        foreach (var property in properties)
        {
            var wireName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            Assert.NotNull(wireName);
            var shape = expected.GetProperty(wireName.Name);
            var optional = shape.GetProperty("optional").GetBoolean();
            Assert.Equal(!optional, property.IsDefined(typeof(JsonRequiredAttribute)));
            Assert.Equal(optional, property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Optional<>));
            if (optional)
                Assert.Equal(JsonIgnoreCondition.WhenWritingDefault, property.GetCustomAttribute<JsonIgnoreAttribute>()!.Condition);
            else
                Assert.Equal(NullabilityState.NotNull, new NullabilityInfoContext().Create(property).ReadState);
            var valueType = optional ? property.PropertyType.GetGenericArguments()[0] : property.PropertyType;
            var valueShape = shape.GetProperty("shape");
            switch (valueShape.GetProperty("kind").GetString())
            {
                case "string": Assert.Equal(typeof(string), valueType); break;
                case "record": Assert.Equal(typeof(IReadOnlyDictionary<string, string>), valueType); break;
                case "ref": Assert.Equal(valueShape.GetProperty("name").GetString(), valueType.Name); break;
                case "union":
                    var tokens = Enum.GetNames(valueType).Select(name => valueType.GetField(name)!.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!.Name);
                    Assert.Equal(valueShape.GetProperty("members").EnumerateArray().Select(item => item.GetProperty("value").GetString()), tokens);
                    break;
                default: throw new InvalidOperationException($"Unexpected canonical property shape for {type.Name}.{property.Name}.");
            }
        }
    }

    [Fact]
    public void Shared_declarations_and_serialization_helpers_have_one_owner()
    {
        (Type Type, string Owner)[] owners =
        [
            (typeof(RuleDefinition), "Rules"), (typeof(RuleTier), "Rules"),
            (typeof(RuleScope), "Rules"), (typeof(RuleActionKind), "Rules"),
            (typeof(InternationalizedText), "Text"),
            (typeof(PresentationHint), "Presentation"), (typeof(PresentationHintSeverityValue), "Presentation"),
            (typeof(Optional<>), "Serialization"), (typeof(OptionalJsonConverterFactory), "Serialization"),
            (typeof(OptionalJsonConverter<>), "Serialization"), (typeof(ClosedEnumJsonConverter<>), "Serialization"),
        ];
        var assembly = typeof(RuleDefinition).Assembly;
        foreach (var (type, owner) in owners)
        {
            Assert.Equal($"Harborline.Contracts.{owner}", type.Namespace);
            Assert.Null(assembly.GetType($"Harborline.Contracts.Forms.{type.Name}"));
        }

        var pending = new Stack<Type>(owners.Select(row => row.Type));
        var visited = new HashSet<Type>();
        while (pending.TryPop(out var type))
        {
            if (!visited.Add(type)) continue;
            Assert.False(type.Namespace?.StartsWith("Harborline.Contracts.Forms", StringComparison.Ordinal) == true,
                $"Shared contract closure contains {type.FullName}.");
            foreach (var argument in type.GetGenericArguments()) pending.Push(argument);
            if (type.Assembly != assembly) continue;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                pending.Push(property.PropertyType);
                var converter = property.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;
                if (converter is not null) pending.Push(converter);
            }
            var typeConverter = type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;
            if (typeConverter is not null) pending.Push(typeConverter);
        }
    }

    [Fact]
    public void Forms_and_workflow_borrow_the_shared_nominal_types()
    {
        Assert.Equal(typeof(IReadOnlyList<RuleDefinition>), typeof(HarborlineOverlay).GetProperty("Rules")!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<RuleDefinition>), typeof(WorkflowDefinition).GetProperty("Guards")!.PropertyType);
        Assert.Equal(typeof(InternationalizedText), typeof(WorkflowDefinition).GetProperty("Title")!.PropertyType);
        Assert.Equal(typeof(InternationalizedText), typeof(FormSection).GetProperty("Title")!.PropertyType);
        Assert.Equal(typeof(Optional<InternationalizedText>), typeof(RuleDefinition).GetProperty("ErrorMessage")!.PropertyType);
        Assert.Equal(typeof(Optional<PresentationHint>), typeof(RuleDefinition).GetProperty("Presentation")!.PropertyType);
        Assert.Equal(typeof(Optional<InternationalizedText>), typeof(PresentationHint).GetProperty("Badge")!.PropertyType);
        Assert.Equal(typeof(Optional<PresentationHintSeverityValue>), typeof(PresentationHint).GetProperty("Severity")!.PropertyType);
    }

    [Fact]
    public void Shared_rule_preserves_literal_wire_members_requiredness_and_closed_values()
    {
        const string json = """
            {"id":"total.required","tier":"JsonLogic","scope":"Field","scopeTarget":"total","expression":"true","action":"Presentation","errorMessage":{"defaultLocale":"en","values":{"en":"Required"}},"presentation":{"severity":"warn","badge":{"defaultLocale":"en","values":{"en":"Check"}},"styleToken":"attention"}}
            """;
        var rule = FormsJson.Deserialize<RuleDefinition>(json);
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(FormsJson.Serialize(rule));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(RuleTier.JsonLogic, rule.Tier);
        Assert.Equal(PresentationHintSeverityValue.Warn, rule.Presentation.Value!.Severity.Value);
        Assert.Equal("Check", rule.Presentation.Value.Badge.Value!.Values["en"]);

        Assert.Equal("missing-required-property", Assert.Throws<FormsWireException>(() =>
            FormsJson.Deserialize<RuleDefinition>("""{"id":"r","tier":"JsonLogic","scope":"Field","scopeTarget":"x","action":"Required"}""")).Code);
        Assert.Equal("invalid-string", Assert.Throws<FormsWireException>(() =>
            FormsJson.Deserialize<RuleDefinition>("""{"id":"r","tier":"JsonLogic","scope":"Field","scopeTarget":"x","expression":null,"action":"Required"}""")).Code);
        Assert.Throws<FormsWireException>(() => FormsJson.Deserialize<PresentationHint>("""{"severity":"fatal"}"""));
        Assert.Throws<FormsWireException>(() => FormsJson.Deserialize<PresentationHint>("""{"badge":null}"""));
    }

    [Theory]
    [InlineData(typeof(RuleTier), "\"JsonLogic\"", "\"FutureTier\"")]
    [InlineData(typeof(RuleScope), "\"Row\"", "\"FutureScope\"")]
    [InlineData(typeof(RuleActionKind), "\"Options\"", "\"FutureAction\"")]
    [InlineData(typeof(PresentationHintSeverityValue), "\"warn\"", "\"fatal\"")]
    public void Shared_closed_enum_converters_preserve_tokens_and_reject_unknown_values(Type type, string valid, string invalid)
    {
        var value = JsonSerializer.Deserialize(valid, type, FormsJson.Options);
        Assert.Equal(valid, JsonSerializer.Serialize(value, type, FormsJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(invalid, type, FormsJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("999", type, FormsJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(Enum.ToObject(type, 999), type, FormsJson.Options));
        Assert.Equal("Harborline.Contracts.Serialization", type.GetCustomAttribute<JsonConverterAttribute>()!.ConverterType!.Namespace);
    }

    [Fact]
    public void Shared_optional_converter_keeps_absence_null_and_value_distinct()
    {
        var absent = FormsJson.Deserialize<Cardinality>("""{"min":0}""");
        var explicitNull = FormsJson.Deserialize<Cardinality>("""{"min":0,"max":null}""");
        var bounded = FormsJson.Deserialize<Cardinality>("""{"min":0,"max":7}""");
        Assert.False(absent.Max.HasValue);
        Assert.Throws<InvalidOperationException>(() => absent.Max.Value);
        Assert.True(explicitNull.Max.HasValue);
        Assert.Null(explicitNull.Max.Value);
        Assert.Equal(7m, bounded.Max.Value);
        Assert.Equal("{\"min\":0}", FormsJson.Serialize(absent));
        Assert.Equal("{\"min\":0,\"max\":null}", FormsJson.Serialize(explicitNull));
        Assert.Equal("{\"min\":0,\"max\":7}", FormsJson.Serialize(bounded));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(Optional<string>), FormsJson.Options));
        Assert.Equal("null", JsonSerializer.Serialize(Optional<string>.Some(null), FormsJson.Options));
    }
}
