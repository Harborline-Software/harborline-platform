using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-461. The propose, save and release half of the governed configuration loop, exercised through
/// the one Records-and-Forms example both runtime lanes complete:
/// <c>conformance/hlp.blocks.builder-definitions/proposal.json</c>.
/// </summary>
public sealed class ConfigurationProposalTests
{
    private const string RecordsEdit = """{"recordType":"invoice","fields":[{"name":"purchaseOrderNumber","kind":"identifier"}]}""";
    private const string FormsEdit = """{"formId":"invoice","sections":[{"id":"header","fields":["purchaseOrderNumber"]}]}""";
    private const string FormsEditWithSupplier = """{"formId":"invoice","sections":[{"id":"header","fields":["purchaseOrderNumber","supplier"]}]}""";
    private static readonly DateTimeOffset SavedAt = DateTimeOffset.Parse("2026-09-20T09:00:00Z", CultureInfo.InvariantCulture);

    private static ConfigurationReference Ref(string key, string revision, char fill) => new(key, revision, new string(fill, 64));

    /// <summary>The Records-and-Forms baseline: one finance package owning an invoice Record type and Form.</summary>
    private static ConfigurationGeneration Generation(string financeRevision = "1.0.0") => ConfigurationGeneration.Resolve(
        new ResolvedConfiguration("tenant-a", ["finance"],
            [new(Ref("finance", financeRevision, 'a'),
                [Ref("records/invoice", financeRevision, 'b'), Ref("forms/invoice", financeRevision, 'c')], [])],
            [new("records/invoice", "finance"), new("forms/invoice", "finance")],
            Ref("platform", "1.0.0", 'd'), []));

    private static ProposedChangeState Edited(ConfigurationGeneration baseline, string forms = FormsEdit)
    {
        var state = ConfigurationProposal.Start("proposal-1", baseline);
        state = ConfigurationProposal.Autosave(state, new("records/invoice", "finance", RecordsEdit));
        return ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", forms));
    }

    private static SavedVersion Save(ProposedChangeState state, int ordinal = 1, string rationale = "Capture the purchase order number on invoices.")
        => ConfigurationProposal.Save(state, ordinal, "dana.okafor", rationale, SavedAt);

