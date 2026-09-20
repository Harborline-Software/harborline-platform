using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>One edited definition inside a proposed change; the body is preserved verbatim.</summary>
/// <param name="DefinitionKey">The definition being edited, as it is named in the baseline closure.</param>
/// <param name="PackageKey">The package that will own the edited definition.</param>
/// <param name="BodyJson">The provider-neutral definition source; never repaired by this producer.</param>
public sealed record ProposedDefinitionEdit(string DefinitionKey, string PackageKey, string BodyJson);

/// <summary>
/// The working state of one proposed change: the exact baseline generation it started from and the
/// autosaved edits made since. A proposed change carries no effective pointer and no projection, so
/// nothing in this contract can make an edit effective while it is being edited.
/// </summary>
/// <param name="ProposalId">Stable host identity for the proposed change.</param>
/// <param name="TenantKey">The tenant the baseline generation belongs to.</param>
/// <param name="BaselineDigest">The complete baseline generation digest, recorded at Start.</param>
/// <param name="Edits">The autosaved working edits, ordinal by definition key.</param>
public sealed record ProposedChangeState(string ProposalId, string TenantKey, string BaselineDigest,
    IReadOnlyList<ProposedDefinitionEdit> Edits);

/// <summary>
/// An immutable checkpoint of a proposed change, with its authorship and rationale. A saved version
/// is identified by a digest over the exact edits it froze; a later edit produces a different
/// working digest and therefore cannot be mistaken for this checkpoint.
/// </summary>
/// <param name="ProposalId">The proposed change this checkpoint belongs to.</param>
/// <param name="BaselineDigest">The baseline the proposed change recorded when it started.</param>
/// <param name="Ordinal">The host-assigned checkpoint number, starting at one.</param>
/// <param name="Author">The authenticated author the host resolved at the moment of saving.</param>
/// <param name="Rationale">Why this version was saved; required, never defaulted.</param>
/// <param name="SavedAt">The admitted instant of the save.</param>
/// <param name="Edits">The frozen edit set.</param>
/// <param name="Digest">Lowercase SHA-256 over the canonical frozen edit set.</param>
public sealed record SavedVersion(string ProposalId, string BaselineDigest, int Ordinal, string Author,
    string Rationale, DateTimeOffset SavedAt, IReadOnlyList<ProposedDefinitionEdit> Edits, string Digest);

/// <summary>
/// A check recorded against one exact working state. This producer owns only the binding and its
/// invalidation; the verification engine and its receipt content belong to the verification slice.
/// </summary>
/// <param name="ProposalId">The proposed change the check ran against.</param>
/// <param name="CheckedDigest">The working digest the check observed.</param>
/// <param name="ReceiptId">The verification receipt identity the host retained.</param>
public sealed record ProposedChangeCheck(string ProposalId, string CheckedDigest, string ReceiptId);

/// <summary>A stable refusal code and the public field that caused it.</summary>
/// <param name="Code">Stable, localizable refusal code.</param>
/// <param name="Target">The public input the refusal is about.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record ConfigurationProposalRefusal(string Code, string Target, string Message);

/// <summary>
/// The provider-neutral released package: closure, manifest and digest as one exported document.
/// Transport, signing and installation are the api's, per ADR 0097 decision 6; nothing here signs.
/// </summary>
/// <param name="ProposalId">The proposed change that was released.</param>
/// <param name="BaselineDigest">The baseline the released saved version was proposed against.</param>
/// <param name="SavedVersionDigest">The exact saved version this package carries.</param>
/// <param name="PackageKey">The released package key.</param>
/// <param name="Revision">The released package revision.</param>
/// <param name="Document">The canonical exported bytes; the digest below is taken over them.</param>
/// <param name="Digest">Lowercase SHA-256 of <paramref name="Document"/>, shown to the author.</param>
public sealed record ReleasedPackage(string ProposalId, string BaselineDigest, string SavedVersionDigest,
    string PackageKey, string Revision, ReadOnlyMemory<byte> Document, string Digest);

