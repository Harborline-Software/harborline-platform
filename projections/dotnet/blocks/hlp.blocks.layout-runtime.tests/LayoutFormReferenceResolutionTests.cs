using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.LayoutRuntime;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

/// <summary>
/// DES-0052 layout-ck-37: a published FormComponent reference resolves its exact immutable form
/// version and never the latest-published one.
/// </summary>
public sealed class LayoutFormReferenceResolutionTests
{
    private const string Tenant = "tenant-a";
    private static readonly DefinitionKey Form = new(Tenant, DefinitionKind.Forms, "form.customer");

    [Fact(DisplayName = "layout-ck-37: a pinned reference resolves its own version while a newer one is published")]
    public async Task A_pinned_reference_resolves_its_own_version_while_a_newer_one_is_published()
    {
        var store = await StoreWithTwoPublishedVersions();

        var resolved = await LayoutFormReferenceResolver.ResolveAsync(
            store, Tenant, new LayoutFormReference("form.customer", "form.customer@2.1.0"));

        Assert.Equal("form.customer@2.1.0", resolved?.Document.VersionId);
        Assert.Equal("form.customer@3.0.0", (await store.GetPublishedHeadAsync(Form))?.Document.VersionId);
    }

    [Theory(DisplayName = "layout-ck-37: a pin with no published version resolves nothing rather than the head")]
    [InlineData("form.customer@2.2.0")]
    [InlineData("form.customer@3.1.0-draft")]
    public async Task A_pin_with_no_published_version_resolves_nothing_rather_than_the_head(string versionId)
    {
        var store = await StoreWithTwoPublishedVersions();

        var resolved = await LayoutFormReferenceResolver.ResolveAsync(
            store, Tenant, new LayoutFormReference("form.customer", versionId));

        Assert.Null(resolved);
    }

    private static async Task<IVersionedDefinitionStore> StoreWithTwoPublishedVersions()
    {
        IVersionedDefinitionStore store = new InMemoryVersionedDefinitionStore(
            Enum.GetValues<DefinitionKind>().ToDictionary(kind => kind, _ => (DefinitionAdmission)((_, _) => [])));
        await store.SaveDraftAsync(new(Form, "form.customer@2.1.0", "2.1.0", "{}"), 0, "draft-2.1.0");
        await store.PublishAsync(Form, "form.customer@2.1.0", 1, "publish-2.1.0");
        await store.SaveDraftAsync(new(Form, "form.customer@3.0.0", "3.0.0", "{}"), 2, "draft-3.0.0");
        await store.PublishAsync(Form, "form.customer@3.0.0", 3, "publish-3.0.0");
        await store.SaveDraftAsync(new(Form, "form.customer@3.1.0-draft", "3.1.0-draft", "{}"), 4, "draft-3.1.0");
        return store;
    }
}
