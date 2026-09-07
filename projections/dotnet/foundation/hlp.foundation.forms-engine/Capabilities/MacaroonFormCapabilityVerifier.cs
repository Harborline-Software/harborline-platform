using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Engine.Capabilities;

/// <summary>
/// Strict verifier for the pinned earlier source Forms macaroon wire format. It preserves the wire and
/// HMAC chain while closing ambiguous caveat and secret-echo behavior at the Harborline seam.
/// </summary>
public sealed class MacaroonFormCapabilityVerifier : IFormCapabilityVerifier
{
    private const string IdentifierVersion = "hlf1";
    private readonly IFormCapabilityRootKeyProvider _keys;

    /// <summary>Creates a verifier over a host-owned key provider.</summary>
    public MacaroonFormCapabilityVerifier(IFormCapabilityRootKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys;
    }

    /// <inheritdoc />
    public async ValueTask<VerifiedFormCapability> VerifyAsync(
        string bearer,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var macaroon = FormMacaroonCodec.Decode(bearer);
            var resolved = await _keys
                .GetRootKeyAsync(macaroon.Location, cancellationToken)
                .ConfigureAwait(false);
            if (resolved is null || resolved.Value.IsEmpty) Deny();

            var expected = FormMacaroonCodec.ComputeChain(
                resolved!.Value.Span,
                macaroon.Identifier,
                macaroon.Caveats);
            if (!CryptographicOperations.FixedTimeEquals(expected, macaroon.Signature)) Deny();

            var identifierAction = ParseIdentifierAction(macaroon.Identifier);
            var parsed = ParseCaveats(macaroon.Caveats);
            if (!parsed.Actions.Contains(identifierAction)) Deny();
            if (now > parsed.ExpiresAt) Deny();
            return new(parsed.Tenant, parsed.Subject, parsed.Actions, parsed.ExpiresAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FormCapabilityDeniedException)
        {
            throw;
        }
        catch
        {
            throw new FormCapabilityDeniedException();
        }
    }

    private static ParsedCaveats ParseCaveats(IReadOnlyList<string> caveats)
    {
        TenantId? tenant = null;
        string? subject = null;
        DateTimeOffset? expiresAt = null;
        var actions = new HashSet<FormCapabilityAction>();

        foreach (var predicate in caveats)
        {
            if (string.IsNullOrWhiteSpace(predicate) || predicate.Length > FormMacaroonCodec.MaximumPartCharacters) Deny();
            var equals = predicate.IndexOf('=');
            if (equals <= 0 || equals == predicate.Length - 1) Deny();
            var key = predicate[..equals].Trim();
            var value = predicate[(equals + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0) Deny();

            switch (key)
            {
                case "tenant":
                    if (tenant is not null) Deny();
                    tenant = new TenantId(value);
                    break;
                case "subject":
                    if (subject is not null) Deny();
                    subject = value;
                    break;
                case "expires":
                    if (expiresAt is not null) Deny();
                    if (!DateTimeOffset.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var parsedExpiry)) Deny();
                    expiresAt = parsedExpiry;
                    break;
                case "role":
                    break;
                case "action":
                    if (actions.Count != 0) Deny();
                    var action = value switch
                    {
                        "read" => FormCapabilityAction.Read,
                        "write" => FormCapabilityAction.Write,
                        _ => throw new FormCapabilityDeniedException(),
                    };
                    actions.Add(action);
                    break;
                default:
                    Deny();
                    break;
            }
        }

        if (tenant is null || subject is null || expiresAt is null || actions.Count != 1) Deny();
        return new(tenant!.Value, subject!, actions.ToFrozenSet(), expiresAt!.Value);
    }

    private static FormCapabilityAction ParseIdentifierAction(string identifier)
    {
        var parts = identifier.Split(':');
        if (parts.Length != 3
            || !string.Equals(parts[0], IdentifierVersion, StringComparison.Ordinal)
            || parts[2].Length != 32
            || parts[2].Any(character => !Uri.IsHexDigit(character))) Deny();
        return parts[1] switch
        {
            "read" => FormCapabilityAction.Read,
            "write" => FormCapabilityAction.Write,
            _ => throw new FormCapabilityDeniedException(),
        };
    }

