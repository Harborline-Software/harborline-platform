using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Security;

public sealed record FormTenantProtectionKey(string KeyVersion, ReadOnlyMemory<byte> KeyMaterial);

public interface IFormTenantProtectionKeyProvider
{
    ValueTask<FormTenantProtectionKey?> GetCurrentAsync(
        TenantId tenant,
        string keyDomain,
        CancellationToken cancellationToken = default);

    ValueTask<FormTenantProtectionKey?> GetAsync(
        TenantId tenant,
        string keyDomain,
        string keyVersion,
        CancellationToken cancellationToken = default);
}

public sealed record FormDecryptCapability(
    string CapabilityId,
    TenantId Tenant,
    string Purpose,
    DateTimeOffset ExpiresAt);

public interface IFormDecryptCapabilityProvider
{
    ValueTask<FormDecryptCapability?> AcquireAsync(
        TenantId tenant,
        string purpose,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}

public sealed record FormFieldGovernancePolicy(
    bool IsResolved,
    bool ProtectAtRest,
    bool RedactOnRead,
    bool AuditOnRead,
    bool RequiresSubjectProtection = false,
    IReadOnlySet<string>? AllowedJurisdictions = null,
    IReadOnlySet<string>? ProhibitedJurisdictions = null,
    FormGovernanceRefusal? Refusal = null);

public sealed class FormFieldSecurityOptions
{
    public string? HostJurisdiction { get; init; }
}

public interface IFormFieldGovernanceResolver
{
    FormFieldGovernancePolicy Resolve(FormDefinition definition, string fieldName);
}

public sealed class DefaultFormFieldGovernanceResolver : IFormFieldGovernanceResolver
{
    public const string DataClassificationSystem = "harborline/data-classification";

    /// <summary>
    /// The pre-rename spelling of <see cref="DataClassificationSystem"/> (ticket 288 slice 2). The
    /// taxonomy is unchanged -- the same pii / identifier / phi / pci / cui codes under the Harborline
    /// identity -- and this resolver fail-closes on any system it does not recognise, so an overlay
    /// authored before the rename is accepted on read. New overlays are authored with
    /// <see cref="DataClassificationSystem"/>.
    /// </summary>
    public const string LegacyDataClassificationSystem = "shipyard/data-classification";

    private static bool IsDataClassification(Tag tag) =>
        string.Equals(tag.System, DataClassificationSystem, StringComparison.Ordinal)
        || string.Equals(tag.System, LegacyDataClassificationSystem, StringComparison.Ordinal);

