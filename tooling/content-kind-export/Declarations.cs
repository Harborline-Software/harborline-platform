namespace Harborline.Tooling.ContentKinds;

/// <summary>A constant explicitly exposed by one identity.</summary>
public sealed record ConstantDeclaration(string Name, string RegistryName, bool IsPillar, string Documentation);
/// <summary>The checked-in output path and partial identity declaration.</summary>
public sealed record IdentityDeclaration(string Path, string Namespace, string Type, IReadOnlyList<ConstantDeclaration> Constants);
/// <summary>The intentionally partial public declarations, independent of registry growth.</summary>
public static class Declarations
{
    /// <summary>The nine identity surfaces specified by T-738.</summary>
    public static IReadOnlyList<IdentityDeclaration> All { get; } =
    [
        new("projections/dotnet/blocks/hlp.blocks.builder-definitions/LayoutPackIdentity.g.cs", "Harborline.Blocks.BuilderDefinitions", "LayoutPackIdentity",
        [
            new("ContentKind", "Layout", false, """
<summary>Layout content follows the existing content kinds 0 through 16.</summary>
"""),
            new("Primitive", "Layout", true, """
<summary>Layout follows primitive buckets 0 through 11; 99 remains Other.</summary>
"""),
        ]),
        new("projections/dotnet/blocks/hlp.blocks.builder-definitions/ReleasedNavigationPackIdentity.g.cs", "Harborline.Blocks.BuilderDefinitions", "ReleasedNavigationPackIdentity",
        [
            new("ContentKind", "ReleasedNavigationDefinition", false, """
<summary>
Content kind 21, shipped in <c>PackContentKindRegistry</c>: the next free value after Assistance's 20
(owner ruling, 2026-09-29). 18 is the api's <c>Resource</c> and was a collision (T-738).
</summary>
"""),
            new("Primitive", "Navigation", true, """
<summary>The Navigation pillar, 3. 13 is Booking's and was a collision (T-738).</summary>
"""),
        ]),
        new("projections/dotnet/blocks/hlp.blocks.calendar/Booking/BookingPackIdentity.g.cs", "Harborline.Blocks.Calendar.Booking", "BookingPackIdentity",
        [
            new("Primitive", "Booking", true, """
<summary>Booking's primitive bucket: after the transport's 0 through 11 and Layout's 12; 99 remains Other.</summary>
"""),
            new("ResourceContentKind", "Resource", false, """
<summary>The Resource content kind: after the transport's 0 through 16 and Layout's 17.</summary>
"""),
            new("BookableContentKind", "Bookable", false, """
<summary>The Bookable content kind.</summary>
"""),
        ]),
        new("projections/dotnet/blocks/hlp.blocks.entity-views/ViewsPackIdentity.g.cs", "Harborline.Blocks.EntityViews", "ViewsPackIdentity",
        [
            new("ContentKind", "ViewDefinition", false, """
<summary>The content kind a <see cref="ViewDefinition"/> travels as.</summary>
"""),
            new("Primitive", "Views", true, """
<summary>The Views primitive bucket.</summary>
"""),
        ]),
        new("projections/dotnet/blocks/hlp.blocks.reports/Definitions/ReportPackIdentity.g.cs", "Harborline.Blocks.Reports.Definitions", "ReportPackIdentity",
        [
            new("ContentKind", "ReportDefinition", false, """
<summary>
Content kind 9, <c>ReportDefinition</c>: the api's <c>PackContentKind.ReportDefinition</c>, as
<see cref="PackContentKindRegistry"/> records it (T-738). DES-0020 line 79's 8 is superseded.
</summary>
"""),
        ]),
        new("projections/dotnet/foundation/hlp.foundation.assistance/AssistancePackIdentity.g.cs", "Harborline.Foundation.Assistance", "AssistancePackIdentity",
        [
            new("ContentKind", "AssistanceDefinition", false, """
<summary>The reserved content kind for Assistance definitions.</summary>
"""),
        ]),
        new("projections/dotnet/foundation/hlp.foundation.data-exchange/DataExchangePackIdentity.g.cs", "Harborline.Foundation.DataExchange", "DataExchangePackIdentity",
        [
            new("ContentKind", "DataExchangeDefinition", false, """
<summary>Content kind 12, <c>DataExchangeDefinition</c>: the api's <c>PackContentKind.DataExchangeDefinition</c>. 11 is <c>ScheduleDefinition</c> and was a collision (T-724 ruling 51).</summary>
"""),
        ]),
        new("projections/dotnet/foundation/hlp.foundation.documents/DocumentsPackIdentity.g.cs", "Harborline.Foundation.Documents", "DocumentsPackIdentity",
        [
            new("ContentKind", "TemplateDefinition", false, """
<summary>Content kind 7, the api's <c>PackContentKind.TemplateDefinition</c>. 6 is <c>TerminologyOverride</c> and was a collision (T-738).</summary>
"""),
        ]),
        new("projections/dotnet/foundation/hlp.foundation.taxonomy/TaxonomyPackIdentity.g.cs", "Harborline.Foundation.Taxonomy", "TaxonomyPackIdentity",
        [
            new("ContentKind", "TaxonomyDefinition", false, """
<summary>Content kind discriminator for taxonomy definitions (8, per T-738).</summary>
"""),
        ]),
    ];
}
