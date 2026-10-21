using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.RuleEngine.Environments;

namespace Harborline.Foundation.Forms.Engine.Authoring;

/// <summary>ADR 0099 decision 7 computation. This class owns no publication or surface writes.</summary>
public sealed class FormPublicationImpactAssessment
{
    /// <summary>Computes from actual typed revisions, registered schemas and a complete surface census.</summary>
    /// <remarks>
    /// Explicit first publication means "no predecessor: nothing to assess". It retains a Complete,
    /// ProposalEligible result with an empty impact set and no proposal; this is not an additional ADR row.
    /// A failed predecessor lookup is Unavailable, never this first-publication case.
    /// </remarks>
    /// <param name="candidate">The admitted Draft that will be published.</param>
    /// <param name="store">The existing Forms revision store.</param>
    /// <param name="context">Trusted explicit binding and historical evidence.</param>
    /// <param name="cancellationToken">Caller cancellation; unrelated cancellation is unavailable input.</param>
    public async ValueTask<FormPublicationAssessment> AssessAsync(FormDefinition candidate,
        IFormDefinitionStore store, FormPublicationAssessmentContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Snapshot caller-owned declaration collections before awaiting a provider.
            var basis = Snapshot(context.Basis);
            if (basis is null || context.Schemas is null || context.Surfaces is null
                || !Matches(basis.Candidate, candidate)
                || basis.NoPredecessor != (basis.Predecessor is null)
                || !Known(basis.CandidateEnvironment))
                return FormPublicationAssessment.Unavailable("publication_basis_unavailable");

            var current = await store.GetCurrentPublishedAsync(candidate.Tenant, candidate.Id, cancellationToken)
                .ConfigureAwait(false);
            FormDefinition? previous = null;
            if (basis.NoPredecessor)
            {
                if (current is not null) return FormPublicationAssessment.Unavailable("predecessor_mismatch");
            }
            else
            {
                var pin = basis.Predecessor!;
                if (pin.Tenant != candidate.Tenant || pin.DefinitionId != candidate.Id
                    || pin.Version == candidate.Version || pin.VersionId == basis.Candidate.VersionId
                    || !Known(basis.PredecessorEnvironment))
                    return FormPublicationAssessment.Unavailable("predecessor_basis_unavailable");
                previous = await store.GetAsync(pin.Tenant, pin.DefinitionId, pin.Version, cancellationToken)
                    .ConfigureAwait(false);
                if (!Matches(pin, previous) || previous.Status != FormDefinitionStatus.Published
                    || current is null || !Matches(pin, current))
                    return FormPublicationAssessment.Unavailable("predecessor_mismatch");
            }

            var nextSchema = await context.Schemas.GetAsync(new Harborline.Kernel.SchemaValidation.SchemaId(candidate.SchemaRef.Value), cancellationToken).ConfigureAwait(false);
            if (nextSchema is null || nextSchema.Id.Value != candidate.SchemaRef.Value
                || !TrySchema(nextSchema.JsonSchemaText, out var next))
                return FormPublicationAssessment.Unavailable("schema_unavailable");
            JsonObject? prior = null;
            if (previous is not null)
            {
                var priorSchema = await context.Schemas.GetAsync(new Harborline.Kernel.SchemaValidation.SchemaId(previous.SchemaRef.Value), cancellationToken).ConfigureAwait(false);
                if (priorSchema is null || priorSchema.Id.Value != previous.SchemaRef.Value
                    || !TrySchema(priorSchema.JsonSchemaText, out prior))
                    return FormPublicationAssessment.Unavailable("schema_unavailable");
            }

            var census = await context.Surfaces.ReadAsync(basis, cancellationToken).ConfigureAwait(false);
            if (census is not FormPublishedSurfaceRead.Complete complete
                || complete.Tenant != candidate.Tenant.Value || !ValidSurfaces(complete.Surfaces, complete.Tenant)
                || (basis.NoPredecessor && complete.Surfaces.Count != 0))
                return FormPublicationAssessment.Unavailable("surface_census_unavailable");
            var surfaces = Array.AsReadOnly(complete.Surfaces
                .OrderBy(row => row.DefinitionId, StringComparer.Ordinal)
                .ThenBy(row => row.VersionId, StringComparer.Ordinal)
                .Select(row => row with { BlockIds = Array.AsReadOnly(row.BlockIds.ToArray()) }).ToArray());
            var outcome = previous is null ? FormPublicationCompatibility.ProposalEligible
                : Compare(previous, candidate, prior!, next!, basis);
            var proposal = outcome == FormPublicationCompatibility.ProposalEligible && basis.Predecessor is not null
                ? new FormRepublishProposal(basis.Predecessor, basis.Candidate, surfaces) : null;
            cancellationToken.ThrowIfCancellationRequested();
            return new(basis, outcome, surfaces, proposal, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Provider failures and unsupported historical inputs carry no optimistic compatibility claim.
            // Do not expose exception messages, bodies or a new public refusal code.
            return FormPublicationAssessment.Unavailable("assessment_input_unavailable");
        }
    }