    public FormFieldGovernancePolicy Resolve(FormDefinition definition, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (!definition.Overlay.Fields.TryGetValue(fieldName, out var field))
            return new(true, true, false, false);

        var aspects = EffectiveAspects(definition.Overlay, fieldName).ToArray();
        var allTags = aspects.SelectMany(aspect => aspect.Classification?.Tags ?? []).ToArray();
        var tags = allTags
            .Where(IsDataClassification)
            .Select(tag => tag.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (allTags.Any(tag =>
                !IsDataClassification(tag) ||
                tag.Code is not ("pii" or "identifier" or "phi" or "pci" or "cui")))
            return new(false, true, true, true, Refusal: FormGovernanceRefusal.PolicyUnresolved);

        var residency = ResolveResidency(aspects);
        if (!residency.IsResolved)
            return new(false, true, true, true, Refusal: FormGovernanceRefusal.PolicyInvalid);
        if (tags.Count == 0)
            return new(
                true,
                field.PiiSensitivity == PiiSensitivity.Sensitive,
                false,
                false,
                AllowedJurisdictions: residency.Allowed,
                ProhibitedJurisdictions: residency.Prohibited);

        return new(
            IsResolved: true,
            ProtectAtRest: true,
            RedactOnRead: tags.Any(code => code is "pii" or "phi" or "cui"),
            AuditOnRead: tags.Any(code => code is "pii" or "phi" or "cui"),
            RequiresSubjectProtection: tags.Contains("phi") || tags.Contains("identifier"),
            AllowedJurisdictions: residency.Allowed,
            ProhibitedJurisdictions: residency.Prohibited);
    }

    private static IEnumerable<AspectOverlay> EffectiveAspects(HarborlineOverlay overlay, string fieldName)
    {
        if (overlay.Aspects is not null) yield return overlay.Aspects;
        foreach (var section in overlay.Sections.Where(section => SectionContains(section, fieldName)))
        {
            if (section.Aspects is not null) yield return section.Aspects;
            foreach (var aspects in ItemAspects(section.Items, fieldName, [])) yield return aspects;
        }
        if (overlay.Fields[fieldName].Aspects is not null) yield return overlay.Fields[fieldName].Aspects!;
    }

    private static bool SectionContains(FormSection section, string fieldName) =>
        section.Fields.Contains(fieldName, StringComparer.Ordinal) || ItemContains(section.Items, fieldName);

    private static bool ItemContains(IReadOnlyList<FormItem>? items, string fieldName) =>
        items?.Any(item =>
            item.Kind == FormItemKind.Field && string.Equals(item.Key, fieldName, StringComparison.Ordinal) ||
            ItemContains(item.Items, fieldName)) == true;

    private static IEnumerable<AspectOverlay> ItemAspects(
        IReadOnlyList<FormItem>? items,
        string fieldName,
        IReadOnlyList<AspectOverlay> inherited)
    {
        foreach (var item in items ?? [])
        {
            var effective = item.Aspects is null ? inherited : inherited.Append(item.Aspects).ToArray();
            if (item.Kind == FormItemKind.Field && string.Equals(item.Key, fieldName, StringComparison.Ordinal))
                foreach (var aspects in effective) yield return aspects;
            foreach (var aspects in ItemAspects(item.Items, fieldName, effective)) yield return aspects;
        }
    }

    private static (bool IsResolved, IReadOnlySet<string>? Allowed, IReadOnlySet<string> Prohibited) ResolveResidency(
        IReadOnlyList<AspectOverlay> aspects)
    {
        HashSet<string>? allowed = null;
        var prohibited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in aspects.Select(aspect => aspect.Lifecycle?.Residency).Where(value => value is not null))
        {
            var next = requirement!.AllowedJurisdictions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            allowed = allowed is null ? next : allowed.Intersect(next, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var jurisdiction in requirement.ProhibitedJurisdictions ?? []) prohibited.Add(jurisdiction);
        }
        if (allowed is not null) allowed.ExceptWith(prohibited);
        return (allowed is null || allowed.Count > 0, allowed, prohibited);
    }
}

public sealed class TenantBoundAesGcmFormFieldSecurity : IFormGovernanceEnforcingFieldSecurity
{
    public const string FieldEncryptionKeyDomain = "encrypted-field-aes";
    public const string CryptoSuite = "AES-256-GCM";
    public static readonly IReadOnlySet<string> AcceptedDecryptPurposes =
        new HashSet<string>([FormEnginePermissions.DecryptOnRenderPurpose], StringComparer.Ordinal);
    private static readonly TimeSpan DecryptCapabilityLifetime = TimeSpan.FromSeconds(30);
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IFormTenantProtectionKeyProvider _keys;
    private readonly IFormDecryptCapabilityProvider _capabilities;
    private readonly IFormFieldGovernanceResolver _governance;
    private readonly FormFieldSecurityOptions _options;
    private readonly TimeProvider _clock;

    public TenantBoundAesGcmFormFieldSecurity(
        IFormTenantProtectionKeyProvider keys,
        IFormDecryptCapabilityProvider capabilities,
        IFormFieldGovernanceResolver? governance = null,
        FormFieldSecurityOptions? options = null,
        TimeProvider? clock = null)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _governance = governance ?? new DefaultFormFieldGovernanceResolver();
        _options = options ?? new FormFieldSecurityOptions();
        _clock = clock ?? TimeProvider.System;
    }

