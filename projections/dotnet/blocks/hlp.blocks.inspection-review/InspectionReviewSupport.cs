using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.MultiTenancy;

namespace Harborline.Blocks.InspectionReview;

/// <summary>Recoverable projection failure that leaves the Forms outbox row pending.</summary>
public sealed class InspectionReviewProjectionException : Exception
{
    /// <summary>Creates a stable coded projection failure.</summary>
    public InspectionReviewProjectionException(string code, string message)
        : base(message) => Code = code;

    /// <summary>Stable machine code.</summary>
    public string Code { get; }
}

internal static class InspectionReviewScope
{
    internal static async ValueTask<TenantId?> ResolveAsync(
        ITenantContext tenants,
        IPartyContext parties,
        CancellationToken cancellationToken)
    {
        var tenant = tenants.Tenant;
        if (tenant is null || tenant.Status != TenantStatus.Active || tenant.Id.IsSystemSentinel) return null;
        try
        {
            var party = await parties.GetCurrentPartyIdAsync(cancellationToken).ConfigureAwait(false);
            return party == Guid.Empty ? (TenantId?)null : tenant.Id;
        }
        catch (PrincipalPartyResolutionException)
        {
            return null;
        }
    }
}

internal sealed record PersistedInspectionReview(
    string SubmissionId,
    string SubjectReference,
    string FormId,
    string FormVersion,
    int ConditionScore,
    InspectionConditionOutcome ConditionOutcome,
    DateTimeOffset SubmittedAt,
    Guid SubmittedByParty,
    string SubmittedByActor);

internal static class InspectionReviewJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static string Serialize(PersistedInspectionReview value) => JsonSerializer.Serialize(value, Options);

    internal static PersistedInspectionReview? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<PersistedInspectionReview>(json, Options); }
        catch (JsonException) { return null; }
    }

    internal static bool TryResolve(JsonElement root, string pointer, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(pointer) || !pointer.StartsWith("/", StringComparison.Ordinal)) return false;
        foreach (var encoded in pointer[1..].Split('/'))
        {
            var token = encoded.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!value.TryGetProperty(token, out value)) return false;
            }
            else if (value.ValueKind == JsonValueKind.Array
                     && int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
                     && index >= 0
                     && index < value.GetArrayLength())
            {
                value = value[index];
            }
            else return false;
        }
        return true;
    }
}
