using System.Text;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// Every receipt in this file is written by an install. None is authored, and none is read from a
/// checked-in document, which is the property the last two tests exist to keep true.
/// </summary>
public sealed class InstallReceiptTests
{
    [Fact]
    public async Task An_install_on_a_clean_node_emits_a_receipt_carrying_every_field()
    {
        var (node, receipt) = await InstallAsync();

        var (parsed, refusal) = InstallReceiptDocument.Parse(receipt);

        Assert.Null(refusal);
        Assert.NotNull(parsed);
        Assert.Equal("harborline.platform", parsed.Artifact.Id);
        Assert.Equal(Published().Digest, parsed.Artifact.Digest);
        var installed = Assert.Single(parsed.Installed);
        Assert.Equal("harborline.platform", installed.Id);
        Assert.Equal("1.0.0", installed.Version);
        Assert.Equal(node.Witness, installed.Witness);
        Assert.Equal(["audit:read", "catalogue:read", "records:read"], parsed.Capabilities.Select(capability => capability.Name));
        Assert.All(parsed.Capabilities, capability => Assert.False(capability.ExpectedAllowed));
        Assert.Equal(
            ["platform.operational-type.audit-entry", "platform.operational-type.notification",
             "platform.operational-type.observation", "platform.operational-type.work-item"],
            parsed.SealedOperationalTypes);
        Assert.Equal(["platform.trait.bookable-resource", "platform.trait.schedulable"], parsed.SealedTraits);
        Assert.Equal(PlatformPackageSeed.Manifest.Items.Count, parsed.Records.Count);
        Assert.All(parsed.Records, record => Assert.Equal(64, record.Digest.Length));
    }

    [Fact]
    public async Task The_checker_accepts_the_receipt_the_real_install_wrote()
    {
        var (node, receipt) = await InstallAsync();

        var check = await InstallReceiptChecker.CheckAsync(receipt, Published(), node, Corpus());

        Assert.True(check.Accepted, $"{check.RefusalCode} {check.Field}");
    }

    [Fact]
    public async Task A_receipt_whose_installed_digest_differs_from_the_published_digest_is_refused_naming_the_field()
    {
        // The node resolved something other than what was published. The receipt reports what
        // resolved, so the two digests part company and the checker says which field parted.
        var (node, receipt) = await InstallAsync(Tampered(), Published());

        var check = await InstallReceiptChecker.CheckAsync(receipt, Published(), node, Corpus());

        Assert.False(check.Accepted);
        Assert.Equal("install-receipt-installed-digest-mismatch", check.RefusalCode);
        Assert.Equal("installed[harborline.platform].digest", check.Field);
    }

    [Fact]
    public async Task The_installed_digest_is_read_back_from_the_installed_state_and_not_echoed_from_the_request()
    {
        var (_, published) = await InstallAsync();
        var (_, drifted) = await InstallAsync(Tampered(), Published());

        var installedDigest = Digest(published);

        // An echo would report the published digest whatever the node holds. These differ only
        // because the value is recomputed from the items the node committed.
        Assert.Equal(Published().Digest, installedDigest);
        Assert.NotEqual(installedDigest, Digest(drifted));
    }

    [Fact]
    public async Task A_receipt_whose_authorization_outcome_differs_from_the_gate_is_refused_naming_the_capability()
    {
        // Same installed declarations, a node whose gate lets catalogue:read through anyway. That is
        // the installer power ck-10 forbids, and it is the receipt's job to name it.
        var node = new InstalledPlatformCatalogue(capability => capability == "catalogue:read");
        Assert.True((await PlatformPackageReplayer.ReplayAsync(PlatformPackageSeed.Manifest, node)).Succeeded);
        var receipt = await InstallReceiptWriter.WriteAsync(Published(), node);

        var check = await InstallReceiptChecker.CheckAsync(receipt, Published(), node, Corpus());

        Assert.False(check.Accepted);
        Assert.Equal("install-receipt-authorization-mismatch", check.RefusalCode);
        Assert.Equal("catalogue:read", check.Field);
    }

    [Fact]
    public async Task A_hand_written_receipt_is_refused()
    {
        var (node, real) = await InstallAsync();
        var (parsed, _) = InstallReceiptDocument.Parse(real);

        // Every value copied from the real receipt except the one value only an install mints.
        var authored = InstallReceiptDocument.Export(parsed! with
        {
            Installed = [.. parsed.Installed.Select(package => package with { Witness = new string('0', 32) })],
        });
        var check = await InstallReceiptChecker.CheckAsync(authored, Published(), node, Corpus());

        Assert.False(check.Accepted);
        Assert.Equal("install-receipt-not-written-by-install", check.RefusalCode);
        Assert.Equal("installed[harborline.platform].witness", check.Field);
    }

    [Fact]
    public async Task A_receipt_kept_as_a_fixture_is_refused_against_the_next_install()
    {
        var (_, kept) = await InstallAsync();
        var (fresh, _) = await InstallAsync();

        // A receipt checked into a repository is exactly this: a real receipt from an install that
        // is not the one being checked. The witness is what makes the next run refuse it.
        var check = await InstallReceiptChecker.CheckAsync(kept, Published(), fresh, Corpus());

        Assert.False(check.Accepted);
        Assert.Equal("install-receipt-not-written-by-install", check.RefusalCode);
    }