    public async ValueTask<FormProtectionResult> ProtectAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        EntityId instanceId,
        JsonDocument acceptedCandidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(acceptedCandidate);
        if (acceptedCandidate.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("A Forms candidate must be an object before protection.");

        FormTenantProtectionKey? key = null;
        var protectedFields = new HashSet<string>(StringComparer.Ordinal);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in acceptedCandidate.RootElement.EnumerateObject())
            {
                var policy = _governance.Resolve(definition, property.Name);
                if (!policy.IsResolved)
                    throw new FormEngineGovernanceException(
                        property.Name,
                        policy.Refusal ?? FormGovernanceRefusal.PolicyUnresolved);
                if (policy.RequiresSubjectProtection)
                    throw new FormEngineGovernanceException(property.Name, FormGovernanceRefusal.SubjectRequired);
                EnforceResidency(property.Name, policy);
                writer.WritePropertyName(property.Name);
                if (!policy.ProtectAtRest)
                    property.Value.WriteTo(writer);
                else
                {
                    key ??= await _keys.GetCurrentAsync(
                        scope.Tenant, FieldEncryptionKeyDomain, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("No tenant protection key is available.");
                    ValidateKey(key);
                    WriteEnvelope(writer, Encrypt(
                        property.Value.GetRawText(), scope.Tenant, definition, instanceId, property.Name, policy, key));
                    protectedFields.Add(property.Name);
                }
            }
            writer.WriteEndObject();
        }
        return new(buffer.ToArray(), protectedFields);
    }

    private void EnforceResidency(string fieldName, FormFieldGovernancePolicy policy)
    {
        if (policy.AllowedJurisdictions is null && (policy.ProhibitedJurisdictions?.Count ?? 0) == 0) return;
        if (string.IsNullOrWhiteSpace(_options.HostJurisdiction))
            throw new FormEngineGovernanceException(fieldName, FormGovernanceRefusal.ResidencyUnconfigured);
        if (policy.AllowedJurisdictions is not null &&
            !policy.AllowedJurisdictions.Contains(_options.HostJurisdiction, StringComparer.OrdinalIgnoreCase) ||
            policy.ProhibitedJurisdictions?.Contains(_options.HostJurisdiction, StringComparer.OrdinalIgnoreCase) == true)
            throw new FormEngineGovernanceException(fieldName, FormGovernanceRefusal.ResidencyDenied);
    }

    public async ValueTask<FormReadableCandidate> ReadAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        FormSubmissionRecord submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(submission);
        using var stored = JsonDocument.Parse(submission.ProtectedAcceptedCandidate);
        if (stored.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("A protected Forms candidate must be an object.");

        var fields = new List<FormFieldReadDecision>();
        foreach (var property in stored.RootElement.EnumerateObject())
        {
            var policy = _governance.Resolve(definition, property.Name);
            if (!policy.IsResolved)
            {
                fields.Add(Withheld(property.Name, PolicyAudit(
                    property.Name, FormSensitiveReadAuditOutcome.Denied, "policy-unresolved")));
                continue;
            }

            var envelopeStatus = TryReadEnvelope(property.Value, out var envelope);
            if (envelopeStatus == EnvelopeStatus.NotEnvelope)
            {
                fields.Add(policy.ProtectAtRest
                    ? Withheld(property.Name, policy.AuditOnRead
                        ? PolicyAudit(property.Name, FormSensitiveReadAuditOutcome.Denied, "plaintext-sensitive")
                        : null)
                    : Plaintext(property));
                continue;
            }
            if (envelopeStatus == EnvelopeStatus.Malformed)
            {
                fields.Add(Withheld(property.Name, policy.AuditOnRead
                    ? PolicyAudit(property.Name, FormSensitiveReadAuditOutcome.Denied, "malformed-envelope")
                    : null));
                continue;
            }

            if (policy.RedactOnRead)
            {
                fields.Add(Withheld(property.Name, policy.AuditOnRead
                    ? PolicyAudit(property.Name, FormSensitiveReadAuditOutcome.Withheld, "policy-redacted")
                    : null));
                continue;
            }
            if (!scope.Roles.Contains(FormEnginePermissions.DecryptSensitive, StringComparer.Ordinal))
            {
                fields.Add(Withheld(property.Name));
                continue;
            }

            FormDecryptCapability? capability;
            try
            {
                capability = await _capabilities.AcquireAsync(
                    scope.Tenant,
                    FormEnginePermissions.DecryptOnRenderPurpose,
                    DecryptCapabilityLifetime,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                fields.Add(Withheld(property.Name, DecryptAudit(
                    property.Name,
                    FormSensitiveReadAuditOutcome.Denied,
                    "capability-provider-unavailable")));
                continue;
            }
            if (capability is null)
            {
                fields.Add(Withheld(property.Name, DecryptAudit(
                    property.Name, FormSensitiveReadAuditOutcome.Denied, "capability-refused")));
                continue;
            }
            if (capability.Tenant != scope.Tenant ||
                !AcceptedDecryptPurposes.Contains(capability.Purpose) ||
                capability.ExpiresAt <= _clock.GetUtcNow())
            {
                var reason = capability.Tenant != scope.Tenant
                    ? "capability-wrong-tenant"
                    : !AcceptedDecryptPurposes.Contains(capability.Purpose)
                        ? "capability-wrong-purpose"
                        : "capability-expired";
                fields.Add(Withheld(property.Name, DecryptAudit(
                    property.Name, FormSensitiveReadAuditOutcome.Denied, reason)));
                continue;
            }

            try
            {
                var key = await _keys.GetAsync(
                    scope.Tenant,
                    FieldEncryptionKeyDomain,
                    envelope.KeyVersion,
                    cancellationToken).ConfigureAwait(false)
                    ?? throw new CryptographicException("The tenant key version is unavailable.");
                ValidateKey(key);
                var raw = Decrypt(
                    envelope, scope.Tenant, definition, submission.InstanceId, property.Name, policy, key);
                using var value = JsonDocument.Parse(raw);
                fields.Add(new(
                    property.Name,
                    FormFieldReadDisposition.DecryptGranted,
                    value.RootElement.Clone(),
                    true,
                    [DecryptAudit(
                        property.Name,
                        FormSensitiveReadAuditOutcome.Granted,
                        decryptCapabilityId: capability.CapabilityId)]));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidOperationException)
            {
                fields.Add(Withheld(property.Name, DecryptAudit(
                    property.Name, FormSensitiveReadAuditOutcome.Denied, "decrypt-denied")));
            }
        }
        return new(fields);
    }

    private static FormFieldReadDecision Plaintext(JsonProperty property) =>
        new(property.Name, FormFieldReadDisposition.Plaintext, property.Value.Clone(), false, []);

    private static FormFieldReadDecision Withheld(
        string field,
        FormSensitiveReadAuditEvent? audit = null) =>
        new(field, FormFieldReadDisposition.Withheld, null, true, audit is null ? [] : [audit]);

    private static FormSensitiveReadAuditEvent PolicyAudit(
        string field,
        FormSensitiveReadAuditOutcome outcome,
        string reason) =>
        new(field, FormSensitiveReadAuditKind.PolicySensitiveRead, outcome, Reason: reason);

    private static FormSensitiveReadAuditEvent DecryptAudit(
        string field,
        FormSensitiveReadAuditOutcome outcome,
        string? reason = null,
        string? decryptCapabilityId = null) =>
        new(
            field,
            FormSensitiveReadAuditKind.DecryptOnRender,
            outcome,
            FormEnginePermissions.DecryptSensitive,
            FormEnginePermissions.DecryptOnRenderPurpose,
            reason,
            decryptCapabilityId);

    private static ProtectedEnvelope Encrypt(
        string rawJson,
        TenantId tenant,
        FormDefinition definition,
        EntityId instanceId,
        string field,
        FormFieldGovernancePolicy policy,
        FormTenantProtectionKey key)
    {
        var plaintext = Encoding.UTF8.GetBytes(rawJson);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key.KeyMaterial.Span, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(tenant, definition, instanceId, field, policy));
        return new(CryptoSuite, key.KeyVersion, nonce, ciphertext, tag);
    }

    private static byte[] Decrypt(
        ProtectedEnvelope envelope,
        TenantId tenant,
        FormDefinition definition,
        EntityId instanceId,
        string field,
        FormFieldGovernancePolicy policy,
        FormTenantProtectionKey key)
    {
        var plaintext = new byte[envelope.Ciphertext.Length];
        using var aes = new AesGcm(key.KeyMaterial.Span, TagSize);
        aes.Decrypt(
            envelope.Nonce,
            envelope.Ciphertext,
            envelope.Tag,
            plaintext,
            AssociatedData(tenant, definition, instanceId, field, policy));
        return plaintext;
    }

    private static byte[] AssociatedData(
        TenantId tenant,
        FormDefinition definition,
        EntityId instanceId,
        string field,
        FormFieldGovernancePolicy policy) => Encoding.UTF8.GetBytes(
            $"{tenant}\n{definition.Id.Value}\n{definition.Version}\n{instanceId}\n{field}\n{GovernanceBinding(policy)}");

    private static string GovernanceBinding(FormFieldGovernancePolicy policy) => string.Join('|',
        policy.ProtectAtRest,
        policy.RedactOnRead,
        policy.AuditOnRead,
        policy.RequiresSubjectProtection,
        string.Join(',', (policy.AllowedJurisdictions ?? new HashSet<string>()).Order(StringComparer.OrdinalIgnoreCase)),
        string.Join(',', (policy.ProhibitedJurisdictions ?? new HashSet<string>()).Order(StringComparer.OrdinalIgnoreCase)));

    private static void ValidateKey(FormTenantProtectionKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.KeyVersion);
        if (key.KeyMaterial.Length != 32)
            throw new InvalidOperationException("Tenant protection keys must be 256-bit AES keys.");
    }

    private static void WriteEnvelope(Utf8JsonWriter writer, ProtectedEnvelope envelope)
    {
        writer.WriteStartObject();
        writer.WriteNumber("$harborlineProtected", 1);
        writer.WriteString("suite", envelope.Suite);
        writer.WriteString("keyVersion", envelope.KeyVersion);
        writer.WriteBase64String("nonce", envelope.Nonce);
        writer.WriteBase64String("ct", envelope.Ciphertext);
        writer.WriteBase64String("tag", envelope.Tag);
        writer.WriteEndObject();
    }

    private static EnvelopeStatus TryReadEnvelope(JsonElement value, out ProtectedEnvelope envelope)
    {
        envelope = default!;
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("$harborlineProtected", out var marker))
            return EnvelopeStatus.NotEnvelope;

        try
        {
            if (marker.ValueKind != JsonValueKind.Number || marker.GetInt32() != 1 ||
                !value.TryGetProperty("suite", out var suite) ||
                !value.TryGetProperty("keyVersion", out var keyVersion) ||
                !value.TryGetProperty("nonce", out var nonce) ||
                !value.TryGetProperty("ct", out var ciphertext) ||
                !value.TryGetProperty("tag", out var tag))
                return EnvelopeStatus.Malformed;

            envelope = new(
                suite.GetString()!,
                keyVersion.GetString()!,
                nonce.GetBytesFromBase64(),
                ciphertext.GetBytesFromBase64(),
                tag.GetBytesFromBase64());
            return string.Equals(envelope.Suite, CryptoSuite, StringComparison.Ordinal) &&
                   !string.IsNullOrWhiteSpace(envelope.KeyVersion) &&
                   envelope.Nonce.Length == NonceSize &&
                   envelope.Tag.Length == TagSize
                ? EnvelopeStatus.Valid
                : EnvelopeStatus.Malformed;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return EnvelopeStatus.Malformed;
        }
    }

    private enum EnvelopeStatus { NotEnvelope, Valid, Malformed }
    private sealed record ProtectedEnvelope(
        string Suite,
        string KeyVersion,
        byte[] Nonce,
        byte[] Ciphertext,
        byte[] Tag);
}

