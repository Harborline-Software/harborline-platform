using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>A source-owned member before read-authority filtering.</summary>
/// <param name="Value">The canonical value admitted by this source.</param>
/// <param name="ResourceId">The source resource checked by the authority adapter.</param>
/// <param name="Fields">Pinned record facts for predicate evaluation; not returned to consumers.</param>
public sealed record FieldDomainMember(string Value, string ResourceId, JsonElement Fields);

/// <summary>A host-supplied, immutable read snapshot, private to domain interpretation.</summary>
/// <remarks>
/// Membership is complete and unfiltered so subset proofs cannot miss unreadable widening.
/// Consumers receive only the runtime's authority-filtered resolution, never this snapshot.
/// All getters refer to the same revision for the lifetime of this object.
/// </remarks>
public interface IFieldDomainSnapshot
{
    /// <summary>The tenant whose sources this snapshot contains.</summary>
    TenantId Tenant { get; }

    /// <summary>The pinned source revision used by every read.</summary>
    string Revision { get; }

    /// <summary>Whether membership is proven complete rather than paged or authority-filtered.</summary>
    bool IsComplete { get; }

    /// <summary>Reads the exact scheme revision, or null when it is unresolved.</summary>
    IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme);

    /// <summary>Reads the record type's complete candidate set, or null when it is unresolved.</summary>
    IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId);
}

/// <summary>The host's source-reading adapter, without member-specific interpretation.</summary>
public interface IFieldDomainSource
{
    /// <summary>Opens a stable tenant snapshot for one resolution or narrowing proof.</summary>
    ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant,
        CancellationToken cancellationToken = default);
}

/// <summary>The adapter to the existing read-authority owner, not a second authorization policy.</summary>
public interface IFieldDomainReadAuthority
{
    /// <summary>Checks the caller's read authority before a member can be returned or counted.</summary>
    ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
        FieldDomainMember member, CancellationToken cancellationToken = default);
}
