using Harborline.Blocks.EntityViews;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewKindRegistryTests
{
    [Fact]
    public async Task Platform_registry_exposes_the_canonical_table_kind()
    {
        var kind = await ViewKindRegistry.Platform.ResolveAsync(ViewKindIds.Table);

        Assert.NotNull(kind);
        Assert.Equal(ViewKindIds.Table, kind.Kind);
        Assert.Equal("hlp.ui.data-grid", kind.Renderer);
        Assert.Equal([ViewShapeRole.Title], kind.RequiredRoles);
        Assert.Equal([ViewKindIds.Table], (await ViewKindRegistry.Platform.ListAsync()).Select(candidate => candidate.Kind));
    }

    [Fact]
    public async Task Platform_registry_does_not_admit_unknown_kinds()
    {
        Assert.Null(await ViewKindRegistry.Platform.ResolveAsync("layout.unknown"));
    }

    [Fact]
    public void Registry_refuses_duplicate_kind_identities()
    {
        var descriptor = new ViewKindDescriptor(ViewKindIds.Table, "hlp.ui.data-grid", [ViewShapeRole.Title]);

        Assert.Throws<ArgumentException>(() => new ViewKindRegistry([descriptor, descriptor]));
    }

    [Fact]
    public async Task Registry_copies_the_supplied_role_contract()
    {
        ViewShapeRole[] roles = [ViewShapeRole.Title];
        var registry = new ViewKindRegistry([new(ViewKindIds.Table, "hlp.ui.data-grid", roles)]);

        roles[0] = ViewShapeRole.PlacedBy;

        Assert.Equal(
            [ViewShapeRole.Title],
            (await registry.ResolveAsync(ViewKindIds.Table))!.RequiredRoles);
    }
}