    [Fact]
    public async Task An_edited_receipt_is_refused_on_its_own_digest()
    {
        var (node, receipt) = await InstallAsync();
        var edited = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(receipt).Replace("\"records:read\"", "\"records:write\"", StringComparison.Ordinal));

        var check = await InstallReceiptChecker.CheckAsync(edited, Published(), node, Corpus());

        Assert.False(check.Accepted);
        Assert.Equal("install-receipt-digest-mismatch", check.RefusalCode);
    }

    [Fact]
    public void The_design_record_rows_enumerate_the_sealed_types_and_traits_the_receipt_reports()
    {
        var corpus = Corpus();

        Assert.Equal(["work-item", "notification", "observation", "audit-entry"], corpus.OperationalTypes);
        // ADR 0097 decision 2 keeps Inspectable out of the seed, and ck-4 says so itself.
        Assert.Equal(["schedulable", "bookable-resource"], corpus.Traits);
    }

    [Theory]
    [InlineData("platform-package-ck-3", "platform.operational-type.notification", "install-receipt-sealed-type-not-reported", "sealedOperationalTypes[notification]")]
    [InlineData("platform-package-ck-4", "platform.trait.schedulable", "install-receipt-sealed-trait-not-reported", "sealedTraits[schedulable]")]
    public async Task A_sealed_member_the_record_enumerates_and_the_install_does_not_report_is_refused_naming_the_id(
        string itemId, string memberId, string code, string field)
    {
        var shipped = Without(itemId, memberId);
        var (node, receipt) = await InstallAsync(shipped);

        var check = await InstallReceiptChecker.CheckAsync(receipt, PublishedOf(shipped), node, Corpus());

        Assert.False(check.Accepted);
        Assert.Equal(code, check.RefusalCode);
        Assert.Equal(field, check.Field);
    }

    [Fact]
    public async Task A_sealed_member_the_install_reports_and_the_record_does_not_enumerate_is_refused_naming_the_id()
    {
        var shipped = With("platform-package-ck-4", "platform.trait.inspectable");
        var (node, receipt) = await InstallAsync(shipped);

        var check = await InstallReceiptChecker.CheckAsync(receipt, PublishedOf(shipped), node, Corpus());

        Assert.False(check.Accepted);
        Assert.Equal("install-receipt-sealed-trait-not-in-record", check.RefusalCode);
        Assert.Equal("sealedTraits[platform.trait.inspectable]", check.Field);
    }

    private static async Task<(InstalledPlatformCatalogue Node, byte[] Receipt)> InstallAsync(
        PlatformPackageManifest? manifest = null, PublishedArtifact? published = null)
    {
        var installing = manifest ?? PlatformPackageSeed.Manifest;
        var node = new InstalledPlatformCatalogue();
        var replay = await PlatformPackageReplayer.ReplayAsync(installing, node);
        Assert.True(replay.Succeeded, $"{replay.RefusalCode} {replay.ItemId}");
        return (node, await InstallReceiptWriter.WriteAsync(published ?? PublishedOf(installing), node));
    }

    private static PublishedArtifact Published() => PublishedArtifact.FromExport(PlatformPackageSeed.Export());

    private static PublishedArtifact PublishedOf(PlatformPackageManifest manifest)
        => PublishedArtifact.FromExport(PlatformPackageExporter.Export(manifest));

    private static string Digest(byte[] receipt)
    {
        var (parsed, refusal) = InstallReceiptDocument.Parse(receipt);
        Assert.Null(refusal);
        return parsed!.Installed.Single().Digest;
    }

    /// <summary>
    /// The DES-0007 rows as the control checkout holds them. This is deliberately not a fixture: the
    /// probe exists to catch the records drifting away from what the platform ships.
    /// </summary>
    private static SealedTypeCorpus Corpus()
    {
        var control = Environment.GetEnvironmentVariable("HARBORLINE_CONTROL_REPO");
        Assert.False(string.IsNullOrWhiteSpace(control),
            "HARBORLINE_CONTROL_REPO must name a harborline-control checkout; the sealed-type probe reads DES-0007 from it.");
        return SealedTypeCorpus.ReadControlRepository(control!);
    }

    private static PlatformPackageManifest Tampered()
        => Replace("platform-package-ck-14", """{"theme":"light"}""");

    private static PlatformPackageManifest Without(string itemId, string memberId)
        => Replace(itemId, Members(itemId, members => members.Where(member => member != memberId)));

    private static PlatformPackageManifest With(string itemId, string memberId)
        => Replace(itemId, Members(itemId, members => members.Append(memberId)));

    private static string Members(string itemId, Func<IEnumerable<string>, IEnumerable<string>> change)
    {
        using var payload = JsonDocument.Parse(Item(itemId).Content.Payload);
        var ids = payload.RootElement.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("id").GetString()!);
        return $$"""{"members":[{{string.Join(',', change(ids).Select(id => $$"""{"id":{{JsonSerializer.Serialize(id)}}}"""))}}]}""";
    }

    private static PlatformPackageItem Item(string itemId)
        => PlatformPackageSeed.Manifest.Items.Single(item => item.Id == itemId);

    private static PlatformPackageManifest Replace(string itemId, string payload)
    {
        var source = PlatformPackageSeed.Manifest;
        return new(source.SchemaVersion, source.PackageKey, source.Revision, source.Items.Select(item => item.Id == itemId
            ? new PlatformPackageItem(item.Id, item.Stage, item.Dependencies, PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(payload)))
            : item));
    }
}