    [DoesNotReturn]
    private static void Deny() => throw new FormCapabilityDeniedException();

    private sealed record ParsedCaveats(
        TenantId Tenant,
        string Subject,
        IReadOnlySet<FormCapabilityAction> Actions,
        DateTimeOffset ExpiresAt);
}

/// <summary>Source-compatible Forms macaroon issuer over the narrowed root-key provider.</summary>
public sealed class MacaroonFormCapabilityIssuer : IFormCapabilityIssuer
{
    /// <summary>The pinned source location used for Forms capability root-key lookup.</summary>
    public const string DefaultLocation = "sunfish/forms";

    private readonly IFormCapabilityRootKeyProvider _keys;
    private readonly string _location;

    /// <summary>Creates an issuer over a host-owned key provider and optional location.</summary>
    public MacaroonFormCapabilityIssuer(
        IFormCapabilityRootKeyProvider keys,
        string location = DefaultLocation)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        _keys = keys;
        _location = location;
    }

    /// <inheritdoc />
    public async ValueTask<string> IssueAsync(
        TenantId tenant,
        string subject,
        IReadOnlyList<string> compatibilityRoles,
        IReadOnlyList<FormCapabilityAction> actions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (tenant.IsSystemSentinel) throw new ArgumentException("A concrete tenant is required.", nameof(tenant));
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(compatibilityRoles);
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count != 1) throw new ArgumentException("Exactly one action is required.", nameof(actions));

        if (compatibilityRoles.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Compatibility roles must be non-empty.", nameof(compatibilityRoles));
        if (actions.Any(action => !Enum.IsDefined(action)))
            throw new ArgumentException("Actions must be known.", nameof(actions));

        var caveats = new List<string>
        {
            $"tenant = {tenant.Value}",
            $"subject = {subject}",
            $"expires = {expiresAt.ToString("O", CultureInfo.InvariantCulture)}",
        };
        if (compatibilityRoles.Count > FormMacaroonCodec.MaximumCaveats - 4)
            throw new ArgumentException("Too many compatibility roles.", nameof(compatibilityRoles));
        caveats.AddRange(compatibilityRoles.Select(role => $"role = {role}"));
        caveats.AddRange(actions.Select(action => $"action = {ActionValue(action)}"));

        var actionValue = ActionValue(actions[0]);
        var identifier = $"hlf1:{actionValue}:{Guid.NewGuid():N}";
        FormMacaroonCodec.ValidateEncodable(_location, identifier, caveats);

        var key = await _keys.GetRootKeyAsync(_location, cancellationToken).ConfigureAwait(false);
        if (key is null || key.Value.IsEmpty)
            throw new InvalidOperationException("The Forms capability signing key is unavailable.");
        return FormMacaroonCodec.EncodeSigned(
            _location,
            identifier,
            caveats,
            key.Value.Span);
    }

    private static string ActionValue(FormCapabilityAction action) => action switch
    {
        FormCapabilityAction.Read => "read",
        FormCapabilityAction.Write => "write",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}

