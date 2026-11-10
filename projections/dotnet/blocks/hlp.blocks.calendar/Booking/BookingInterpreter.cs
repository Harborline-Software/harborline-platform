using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>Entry seam for a Booking claim, before definition or supply resolution.</summary>
public interface IBookingInterpreter
{
    /// <summary>Claims the named Bookable using caller-supplied Resource suppliers.</summary>
    Task<BookableClaimOutcome> Claim(TenantId tenant, string bookableId,
        IReadOnlyDictionary<string, ParticipantRef> suppliers, DateTimeOffset startUtc, DateTimeOffset endUtc,
        CancellationToken ct = default);
}

/// <summary>Resolves Booking definitions at their published heads before claiming capacity.</summary>
public sealed class BookingInterpreter(IVersionedDefinitionStore definitions, BookableClaimService claims) : IBookingInterpreter
{
    /// <summary>Claims the named Bookable using caller-supplied Resource suppliers.</summary>
    public async Task<BookableClaimOutcome> Claim(TenantId tenant, string bookableId,
        IReadOnlyDictionary<string, ParticipantRef> suppliers, DateTimeOffset startUtc, DateTimeOffset endUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(suppliers);
        var head = await definitions.GetPublishedHeadAsync(new(tenant.Value, DefinitionKind.Bookables, bookableId), ct).ConfigureAwait(false);
        if (head is null)
            return BookableClaimOutcome.Refused(BookableClaimCodes.RequirementUnresolved) with { UnresolvedName = bookableId };
        var bookable = BookingBookableDefinition.Parse(head.Document.BodyJson);
        var resources = new List<BookableClaimResource>();
        foreach (var id in bookable.Requires)
        {
            var resource = await definitions.GetPublishedHeadAsync(new(tenant.Value, DefinitionKind.Resources, id), ct).ConfigureAwait(false);
            if (resource is null || !suppliers.TryGetValue(id, out var supplier))
                return BookableClaimOutcome.Refused(BookableClaimCodes.RequirementUnresolved) with { UnresolvedName = id };
            resources.Add(new(supplier, BookingResourceDefinition.Parse(resource.Document.BodyJson), id));
        }
        return await claims.Claim(tenant, bookable, resources, startUtc, endUtc, ct).ConfigureAwait(false);
    }
}
