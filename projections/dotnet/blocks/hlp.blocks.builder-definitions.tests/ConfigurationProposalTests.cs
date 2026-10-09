using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Harborline.Kernel.Core;
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
    // T-667: the producer states the content kind. These are the transport's names, supplied by the
    // author of the edit; this block neither enumerates them nor derives them from a definition key.
    private const string RecordsKind = "AssetTypeDefinition";
    private const string FormsKind = "FormDefinition";
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
        state = ConfigurationProposal.Autosave(state, new("records/invoice", "finance", RecordsEdit, RecordsKind));
        return ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", forms, FormsKind));
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
        var first = ConfigurationProposal.Autosave(state, new("records/invoice", "finance", RecordsEdit, RecordsKind));
        AssertFixture("autosaved-one-of-two", ConfigurationProposalDetail.Proposed(first, baseline));

        var both = ConfigurationProposal.Autosave(first, new("forms/invoice", "finance", FormsEdit, FormsKind));
        // Autosave preserves the earlier edit rather than replacing the working set.
        Assert.Equal(["forms/invoice", "records/invoice"], both.Edits.Select(edit => edit.DefinitionKey));
        Assert.Equal(RecordsEdit, both.Edits.Single(edit => edit.DefinitionKey == "records/invoice").BodyJson);
        // Re-autosaving the same definition replaces only that definition's body.
        var replaced = ConfigurationProposal.Autosave(both, new("forms/invoice", "finance", FormsEditWithSupplier, FormsKind));
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
        Assert.Throws<NotSupportedException>(() => ((IList<ProposedDefinitionEdit>)version.Edits)[0] = new("x", "y", "{}", FormsKind));
        var after = ConfigurationProposal.Autosave(both, new("forms/invoice", "finance", FormsEditWithSupplier, FormsKind));
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
        Assert.Equal(["finance@1.0.0"], Closure(released.Document));
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
        var edited = ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", FormsEditWithSupplier, FormsKind));
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
        var other = ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", FormsEditWithSupplier, FormsKind));
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
    [InlineData("", "finance", "{}", FormsKind)]
    [InlineData("records/invoice", "", "{}", FormsKind)]
    [InlineData("records/invoice", "finance", "not json", FormsKind)]
    // T-667: an edit that states no content kind is as incomplete as one that states no package. The
    // producer refuses it here rather than exporting a package whose items no consumer can classify.
    [InlineData("records/invoice", "finance", "{}", "")]
    public void An_incomplete_or_unparseable_edit_refuses_rather_than_being_repaired(string key, string package, string body, string kind)
    {
        var state = ConfigurationProposal.Start("proposal-1", Generation());
        Assert.Throws<ArgumentException>(() => ConfigurationProposal.Autosave(state, new(key, package, body, kind)));
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

    // T-667: the released document carries the content kind the producer stated, item by item, so the
    // consumer that turns this document into an installable artifact reads a kind rather than inferring
    // one. The kind is also part of the working digest, so restating a definition under a different kind
    // is an edit like any other and invalidates a check taken before it.
    [Fact]
    public void The_released_document_carries_the_content_kind_the_producer_stated()
    {
        var baseline = Generation();
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var released = ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.invoice-purchase-order", "1.1.0");
        Assert.Null(released.Refusal);

        using var document = JsonDocument.Parse(released.Released!.Document.ToArray());
        var kinds = document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("content"))
            .Where(content => content.GetProperty("classification").GetString() == "present")
            .Select(content => content.GetProperty("payload"))
            .Where(payload => payload.TryGetProperty("definitionKey", out _))
            .ToDictionary(payload => payload.GetProperty("definitionKey").GetString()!,
                payload => payload.GetProperty("contentKind").GetString()!);
        Assert.Equal(RecordsKind, kinds["records/invoice"]);
        Assert.Equal(FormsKind, kinds["forms/invoice"]);

        // Restating the same body under another kind is a different working state, so a check taken
        // against the first cannot be carried over to the second.
        var restated = ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", FormsEdit, "ViewDefinition"));
        Assert.NotEqual(ConfigurationProposal.WorkingDigest(state), ConfigurationProposal.WorkingDigest(restated));
        Assert.False(ConfigurationProposal.IsCurrent(check, restated));
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

    // ck-2 S7 (DES-0029 ck-2, D1/D2): a released package carries its real package closure. Every
    // package its edits reference, the baseline owner of each edited definition and the package each
    // edit names, is a dependency pinned at the revision the baseline generation resolved, never an
    // empty closure and never a version from anywhere but that baseline.
    [Fact]
    public void A_released_package_carries_its_dependency_closure()
    {
        var baseline = Generation("1.2.0");
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var released = ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.invoice-purchase-order", "1.1.0").Released!;
        Assert.Equal(["finance@1.2.0"], Closure(released.Document));
    }

    // The closure the exporter writes is the one the kernel resolves: read back from the released
    // bytes alone, it resolves platform first, then the pinned dependency, then the released package,
    // and it refuses by the kernel's own codes when the dependency is absent or active below the pin.
    [Fact]
    public void The_released_closure_resolves_under_the_kernel_package_closure()
    {
        var baseline = Generation("1.2.0");
        var state = Edited(baseline);
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var released = ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.invoice-purchase-order", "1.1.0").Released!;
        var package = AsKernelManifest(released.Document);
        KernelPackageManifest Platform() => new(KernelPackageClosure.PlatformPackageKey, "1.0.0", []);
        KernelPackageManifest Finance(string at) => new("finance", at, []);
        IReadOnlyDictionary<string, string> Active(params KernelPackageManifest[] packages) =>
            packages.ToDictionary(item => item.Key, item => item.Version, StringComparer.Ordinal);

        var resolved = KernelPackageClosure.Resolve([package.Key], [package, Finance("1.2.0"), Platform()],
            Active(package, Finance("1.2.0"), Platform()));
        Assert.Equal([KernelPackageClosure.PlatformPackageKey, "finance", "tenant-a.invoice-purchase-order"],
            resolved.Packages.Select(item => item.Key));

        var missing = Assert.Throws<KernelClosureRefusalException>(() =>
            KernelPackageClosure.Resolve([package.Key], [package, Platform()], Active(package, Platform())));
        Assert.Equal(KernelClosureErrors.DependencyMissing, missing.Code);
        Assert.Equal(["tenant-a.invoice-purchase-order", "finance"], missing.Path);

        var below = Assert.Throws<KernelClosureRefusalException>(() => KernelPackageClosure.Resolve([package.Key],
            [package, Finance("1.1.9"), Platform()], Active(package, Finance("1.1.9"), Platform())));
        Assert.Equal(KernelClosureErrors.DependencyBelowPin, below.Code);
    }

    // A package the release edits into, or whose definition it edits, is a dependency; the released
    // package itself is not, so an edit that the release owns outright adds nothing to the closure.
    [Fact]
    public void The_closure_names_each_referenced_package_once_and_never_the_released_package()
    {
        var baseline = ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-a", ["finance", "payroll"],
            [new(Ref("finance", "1.2.0", 'a'), [Ref("records/invoice", "1.2.0", 'b'), Ref("forms/invoice", "1.2.0", 'c')], []),
             new(Ref("payroll", "3.0.0", 'e'), [Ref("records/payslip", "3.0.0", 'f')], [])],
            [new("records/invoice", "finance"), new("forms/invoice", "finance"), new("records/payslip", "payroll")],
            Ref("platform", "1.0.0", 'd'), []));
        var state = ConfigurationProposal.Start("proposal-1", baseline);
        // Edits a payroll-owned definition while naming finance as its owner: both are referenced.
        state = ConfigurationProposal.Autosave(state, new("records/payslip", "finance", RecordsEdit, RecordsKind));
        state = ConfigurationProposal.Autosave(state, new("forms/invoice", "finance", FormsEdit, FormsKind));
        // A new definition the released package owns outright references no other package.
        state = ConfigurationProposal.Autosave(state, new("forms/timesheet", "tenant-a.release", FormsEdit, FormsKind));
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var released = ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.release", "1.0.0").Released!;
        Assert.Equal(["finance@1.2.0", "payroll@3.0.0"], Closure(released.Document));
    }

    // A referenced package the baseline generation does not resolve has no pinned version to carry,
    // so the release refuses by name rather than exporting a closure with the dependency dropped.
    [Fact]
    public void A_release_that_references_a_package_outside_the_baseline_refuses_by_name()
    {
        var baseline = Generation();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("records/payslip", "payroll", RecordsEdit, RecordsKind));
        var version = Save(state);
        var check = new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1");
        var refused = ConfigurationProposal.Release(state, version, check, baseline, "tenant-a.release", "1.0.0");
        Assert.Null(refused.Released);
        Assert.Equal("configuration-release-dependency-unpinned", refused.Refusal!.Code);
        Assert.Equal("payroll", refused.Refusal.Target);
        Assert.Contains("payroll", refused.Refusal.Message, StringComparison.Ordinal);
    }

    // records-ck-41 (T-615 slice 11): a definition's envelope requires entry spelled pack-key@interfaceVersion
    // (ADR-0006) is a package the release depends on, so it joins the closure at the baseline's pin, the same
    // pin an edit naming that package would carry.
    [Fact]
    public void A_definition_that_requires_another_package_adds_it_to_the_closure_at_its_baseline_pin()
    {
        var baseline = TwoPackageBaseline();
        var released = Released(baseline, EditedWith(baseline, Enveloped("""{"requires":[{"capability":"payroll@2"}]}""")));
        Assert.Null(released.Refusal);
        Assert.Equal(["finance@1.2.0", "payroll@3.0.0"], Closure(released.Released!.Document));
    }

    // A required package the baseline does not resolve has no pin to carry, so the release refuses by its name,
    // the code an edit naming an unresolved package already refuses with.
    [Fact]
    public void A_required_package_outside_the_baseline_refuses_by_name()
    {
        var baseline = TwoPackageBaseline();
        var refused = Released(baseline, EditedWith(baseline, Enveloped("""{"requires":[{"capability":"eam-core@1"}]}""")));
        Assert.Null(refused.Released);
        Assert.Equal("configuration-release-dependency-unpinned", refused.Refusal!.Code);
        Assert.Equal("eam-core", refused.Refusal.Target);
    }

    // In a Records object requirement, only pack-key@interfaceVersion names a package. A platform capability
    // with no interface version, a string entry and a non-positive version name no package here.
    [Theory]
    [InlineData("""{"requires":[{"capability":"layout.grid"}]}""")]
    [InlineData("""{"requires":["payroll@2"]}""")]
    [InlineData("""{"requires":[{"capability":"payroll@0"}]}""")]
    [InlineData("""{"requires":{"capability":"payroll@2"}}""")]
    public void A_requires_entry_that_names_no_package_adds_nothing_to_the_closure(string envelope)
    {
        var baseline = TwoPackageBaseline();
        var released = Released(baseline, EditedWith(baseline, Enveloped(envelope)));
        Assert.Null(released.Refusal);
        Assert.NotNull(released.Released);
        Assert.Equal(["finance@1.2.0"], Closure(released.Released.Document));
    }

    // Literal oracles from RuleDefinitionCodec/RuleCrossPackageAuthoring and
    // LayoutDefinitionJson/LayoutCrossPackageAuthoring: package IDs are compared exactly; the
    // baseline's package revision, not an interface or platform version, is the dependency pin.
    // The Rule transport name is intentionally opaque: ConfigurationProposal preserves the caller's
    // stated kind; these controls qualify source extraction, not consumer admission or a new kind.
    [Theory]
    [InlineData("opaque-rule-kind", """{"requires":["payroll"]}""")]
    [InlineData("Layout", """{"requires":[{"capability":"payroll","minimum_platform_version":"8.0.0"},{"capability":"platform.layout","minimum_platform_version":"9.0.0"}]}""")]
    public void Bare_package_requirements_join_the_closure_at_the_exact_baseline_pin(string contentKind, string envelope)
    {
        var baseline = TwoPackageBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/consumer", "finance", RequirementSource(contentKind, envelope), contentKind));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal(["finance@1.2.0", "payroll@3.0.0"], Closure(released.Released!.Document));
    }

    [Theory]
    [InlineData("opaque-rule-kind", """{"requires":["eam-core"]}""")]
    [InlineData("Layout", """{"requires":[{"capability":"eam-core"}]}""")]
    public void Bare_package_requirements_outside_the_baseline_refuse_without_a_release(string contentKind, string envelope)
    {
        var baseline = TwoPackageBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/consumer", "finance", RequirementSource(contentKind, envelope), contentKind));
        var refused = Released(baseline, state);
        Assert.Null(refused.Released);
        Assert.Equal("configuration-release-dependency-unpinned", refused.Refusal!.Code);
        Assert.Equal("eam-core", refused.Refusal.Target);
    }

    [Theory]
    [InlineData("opaque-rule-kind", """{"requires":["payroll@2"]}""")]
    [InlineData("Layout", """{"requires":[{"capability":"payroll@2"}]}""")]
    public void Bare_requirement_contracts_preserve_the_exact_package_id_without_guessing_an_interface_version(string contentKind, string envelope)
    {
        var baseline = TwoPackageBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/consumer", "finance", RequirementSource(contentKind, envelope), contentKind));
        var refused = Released(baseline, state);
        Assert.Null(refused.Released);
        Assert.Equal("configuration-release-dependency-unpinned", refused.Refusal!.Code);
        Assert.Equal("payroll@2", refused.Refusal.Target);
    }

    [Theory]
    [InlineData("payroll", null, null)]
    [InlineData("eam-core", "configuration-release-dependency-unpinned", "eam-core")]
    public void Stored_rule_bodies_use_the_same_exact_package_requirements_without_inventing_shared_metadata(
        string package, string? refusalCode, string? refusalTarget)
    {
        var baseline = TwoPackageBaseline();
        // Literal stored-body shape from RuleDefinitionCodec.SerializeBody: id, tenant and version
        // are absent, as the shared definition header owns them. No header is synthesized for release.
        var body = $$$$"""
            {"envelope":{"cascadeLayer":"domain-package","provenance":{"kind":"package","id":"finance"},
             "requires":["{{{{package}}}}"],"contract":{"major":1,"minor":0}},
             "name":"Amount rule","tier":"JsonLogic","draft":{"kind":"Formula","scope":"Field",
             "scopeTarget":"total","outputType":"Compute","inputs":[],
             "expression":{"kind":"Literal","value":"1","valueType":"Number"}}}
            """;
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/rule", "finance", body, "opaque-rule-kind"));
        var released = Released(baseline, state);
        Assert.Equal(refusalCode, released.Refusal?.Code);
        Assert.Equal(refusalTarget, released.Refusal?.Target);
        if (refusalCode is null)
        {
            Assert.Equal(["finance@1.2.0", "payroll@3.0.0"], Closure(released.Released!.Document));
            using var document = JsonDocument.Parse(released.Released.Document);
            var definition = document.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "tenant-a.release.definition-1")
                .GetProperty("content").GetProperty("payload");
            // Existing export format wraps the exact authored body; dependency extraction may not
            // inject the read-only codec header values into it.
            Assert.Equal("opaque-rule-kind", definition.GetProperty("contentKind").GetString());
            var envelope = definition.GetProperty("body").GetProperty("envelope");
            Assert.False(envelope.TryGetProperty("id", out _));
            Assert.False(envelope.TryGetProperty("tenant", out _));
            Assert.False(envelope.TryGetProperty("version", out _));
        }
        else Assert.Null(released.Released);
    }

    [Theory]
    [InlineData("""{"requires":["payroll"]}""")]
    [InlineData("""{"requires":[null,7,{}, {"capability":7}, {"capability":null}, {"capability":""}, {"capability":" "}]}""")]
    public void Layout_requirement_shapes_other_than_nonblank_capability_objects_add_no_packages(string envelope)
    {
        var baseline = TwoPackageBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/surface", "finance", Enveloped(envelope), "Layout"));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal(["finance@1.2.0"], Closure(released.Released!.Document));
    }

    [Fact]
    public void Layouts_sealed_platform_capability_is_not_a_package_even_when_the_baseline_has_that_key()
    {
        var baseline = ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-a", ["platform.layout"],
            [new(Ref("platform.layout", "7.0.0", 'a'), [], [])], [], Ref("platform", "1.0.0", 'd'), []));
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/surface", "tenant-a.release", Enveloped("""{"requires":[{"capability":"platform.layout","minimum_platform_version":"1.0.0"}]}"""), "Layout"));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Empty(Closure(released.Released!.Document));
    }

    [Fact]
    public void Requirements_deduplicate_edit_owners_and_exclude_the_released_package_across_producer_shapes()
    {
        var baseline = TwoPackageBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/rule", "finance", RequirementSource("opaque-rule-kind", """{"requires":["finance","payroll","payroll","tenant-a.release"]}"""), "opaque-rule-kind"));
        state = ConfigurationProposal.Autosave(state,
            new("new/surface", "tenant-a.release", Enveloped("""{"requires":[{"capability":"finance"},{"capability":"payroll"},{"capability":"tenant-a.release"}]}"""), "Layout"));
        try
        {
            var released = Released(baseline, state);
            Assert.Null(released.Refusal);
            Assert.NotNull(released.Released);
            Assert.Equal(["finance@1.2.0", "payroll@3.0.0"], Closure(released.Released.Document));
        }
        catch (ArgumentException exception) when (exception.ParamName == "dependencies"
            && exception.Message == "platform-package-dependency-duplicate (Parameter 'dependencies')")
        {
            // The producer must deduplicate before the manifest validator sees these references.
            // Assert that this exact business rejection is absent; other exceptions still escape.
            Assert.Null(exception);
        }
    }

    // ADR-0028 plus the approved own-only exposure decision: literal keys and versions below
    // belong to the release in the complete baseline; foreign narrowing never advertises ownership.
    [Fact]
    public void The_released_manifest_lists_the_exposed_definitions_at_their_interface_version()
    {
        var baseline = OwnedExposureBaseline();
        using var document = JsonDocument.Parse(Released(baseline, OwnedEditedWith(baseline, Enveloped("""{"exposes":{"interface_version":2}}"""))).Released!.Document);
        Assert.True(document.RootElement.TryGetProperty("exposes", out var exposes));
        Assert.True(document.RootElement.TryGetProperty("interfaceVersion", out var interfaceVersion));
        Assert.Equal(["records/owned-a"], exposes.EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(2, interfaceVersion.GetInt32());

        using var unexposed = JsonDocument.Parse(Released(baseline, OwnedEditedWith(baseline, Enveloped("""{"requires":[]}"""))).Released!.Document);
        Assert.False(unexposed.RootElement.TryGetProperty("exposes", out _));
        Assert.False(unexposed.RootElement.TryGetProperty("interfaceVersion", out _));
    }

    // Only an exposes object holding an integer interface_version is an exposure declaration; any other shape
    // declares nothing, so the release exposes nothing rather than failing on a member it does not read.
    [Theory]
    [InlineData("""{"exposes":2}""")]
    [InlineData("""{"exposes":[2]}""")]
    [InlineData("""{"exposes":{"interface_version":"2"}}""")]
    [InlineData("""{"exposes":{"interface_version":0}}""")]
    [InlineData("""{"exposes":{}}""")]
    public void An_exposes_member_that_is_not_an_exposure_declaration_exposes_nothing(string envelope)
    {
        var baseline = OwnedExposureBaseline();
        var released = Released(baseline, OwnedEditedWith(baseline, Enveloped(envelope)));
        Assert.Null(released.Refusal);
        using var document = JsonDocument.Parse(released.Released!.Document);
        Assert.False(document.RootElement.TryGetProperty("exposes", out _));
        Assert.False(document.RootElement.TryGetProperty("interfaceVersion", out _));
    }

    // A package has one interface version, so definitions exposed at two refuse rather than picking one.
    [Fact]
    public void Definitions_exposed_at_different_interface_versions_refuse()
    {
        var baseline = OwnedExposureBaseline();
        var state = OwnedEditedWith(baseline, Enveloped("""{"exposes":{"interface_version":1}}"""));
        state = ConfigurationProposal.Autosave(state, new("records/owned-z", "tenant-a.release",
            Enveloped("""{"exposes":{"interface_version":2}}"""), RecordsKind));
        var refused = Released(baseline, state);
        Assert.Null(refused.Released);
        Assert.Equal("configuration-release-interface-ambiguous", refused.Refusal!.Code);
        Assert.Equal("exposes", refused.Refusal.Target);
        Assert.Contains("1, 2", refused.Refusal.Message, StringComparison.Ordinal);
    }

    // The original narrowing payload and exact baseline pins remain transport inputs, even
    // when that foreign definition declares exposure for its own package.
    [Fact]
    public void Foreign_only_exposure_preserves_the_narrowing_body_and_pin_without_manifest_exposure()
    {
        var baseline = TwoPackageBaseline();
        var state = EditedWith(baseline, Enveloped("""{"exposes":{"interface_version":97}}"""));
        state = ConfigurationProposal.Autosave(state,
            new("records/payslip", "payroll", Enveloped("""{"exposes":{"interface_version":98}}"""), RecordsKind));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal(["finance@1.2.0", "payroll@3.0.0"], Closure(released.Released!.Document));
        using var document = JsonDocument.Parse(released.Released.Document);
        Assert.False(document.RootElement.TryGetProperty("exposes", out _));
        Assert.False(document.RootElement.TryGetProperty("interfaceVersion", out _));
        var payload = document.RootElement.GetProperty("items")[1].GetProperty("content").GetProperty("payload");
        Assert.Equal("records/invoice", payload.GetProperty("definitionKey").GetString());
        Assert.Equal("finance", payload.GetProperty("packageKey").GetString());
        Assert.Equal(97, payload.GetProperty("body").GetProperty("envelope").GetProperty("exposes").GetProperty("interface_version").GetInt32());
    }

    [Fact]
    public void Foreign_versions_do_not_make_owned_exposure_ambiguous_and_owned_keys_are_ordinal()
    {
        var baseline = OwnedExposureBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("records/owned-z", "tenant-a.release", Enveloped("""{"exposes":{"interface_version":2}}"""), RecordsKind));
        state = ConfigurationProposal.Autosave(state,
            new("records/invoice", "finance", Enveloped("""{"exposes":{"interface_version":97}}"""), RecordsKind));
        state = ConfigurationProposal.Autosave(state,
            new("records/owned-a", "tenant-a.release", Enveloped("""{"exposes":{"interface_version":2}}"""), RecordsKind));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal(["finance@1.2.0"], Closure(released.Released!.Document));
        using var document = JsonDocument.Parse(released.Released.Document);
        Assert.True(document.RootElement.TryGetProperty("exposes", out var exposes));
        Assert.True(document.RootElement.TryGetProperty("interfaceVersion", out var interfaceVersion));
        Assert.Equal(["records/owned-a", "records/owned-z"], exposes.EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(2, interfaceVersion.GetInt32());
        var foreign = document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("content").GetProperty("payload"))
            .Single(payload => payload.TryGetProperty("definitionKey", out var key) && key.GetString() == "records/invoice");
        Assert.Equal("finance", foreign.GetProperty("packageKey").GetString());
        Assert.Equal(97, foreign.GetProperty("body").GetProperty("envelope").GetProperty("exposes").GetProperty("interface_version").GetInt32());
    }

    // These authored claims conflict with canonical ownership. Neither a forged package claim
    // nor a key prefix can turn an existing foreign definition into the release's interface.
    [Theory]
    [InlineData("records/invoice", "finance@1.2.0")]
    [InlineData("records/payslip", "payroll@3.0.0")]
    [InlineData("tenant-a.release/foreign", "finance@1.2.0")]
    public void A_claimed_release_owner_cannot_expose_a_baseline_foreign_definition(string key, string pin)
    {
        var baseline = OwnedExposureBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new(key, "tenant-a.release", Enveloped("""{"exposes":{"interface_version":2}}"""), RecordsKind));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal([pin], Closure(released.Released!.Document));
        using var document = JsonDocument.Parse(released.Released.Document);
        Assert.False(document.RootElement.TryGetProperty("exposes", out _));
        Assert.False(document.RootElement.TryGetProperty("interfaceVersion", out _));
        Assert.Equal("tenant-a.release", document.RootElement.GetProperty("items")[1]
            .GetProperty("content").GetProperty("payload").GetProperty("packageKey").GetString());
    }

    [Theory]
    [InlineData("records/owned-a")]
    [InlineData("finance/owned")]
    public void Baseline_owned_exposure_uses_the_selected_owner_without_rewriting_the_edit_claim(string key)
    {
        var baseline = OwnedExposureBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new(key, "finance", Enveloped("""{"exposes":{"interface_version":2}}"""), RecordsKind));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal(["finance@1.2.0"], Closure(released.Released!.Document));
        using var document = JsonDocument.Parse(released.Released.Document);
        Assert.True(document.RootElement.TryGetProperty("exposes", out var exposes));
        Assert.True(document.RootElement.TryGetProperty("interfaceVersion", out var interfaceVersion));
        Assert.Equal([key], exposes.EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(2, interfaceVersion.GetInt32());
        Assert.Equal("finance", document.RootElement.GetProperty("items")[1]
            .GetProperty("content").GetProperty("payload").GetProperty("packageKey").GetString());
    }

    [Fact]
    public void A_new_definition_without_a_baseline_selection_uses_its_stated_owner_for_exposure()
    {
        var baseline = TwoPackageBaseline();
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("new/owned", "tenant-a.release", Enveloped("""{"exposes":{"interface_version":2}}"""), RecordsKind));
        state = ConfigurationProposal.Autosave(state,
            new("new/foreign", "finance", Enveloped("""{"exposes":{"interface_version":97}}"""), RecordsKind));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal(["finance@1.2.0"], Closure(released.Released!.Document));
        using var document = JsonDocument.Parse(released.Released.Document);
        Assert.True(document.RootElement.TryGetProperty("exposes", out var exposes));
        Assert.True(document.RootElement.TryGetProperty("interfaceVersion", out var interfaceVersion));
        Assert.Equal(["new/owned"], exposes.EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(2, interfaceVersion.GetInt32());
    }

    [Fact]
    public void Exposure_owner_keys_are_case_sensitive()
    {
        var baseline = ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-a", ["Tenant-a.release"],
            [new(Ref("Tenant-a.release", "4.0.0", 'a'), [Ref("records/case", "4.0.0", 'b')], [])],
            [new("records/case", "Tenant-a.release")], Ref("platform", "1.0.0", 'd'), []));
        var state = ConfigurationProposal.Autosave(ConfigurationProposal.Start("proposal-1", baseline),
            new("records/case", "Tenant-a.release", Enveloped("""{"exposes":{"interface_version":2}}"""), RecordsKind));
        var released = Released(baseline, state);
        Assert.Null(released.Refusal);
        Assert.Equal(["Tenant-a.release@4.0.0"], Closure(released.Released!.Document));
        using var document = JsonDocument.Parse(released.Released.Document);
        Assert.False(document.RootElement.TryGetProperty("exposes", out _));
        Assert.False(document.RootElement.TryGetProperty("interfaceVersion", out _));
    }

    private static string RequirementSource(string contentKind, string envelope)
    {
        if (contentKind == "Layout") return Enveloped(envelope);
        // Literal Rule source from the RuleDefinitionCodec contract and the existing rule catalogue
        // source fixture; the requires entries are authored input, never expected production output.
        using var requirements = JsonDocument.Parse(envelope);
        return $$$$"""
            {"envelope":{"id":"new/rule","version":"1.0.0","tenant":"tenant-a",
             "cascadeLayer":"domain-package","provenance":{"kind":"package","id":"finance"},
             "requires":{{{{requirements.RootElement.GetProperty("requires").GetRawText()}}}},"contract":{"major":1,"minor":0}},
             "name":"Amount rule","tier":"JsonLogic","draft":{"kind":"Formula","scope":"Field",
             "scopeTarget":"total","outputType":"Compute","inputs":[],
             "expression":{"kind":"Literal","value":"1","valueType":"Number"}}}
            """;
    }

    private static ConfigurationGeneration TwoPackageBaseline() => ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-a", ["finance", "payroll"],
        [new(Ref("finance", "1.2.0", 'a'), [Ref("records/invoice", "1.2.0", 'b')], []),
         new(Ref("payroll", "3.0.0", 'e'), [Ref("records/payslip", "3.0.0", 'f')], [])],
        [new("records/invoice", "finance"), new("records/payslip", "payroll")],
        Ref("platform", "1.0.0", 'd'), []));

    private static ConfigurationGeneration OwnedExposureBaseline() => ConfigurationGeneration.Resolve(new ResolvedConfiguration("tenant-a", ["tenant-a.release", "finance", "payroll"],
        [new(Ref("tenant-a.release", "0.9.0", 'c'), [Ref("records/owned-a", "0.9.0", 'a'), Ref("records/owned-z", "0.9.0", 'b'), Ref("finance/owned", "0.9.0", 'c')], []),
         new(Ref("finance", "1.2.0", 'a'), [Ref("records/invoice", "1.2.0", 'b'), Ref("tenant-a.release/foreign", "1.2.0", 'c')], []),
         new(Ref("payroll", "3.0.0", 'e'), [Ref("records/payslip", "3.0.0", 'f')], [])],
        [new("records/owned-a", "tenant-a.release"), new("records/owned-z", "tenant-a.release"), new("finance/owned", "tenant-a.release"),
         new("records/invoice", "finance"), new("tenant-a.release/foreign", "finance"), new("records/payslip", "payroll")],
        Ref("platform", "1.0.0", 'd'), []));

    private static ProposedChangeState OwnedEditedWith(ConfigurationGeneration baseline, string records) => ConfigurationProposal.Autosave(
        ConfigurationProposal.Start("proposal-1", baseline), new("records/owned-a", "tenant-a.release", records, RecordsKind));

    private static string Enveloped(string envelope) => $$"""{"envelope":{{envelope}},"recordType":"invoice"}""";

    private static ProposedChangeState EditedWith(ConfigurationGeneration baseline, string records) => ConfigurationProposal.Autosave(
        ConfigurationProposal.Start("proposal-1", baseline), new("records/invoice", "finance", records, RecordsKind));

    private static ConfigurationReleaseResult Released(ConfigurationGeneration baseline, ProposedChangeState state)
    {
        var version = Save(state);
        return ConfigurationProposal.Release(state, version, new ProposedChangeCheck("proposal-1", version.Digest, "receipt-1"),
            baseline, "tenant-a.release", "1.0.0");
    }

    private static string[] Closure(ReadOnlyMemory<byte> document)
    {
        using var parsed = JsonDocument.Parse(document);
        return parsed.RootElement.GetProperty("closure").GetProperty("dependencies").EnumerateArray()
            .Select(item => $"{item.GetProperty("key").GetString()}@{item.GetProperty("version").GetString()}").ToArray();
    }

    private static KernelPackageManifest AsKernelManifest(ReadOnlyMemory<byte> document)
    {
        using var parsed = JsonDocument.Parse(document);
        var root = parsed.RootElement;
        return new(root.GetProperty("packageKey").GetString()!, root.GetProperty("revision").GetString()!,
            root.GetProperty("closure").GetProperty("dependencies").EnumerateArray()
                .Select(item => new KernelPackageDependency(item.GetProperty("key").GetString()!, item.GetProperty("version").GetString()!))
                .ToArray());
    }
}
