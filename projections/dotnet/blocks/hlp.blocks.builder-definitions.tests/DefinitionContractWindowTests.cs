using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class DefinitionContractWindowTests
{
    [Fact]
    public void ContractWindow_reads_the_ck_1_contract_from_the_checked_in_export()
    {
        using var document = JsonDocument.Parse(PlatformPackageSeed.LoadCheckedInExport());
        var contract = document.RootElement.GetProperty("items")[0]
            .GetProperty("content").GetProperty("payload").GetProperty("contract");

        Assert.Equal(contract.GetProperty("major").GetInt32(), PlatformPackageSeed.ContractWindow.Major);
        Assert.Equal(contract.GetProperty("minor").GetInt32(), PlatformPackageSeed.ContractWindow.Minor);
        Assert.Equal(contract.GetProperty("window")[0].GetInt32(), PlatformPackageSeed.ContractWindow.OldestMajor);
    }

    [Theory]
    [MemberData(nameof(MalformedContracts))]
    public void ContractWindow_refuses_malformed_ck_1_contracts(object contract)
    {
        var first = PlatformPackageSeed.Manifest.Items[0];
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = "harborline.platform",
            provenance = new { kind = "platform" },
            version = "1.0.0",
            contract,
        });
        var replacement = new PlatformPackageItem(
            first.Id,
            first.Stage,
            first.Dependencies,
            PlatformPackageContent.PresentJson(payload));
        var manifest = new PlatformPackageManifest(
            PlatformPackageSeed.Manifest.SchemaVersion,
            PlatformPackageSeed.Manifest.PackageKey,
            PlatformPackageSeed.Manifest.Revision,
            [replacement, .. PlatformPackageSeed.Manifest.Items.Skip(1)]);

        Assert.Throws<InvalidDataException>(() => PlatformPackageSeed.ReadContractWindow(PlatformPackageExporter.Export(manifest)));
    }

    public static TheoryData<object> MalformedContracts => new()
    {
        { new { major = 1, minor = 0, window = new[] { 1, 2 } } },
        { new { major = 1, minor = 0, window = new[] { 2, 1 } } },
        { new { major = 1, minor = 0, window = new[] { 0, 1 } } },
        { new { major = -1, minor = 0, window = new[] { -1, -1 } } },
        { new { major = 1, minor = -1, window = new[] { 1, 1 } } },
        { new { major = 1, minor = 0 } },
    };
}
