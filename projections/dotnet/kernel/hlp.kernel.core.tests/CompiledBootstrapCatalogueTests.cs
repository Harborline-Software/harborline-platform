using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class CompiledBootstrapCatalogueTests
{
    [Fact]
    public async Task EmptyStoreResolvesExactlyThreeCompiledShapesBeforeFirstRead()
    {
        var reader = new RecordingReader();
        var catalogue = new CompiledBootstrapCatalogue(reader);

        Assert.Equal(
            [CompiledBootstrapCatalogue.DefinitionPackage, CompiledBootstrapCatalogue.RecordType, CompiledBootstrapCatalogue.Field],
            CompiledBootstrapCatalogue.Shapes.Select(row => row.Identity).ToArray());
        foreach (var shape in CompiledBootstrapCatalogue.Shapes)
            Assert.Equal(shape, await catalogue.ResolveAsync(shape.Identity));
        Assert.Equal(0, reader.Reads);
    }

    [Theory]
    [InlineData("kernel.definition-package")]
    [InlineData("kernel.record-type")]
    [InlineData("kernel.field")]
    public void PackageCannotReplaceCompiledFloor(string identity)
    {
        var error = Assert.Throws<CompiledShapeReplacementException>(() =>
            CompiledBootstrapCatalogue.RefusePackageReplacement([new(identity)]));
        Assert.Equal(KernelBootstrapErrors.CompiledShapeReplacement, error.Code);
        Assert.Equal(identity, error.Identity.Value);
    }

    private sealed class RecordingReader : IKernelCatalogueReader
    {
        public int Reads { get; private set; }
        public ValueTask<CompiledBootstrapShape?> ReadAsync(CompiledShapeIdentity identity, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult<CompiledBootstrapShape?>(null);
        }
    }
}
