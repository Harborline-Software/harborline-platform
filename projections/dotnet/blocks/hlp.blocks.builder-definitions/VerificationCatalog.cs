using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The declared shape of one observation. A case compares typed values, never prose.</summary>
public enum VerificationValueKind
{
    /// <summary>JSON true or false.</summary>
    Boolean,

    /// <summary>A JSON string.</summary>
    Text,

    /// <summary>A JSON number, compared as a canonical decimal.</summary>
    Number,

    /// <summary>A JSON array of strings, compared in the fixture's declared ordering.</summary>
    TextList,
}

/// <summary>
/// One registered action a case may name. An action declares the observation channels executing it
/// produces and the parameters it requires; a case that leaves a parameter unbound is refused rather
/// than defaulted, because an unbound input has no determinate result to assert.
/// </summary>
/// <param name="ActionId">Stable identity, referenced by a case.</param>
/// <param name="Version">The action contract version recorded in the receipt.</param>
/// <param name="Parameters">Every input the action requires, ordinal by name.</param>
/// <param name="Channels">The observation channels executing it produces.</param>
public sealed record VerificationAction(string ActionId, string Version,
    IReadOnlyList<string> Parameters, IReadOnlyList<string> Channels);

/// <summary>
/// One registered predicate. A predicate reads exactly one observation channel and compares it with
/// an expected value of one declared kind, so a case can address public behaviour without an
/// expression language, a script or a probabilistic judge.
/// </summary>
/// <param name="PredicateId">Stable identity, referenced by an assertion.</param>
/// <param name="Version">The predicate contract version recorded in the receipt.</param>
/// <param name="Channel">The observation channel it reads.</param>
/// <param name="Expects">The kind of value it compares.</param>
/// <param name="RequiresTarget">Whether the assertion must name a target inside the channel.</param>
public sealed record VerificationPredicate(string PredicateId, string Version, string Channel,
    VerificationValueKind Expects, bool RequiresTarget);

/// <summary>
/// The closed action and predicate catalogue for the Records-and-Rules verification slice. It is
/// closed on purpose: a case can only name what is here, so there is no path by which an author
/// introduces executable content, and every assertion resolves to a versioned typed comparison.
/// Later phases extend this catalogue; they do not add an alternate execution path.
/// </summary>
public static class VerificationCatalog
{
    private const string Contract = "harborline.verification-catalogue/v1";

    /// <summary>Every action a case may name.</summary>
    public static IReadOnlyList<VerificationAction> Actions { get; } = Array.AsReadOnly(new VerificationAction[]
    {
        new("records.create", "1.0.0", ["recordType", "values"],
            ["outcome", "record", "authorization", "evidence"]),
    });

    /// <summary>Every predicate an assertion may name.</summary>
    public static IReadOnlyList<VerificationPredicate> Predicates { get; } = Array.AsReadOnly(new VerificationPredicate[]
    {
        new("outcome.accepted", "1.0.0", "outcome", VerificationValueKind.Boolean, false),
        new("outcome.refusalCode", "1.0.0", "outcome", VerificationValueKind.Text, false),
        new("outcome.refusalPointer", "1.0.0", "outcome", VerificationValueKind.Text, false),
        new("record.field", "1.0.0", "record", VerificationValueKind.Text, true),
        new("record.number", "1.0.0", "record", VerificationValueKind.Number, true),
        new("authorization.decision", "1.0.0", "authorization", VerificationValueKind.Text, false),
        new("evidence.emitted", "1.0.0", "evidence", VerificationValueKind.TextList, false),
    });

    /// <summary>
    /// The catalogue's own versioned reference. A receipt must carry it, so a receipt states which
    /// action and predicate vocabulary produced its observations rather than leaving it to be guessed.
    /// </summary>
    public static ConfigurationReference Reference { get; } = Derive();

    /// <summary>The action with this identity, or null when it is not registered.</summary>
    public static VerificationAction? Action(string actionId) =>
        Actions.FirstOrDefault(action => action.ActionId == actionId);

    /// <summary>The predicate with this identity, or null when it is not registered.</summary>
    public static VerificationPredicate? Predicate(string predicateId) =>
        Predicates.FirstOrDefault(predicate => predicate.PredicateId == predicateId);

    /// <summary>
    /// Whether the JSON text is a single well-formed value of the declared kind. An absent, blank or
    /// null expected value is never well typed: "no expectation" is what an empty check looks like.
    /// </summary>
    public static bool IsWellTyped(VerificationValueKind kind, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        JsonElement value;
        try
        {
            using var document = JsonDocument.Parse(json);
            value = document.RootElement.Clone();
        }
        catch (JsonException) { return false; }
        return kind switch
        {
            VerificationValueKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            VerificationValueKind.Text => value.ValueKind is JsonValueKind.String,
            VerificationValueKind.Number => value.ValueKind is JsonValueKind.Number,
            VerificationValueKind.TextList => value.ValueKind is JsonValueKind.Array
                && value.EnumerateArray().All(item => item.ValueKind is JsonValueKind.String),
            _ => false,
        };
    }

    private static ConfigurationReference Derive()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", Contract);
            writer.WriteStartArray("actions");
            foreach (var action in Actions.OrderBy(action => action.ActionId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("actionId", action.ActionId);
                writer.WriteString("version", action.Version);
                writer.WriteStartArray("parameters");
                foreach (var parameter in action.Parameters.OrderBy(name => name, StringComparer.Ordinal))
                    writer.WriteStringValue(parameter);
                writer.WriteEndArray();
                writer.WriteStartArray("channels");
                foreach (var channel in action.Channels.OrderBy(name => name, StringComparer.Ordinal))
                    writer.WriteStringValue(channel);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("predicates");
            foreach (var predicate in Predicates.OrderBy(predicate => predicate.PredicateId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("predicateId", predicate.PredicateId);
                writer.WriteString("version", predicate.Version);
                writer.WriteString("channel", predicate.Channel);
                writer.WriteString("expects", predicate.Expects.ToString());
                writer.WriteBoolean("requiresTarget", predicate.RequiresTarget);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new(Contract, "1.0.0", Convert.ToHexStringLower(SHA256.HashData(stream.ToArray())));
    }
}