/// <summary>A release outcome. A refusal never carries a package.</summary>
public sealed class ConfigurationReleaseResult
{
    internal ConfigurationReleaseResult(ReleasedPackage? released, ConfigurationProposalRefusal? refusal)
    {
        Released = released;
        Refusal = refusal;
    }

    /// <summary>The released package, available only when every release check passed.</summary>
    public ReleasedPackage? Released { get; }

    /// <summary>Why no package was released.</summary>
    public ConfigurationProposalRefusal? Refusal { get; }
}

/// <summary>
/// The pure producer for the propose, save and release half of the governed configuration loop.
/// It holds no state and performs no persistence: the host stores the working state, the saved
/// versions and the released documents, and authenticates the author it passes in.
/// </summary>
public static class ConfigurationProposal
{
    private const string EditsContract = "harborline.configuration-proposal-edits/v1";

    /// <summary>Starts a proposed change from an exact effective generation, recording it as the baseline.</summary>
    public static ProposedChangeState Start(string proposalId, ConfigurationGeneration baseline)
    {
        Required(proposalId, nameof(proposalId));
        ArgumentNullException.ThrowIfNull(baseline);
        return new(proposalId, ConfigurationPreparation.Tenant(baseline), baseline.Digest, []);
    }

    /// <summary>
    /// Records one autosaved edit, replacing any earlier edit of the same definition. Autosave
    /// preserves work without creating a checkpoint, and cannot change the recorded baseline.
    /// </summary>
    public static ProposedChangeState Autosave(ProposedChangeState state, ProposedDefinitionEdit edit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(edit);
        Required(edit.DefinitionKey, nameof(edit.DefinitionKey));
        Required(edit.PackageKey, nameof(edit.PackageKey));
        Required(edit.BodyJson, nameof(edit.BodyJson));
        try { using var _ = JsonDocument.Parse(edit.BodyJson); }
        catch (JsonException) { throw new ArgumentException("configuration-proposal-body-invalid"); }
        var edits = (state.Edits ?? []).Where(existing => existing.DefinitionKey != edit.DefinitionKey)
            .Append(edit).OrderBy(item => item.DefinitionKey, StringComparer.Ordinal).ToArray();
        return state with { Edits = Array.AsReadOnly(edits) };
    }

    /// <summary>
    /// The digest of the exact current working edits. It changes on every edit, which is what makes
    /// an earlier check or saved version distinguishable from the state now being edited.
    /// </summary>
    public static string WorkingDigest(ProposedChangeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Digest(state.ProposalId, state.TenantKey, state.BaselineDigest, state.Edits ?? []);
    }

    /// <summary>
    /// Freezes the current working edits into an immutable checkpoint with authorship and rationale.
    /// An empty edit set, a missing author or a missing rationale refuses rather than being defaulted.
    /// </summary>
    public static SavedVersion Save(ProposedChangeState state, int ordinal, string author, string rationale,
        DateTimeOffset savedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        Required(author, nameof(author));
        Required(rationale, nameof(rationale));
        var edits = (state.Edits ?? []).ToArray();
        if (edits.Length == 0) throw new ArgumentException("configuration-proposal-empty");
        return new(state.ProposalId, state.BaselineDigest, ordinal, author, rationale, savedAt,
            Array.AsReadOnly(edits), WorkingDigest(state));
    }

