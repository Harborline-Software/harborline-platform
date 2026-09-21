using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The released domain-facing vocabulary and Form for the propose, save and release steps. Both
/// runtime lanes render this definition; neither authors a label of its own, which is what keeps
/// "Proposed change", "Saved version" and "Released package" in the surfaces rather than only in
/// code comments.
/// </summary>
public static class ConfigurationProposalDetail
{
    /// <summary>The three domain-facing steps, as stable codes with localized labels.</summary>
    public static JsonElement Statuses { get; } = JsonSerializer.SerializeToElement(new
    {
        proposed = Text("Proposed change"),
        saved = Text("Saved version"),
        released = Text("Released package"),
    });

    /// <summary>One read-only Form over the whole propose, save and release step.</summary>
    public static JsonElement Definition { get; } = JsonSerializer.SerializeToElement(new
    {
        formId = "platform.detail.configuration-proposal", version = "1.0.0",
        title = Text("Proposed change"),
        description = Text("A Proposed change is edited away from operational users, frozen as a Saved version, and released as a Released package."),
        sections = new[]
        {
            new
            {
                id = "proposal", title = Text("Proposed change"),
                fields = new[]
                {
                    Field("status", "Step"),
                    Field("tenantKey", "Tenant"),
                    Field("proposalId", "Proposed change"),
                    Field("baselineDigest", "Baseline generation"),
                    Field("effectiveDigest", "Effective generation"),
                    Field("editedDefinitions", "Edited definitions"),
                    Field("workingDigest", "Unsaved working state"),
                    Field("savedVersion", "Saved version"),
                    Field("savedBy", "Saved by"),
                    Field("rationale", "Rationale"),
                    Field("checkState", "Check"),
                    Field("releasedPackage", "Released package"),
                    Field("refusals", "Why the release was refused"),
                },
            },
        },
    });

    /// <summary>Binds a Proposed change that is still being edited. It is never effective.</summary>
    public static IReadOnlyDictionary<string, string> Proposed(ProposedChangeState state,
        ConfigurationGeneration effective, ProposedChangeCheck? check = null) =>
        Bind("proposed", state, effective, check, null, null, []);

    /// <summary>Binds a Saved version: the immutable checkpoint with its authorship and rationale.</summary>
    public static IReadOnlyDictionary<string, string> Saved(ProposedChangeState state,
        ConfigurationGeneration effective, SavedVersion version, ProposedChangeCheck? check = null) =>
        Bind("saved", state, effective, check, version, null, []);

    /// <summary>
    /// Binds a release outcome. The digest shown here is the digest of the exported artifact, so a
    /// surface cannot present a released package identity the bytes do not carry.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Bind(ProposedChangeState state,
        ConfigurationGeneration effective, SavedVersion version, ProposedChangeCheck? check,
        ConfigurationReleaseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Bind(result.Released is null ? "saved" : "released", state, effective, check, version,
            result.Released, result.Refusal is null ? [] : [result.Refusal]);
    }

    private static ReadOnlyDictionary<string, string> Bind(string status, ProposedChangeState state,
        ConfigurationGeneration effective, ProposedChangeCheck? check, SavedVersion? version,
        ReleasedPackage? released, IReadOnlyList<ConfigurationProposalRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(effective);
        var working = ConfigurationProposal.WorkingDigest(state);
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = Label(status),
            ["tenantKey"] = state.TenantKey,
            ["proposalId"] = state.ProposalId,
            ["baselineDigest"] = state.BaselineDigest,
            ["effectiveDigest"] = effective.Digest,
            ["editedDefinitions"] = string.Join("\n", (state.Edits ?? [])
                .Select(edit => $"{edit.DefinitionKey} ({edit.PackageKey})")),
            ["workingDigest"] = working,
            ["savedVersion"] = version is null ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $"{version.Ordinal}: {version.Digest}"),
            ["savedBy"] = version?.Author ?? string.Empty,
            ["rationale"] = version?.Rationale ?? string.Empty,
            ["checkState"] = check is null ? "No check recorded."
                : ConfigurationProposal.IsCurrent(check, state)
                    ? $"Current ({check.ReceiptId})"
                    : $"Invalidated by a later edit ({check.ReceiptId})",
            ["releasedPackage"] = released is null ? string.Empty
                : $"{released.PackageKey} {released.Revision}: {released.Digest}",
            ["refusals"] = string.Join("\n", refusals.Select(refusal => $"{refusal.Code} ({refusal.Target}): {refusal.Message}")),
        });
    }

    /// <summary>The released English label for one step code; surfaces never author their own.</summary>
    public static string Label(string status) =>
        Statuses.GetProperty(status).GetProperty("values").GetProperty("en").GetString()!;

    private static object Text(string text) => new { defaultLocale = "en", values = new { en = text } };
    private static object Field(string name, string label) => new
    {
        name, label = Text(label), controlHint = "readonly", isSensitive = false, isReadable = true, readOnly = true,
    };
}
