using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>
/// Booking's additive pack wire identities (DES-0025 booking-ck-1, ck-2, ck-9), independent of the
/// archive namespaces <see cref="DefinitionKind.Resources"/> and <see cref="DefinitionKind.Bookables"/>.
/// </summary>
public static class BookingPackIdentity
{
    /// <summary>Booking's primitive bucket: after the transport's 0 through 11 and Layout's 12; 99 remains Other.</summary>
    public const int Primitive = 13;

    /// <summary>The Resource content kind: after the transport's 0 through 16 and Layout's 17.</summary>
    public const int ResourceContentKind = 18;

    /// <summary>The Bookable content kind.</summary>
    public const int BookableContentKind = 19;

    /// <summary>The content kind a Booking archive namespace travels as.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The namespace is not a Booking definition kind.</exception>
    public static int ContentKindOf(DefinitionKind kind) => kind switch
    {
        DefinitionKind.Resources => ResourceContentKind,
        DefinitionKind.Bookables => BookableContentKind,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "booking.definition.kind_unknown"),
    };
}