    private static bool Matches(FormPublicationBinding? binding, FormDefinition definition) =>
        binding is not null && !string.IsNullOrWhiteSpace(binding.Tenant.Value)
        && !binding.Tenant.IsSystemSentinel && !string.IsNullOrWhiteSpace(binding.DefinitionId.Value)
        && !string.IsNullOrWhiteSpace(binding.VersionId) && !string.IsNullOrWhiteSpace(binding.Provenance)
        && binding.Tenant == definition.Tenant && binding.DefinitionId == definition.Id
        && binding.Version == definition.Version;

    private static FormPublicationBasis? Snapshot(FormPublicationBasis? basis)
    {
        if (basis is null) return null;
        return basis with
        {
            CandidateEnvironment = Copy(basis.CandidateEnvironment),
            PredecessorEnvironment = basis.PredecessorEnvironment is null ? null : Copy(basis.PredecessorEnvironment),
        };
    }

    private static FormPublicationEnvironment Copy(FormPublicationEnvironment environment) => new(
        environment.Declaration is null ? null : JsonSerializer.Deserialize<BorrowerEnvironmentDeclaration>(
            JsonSerializer.Serialize(environment.Declaration)), environment.Provenance);

    private static bool Known(FormPublicationEnvironment? environment)
    {
        if (environment?.Declaration is null || string.IsNullOrWhiteSpace(environment.Provenance)) return false;
        _ = BorrowerEnvironmentAdmission.Admit(environment.Declaration);
        return true;
    }

    private static bool ValidSurfaces(IReadOnlyList<FormPublishedSurface>? rows, string tenant)
    {
        if (rows is null) return false;
        var seen = new HashSet<(string, string)>();
        foreach (var row in rows)
        {
            if (row is null || row.Tenant != tenant || string.IsNullOrWhiteSpace(row.DefinitionId)
                || string.IsNullOrWhiteSpace(row.VersionId) || string.IsNullOrWhiteSpace(row.Version)
                || string.IsNullOrWhiteSpace(row.Digest) || row.BlockIds is null || row.BlockIds.Count == 0
                || row.BlockIds.Any(string.IsNullOrWhiteSpace)
                || row.BlockIds.Distinct(StringComparer.Ordinal).Count() != row.BlockIds.Count
                || !seen.Add((row.DefinitionId, row.VersionId))) return false;
        }
        return true;
    }

