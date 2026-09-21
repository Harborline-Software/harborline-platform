using System.Collections.ObjectModel;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Released status vocabulary, Form definition and safe bindings consumed by both runtime lanes.</summary>
public static class ConfigurationActivationDetail
{
    /// <summary>Stable codes and localized labels for the four distinct outcomes.</summary>
    public static JsonElement Statuses { get; } = JsonSerializer.SerializeToElement(new
    {
        released = Text("Released"), preparing = Text("Preparing"), refused = Text("Refused"), effective = Text("Effective"),
    });

    /// <summary>One read-only status Form; no app-local status presentation is required.</summary>
    public static JsonElement Definition { get; } = JsonSerializer.SerializeToElement(new
    {
        formId = "platform.detail.configuration-activation", version = "1.0.0",
        title = Text("Configuration activation"),
        description = Text("Released and preparing configurations become effective only after activation commits."),
        sections = new[]
        {
            new
            {
                id = "activation", title = Text("Activation status"),
                fields = new[]
                {
                    Field("status", "Status"), Field("tenantKey", "Tenant"),
                    Field("candidateDigest", "Candidate generation"), Field("expectedBaselineDigest", "Expected baseline"),
                    Field("effectiveDigest", "Effective generation"), Field("refusals", "Why activation was refused"),
                },
            },
        },
    });

    /// <summary>Reports a host-verified release while identifying the current effective baseline.</summary>
    public static IReadOnlyDictionary<string, string> Released(ConfigurationGeneration baseline, ConfigurationGeneration candidate) =>
        Bind("released", baseline, candidate, baseline.Digest, []);

    /// <summary>Reports preparation in progress, never effective.</summary>
    public static IReadOnlyDictionary<string, string> Preparing(ConfigurationGeneration baseline, ConfigurationGeneration candidate) =>
        Bind("preparing", baseline, candidate, baseline.Digest, []);

    /// <summary>Even a successfully prepared candidate remains preparing until a host confirms commit.</summary>
    public static IReadOnlyDictionary<string, string> Bind(ConfigurationPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        return Bind(preparation.Prepared is null ? "refused" : "preparing", preparation.Baseline,
            preparation.Candidate, preparation.ExpectedBaselineDigest, preparation.Refusals);
    }

    /// <summary>Derives status from an outcome; callers cannot label a refusal effective.</summary>
    public static IReadOnlyDictionary<string, string> Bind(ConfigurationActivationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var decision = outcome.Decision;
        return Bind(decision.Refusal is null ? "effective" : "refused", outcome.EffectiveGeneration,
            decision.Request.Prepared.Candidate, decision.Request.Prepared.Baseline.Digest,
            decision.Refusal is null ? [] : [decision.Refusal]);
    }

    private static ReadOnlyDictionary<string, string> Bind(string status, ConfigurationGeneration effective,
        ConfigurationGeneration candidate, string expectedBaselineDigest, IReadOnlyList<ConfigurationActivationRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(effective);
        ArgumentNullException.ThrowIfNull(candidate);
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = Statuses.GetProperty(status).GetProperty("values").GetProperty("en").GetString()!,
            ["tenantKey"] = ConfigurationPreparation.Tenant(effective),
            ["candidateDigest"] = candidate.Digest,
            ["expectedBaselineDigest"] = expectedBaselineDigest,
            ["effectiveDigest"] = effective.Digest,
            ["refusals"] = string.Join("\n", refusals.Select(refusal => $"{refusal.Code} ({refusal.Target}): {refusal.Message}")),
        });
    }

    private static object Text(string text) => new { defaultLocale = "en", values = new { en = text } };
    private static object Field(string name, string label) => new
    {
        name, label = Text(label), controlHint = "readonly", isSensitive = false, isReadable = true, readOnly = true,
    };
}
