using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Blocks.EntityViews;

/// <summary>
/// The one JSON wire contract for <see cref="ViewDefinitionDraft"/>: the editor emits it and the api accepts it, so neither
/// invents its own (T-486/engine-gaps). Member names are camelCase, so the RFC 6901 pointers admission reports, such as
/// <c>/envelope/contract</c> and <c>/aggregationExpression</c>, address this document. Enums are camelCase strings, the
/// filter tree keeps its <c>kind</c> discriminator, and null members are omitted. Reading is strict: an unknown member, a
/// repeated member, a missing required member, a null list entry or map value, or a trailing value refuses with
/// <see cref="JsonException"/>. The runtime
/// Access predicate is not part of the grammar and cannot be written. Schema: <c>viewDefinitionDraftWire</c> in this
/// module's <c>interface.yaml</c>.
/// </summary>
public static class ViewDefinitionDraftJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Writes the draft as compact UTF-8 JSON in declaration order.</summary>
    public static string Serialize(ViewDefinitionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return JsonSerializer.Serialize(draft, Options);
    }

    /// <summary>Reads a draft, refusing anything outside the contract with <see cref="JsonException"/>.</summary>
    public static ViewDefinitionDraft Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<ViewDefinitionDraft>(json, Options)
                ?? throw new JsonException("The view-definition draft is null.");
        }
        catch (NotSupportedException exception)
        {
            // A filter or operand object without its `kind` discriminator makes System.Text.Json try to construct the
            // abstract base, which it reports as NotSupportedException. On this fixed grammar that is malformed input.
            throw new JsonException("A filter or operand in the view-definition draft has no recognised kind.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            // A nullable constructor parameter is optional on the wire, as writing omits a null member; every other one stays required.
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers = { NullableParametersAreOptional, CollectionEntriesAreNotNull },
            },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static void NullableParametersAreOptional(System.Text.Json.Serialization.Metadata.JsonTypeInfo type)
    {
        foreach (var property in type.Properties)
        {
            if (property.AssociatedParameter is { IsNullable: true }) property.IsRequired = false;
        }
    }

    // RespectNullableAnnotations does not reach collection elements, and no list or map in the draft grammar admits a null entry.
    private static void CollectionEntriesAreNotNull(System.Text.Json.Serialization.Metadata.JsonTypeInfo type)
    {
        if (type.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object) return;
        var properties = type.Properties.Where(property => property.Get is not null).ToArray();
        type.OnDeserialized = value =>
        {
            foreach (var property in properties)
            {
                var entries = property.Get!(value) switch
                {
                    System.Collections.IDictionary map => map.Values,
                    string => null,
                    System.Collections.IEnumerable list => list,
                    _ => null,
                };
                if (entries is not null && entries.Cast<object?>().Any(entry => entry is null))
                    throw new JsonException($"The '{property.Name}' member holds a null entry.");
            }
        };
    }
}
