using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

/// <summary>
/// T-1177 (call F, option 1, ruled 2026-11-08): a published view's shape roles are checked at the shared store's commit
/// against the field kinds the Record Type head pins. Codes and pointers are DES-0017 <c>views-auth-15</c> literals; the
/// temporal formats are RFC 3339 §5.6's <c>full-date</c>, <c>date-time</c> and <c>full-time</c> as JSON Schema 2020-12
/// Validation §7.3.1 names them (<c>date</c>, <c>date-time</c>, <c>time</c>).
/// </summary>
public sealed class ViewShapeRoleCommitTests
{
    private const string Tenant = "tenant-a";
    private const string Incompatible = "view_definition.shape_role_field_incompatible";
    private const string Timeline = "views.timeline";

    [Theory(DisplayName = "views-ck-5: a field kind's shape and format map to exactly one view kind")]
    [InlineData(FieldScalarValueShape.Text, null, ViewRecordFieldKind.Text)]
    [InlineData(FieldScalarValueShape.Text, "date", ViewRecordFieldKind.DateTime)]
    [InlineData(FieldScalarValueShape.Text, "date-time", ViewRecordFieldKind.DateTime)]
    [InlineData(FieldScalarValueShape.Text, "time", ViewRecordFieldKind.DateTime)]
    [InlineData(FieldScalarValueShape.Integer, null, ViewRecordFieldKind.Ordered)]
    [InlineData(FieldScalarValueShape.Number, null, ViewRecordFieldKind.Ordered)]
    [InlineData(FieldScalarValueShape.Boolean, null, ViewRecordFieldKind.Scalar)]
    public void AFieldKindsDeclarationMapsToOneViewKind(FieldScalarValueShape shape, string? format, ViewRecordFieldKind expected) =>
        Assert.Equal(expected, ViewBindingCompatibility.KindOf(new("k", "1.0.0", null, shape, TemporalFormat: format)));

    [Theory(DisplayName = "views-ck-5: a declaration outside the admitted formats has no view kind rather than a guessed one")]
    [InlineData(FieldScalarValueShape.Text, "week")]
    [InlineData(FieldScalarValueShape.Integer, "date")]
    [InlineData(FieldScalarValueShape.Number, "date-time")]
    [InlineData(FieldScalarValueShape.Boolean, "time")]
    [InlineData((FieldScalarValueShape)99, null)]
    public void ADeclarationOutsideTheFormatsHasNoViewKind(FieldScalarValueShape shape, string? format) =>
        Assert.Null(ViewBindingCompatibility.KindOf(new("k", "1.0.0", null, shape, TemporalFormat: format)));

    [Theory(DisplayName = "views-auth-15: a direct store publish binding Title to a non-text field is refused at the commit, and no head appears")]
    [InlineData("priority")]
    [InlineData("score")]
    [InlineData("done")]
    [InlineData("due")]
    public async Task TitleOnANonTextFieldIsRefusedAtTheCommit(string field)
    {
        var shared = Store();
        await SaveDraft(shared, Roles((ViewShapeRole.Title, field)));

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await Publish(shared));

