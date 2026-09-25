using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;

using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>T-605: Booking's Resource and Bookable definitions and their structural admission (DES-0025).</summary>
public sealed class BookingDefinitionTests
{
    // The transport's content kinds 0..16 (harborline-api PackContentKind at e81d52fa) plus the
    // platform's additive Layout kind; primitive buckets 0..11 and 99 plus Layout's bucket.
    private static readonly int[] TakenContentKinds = [.. Enumerable.Range(0, 17), LayoutPackIdentity.ContentKind];
    private static readonly int[] TakenPrimitives = [.. Enumerable.Range(0, 12), 99, LayoutPackIdentity.Primitive];

    [Fact(DisplayName = "booking-ck-1,2,9: Booking takes an unused primitive bucket, two unused content kinds and two archive namespaces")]
    public void BookingWireValuesDoNotCollide()
    {
        Assert.DoesNotContain(BookingPackIdentity.Primitive, TakenPrimitives);
        Assert.DoesNotContain(BookingPackIdentity.ResourceContentKind, TakenContentKinds);
        Assert.DoesNotContain(BookingPackIdentity.BookableContentKind, TakenContentKinds);
        Assert.NotEqual(BookingPackIdentity.ResourceContentKind, BookingPackIdentity.BookableContentKind);
        Assert.Equal(BookingPackIdentity.ResourceContentKind, BookingPackIdentity.ContentKindOf(DefinitionKind.Resources));
        Assert.Equal(BookingPackIdentity.BookableContentKind, BookingPackIdentity.ContentKindOf(DefinitionKind.Bookables));
        Assert.NotEqual(DefinitionKind.Resources, DefinitionKind.Bookables);
        Assert.Equal(Enum.GetValues<DefinitionKind>().Length, Enum.GetValues<DefinitionKind>().Distinct().Count());
    }
}