    private static JsonElement Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "conformance"))) root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root!.FullName, "conformance/hlp.blocks.builder-definitions/proposal.json")));
        return document.RootElement.Clone();
    }

    private static JsonElement Case(string step) => Fixture().GetProperty("cases").EnumerateArray()
        .Single(item => item.GetProperty("step").GetString() == step).GetProperty("values");

    private static void AssertFixture(string step, IReadOnlyDictionary<string, string> bound)
    {
        var expected = Case(step);
        foreach (var field in expected.EnumerateObject())
            Assert.Equal(field.Value.GetString(), bound[field.Name]);
        Assert.Equal(expected.EnumerateObject().Count(), bound.Count);
    }

    // Acceptance 1: a proposed change records its baseline generation and never changes effective
    // behaviour while being edited.
    [Fact]
    public void A_proposed_change_records_its_baseline_and_editing_it_changes_no_effective_behaviour()
    {
        var baseline = Generation();
        var state = ConfigurationProposal.Start("proposal-1", baseline);
        Assert.Equal(baseline.Digest, state.BaselineDigest);
        Assert.Equal("tenant-a", state.TenantKey);
        Assert.Empty(state.Edits);

        var edited = Edited(baseline);
        var saved = Save(edited);
        // The baseline recorded at Start survives every edit and every checkpoint.
        Assert.Equal(baseline.Digest, edited.BaselineDigest);
        Assert.Equal(baseline.Digest, saved.BaselineDigest);
        // The generation the tenant is actually running is re-resolved from the same resolved
        // configuration and is byte-identical: nothing on the proposal path reached it.
        Assert.Equal(baseline.Digest, Generation().Digest);
        Assert.Equal(baseline.Digest, ConfigurationProposalDetail.Proposed(edited, Generation())["effectiveDigest"]);
        // There is no path from a proposed change to an effective pointer: the only type that can
        // become effective is a prepared candidate, and preparation takes a generation, not a proposal.
        Assert.DoesNotContain(typeof(ConfigurationProposal).GetMethods(),
            method => typeof(ConfigurationGeneration).IsAssignableFrom(method.ReturnType));
    }

    // Acceptance 2: autosave preserves work; Save version creates an immutable checkpoint with
    // authorship and rationale.
    [Fact]
    public void Autosave_preserves_work_and_save_version_freezes_an_authored_checkpoint()
    {
        var baseline = Generation();
        var state = ConfigurationProposal.Start("proposal-1", baseline);
        var first = ConfigurationProposal.Autosave(state, new("records/invoice", "finance", RecordsEdit));
        AssertFixture("autosaved-one-of-two", ConfigurationProposalDetail.Proposed(first, baseline));

        var both = ConfigurationProposal.Autosave(first, new("forms/invoice", "finance", FormsEdit));
        // Autosave preserves the earlier edit rather than replacing the working set.
        Assert.Equal(["forms/invoice", "records/invoice"], both.Edits.Select(edit => edit.DefinitionKey));
        Assert.Equal(RecordsEdit, both.Edits.Single(edit => edit.DefinitionKey == "records/invoice").BodyJson);
        // Re-autosaving the same definition replaces only that definition's body.
        var replaced = ConfigurationProposal.Autosave(both, new("forms/invoice", "finance", FormsEditWithSupplier));
        Assert.Equal(2, replaced.Edits.Count);
        Assert.Equal(FormsEditWithSupplier, replaced.Edits.Single(edit => edit.DefinitionKey == "forms/invoice").BodyJson);
        AssertFixture("proposed", ConfigurationProposalDetail.Proposed(both, baseline));

        var version = Save(both);
        Assert.Equal("dana.okafor", version.Author);
        Assert.Equal("Capture the purchase order number on invoices.", version.Rationale);
        Assert.Equal(SavedAt, version.SavedAt);
        Assert.Equal(1, version.Ordinal);
        // The checkpoint is immutable: its frozen edits cannot be written through, and a later edit
        // to the proposed change leaves the saved version's digest and bodies untouched.
        Assert.Throws<NotSupportedException>(() => ((IList<ProposedDefinitionEdit>)version.Edits)[0] = new("x", "y", "{}"));
        var after = ConfigurationProposal.Autosave(both, new("forms/invoice", "finance", FormsEditWithSupplier));
        Assert.Equal(FormsEdit, version.Edits.Single(edit => edit.DefinitionKey == "forms/invoice").BodyJson);
        Assert.NotEqual(version.Digest, ConfigurationProposal.WorkingDigest(after));
        // Authorship and rationale are required, never defaulted.
        Assert.Throws<ArgumentException>(() => ConfigurationProposal.Save(both, 1, " ", "reason", SavedAt));
        Assert.Throws<ArgumentException>(() => ConfigurationProposal.Save(both, 1, "dana.okafor", " ", SavedAt));
        Assert.Throws<ArgumentException>(() => Save(ConfigurationProposal.Start("proposal-1", baseline)));
    }

    // Acceptance 3: release exports the exact saved candidate, and any edit after a check
    // invalidates that check.
    [Fact]
    public void Release_exports_the_exact_saved_candidate_and_a_later_edit_invalidates_the_check()
    {
        var baseline = Generation();
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var release = ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.invoice-purchase-order", "1.1.0");
        Assert.Null(release.Refusal);
        var released = release.Released!;
        Assert.Equal(version.Digest, released.SavedVersionDigest);
        Assert.Equal(baseline.Digest, released.BaselineDigest);
        AssertFixture("released", ConfigurationProposalDetail.Bind(state, baseline, version, check, release));

        // The exported document is the provider-neutral closure, manifest and digest, and it carries
        // the baseline, the saved version, its authorship and its rationale in its own package record.
        using var document = JsonDocument.Parse(released.Document);
        var root = document.RootElement;
        Assert.Equal("tenant-a.invoice-purchase-order", root.GetProperty("packageKey").GetString());
        Assert.Empty(root.GetProperty("closure").GetProperty("dependencies").EnumerateArray());
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        var record = items[0].GetProperty("content").GetProperty("payload");
        Assert.Equal(baseline.Digest, record.GetProperty("baselineDigest").GetString());
        Assert.Equal(version.Digest, record.GetProperty("savedVersionDigest").GetString());
        Assert.Equal("dana.okafor", record.GetProperty("author").GetString());
        Assert.Equal("Capture the purchase order number on invoices.", record.GetProperty("rationale").GetString());
        Assert.Equal("receipt-1", record.GetProperty("checkReceiptId").GetString());
        Assert.Equal(new[] { "forms/invoice", "records/invoice" },
            items.Skip(1).Select(item => item.GetProperty("content").GetProperty("payload")
                .GetProperty("definitionKey").GetString()!).Order(StringComparer.Ordinal));

        // One more edit after the check, and the same release refuses by name rather than signing
        // something the check never saw.
        var edited = ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", FormsEditWithSupplier));
        Assert.False(ConfigurationProposal.IsCurrent(check, edited));
        var refused = ConfigurationProposal.Release(edited, version, check, baseline, "tenant-a.invoice-purchase-order", "1.1.0");
        Assert.Null(refused.Released);
        Assert.Equal("configuration-check-invalidated", refused.Refusal!.Code);
        AssertFixture("check-invalidated-by-a-later-edit",
            ConfigurationProposalDetail.Bind(edited, baseline, version, check, refused));

        // A current check for a later saved version does not license releasing the earlier one.
        var second = ConfigurationProposal.Save(edited, 2, "dana.okafor", "Also show the supplier.",
            DateTimeOffset.Parse("2026-09-20T09:30:00Z", CultureInfo.InvariantCulture));
        var secondCheck = new ProposedChangeCheck("proposal-1", second.Digest, "receipt-2");
        var superseded = ConfigurationProposal.Release(edited, version, secondCheck, baseline, "tenant-a.invoice-purchase-order", "1.1.0");
        Assert.Null(superseded.Released);
        Assert.Equal("configuration-check-invalidated", superseded.Refusal!.Code);
        AssertFixture("released-a-superseded-saved-version",
            ConfigurationProposalDetail.Bind(edited, baseline, version, secondCheck, superseded));
    }

    // Acceptance 3, second half: releasing against a baseline that moved refuses rather than
    // releasing a candidate nobody reviewed against the generation now effective.
    [Fact]
    public void Release_refuses_when_the_baseline_generation_moved_under_the_author()
    {
        var baseline = Generation();
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var moved = Generation("1.0.1");
        Assert.NotEqual(baseline.Digest, moved.Digest);
        var refused = ConfigurationProposal.Release(state, version, check, moved, "tenant-a.invoice-purchase-order", "1.1.0");
        Assert.Null(refused.Released);
        Assert.Equal("configuration-baseline-stale", refused.Refusal!.Code);
        Assert.Contains(baseline.Digest, refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains(moved.Digest, refused.Refusal.Message, StringComparison.Ordinal);
        AssertFixture("released-against-a-stale-baseline",
            ConfigurationProposalDetail.Bind(state, moved, version, check, refused));
    }

    // Acceptance 5: the released digest shown to the author is the digest of the exported artifact,
    // so it cannot drift from the bytes an activation is later offered.
    [Fact]
    public void The_released_digest_shown_to_the_author_is_the_digest_of_the_exported_artifact()
    {
        var baseline = Generation();
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var released = ConfigurationProposal.Release(state, version, check, baseline,
            "tenant-a.invoice-purchase-order", "1.1.0").Released!;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(released.Document.Span)), released.Digest);
        Assert.Equal(Fixture().GetProperty("releasedPackageDigest").GetString(), released.Digest);
        // The digest the surface shows the author is that same artifact digest, not a stored label.
        Assert.Contains(released.Digest, ConfigurationProposalDetail.Bind(state, baseline, version, check,
            ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.invoice-purchase-order", "1.1.0"))
            ["releasedPackage"], StringComparison.Ordinal);
        // Releasing the same saved version twice exports byte-identical bytes, so the digest the
        // author was shown identifies exactly one artifact.
        var again = ConfigurationProposal.Release(state, version, check, baseline,
            "tenant-a.invoice-purchase-order", "1.1.0").Released!;
        Assert.True(released.Document.Span.SequenceEqual(again.Document.Span));
        Assert.Equal(released.Digest, again.Digest);
        // A different saved version exports a different artifact and therefore a different digest.
        var other = ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", FormsEditWithSupplier));
        var otherVersion = ConfigurationProposal.Save(other, 2, "dana.okafor", "Also show the supplier.", SavedAt);
        var otherReleased = ConfigurationProposal.Release(other, otherVersion,
            new("proposal-1", otherVersion.Digest, "receipt-2"), baseline, "tenant-a.invoice-purchase-order", "1.1.0").Released!;
        Assert.NotEqual(released.Digest, otherReleased.Digest);
    }

    // Acceptance 6: the released vocabulary is Proposed change, Saved version and Released package,
    // and it ships in the exported platform pack rather than being authored by a surface.
    [Fact]
    public void The_released_vocabulary_is_domain_facing_and_ships_in_the_platform_pack()
    {
        Assert.Equal("Proposed change", ConfigurationProposalDetail.Label("proposed"));
        Assert.Equal("Saved version", ConfigurationProposalDetail.Label("saved"));
        Assert.Equal("Released package", ConfigurationProposalDetail.Label("released"));

        using var pack = JsonDocument.Parse(PlatformPackageSeed.LoadCheckedInExport());
        var views = pack.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "platform-package-ck-7")
            .GetProperty("content").GetProperty("payload");
        Assert.Equal(JsonSerializer.Serialize(ConfigurationProposalDetail.Statuses),
            JsonSerializer.Serialize(views.GetProperty("configurationProposalStatuses")));
        var definition = views.GetProperty("configurationProposalDetail");
        Assert.Equal("platform.detail.configuration-proposal", definition.GetProperty("formId").GetString());
        Assert.Equal("Proposed change", definition.GetProperty("title").GetProperty("values").GetProperty("en").GetString());
        var labels = definition.GetProperty("sections").EnumerateArray().Single().GetProperty("fields").EnumerateArray()
            .ToDictionary(field => field.GetProperty("name").GetString()!,
                field => field.GetProperty("label").GetProperty("values").GetProperty("en").GetString()!);
        Assert.Equal("Proposed change", labels["proposalId"]);
        Assert.Equal("Saved version", labels["savedVersion"]);
        Assert.Equal("Released package", labels["releasedPackage"]);
        Assert.True(PlatformPackageSeed.VerifyCheckedInExport());

        // The bound values carry the same vocabulary, so a surface that renders the released Form
        // over the released bindings shows domain language without authoring any of it.
        var baseline = Generation();
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        Assert.Equal("Proposed change", ConfigurationProposalDetail.Proposed(state, baseline)["status"]);
        AssertFixture("saved", ConfigurationProposalDetail.Saved(state, baseline, version, check));
        Assert.Equal("Released package", ConfigurationProposalDetail.Bind(state, baseline, version, check,
            ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.invoice-purchase-order", "1.1.0"))["status"]);
    }

    [Theory]
    [InlineData("", "finance", "{}")]
    [InlineData("records/invoice", "", "{}")]
    [InlineData("records/invoice", "finance", "not json")]
    public void An_incomplete_or_unparseable_edit_refuses_rather_than_being_repaired(string key, string package, string body)
    {
        var state = ConfigurationProposal.Start("proposal-1", Generation());
        Assert.Throws<ArgumentException>(() => ConfigurationProposal.Autosave(state, new(key, package, body)));
    }

    [Fact]
    public void An_unchecked_proposed_change_cannot_be_released()
    {
        var baseline = Generation();
        var state = Edited(baseline);
        var refused = ConfigurationProposal.Release(state, Save(state), null, baseline, "p", "1.0.0");
        Assert.Null(refused.Released);
        Assert.Equal("configuration-check-required", refused.Refusal!.Code);
        Assert.Equal("No check recorded.",
            ConfigurationProposalDetail.Bind(state, baseline, Save(state), null, refused)["checkState"]);
    }

    [Fact]
    public void A_proposed_change_for_another_tenant_or_another_proposal_cannot_be_released()
    {
        var baseline = Generation();
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var elsewhere = ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-b", ["finance"],
            [new(Ref("finance", "1.0.0", 'a'), [Ref("records/invoice", "1.0.0", 'b')], [])],
            [new("records/invoice", "finance")], Ref("platform", "1.0.0", 'd'), []));
        Assert.Equal("configuration-tenant-mismatch",
            ConfigurationProposal.Release(state, version, check, elsewhere, "p", "1.0.0").Refusal!.Code);
        Assert.Equal("configuration-release-proposal-mismatch",
            ConfigurationProposal.Release(state, version with { ProposalId = "proposal-2" }, check, baseline, "p", "1.0.0").Refusal!.Code);
    }
}