        Assert.Equal([new DefinitionRefusal(Incompatible, "/binding/shapeRoles/title")], refused.Refusals);
        Assert.Null(await shared.GetPublishedHeadAsync(Key));
    }

    [Theory(DisplayName = "views-auth-15: a direct store publish binding PlacedBy to a field neither date/time nor ordered is refused at the commit")]
    [InlineData("title")]
    [InlineData("done")]
    public async Task PlacedByOnANonTemporalUnorderedFieldIsRefusedAtTheCommit(string field)
    {
        var shared = Store();
        await SaveDraft(shared, Roles((ViewShapeRole.Title, "title"), (ViewShapeRole.PlacedBy, field)), Timeline);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await Publish(shared));

        Assert.Equal([new DefinitionRefusal(Incompatible, "/binding/shapeRoles/placedBy")], refused.Refusals);
        Assert.Null(await shared.GetPublishedHeadAsync(Key));
    }

    [Theory(DisplayName = "views-auth-15: PlacedBy on a date, date-time, time, integer or number field publishes at the commit")]
    [InlineData("due")]
    [InlineData("dueAt")]
    [InlineData("opensAt")]
    [InlineData("priority")]
    [InlineData("score")]
    public async Task PlacedByOnATemporalOrOrderedFieldPublishes(string field)
    {
        var shared = Store();
        await SaveDraft(shared, Roles((ViewShapeRole.Title, "title"), (ViewShapeRole.PlacedBy, field)), Timeline);

        await Publish(shared);

        Assert.Equal("1.0.0", (await shared.GetPublishedHeadAsync(Key))!.Document.VersionId);
    }

    [Theory(DisplayName = "views-auth-15: a shape-role field with no bound kind, or a kind the host has not admitted, is refused at the commit")]
    [InlineData(ViewShapeRole.Title, "notes", "/binding/shapeRoles/title")]
    [InlineData(ViewShapeRole.GroupedBy, "notes", "/binding/shapeRoles/groupedBy")]
    [InlineData(ViewShapeRole.Title, "orphan", "/binding/shapeRoles/title")]
    public async Task AShapeRoleFieldWithNoDeclaredKindIsRefused(ViewShapeRole role, string field, string target)
    {
        var shared = Store();
        var roles = role == ViewShapeRole.Title ? Roles((role, field)) : Roles((ViewShapeRole.Title, "title"), (role, field));
        await SaveDraft(shared, roles);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await Publish(shared));

        Assert.Equal([new DefinitionRefusal(Incompatible, target)], refused.Refusals);
        Assert.Null(await shared.GetPublishedHeadAsync(Key));
    }

    [Fact(DisplayName = "views-auth-15: temporal meaning is read from the exact kind revision the Record Type head pins")]
    public async Task TemporalMeaningComesFromThePinnedKindRevision()
    {
        var shared = Store();
        await SaveDraft(shared, Roles((ViewShapeRole.Title, "title"), (ViewShapeRole.PlacedBy, "stampedV1")), Timeline);
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await Publish(shared));

        var later = Draft(Roles((ViewShapeRole.Title, "title"), (ViewShapeRole.PlacedBy, "stampedV2")), Timeline, "1.0.1");
        await shared.SaveDraftAsync(Document(later), 1, "stamped-v2");
        await shared.PublishAsync(Key, "1.0.1", 2, "publish-v2");

        Assert.Equal([new DefinitionRefusal(Incompatible, "/binding/shapeRoles/placedBy")], refused.Refusals);
        Assert.Equal("1.0.1", (await shared.GetPublishedHeadAsync(Key))!.Document.VersionId);
    }

    [Fact(DisplayName = "views-auth-15: each commit reads the Record Type head current at that commit, as the head changes")]
    public async Task EachCommitReadsTheCurrentRecordTypeHead()
    {
        var shared = Store();
        var title = Roles((ViewShapeRole.Title, "title"));
        await shared.SaveDraftAsync(Document(Draft(title)), 0, "first");
        await shared.PublishAsync(Key, "1.0.0", 1, "publish-first");

        ViewStores.PublishRecordType(shared, Tenant, "timed-item", [("title", "test.integer", "1.0.0")], "2.0.0", 2);
        await shared.SaveDraftAsync(Document(Draft(title, version: "1.0.1")), 2, "second");
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await shared.PublishAsync(Key, "1.0.1", 3, "publish-second"));
        var headAfterRefusal = (await shared.GetPublishedHeadAsync(Key))!.Document.VersionId;

        ViewStores.PublishRecordType(shared, Tenant, "timed-item", [("title", "test.text", "1.0.0")], "3.0.0", 4);
        await shared.SaveDraftAsync(Document(Draft(title, version: "1.0.2")), 3, "third");
        await shared.PublishAsync(Key, "1.0.2", 4, "publish-third");

        Assert.Equal([new DefinitionRefusal(Incompatible, "/binding/shapeRoles/title")], refused.Refusals);
        Assert.Equal("1.0.0", headAfterRefusal);
        Assert.Equal("1.0.2", (await shared.GetPublishedHeadAsync(Key))!.Document.VersionId);
    }

    [Fact(DisplayName = "views-auth-4, views-auth-12: column treatment and density never change a compatibility result")]
    public async Task DisplaySettingsNeverChangeCompatibility()
    {
        var plain = Draft(Roles((ViewShapeRole.Title, "priority")));
        var dressed = plain with
        {
            Definition = plain.Definition with
            {
                Parameters = plain.Definition.Parameters with { Columns = [new("priority", 64, "date")] },
            },
            Binding = plain.Binding with { Density = ViewDensity.Compact },
        };

        var refusals = new List<IReadOnlyList<DefinitionRefusal>>();
        foreach (var draft in new[] { plain, dressed })
        {
            var shared = Store();
            await shared.SaveDraftAsync(Document(draft), 0, "draft");
            refusals.Add((await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await Publish(shared))).Refusals);
        }

        Assert.All(refusals, found => Assert.Equal([new DefinitionRefusal(Incompatible, "/binding/shapeRoles/title")], found));
    }

    private static readonly DefinitionKey Key = ViewDefinitionCatalogue.KeyOf(Tenant, "timed.queue");

    private static IVersionedDefinitionStore Store()
    {
        var registers = ViewStores.Registers(
        [
            new("layout.table", "hlp.ui.data-grid", [ViewShapeRole.Title]),
            new(Timeline, "hlp.ui.timeline", [ViewShapeRole.Title, ViewShapeRole.PlacedBy]),
        ]) with
        {
            FieldKinds = [.. ViewStores.FieldKinds, new("test.stamp", "1.0.0", null), new("test.stamp", "2.0.0", null, TemporalFormat: "date-time")],
        };
        var (_, shared) = ViewStores.Create(registers);
        ViewStores.PublishRecordType(shared, Tenant, "timed-item",
        [
            ("title", "test.text", "1.0.0"),
            ("priority", "test.integer", "1.0.0"),
            ("score", "test.number", "1.0.0"),
            ("done", "test.boolean", "1.0.0"),
            ("due", "test.date", "1.0.0"),
            ("dueAt", "test.date-time", "1.0.0"),
            ("opensAt", "test.time", "1.0.0"),
            ("notes", null, null),
            ("orphan", "test.not-admitted", "1.0.0"),
            ("stampedV1", "test.stamp", "1.0.0"),
            ("stampedV2", "test.stamp", "2.0.0"),
        ]);
        return shared;
    }

    private static Dictionary<ViewShapeRole, string> Roles(params (ViewShapeRole Role, string Field)[] roles) =>
        roles.ToDictionary(role => role.Role, role => role.Field);

    private static async Task SaveDraft(IVersionedDefinitionStore shared, Dictionary<ViewShapeRole, string> roles, string kind = "layout.table") =>
        await shared.SaveDraftAsync(Document(Draft(roles, kind)), 0, "draft");

    private static async Task Publish(IVersionedDefinitionStore shared) => await shared.PublishAsync(Key, "1.0.0", 1, "publish");

    private static DefinitionDocument Document(ViewDefinitionDraft draft) =>
        new(Key, draft.Definition.Version, draft.Definition.Version, ViewDefinitionDraftJson.Serialize(draft));

    private static ViewDefinitionDraft Draft(Dictionary<ViewShapeRole, string> roles, string kind = "layout.table", string version = "1.0.0") => new(
        new ViewDefinition(
            new("timed.queue", version, Tenant, ViewCascadeLayer.Tenant, JsonSerializer.SerializeToElement(new { source = "test" }), [], new(1, 0)),
            SchemaVersion: 1,
            Title: "Timed queue",
            RecordType: "timed-item",
            Ownership: ViewOwnershipTier.Public,
            OpenPermission: "work:read",
            Parameters: new([new("title", 240)], [], null, null, null)),
        new ViewBinding(kind, roles));
}
