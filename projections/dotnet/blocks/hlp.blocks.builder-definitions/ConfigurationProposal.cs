using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Harborline.Foundation.RuleAuthoring;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>One edited definition inside a proposed change; the body is preserved verbatim.</summary>
/// <remarks>
/// <b>The producer states the content kind</b> (T-667). The edit carries the transport content kind its
/// definition is, so the mapping from an edited definition to a content kind is stated once, here, by
/// whoever authored the edit. A consumer converts it mechanically and never derives it from
/// <paramref name="DefinitionKey"/>: a host-side definition-key to content-kind table would be a second
/// place that has to know the set of kinds, and that set belongs to the transport, not here. This
/// producer therefore validates that a kind was stated and deliberately does not enumerate the kinds;
/// a name the transport does not define is that consumer's named refusal, not a value this block ranks.
/// </remarks>
/// <param name="DefinitionKey">The definition being edited, as it is named in the baseline closure.</param>
/// <param name="PackageKey">The package stated by the edit; it cannot override baseline ownership for release exposure.</param>
/// <param name="BodyJson">The provider-neutral definition source; never repaired by this producer.</param>
/// <param name="ContentKind">The transport content-kind name this definition is, stated by the producer.</param>
public sealed record ProposedDefinitionEdit(string DefinitionKey, string PackageKey, string BodyJson,
    string ContentKind);

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
    private const string EditsContract = "harborline.configuration-proposal-edits/v2";

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
        Required(edit.ContentKind, nameof(edit.ContentKind));
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
    /// <remarks>
    /// This overload binds no exposed definition to a published revision, so the released exposure names its keys and
    /// interface version only, exactly as before Goal 3 P1, and a consumer's pin to one of them finds no signed version
    /// or digest and refuses <c>records.reference.closure_missing</c> at install. Use the overload that takes a
    /// published-revision resolver to carry them.
    /// </remarks>
    public static ConfigurationReleaseResult Release(ProposedChangeState state, SavedVersion version,
        ProposedChangeCheck? check, ConfigurationGeneration effectiveNow, string packageKey, string revision)
        => ReleaseCore(state, version, check, effectiveNow, packageKey, revision, publishedRevision: null);

    /// <summary>
    /// Releases exactly as the overload without a resolver does, and binds every exposed definition to the exact
    /// published catalogue revision the host resolves for its key (Goal 3 P1, Option A; DES-0015 records-ck-40,
    /// records-ck-41). The bound revision must be Published in the proposal's tenant, of kind
    /// <see cref="DefinitionKind.Records"/>, hold the edit's body byte for byte (ordinal), be catalogued under the
    /// top-level <c>record_type_id</c> string that body names (ordinal; review #282 identity), and name
    /// <paramref name="packageKey"/> as its envelope <c>package_id</c>; otherwise the release refuses
    /// <c>configuration-release-exposure-unpublished</c> (also for a body naming no such id, or a revision catalogued
    /// under another id), <c>configuration-release-exposure-not-records</c>,
    /// <c>configuration-release-exposure-body-mismatch</c> or <c>configuration-release-exposure-package-mismatch</c>,
    /// naming the definition key. An exposed Class is not a Record Type, so under the Class hold it refuses
    /// <c>configuration-release-exposure-not-records</c> and a package exposing one cannot be released bound. The released exposure then
    /// carries, per key, that revision's catalogue version and <see cref="CatalogueFieldSource.DigestOf"/> digest, never
    /// the package revision. An edited definition that is not exposed is never resolved and may still be unpublished.
    /// The resolver is the host's read of its catalogue: the published revision an exposed definition key stands for, or
    /// <see langword="null"/> when there is none. It is trusted composition and is asked only for this package's own
    /// exposed definitions, in ordinal key order.
    /// The exposed key is the baseline-closure name, not a catalogue key, and this block derives no catalogue id from it:
    /// mapping the key to the definition it names stays the resolver's (host) duty. The identity check refuses a revision
    /// that is not the definition its own body names; it does not choose which definition a key names.
    /// </summary>
    public static ConfigurationReleaseResult Release(ProposedChangeState state, SavedVersion version,
        ProposedChangeCheck? check, ConfigurationGeneration effectiveNow, string packageKey, string revision,
        Func<string, DefinitionRevision?> publishedRevision)
    {
        ArgumentNullException.ThrowIfNull(publishedRevision);
        return ReleaseCore(state, version, check, effectiveNow, packageKey, revision, publishedRevision);
    }

    private static ConfigurationReleaseResult ReleaseCore(ProposedChangeState state, SavedVersion version,
        ProposedChangeCheck? check, ConfigurationGeneration effectiveNow, string packageKey, string revision,
        Func<string, DefinitionRevision?>? publishedRevision)
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
                contentKind = edit.ContentKind,
                body = JsonDocument.Parse(edit.BodyJson).RootElement,
            }))));
        // The closure is every package the edits reference, the baseline owner of each edited definition
        // and the package each edit names, pinned at the revision the baseline generation resolved it at.
        // A referenced package the baseline does not resolve has no pin, so it refuses rather than drops.
        var references = effectiveNow.References;
        var pinned = references.GetProperty("packages").EnumerateArray().Select(package => package.GetProperty("reference"))
            .ToDictionary(reference => reference.GetProperty("key").GetString()!, reference => reference.GetProperty("revision").GetString()!, StringComparer.Ordinal);
        var owners = references.GetProperty("ownership").EnumerateArray().ToDictionary(
            owner => owner.GetProperty("definitionKey").GetString()!, owner => owner.GetProperty("packageKey").GetString()!, StringComparer.Ordinal);
        // The producer contracts differ: Rules name packages with strings, Layout with bare capability
        // objects, and Records with pack-key@interfaceVersion objects (records-ck-41).
        var declarations = version.Edits.Select(edit => (edit, Declared: DeclaredPackages(edit.BodyJson, edit.ContentKind))).ToArray();
        var dependencies = new List<PlatformPackageDependency>();
        foreach (var referenced in declarations
            .SelectMany(declared => (owners.TryGetValue(declared.edit.DefinitionKey, out var owner) ? [owner, declared.edit.PackageKey] : new[] { declared.edit.PackageKey })
                .Concat(declared.Declared.Requires))
            .Where(key => key != packageKey).Distinct(StringComparer.Ordinal))
        {
            if (!pinned.TryGetValue(referenced, out var pin))
                return Refuse("configuration-release-dependency-unpinned", referenced,
                    $"The saved version references package {referenced}, which the baseline generation does not resolve, so it has no pinned version.");
            dependencies.Add(new(referenced, pin));
        }
        // A package exposes only its own definitions. The canonical baseline owner takes precedence
        // over the edit's claim; only a new key without a baseline selection uses the stated owner.
        // Foreign narrowing keeps its body and dependency pins, but does not set this package's interface.
        var exposed = declarations.Where(declared => declared.Declared.InterfaceVersion is not null
            && string.Equals(owners.TryGetValue(declared.edit.DefinitionKey, out var owner)
                ? owner : declared.edit.PackageKey, packageKey, StringComparison.Ordinal)).ToArray();
        var interfaces = exposed.Select(declared => declared.Declared.InterfaceVersion!.Value).Distinct().ToArray();
        if (interfaces.Length > 1)
            return Refuse("configuration-release-interface-ambiguous", "exposes",
                $"The saved version exposes definitions at interface versions {string.Join(", ", interfaces.Order())}; a package has one interface version.");
        // Option A: each exposed definition's version and digest come from the exact published revision its body is,
        // never from the package revision, a content id or the body. An edit is a working body, so it is bound only
        // when it is byte for byte the published body; otherwise the release refuses rather than exposing it unbound.
        var bound = new List<PlatformPackageExposedDefinition>();
        if (publishedRevision is not null)
            foreach (var (edit, declared) in exposed.OrderBy(pair => pair.edit.DefinitionKey, StringComparer.Ordinal))
            {
                var key = edit.DefinitionKey;
                var published = publishedRevision(key);
                // A revision in another tenant's catalogue is not this tenant's published revision (review B-2).
                if (published is null || published.Status != DefinitionStatus.Published
                    || !string.Equals(published.Document.Key.Tenant, state.TenantKey, StringComparison.Ordinal))
                    return Refuse("configuration-release-exposure-unpublished", key,
                        $"The saved version exposes '{key}', which has no published catalogue revision in tenant {state.TenantKey} to state its version and digest.");
                if (published.Document.Key.Kind != DefinitionKind.Records)
                    return Refuse("configuration-release-exposure-not-records", key,
                        $"The saved version exposes '{key}', whose published revision is a {published.Document.Key.Kind} definition, not a Record Type.");
                if (!string.Equals(edit.BodyJson, published.Document.BodyJson, StringComparison.Ordinal))
                    return Refuse("configuration-release-exposure-body-mismatch", key,
                        $"The saved version exposes '{key}', but its body is not byte for byte the published revision {published.Document.Version}.");
                // Records admission makes a Record Type's body id its catalogue key, so the revision must be filed under the
                // record_type_id its own body names. Another definition's revision is not this definition's published
                // revision, even with a byte-equal body (review #282 identity); naming the key's definition is the resolver's.
                var bodyId = RecordTypeId(published.Document.BodyJson);
                if (bodyId is null || !string.Equals(published.Document.Key.DefinitionId, bodyId, StringComparison.Ordinal))
                {
                    var named = bodyId is null ? "no record_type_id" : $"record_type_id '{bodyId}'";
                    return Refuse("configuration-release-exposure-unpublished", key,
                        $"The saved version exposes '{key}', but the resolved revision {published.Document.Version} is catalogued as '{published.Document.Key.DefinitionId}' while its body names {named}, so it is not this definition's published revision.");
                }
                // A consumer's pin names the producer body's envelope package_id, so a carrier released under another
                // package key could never match one (review B-1; lead decision 2026-10-12 within P10). The bodies are
                // equal, so the edit's declared package is the published revision's.
                if (!string.Equals(declared.PackageId, packageKey, StringComparison.Ordinal))
                {
                    var named = declared.PackageId is null ? "no package" : $"package '{declared.PackageId}'";
                    return Refuse("configuration-release-exposure-package-mismatch", key,
                        $"The saved version exposes '{key}', whose published body names {named}, not {packageKey}, so no consumer pin could match this release.");
                }
                bound.Add(new(key, published.Document.Version, CatalogueFieldSource.DigestOf(published)));
            }
        var exposure = interfaces.Length == 0 ? null
            : new PlatformPackageExposure(interfaces[0], exposed.Select(declared => declared.edit.DefinitionKey).ToArray())
            {
                ExposedDefinitions = bound,
            };
        var manifest = new PlatformPackageManifest(1, packageKey, revision, items.Prepend(record), dependencies, exposure);
        var validation = PlatformPackageReplayer.Validate(manifest);
        if (!validation.Succeeded)
            return Refuse(validation.RefusalCode!, validation.ItemId ?? "savedVersion",
                "The saved version does not export as a replayable package.");
        var document = PlatformPackageExporter.Export(manifest);
        return new(new ReleasedPackage(state.ProposalId, state.BaselineDigest, version.Digest, packageKey,
            revision, document, Convert.ToHexStringLower(SHA256.HashData(document))), null);
    }

    // The body's own top-level Records record_type_id; null when the body is not an object or the member is absent or
    // not a string.
    private static string? RecordTypeId(string bodyJson)
    {
        using var body = JsonDocument.Parse(bodyJson);
        return body.RootElement.ValueKind == JsonValueKind.Object
            && body.RootElement.TryGetProperty("record_type_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() : null;
    }

    // Reads the existing producer spellings without inventing interface versions for bare package IDs.
    // Layout's sealed platform capability is not a package dependency (layout-ck-42).
    private static (string[] Requires, int? InterfaceVersion, string? PackageId) DeclaredPackages(string bodyJson, string contentKind)
    {
        using var body = JsonDocument.Parse(bodyJson);
        if (body.RootElement.ValueKind != JsonValueKind.Object
            || !body.RootElement.TryGetProperty("envelope", out var envelope) || envelope.ValueKind != JsonValueKind.Object)
            return ([], null, null);
        // The Rule codec identifies its own source shape; no Rule transport kind is invented here.
        // Its Requires values are exact package IDs (RuleCrossPackageAuthoring), never @version syntax.
        var rule = RuleDefinitionCodec.Parse(bodyJson, RuleIntentPhase.Publish).Document
            // Stored Rule bodies omit shared identity metadata (SerializeBody/ParseBody). These
            // empty read-only header values supply no identity/version claim: only Requires is used.
            ?? RuleDefinitionCodec.ParseBody(bodyJson, "", "", "", RuleIntentPhase.Publish).Document;
        var requires = rule is not null ? rule.Envelope.Requires.ToArray()
            : envelope.TryGetProperty("requires", out var entries) && entries.ValueKind == JsonValueKind.Array
                ? entries.EnumerateArray().Select(entry => RequiredPackage(entry, contentKind)).OfType<string>().ToArray()
                : [];
        int? interfaceVersion = envelope.TryGetProperty("exposes", out var exposes) && exposes.ValueKind == JsonValueKind.Object
            && exposes.TryGetProperty("interface_version", out var declared) && declared.ValueKind == JsonValueKind.Number
            && declared.TryGetInt32(out var parsed) && parsed > 0 ? parsed : null;
        // The envelope's own package (records package_id); null when it is absent or not a string.
        var packageId = envelope.TryGetProperty("package_id", out var package) && package.ValueKind == JsonValueKind.String
            ? package.GetString() : null;
        return (requires, interfaceVersion, packageId);
    }

    private static string? RequiredPackage(JsonElement entry, string contentKind)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("capability", out var capability)
            || capability.ValueKind != JsonValueKind.String)
            return null;
        var value = capability.GetString();
        // LayoutCrossPackageAuthoring compares the bare capability to the exact target PackageId;
        // its minimum_platform_version belongs to host admission, never the package's revision pin.
        if (contentKind == "Layout")
            return string.IsNullOrWhiteSpace(value) || value == LayoutPackIdentity.Capability ? null : value;
        return RecordsRequirement.TryParse(value, out var package, out _) ? package : null;
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
                writer.WriteString("contentKind", edit.ContentKind);
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