    private static FormPublicationCompatibility Compare(FormDefinition old, FormDefinition next,
        JsonObject prior, JsonObject candidate, FormPublicationBasis basis)
    {
        var oldProperties = (JsonObject)prior["properties"]!;
        var newProperties = (JsonObject)candidate["properties"]!;
        var oldRequired = Required(prior);
        var newRequired = Required(candidate);
        ArgumentNullException.ThrowIfNull(old.Overlay.Fields);
        ArgumentNullException.ThrowIfNull(next.Overlay.Fields);
        var addedOptional = newProperties.Select(property => property.Key)
            .Where(key => !oldProperties.ContainsKey(key) && !newRequired.Contains(key))
            .ToHashSet(StringComparer.Ordinal);
        // Block takes precedence when several changes occur together. No migration decision is invented.
        if ((old.RecordTypeId is not null && old.RecordTypeId != next.RecordTypeId)
            || newRequired.Except(oldRequired, StringComparer.Ordinal)
            .Any(key => !((JsonObject)newProperties[key]!).ContainsKey("default")))
            return FormPublicationCompatibility.Block;

        if (old.RecordTypeId != next.RecordTypeId
            || !Equal(old.Overlay.Rules, next.Overlay.Rules)
            || !Equal(basis.PredecessorEnvironment!.Declaration, basis.CandidateEnvironment.Declaration)
            || oldRequired.Except(newRequired, StringComparer.Ordinal).Any()
            || !Equal(old.SubmitGate, next.SubmitGate) || !Equal(old.Lineage, next.Lineage)
            || !JsonNode.DeepEquals(Without((JsonObject)JsonSerializer.SerializeToNode(old.Overlay.Wizard ?? new WizardSettings())!,
                    "Review", "Confirmation", "ConfirmationMessage"),
                Without((JsonObject)JsonSerializer.SerializeToNode(next.Overlay.Wizard ?? new WizardSettings())!,
                    "Review", "Confirmation", "ConfirmationMessage"))
            || !Equal(old.Overlay.AsyncChecks, next.Overlay.AsyncChecks)
            || !Equal(old.Overlay.Aspects, next.Overlay.Aspects)
            || !JsonNode.DeepEquals(Without(prior, "properties", "required", "title", "description", "$id"),
                Without(candidate, "properties", "required", "title", "description", "$id")))
            return FormPublicationCompatibility.AuthorResolution;

        foreach (var property in oldProperties)
        {
            if (!newProperties.TryGetPropertyValue(property.Key, out var replacement)
                || !JsonNode.DeepEquals(Without((JsonObject)property.Value!, "title", "description"),
                    Without((JsonObject)replacement!, "title", "description")))
                return FormPublicationCompatibility.AuthorResolution;
        }
        if (!SameFieldMetadata(old.Overlay.Fields, next.Overlay.Fields, addedOptional,
            (left, right) => JsonNode.DeepEquals(Without((JsonObject)JsonSerializer.SerializeToNode(left)!,
                    "Label", "HelpText", "ControlHint"),
                Without((JsonObject)JsonSerializer.SerializeToNode(right)!, "Label", "HelpText", "ControlHint"))))
            return FormPublicationCompatibility.AuthorResolution;
        // Remove only explicit presentation members and memberships of genuinely new optional fields.
        // Page guards/checks, section access, old memberships, aspects and recursive semantics remain.
        if (!SameExceptPresentation(old.Overlay.Pages, next.Overlay.Pages, null, "Title")
            || !SameExceptPresentation(old.Overlay.Sections, next.Overlay.Sections, addedOptional, "Title", "Layout", "FieldPlacement"))
            return FormPublicationCompatibility.AuthorResolution;
        if (!oldRequired.SetEquals(newRequired)) return FormPublicationCompatibility.AuthorResolution;
        // Authoring's only root member is its field map. Null and an empty map carry no field facts;
        // introducing metadata on a pre-existing property is still an author-resolution change.
        if (!SameFieldMetadata(old.Authoring?.Fields, next.Authoring?.Fields, addedOptional,
            (left, right) => Equal(left, right))) return FormPublicationCompatibility.AuthorResolution;
        return FormPublicationCompatibility.ProposalEligible;
    }

    private static bool Equal<T>(T left, T right) => JsonNode.DeepEquals(
        JsonSerializer.SerializeToNode(left), JsonSerializer.SerializeToNode(right));

    private static bool SameFieldMetadata<T>(IReadOnlyDictionary<string, T>? left, IReadOnlyDictionary<string, T>? right,
        IReadOnlySet<string> addedOptional, Func<T, T, bool> same)
    {
        foreach (var key in (left?.Keys ?? Enumerable.Empty<string>()).Union(right?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal))
        {
            if (left is null || !left.TryGetValue(key, out var prior))
            {
                if (addedOptional.Contains(key)) continue;
                return false;
            }
            if (right is null || !right.TryGetValue(key, out var next) || !same(prior, next)) return false;
        }
        return true;
    }