internal static class FormMacaroonCodec
{
    internal const int MaximumPartCharacters = 2048;
    internal const int MaximumEncodedCharacters = 32768;
    internal const int MaximumCaveats = 64;
    private const byte RecordSeparator = 0x1e;
    private const byte UnitSeparator = 0x1f;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static FormMacaroon Decode(string bearer)
    {
        if (string.IsNullOrWhiteSpace(bearer)
            || bearer.Length > MaximumEncodedCharacters
            || bearer.Any(character => !IsBase64Url(character)))
            throw new FormCapabilityDeniedException();

        var remainder = bearer.Length % 4;
        if (remainder == 1) throw new FormCapabilityDeniedException();
        var normalized = bearer.Replace('-', '+').Replace('_', '/')
            + new string('=', remainder == 0 ? 0 : 4 - remainder);
        var bytes = Convert.FromBase64String(normalized);
        var unit = Array.IndexOf(bytes, UnitSeparator);
        if (unit < 0 || bytes.Length - unit - 1 != 32) throw new FormCapabilityDeniedException();

        var parts = Split(bytes.AsSpan(0, unit), RecordSeparator);
        if (parts.Count < 2 || parts.Count > MaximumCaveats + 2) throw new FormCapabilityDeniedException();
        var location = DecodePart(parts[0]);
        var identifier = DecodePart(parts[1]);
        if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(identifier))
            throw new FormCapabilityDeniedException();
        var caveats = parts.Skip(2).Select(DecodePart).ToArray();
        return new(location, identifier, caveats, bytes.AsSpan(unit + 1, 32).ToArray());
    }

    internal static string EncodeSigned(
        string location,
        string identifier,
        IReadOnlyList<string> caveats,
        ReadOnlySpan<byte> rootKey)
    {
        ValidateEncodable(location, identifier, caveats);
        var signature = ComputeChain(rootKey, identifier, caveats);
        return EncodeWithSignature(location, identifier, caveats, signature);
    }

    internal static string EncodeWithSignature(
        string location,
        string identifier,
        IReadOnlyList<string> caveats,
        ReadOnlySpan<byte> signature)
    {
        ValidateEncodable(location, identifier, caveats);
        if (signature.Length != 32) throw new ArgumentException("A 32-byte signature is required.", nameof(signature));
        var bytes = new List<byte>();
        Append(bytes, location);
        bytes.Add(RecordSeparator);
        Append(bytes, identifier);
        foreach (var caveat in caveats)
        {
            bytes.Add(RecordSeparator);
            Append(bytes, caveat);
        }
        bytes.Add(UnitSeparator);
        bytes.AddRange(signature.ToArray());
        var encoded = Convert.ToBase64String(bytes.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (encoded.Length > MaximumEncodedCharacters) throw new ArgumentException("The encoded capability is too large.", nameof(caveats));
        return encoded;
    }

    internal static void ValidateEncodable(
        string location,
        string identifier,
        IReadOnlyList<string> caveats)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(caveats);
        if (caveats.Count > MaximumCaveats) throw new ArgumentException("Too many caveats.", nameof(caveats));
        ValidatePart(location, nameof(location));
        ValidatePart(identifier, nameof(identifier));
        foreach (var caveat in caveats) ValidatePart(caveat, nameof(caveats));
        var wireBytes = StrictUtf8.GetByteCount(location)
            + 1
            + StrictUtf8.GetByteCount(identifier)
            + caveats.Sum(caveat => 1 + StrictUtf8.GetByteCount(caveat))
            + 1
            + 32;
        if (((wireBytes + 2) / 3) * 4 > MaximumEncodedCharacters)
            throw new ArgumentException("The encoded capability is too large.", nameof(caveats));
    }

    internal static byte[] ComputeChain(
        ReadOnlySpan<byte> rootKey,
        string identifier,
        IReadOnlyList<string> caveats)
    {
        var signature = HMACSHA256.HashData(rootKey, StrictUtf8.GetBytes(identifier));
        foreach (var caveat in caveats)
            signature = HMACSHA256.HashData(signature, StrictUtf8.GetBytes(caveat));
        return signature;
    }

    private static bool IsBase64Url(char value) =>
        value is >= 'A' and <= 'Z'
        or >= 'a' and <= 'z'
        or >= '0' and <= '9'
        or '-' or '_';

    private static string DecodePart(byte[] value)
    {
        var result = StrictUtf8.GetString(value);
        if (result.Length > MaximumPartCharacters) throw new FormCapabilityDeniedException();
        return result;
    }

    private static void ValidatePart(string value, string parameterName)
    {
        if (value.Length > MaximumPartCharacters
            || value.IndexOf((char)RecordSeparator) >= 0
            || value.IndexOf((char)UnitSeparator) >= 0)
            throw new ArgumentException("Capability material exceeds the bounded wire format.", parameterName);
    }

    private static List<byte[]> Split(ReadOnlySpan<byte> source, byte delimiter)
    {
        var result = new List<byte[]>();
        var start = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != delimiter) continue;
            result.Add(source[start..index].ToArray());
            start = index + 1;
        }
        result.Add(source[start..].ToArray());
        return result;
    }

    private static void Append(List<byte> target, string value) => target.AddRange(StrictUtf8.GetBytes(value));

    internal sealed record FormMacaroon(
        string Location,
        string Identifier,
        IReadOnlyList<string> Caveats,
        byte[] Signature);
}
