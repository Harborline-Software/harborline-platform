using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.MeasureCatalogue;
using Harborline.Blocks.Reports.Definitions;
using Harborline.Blocks.Reports.Measures;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// T-489 S4a (DES-0020): a report definition names its measures by stable catalogue path, and admission resolves
/// each one through <see cref="IMeasureCatalogue.ResolveAsync"/> at every phase, refusing an unknown one by name.
/// </summary>
public sealed class ReportDefinitionAdmissionTests
{
    private const string CatalogueName = "harborline.measures";
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);
    private static readonly DefinitionContractVersion Contract = new(1, 0);

    [Trait("Holds", "reports-auth-15")]
    [Trait("Holds", "reports-eng-3")]
    [Theory(DisplayName = "reports-auth-15: a measure the catalogue lacks is refused by name, naming the measure and the catalogue")]
    [InlineData(DefinitionAdmissionPhase.Author)]
    [InlineData(DefinitionAdmissionPhase.Install)]
    public async Task reports_auth_15_unknown_measure_refuses_naming_measure_and_catalogue(DefinitionAdmissionPhase phase)
    {
        var report = await ReportDefinitionAdmission.AdmitAsync(
            Definition("finance.trial-balance", "finance.cash-flow"), phase, Window, Shipped());

        Assert.Equal(phase, report.Stage);
        var refusal = Assert.Single(report.Refusals);
        Assert.Equal(new ReportRefusal(ReportDefinitionCodes.MeasureUnknown, "/measures/1/measure",
            "finance.cash-flow", CatalogueName), refusal);
    }

    [Trait("Holds", "reports-eng-3")]
    [Theory(DisplayName = "reports-eng-3: a definition naming a measure the catalogue lacks is refused at every phase, and Require throws")]
    [InlineData(DefinitionAdmissionPhase.Author)]
    [InlineData(DefinitionAdmissionPhase.Publish)]
    [InlineData(DefinitionAdmissionPhase.Install)]
    [InlineData(DefinitionAdmissionPhase.Render)]
    public async Task reports_eng_3_unknown_measure_is_refused_at_every_phase(DefinitionAdmissionPhase phase)
    {
        var definition = Definition("occupancy.vacancy-rate");

        var refused = await Assert.ThrowsAsync<ReportAdmissionException>(async () =>
            await ReportDefinitionAdmission.RequireAsync(definition, phase, Window, Shipped()));

        Assert.Equal("The report definition was refused.", refused.Message);
        Assert.Equal(phase, refused.Report.Stage);
        Assert.Equal([new ReportRefusal(ReportDefinitionCodes.MeasureUnknown, "/measures/0/measure",
            "occupancy.vacancy-rate", CatalogueName)], refused.Report.Refusals);
    }

    [Trait("Holds", "reports-eng-3")]
    [Fact(DisplayName = "reports-eng-3: every unknown measure is refused, in definition order, and known ones are not")]
    public async Task Every_unknown_measure_is_refused_in_order()
    {
        var report = await ReportDefinitionAdmission.AdmitAsync(
            Definition("finance.cash-flow", "finance.balance-sheet", "payables.vendor-spend"),
            DefinitionAdmissionPhase.Publish, Window, Shipped());

        Assert.Equal(
            [
                new ReportRefusal(ReportDefinitionCodes.MeasureUnknown, "/measures/0/measure", "finance.cash-flow", CatalogueName),
                new ReportRefusal(ReportDefinitionCodes.MeasureUnknown, "/measures/2/measure", "payables.vendor-spend", CatalogueName),
            ],
            report.Refusals);
    }

    [Trait("Holds", "reports-ck-1")]
    [Trait("Holds", "reports-ck-2")]
    [Trait("Holds", "reports-ck-9")]
    [Theory(DisplayName = "reports-ck-1,2,9: every shipped measure path is admitted at every phase and kept verbatim")]
    [InlineData(DefinitionAdmissionPhase.Author)]
    [InlineData(DefinitionAdmissionPhase.Publish)]
    [InlineData(DefinitionAdmissionPhase.Install)]
    [InlineData(DefinitionAdmissionPhase.Render)]
    public async Task reports_ck_every_shipped_path_is_admitted(DefinitionAdmissionPhase phase)
    {
        var paths = new List<string>();
        foreach (var entry in ReportMeasureEntries.All()) paths.Add(entry.Reference.Value);
        var definition = Definition([.. paths]);

        var admitted = await ReportDefinitionAdmission.RequireAsync(definition, phase, Window, Shipped());

        Assert.Same(definition, admitted);
        Assert.Equal(paths, admitted.Measures.Select(measure => measure.Measure));
    }

    [Trait("Holds", "reports-ck-1")]
    [Trait("Holds", "reports-ck-2")]
    [Trait("Holds", "reports-ck-9")]
    [Fact(DisplayName = "reports-ck-1,2,9: admission resolves each path through ResolveAsync alone and evaluates nothing")]
    public async Task Admission_resolves_by_path_and_evaluates_nothing()
    {
        var catalogue = new RecordingCatalogue("finance.trial-balance");

        var report = await ReportDefinitionAdmission.AdmitAsync(
            Definition("finance.trial-balance", "finance.cash-flow"), DefinitionAdmissionPhase.Install, Window,
            new NamedMeasureCatalogue("tenant.catalogue", catalogue));

        Assert.Equal(["finance.trial-balance", "finance.cash-flow"], catalogue.Resolved);
        Assert.Equal(0, catalogue.Evaluated);
        Assert.Equal("tenant.catalogue", Assert.Single(report.Refusals).Catalogue);
    }

    [Trait("Holds", "reports-ck-1")]
    [Trait("Holds", "reports-ck-2")]
    [Trait("Holds", "reports-ck-9")]
    [Theory(DisplayName = "reports-ck-1,2,9: a path that is not a legal catalogue address is refused by name without being resolved")]
    [InlineData("trial-balance")]
    [InlineData("Finance.Trial-Balance")]
    [InlineData("finance..trial-balance")]
    [InlineData("finance.trial-balance-")]
    [InlineData("")]
    public async Task Malformed_path_is_refused_by_name_without_resolution(string path)
    {
        var catalogue = new RecordingCatalogue(path);

        var report = await ReportDefinitionAdmission.AdmitAsync(
            Definition(path), DefinitionAdmissionPhase.Author, Window, new NamedMeasureCatalogue(CatalogueName, catalogue));

        Assert.Equal([new ReportRefusal(ReportDefinitionCodes.MeasureMalformed, "/measures/0/measure", path, CatalogueName)],
            report.Refusals);
        Assert.Empty(catalogue.Resolved);
    }

    [Trait("Holds", "reports-ck-1")]
    [Trait("Holds", "reports-ck-2")]
    [Trait("Holds", "reports-ck-9")]
    [Fact(DisplayName = "reports-ck-1,2,9: a null path is refused as malformed")]
    public async Task Null_path_is_refused_as_malformed()
    {
        var definition = Definition() with { Measures = [new ReportMeasureReference(null!)] };

        var report = await ReportDefinitionAdmission.AdmitAsync(definition, DefinitionAdmissionPhase.Author, Window, Shipped());

        Assert.Equal([new ReportRefusal(ReportDefinitionCodes.MeasureMalformed, "/measures/0/measure", null, CatalogueName)],
            report.Refusals);
    }

    [Fact(DisplayName = "T-489 S4a: a report definition is the api's content kind 9, PackContentKind.ReportDefinition")]
    public void Content_kind_is_the_api_value()
    {
        Assert.Equal(9, ReportPackIdentity.ContentKind);
        Assert.Equal(ReportPackIdentity.ContentKind, PackContentKindRegistry.Kind("ReportDefinition").Value);
    }

    [Theory(DisplayName = "T-572 (rulings 85-88): a report envelope whose contract is missing or outside the window is refused at /envelope/contract")]
    [InlineData(null, null, "definition.contract.missing")]
    [InlineData(2, 0, "definition.contract.out_of_window")]
    [InlineData(1, 1, "definition.contract.out_of_window")]
    [InlineData(1, 0, null)]
    public async Task Contract_is_checked_against_the_window(int? major, int? minor, string? expected)
    {
        var contract = major is null ? null : new DefinitionContractVersion(major.Value, minor!.Value);
        var definition = Definition("finance.trial-balance") with
        {
            Envelope = new ReportDefinitionEnvelope("rent-review", "1.0.0", "tenant-r", contract),
        };

        foreach (var phase in Enum.GetValues<DefinitionAdmissionPhase>())
        {
            var report = await ReportDefinitionAdmission.AdmitAsync(definition, phase, Window, Shipped());
            Assert.Equal(expected is null ? [] : [new ReportRefusal(expected, "/envelope/contract")], report.Refusals);
        }
    }

    [Theory(DisplayName = "T-489 S4a: an envelope is optional at Author and required at every later phase")]
    [InlineData(DefinitionAdmissionPhase.Author, false)]
    [InlineData(DefinitionAdmissionPhase.Publish, true)]
    [InlineData(DefinitionAdmissionPhase.Install, true)]
    [InlineData(DefinitionAdmissionPhase.Render, true)]
    public async Task Envelope_is_required_past_author(DefinitionAdmissionPhase phase, bool refused)
    {
        var definition = Definition("finance.trial-balance") with { Envelope = null };

        var report = await ReportDefinitionAdmission.AdmitAsync(definition, phase, Window, Shipped());

        Assert.Equal(refused ? [new ReportRefusal(ReportDefinitionCodes.EnvelopeRequired, "/envelope")] : [], report.Refusals);
    }

    [Theory(DisplayName = "T-489 S4a: an envelope whose identity, version or tenant disagrees with the body is refused")]
    [InlineData("other-key", "1.0.0", "tenant-r")]
    [InlineData("rent-review", "2.0.0", "tenant-r")]
    [InlineData("rent-review", "1.0.0", "tenant-x")]
    public async Task Envelope_mismatch_is_refused(string identity, string version, string tenant)
    {
        var definition = Definition("finance.trial-balance") with
        {
            Envelope = new ReportDefinitionEnvelope(identity, version, tenant, Contract),
        };

        var report = await ReportDefinitionAdmission.AdmitAsync(definition, DefinitionAdmissionPhase.Author, Window, Shipped());

        Assert.Equal([new ReportRefusal(ReportDefinitionCodes.EnvelopeMismatch, "/envelope")], report.Refusals);
    }

    [Fact(DisplayName = "T-489 S4a: admission refuses null arguments")]
    public async Task Null_arguments_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ReportDefinitionAdmission.AdmitAsync(null!, DefinitionAdmissionPhase.Author, Window, Shipped()));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ReportDefinitionAdmission.AdmitAsync(Definition(), DefinitionAdmissionPhase.Author, null!, Shipped()));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ReportDefinitionAdmission.AdmitAsync(Definition(), DefinitionAdmissionPhase.Author, Window, null!));
    }

    private static ReportDefinition Definition(params string[] measures) => new(
        "tenant-r",
        "rent-review",
        "1.0.0",
        [.. measures.Select(measure => new ReportMeasureReference(measure))],
        new ReportDefinitionEnvelope("rent-review", "1.0.0", "tenant-r", Contract));

    // The shipped catalogue over the production AccessProvider. Resolution never binds the filter, so a gate that
    // refuses every record proves admission reads no row.
    private static NamedMeasureCatalogue Shipped() => new(CatalogueName,
        new MeasureCatalogue.MeasureCatalogue(new AccessProvider(new DenyAll()), ReportMeasureEntries.All()));

    private sealed class DenyAll : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuthorizationDecisionEvidence(request, false, "record_not_visible", "grant:0", [], []));
    }

    private sealed class RecordingCatalogue(params string[] known) : IMeasureCatalogue
    {
        public List<string> Resolved { get; } = [];
        public int Evaluated { get; private set; }

        public ValueTask<MeasureDescriptor?> ResolveAsync(MeasureRef reference, CancellationToken cancellationToken = default)
        {
            Resolved.Add(reference.Value);
            return ValueTask.FromResult(Array.IndexOf(known, reference.Value) >= 0 ? new MeasureDescriptor(reference, []) : null);
        }

        public ValueTask<MeasureResult> EvaluateAsync(MeasureRef reference, MeasureRequest request, CancellationToken cancellationToken = default)
        {
            Evaluated++;
            throw new InvalidOperationException("Admission must not evaluate a measure.");
        }
    }
}