    private static bool SameExceptPresentation<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right,
        IReadOnlySet<string>? addedOptional, params string[] members)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (JsonSerializer.SerializeToNode(left[index]) is not JsonObject old
                || JsonSerializer.SerializeToNode(right[index]) is not JsonObject next) return false;
            old = Without(old, members);
            next = Without(next, members);
            // New optional fields may join an existing section. Never erase an old membership or
            // reorder its surviving members, and never use this exception for pages or required fields.
            if (addedOptional is not null && next["Fields"] is JsonArray fields)
                next["Fields"] = new JsonArray(fields.Where(field => field is null || !addedOptional.Contains(field.GetValue<string>()))
                    .Select(field => field?.DeepClone()).ToArray());
            if (old["Items"] is JsonArray oldItems) old["Items"] = ItemSemantics(oldItems);
            if (next["Items"] is JsonArray nextItems) next["Items"] = ItemSemantics(nextItems);
            if (!JsonNode.DeepEquals(old, next)) return false;
        }
        return true;
    }

    private static JsonArray ItemSemantics(JsonArray items)
    {
        var result = new JsonArray();
        foreach (var item in items)
        {
            if (item is not JsonObject value) throw new InvalidOperationException("unsupported_form_item");
            var copy = Without(value, "Title", "Layout", "Placement", "Table");
            if (copy["Items"] is JsonArray children) copy["Items"] = ItemSemantics(children);
            result.Add(copy);
        }
        return result;
    }

    private static JsonObject Without(JsonObject value, params string[] keys)
    {
        var copy = (JsonObject)value.DeepClone();
        foreach (var key in keys) copy.Remove(key);
        return copy;
    }

    private static HashSet<string> Required(JsonObject schema) => schema["required"] is JsonArray required
        ? required.Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) : new(StringComparer.Ordinal);

    private static bool TrySchema(string text, out JsonObject? schema)
    {
        schema = JsonNode.Parse(text) as JsonObject;
        if (schema is null || schema["type"]?.GetValue<string>() != "object"
            || schema["properties"] is not JsonObject properties) return false;
        // Resolve neither references nor composition by guessing. The registry remains the schema authority;
        // this first compatibility interpreter deliberately returns unavailable for unsupported constructs.
        string[] roots = ["$schema", "$id", "type", "properties", "required", "additionalProperties", "title", "description"];
        if (schema.Any(item => !roots.Contains(item.Key, StringComparer.Ordinal))) return false;
        foreach (var name in new[] { "$schema", "$id", "title", "description" })
            if (schema.ContainsKey(name) && !IsString(schema[name])) return false;
        if (schema["$schema"] is { } dialect && dialect.GetValue<string>() != "https://json-schema.org/draft/2020-12/schema") return false;
        if (schema.TryGetPropertyValue("additionalProperties", out var additional)
            && (additional is not JsonValue additionalValue || !additionalValue.TryGetValue<bool>(out _))) return false;
        if (schema.TryGetPropertyValue("required", out var requiredValue) && requiredValue is not JsonArray) return false;
        var required = Required(schema);
        if (schema["required"] is JsonArray list && list.Count != required.Count) return false;
        if (required.Any(key => string.IsNullOrWhiteSpace(key) || !properties.ContainsKey(key))) return false;
        string[] keywords = ["type", "title", "description", "default", "enum", "const", "minimum", "maximum",
            "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength", "format"];
        foreach (var entry in properties)
        {
            if (entry.Value is not JsonObject property || property["type"] is not JsonValue type
                || !type.TryGetValue<string>(out var name)
                || name is not ("string" or "number" or "integer" or "boolean" or "null")
                || property.Any(item => !keywords.Contains(item.Key, StringComparer.Ordinal))) return false;
            foreach (var keyword in new[] { "title", "description", "format" })
                if (property.ContainsKey(keyword) && !IsString(property[keyword])) return false;
            foreach (var keyword in new[] { "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength" })
            {
                if (!property.TryGetPropertyValue(keyword, out var value)) continue;
                if (value is not JsonValue number || !number.TryGetValue<decimal>(out var amount)) return false;
                if (keyword == "multipleOf" && amount <= 0) return false;
                if ((keyword is "minLength" or "maxLength") && (amount < 0 || decimal.Truncate(amount) != amount)) return false;
            }
            if (property.TryGetPropertyValue("enum", out var choices))
            {
                if (choices is not JsonArray values || values.Count == 0) return false;
                for (var index = 0; index < values.Count; index++)
                    if (!Primitive(values[index]) || values.Take(index).Any(value => JsonNode.DeepEquals(value, values[index]))) return false;
            }
            if (property.TryGetPropertyValue("const", out var constant) && !Primitive(constant)) return false;
            // A schema default is an annotation. Do not count an unusable annotation as a usable field default.
            if (property.TryGetPropertyValue("default", out var initial))
            {
                if (!DefaultMatches(name, initial)) return false;
                // Establishing a usable constrained default belongs to the registry's full evaluator.
                // This bounded interpreter does not assert usability from type alone or recreate that evaluator.
                string[] unconstrained = ["type", "title", "description", "default"];
                if (property.Any(item => !unconstrained.Contains(item.Key, StringComparer.Ordinal))) return false;
            }
        }
        return true;
    }

    private static bool IsString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out _);

    private static bool Primitive(JsonNode? node) => node is null || node is JsonValue value
        && (value.TryGetValue<string>(out _) || value.TryGetValue<bool>(out _) || value.TryGetValue<decimal>(out _));

    private static bool DefaultMatches(string type, JsonNode? node)
    {
        if (node is null) return type == "null";
        if (node is not JsonValue value) return false;
        return type switch
        {
            "string" => value.TryGetValue<string>(out _),
            "boolean" => value.TryGetValue<bool>(out _),
            "number" => value.TryGetValue<decimal>(out _),
            "integer" => value.TryGetValue<decimal>(out var number) && decimal.Truncate(number) == number,
            _ => false,
        };
    }
}
