using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// The availability substrate contract (DES-0033 §3; ADR 0080; T-626). One composition every member
/// reaches — a form offering slots, a view drawing a calendar, a workflow governing a booking, a rule
/// expressing eligibility and Booking's own gate — so two surfaces cannot disagree.
/// </summary>
/// <remarks>
/// Given a bounded window and the admitted capacity of each required resource, the runtime:
/// refuses an unbounded or inverted window before reading anything; expands the supply (base hours,
/// wall-clock kept across daylight saving); subtracts the resource's own and its subscribed shared
/// exception days; reads the current allocations and holds beside the store, each over its occupied
/// footprint; applies the capacity kind (exclusive: any overlap; pool: overlap depth up to the size);
/// evaluates the required set as a conjunction over the window plus each resource's buffers; and
/// returns free and busy intervals clipped to the window. Nothing is stored and nothing is cached.
/// </remarks>
public interface IAvailabilityRuntime
{
    /// <summary>
    /// Read availability for <paramref name="request"/> in <paramref name="tenantId"/>. Returns a
    /// refusal (no store read) when the window omits an endpoint or is inverted.
    /// </summary>
    Task<AvailabilityAnswer> Read(TenantId tenantId, AvailabilityRequest request, CancellationToken ct = default);
}