/// <summary>
/// Read-withholding profile for a host where decrypt-on-render is deliberately unavailable.
/// Writes still delegate to a real protection adapter; encrypted values are never passed through.
/// </summary>
public sealed class WithholdingFormFieldSecurity : IFormGovernanceEnforcingFieldSecurity
{
    private readonly IFormGovernanceEnforcingFieldSecurity _writeProtection;

    public WithholdingFormFieldSecurity(IFormFieldSecurity writeProtection)
    {
        ArgumentNullException.ThrowIfNull(writeProtection);
        _writeProtection = writeProtection as IFormGovernanceEnforcingFieldSecurity
            ?? throw new ArgumentException("The withholding profile requires a governance-enforcing write adapter.", nameof(writeProtection));
    }

    public ValueTask<FormProtectionResult> ProtectAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        EntityId instanceId,
        JsonDocument acceptedCandidate,
        CancellationToken cancellationToken = default) =>
        _writeProtection.ProtectAsync(scope, definition, instanceId, acceptedCandidate, cancellationToken);

    public ValueTask<FormReadableCandidate> ReadAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        FormSubmissionRecord submission,
        CancellationToken cancellationToken = default)
    {
        using var stored = JsonDocument.Parse(submission.ProtectedAcceptedCandidate);
        if (stored.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("A protected Forms candidate must be an object.");

        var fields = stored.RootElement.EnumerateObject().Select(property =>
            property.Value.ValueKind == JsonValueKind.Object &&
            property.Value.TryGetProperty("$harborlineProtected", out _)
                ? new FormFieldReadDecision(
                    property.Name,
                    FormFieldReadDisposition.Withheld,
                    null,
                    true,
                    [])
                : new FormFieldReadDecision(
                    property.Name,
                    FormFieldReadDisposition.Plaintext,
                    property.Value.Clone(),
                    false,
                    [])).ToArray();
        return ValueTask.FromResult(new FormReadableCandidate(fields));
    }
}