    /// <summary>Whether a recorded check still describes the state now being edited.</summary>
    public static bool IsCurrent(ProposedChangeCheck check, ProposedChangeState state)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.ProposalId == state.ProposalId && check.CheckedDigest == WorkingDigest(state);
    }

    /// <summary>
    /// Exports the exact saved version as one provider-neutral released package, but only when that
    /// saved version is still the state that was checked. Any edit after the check moves the working
    /// digest, so the check no longer describes the candidate and the release refuses by name. The
    /// baseline is compared against the generation effective right now, so a baseline that moved
    /// underneath the author refuses rather than releasing against a generation nobody reviewed.
    /// </summary>
    public static ConfigurationReleaseResult Release(ProposedChangeState state, SavedVersion version,
        ProposedChangeCheck? check, ConfigurationGeneration effectiveNow, string packageKey, string revision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(effectiveNow);
        Required(packageKey, nameof(packageKey));
        Required(revision, nameof(revision));
        static ConfigurationReleaseResult Refuse(string code, string target, string message) =>
            new(null, new(code, target, message));

        if (check is null)
            return Refuse("configuration-check-required", "check",
                "No check is recorded for this proposed change.");
        if (version.ProposalId != state.ProposalId)
            return Refuse("configuration-release-proposal-mismatch", "savedVersion",
                "The saved version belongs to another proposed change.");
        if (ConfigurationPreparation.Tenant(effectiveNow) != state.TenantKey)
            return Refuse("configuration-tenant-mismatch", "proposedChange",
                "The proposed change belongs to another tenant.");
        if (state.BaselineDigest != effectiveNow.Digest)
            return Refuse("configuration-baseline-stale", "baselineDigest",
                $"The proposed change was started from {state.BaselineDigest}, which is no longer the effective generation {effectiveNow.Digest}.");
        if (!IsCurrent(check, state))
            return Refuse("configuration-check-invalidated", "check",
                "The proposed change has been edited since it was checked, so that check no longer applies.");
        // The check is current, so it describes the working state. A saved version that does not
        // match that state is therefore a version the current check never covered.
        if (check.CheckedDigest != version.Digest)
            return Refuse("configuration-check-invalidated", "check",
                "The recorded check does not describe this saved version.");

        // The manifest's own package record carries the closure this package is bound into: the exact
        // baseline generation and the exact saved version, with its authorship and rationale. A reader
        // of the exported bytes alone can therefore tell which generation the package was proposed
        // against, without consulting any host record.
        var record = new PlatformPackageItem($"{packageKey}.proposed-change", PlatformSeedStage.PackageRecord, [],
            PlatformPackageContent.PresentJson(JsonSerializer.SerializeToUtf8Bytes(new
            {
                proposalId = state.ProposalId,
                tenantKey = state.TenantKey,
                baselineDigest = state.BaselineDigest,
                savedVersionDigest = version.Digest,
                savedVersionOrdinal = version.Ordinal,
                author = version.Author,
                rationale = version.Rationale,
                savedAt = version.SavedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                checkReceiptId = check.ReceiptId,
            })));
        var items = version.Edits.Select((edit, index) => new PlatformPackageItem(
            $"{packageKey}.definition-{index + 1}", PlatformSeedStage.SealedDefinitions,
            [index == 0 ? record.Id : $"{packageKey}.definition-{index}"],
            PlatformPackageContent.PresentJson(JsonSerializer.SerializeToUtf8Bytes(new
            {
                definitionKey = edit.DefinitionKey,
                packageKey = edit.PackageKey,
                body = JsonDocument.Parse(edit.BodyJson).RootElement,
            }))));
        var manifest = new PlatformPackageManifest(1, packageKey, revision, items.Prepend(record));
        var validation = PlatformPackageReplayer.Validate(manifest);
        if (!validation.Succeeded)
            return Refuse(validation.RefusalCode!, validation.ItemId ?? "savedVersion",
                "The saved version does not export as a replayable package.");
        var document = PlatformPackageExporter.Export(manifest);
        return new(new ReleasedPackage(state.ProposalId, state.BaselineDigest, version.Digest, packageKey,
            revision, document, Convert.ToHexStringLower(SHA256.HashData(document))), null);
    }

    private static string Digest(string proposalId, string tenantKey, string baselineDigest,
        IReadOnlyList<ProposedDefinitionEdit> edits)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", EditsContract);
            writer.WriteString("proposalId", proposalId);
            writer.WriteString("tenantKey", tenantKey);
            writer.WriteString("baselineDigest", baselineDigest);
            writer.WriteStartArray("edits");
            foreach (var edit in edits.OrderBy(edit => edit.DefinitionKey, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("definitionKey", edit.DefinitionKey);
                writer.WriteString("packageKey", edit.PackageKey);
                writer.WriteString("body", edit.BodyJson);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"configuration-proposal-{name}-required");
    }
}
