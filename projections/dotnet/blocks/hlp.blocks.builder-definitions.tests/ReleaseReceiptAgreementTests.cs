using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-671's half of the seam between the two receipts. T-586's install receipt takes its artifact
/// identity from the seed the code produces; T-671's release receipt takes it from the artifact
/// blob the commit holds. They are the same rule over what must be the same bytes, and this file
/// is where "must be" is enforced from the code side.
///
/// The other direction is enforced in `tooling/tests/release-receipt.test.mjs`: an artifact whose
/// stamped digest is not true of its own bytes is refused naming `pack.seedDigest`. Between the
/// two, the artifact cannot drift from the seed without something refusing — a self-consistent
/// artifact that is not what the seed produces fails here, and an artifact edited to match a
/// receipt fails there. Both run in the gate, under `native-tests` and `release-receipt`.
/// </summary>
public sealed class ReleaseReceiptAgreementTests
{
    [Fact]
    public void The_two_receipts_read_the_same_artifact_identity_from_the_same_bytes()
    {
        var install = PublishedArtifact.FromExport(PlatformPackageSeed.Export());
        var release = PublishedArtifact.FromExport(PlatformPackageSeed.LoadCheckedInExport());

        Assert.Equal(install.Id, release.Id);
        Assert.Equal(install.Version, release.Version);
        Assert.Equal(install.Digest, release.Digest);
    }

    [Fact]
    public void The_checked_in_artifact_the_release_receipt_binds_is_the_seed_the_install_receipt_binds()
    {
        Assert.True(
            PlatformPackageSeed.VerifyCheckedInExport(),
            "The checked-in platform pack is not byte-identical to PlatformPackageSeed.Export. The release "
            + "receipt binds the checked-in artifact and the install receipt binds the seed, so while these "
            + "differ the two receipts name different digests for one release.");
    }

    [Fact]
    public void A_drifted_artifact_cannot_pass_as_the_seed_the_install_receipt_binds()
    {
        // A self-consistent artifact that is not what the seed produces: its own digest is true of
        // its bytes, so nothing downstream of the document can tell. Only comparing it against the
        // seed can, which is what the assertion above does on every gate run.
        var drifted = PlatformPackageExporter.Export(new PlatformPackageManifest(
            PlatformPackageSeed.Manifest.SchemaVersion,
            PlatformPackageSeed.Manifest.PackageKey,
            PlatformPackageSeed.Manifest.Revision,
            PlatformPackageSeed.Manifest.Items.Take(PlatformPackageSeed.Manifest.Items.Count - 1)));

        Assert.NotEqual(
            PublishedArtifact.FromExport(PlatformPackageSeed.Export()).Digest,
            PublishedArtifact.FromExport(drifted).Digest);
        Assert.False(PlatformPackageExporter.Verify(PlatformPackageSeed.Manifest, drifted));
    }
}
