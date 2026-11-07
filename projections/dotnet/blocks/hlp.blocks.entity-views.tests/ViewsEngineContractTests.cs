using System.Reflection;
using System.Text.RegularExpressions;

using Harborline.Foundation.Definitions;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

/// <summary>
/// T-486/engine-gaps: the DES-0017 items that had no test carrying their id. Every expected value is a literal from
/// DES-0017 or its board record, never the production constant under test (T-724 ruling 121).
/// </summary>
public sealed class ViewsEngineContractTests
{
    private const string EngineAssembly = "Harborline.Blocks.EntityViews";

    [Fact(DisplayName = "views-ck-1: ViewDefinition is content kind 10 under the Views pillar 9, and the wire registry agrees")]
    public void ViewDefinitionIsContentKindTenUnderTheViewsPillarNine()
    {
        // DES-0017 views-ck-1 and board finding F1: content kind 10, PackPillar.Views 9, no renumbering.
        Assert.Equal(10, ViewsPackIdentity.ContentKind);
        Assert.Equal(9, ViewsPackIdentity.Primitive);

        var kind = PackContentKindRegistry.Kind("ViewDefinition");
        var pillar = PackContentKindRegistry.PillarOf("ViewDefinition");
        Assert.Equal(10, kind.Value);
        Assert.Equal(("Views", 9), (pillar.Name, pillar.Value));
    }

    [Fact(DisplayName = "views-ck-13: a view result is not a content kind and carries no owner, signatory, edition or basis")]
    public void AViewResultIsNotContentAndCarriesNoOwnerSignatoryEditionOrBasis()
    {
        // DES-0017 views-ck-13 (DES-0002 §12; L325) names the four things a result never carries.
        var members = typeof(ViewQueryResult)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name)
            .ToArray();
        Assert.Contains("Rows", members); // the reflection reads the real result type
        foreach (var forbidden in new[] { "Owner", "Signatory", "Signer", "Edition", "Basis" })
        {
            Assert.DoesNotContain(members, member => member.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }

        var kinds = PackContentKindRegistry.Kinds.Select(entry => entry.Name).ToArray();
        Assert.Contains("ViewDefinition", kinds);
        Assert.DoesNotContain(kinds, name => name.Contains("Result", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "views-eng-8: the authored sort orders every key in turn and the grouping key counts the whole authorized set, not the page")]
    public async Task TheAuthoredSortAndGroupingKeyApplyToTheWholeSetBeforeThePage()
    {
        // Interleaved on purpose, so input order matches neither the sort nor the groups.
        var source = new InMemoryViewRowSource([
            Row("a", "open", 2),
            Row("b", "closed", 5),
            Row("c", "open", 9),
            Row("d", "closed", 1),
            Row("e", "open", 4),
        ]);
        var plan = new ViewQueryPlan(
            "tenant-a",
            "work-item",
            new Dictionary<string, ViewRecordFieldKind>
            {
                ["state"] = ViewRecordFieldKind.Scalar,
                ["priority"] = ViewRecordFieldKind.Ordered,
            },
            [new("state", 120)],
            [],
            [new("state", ViewSortDirection.Ascending), new("priority", ViewSortDirection.Descending)],
            "state",
            null,
            new ViewPage(Offset: 1, Limit: 3),
            DateTimeOffset.UnixEpoch);

        var page = await source.QueryAsync(plan);

        Assert.Equal(["b", "d", "c", "e", "a"], page.CurrentRows.Select(row => row.Id));
        Assert.Equal(["d", "c", "e"], page.Rows.Select(row => row.Id));
        Assert.Equal(5, page.Total);
        Assert.Equal([new ViewGroup("closed", 2), new ViewGroup("open", 3)], page.Groups);
    }

    [Fact(DisplayName = "DES-0017 §3 layers: views-eng-12's interpreter and admission sit in a subsystem assembly that no kernel assembly references")]
    public void TheInterpreterIsASubsystemNoKernelAssemblyNeeds()
    {
        // DES-0002 §2: a subsystem is compiled but not invariant, so a headless install runs without it.
        Assert.Equal(EngineAssembly, typeof(ViewQueryRuntime).Assembly.GetName().Name);
        Assert.Equal(EngineAssembly, typeof(ViewDefinitionAdmission).Assembly.GetName().Name);

        var kernel = new[] { "Harborline.Kernel.Core", "Harborline.Kernel.SchemaValidation" }
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .ToArray();
        Assert.All(kernel, assembly => Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name!.StartsWith("Harborline.Blocks.", StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "DES-0017 §3 layers: views-eng-11's clock is the host's, and the engine never reads the wall clock itself")]
    public void TheEngineTakesItsClockFromTheHostAndNeverReadsTheWallClock()
    {
        // DES-0017 views-eng-11: the clock that fixes "now" is kernel core; device time is never authority.
        Assert.All(
            typeof(ViewQueryRuntime).GetConstructors(),
            constructor => Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(TimeProvider)));

        var wallClock = new Regex(@"\bDateTime(?:Offset)?\s*\.\s*(?:Now|UtcNow|Today)\b|TimeProvider\s*\.\s*System\b");
        var reads = EngineSources()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(entry => wallClock.IsMatch(entry.line) && !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.index + 1}")
            .ToArray();
        Assert.Empty(reads);
    }

    [Fact(DisplayName = "DES-0017 §3 layers: views-eng-14's renderers are UI packages, not part of the compiled engine")]
    public void TheRenderersAreUiPackagesAndTheEngineReferencesNone()
    {
        // DES-0017 views-eng-14: the renderers behind each shape are platform packages.
        Assert.DoesNotContain(
            typeof(ViewQueryRuntime).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.Contains(".UI", StringComparison.Ordinal)
                || reference.Name.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal)
                || reference.Name.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(RepositoryRoot(), "projections", "react", "ui", "hlp.ui.view-runtime")));
        Assert.True(Directory.Exists(Path.Combine(RepositoryRoot(), "projections", "blazor", "ui", "hlp.ui.view-runtime")));
    }

    [Fact(DisplayName = "DES-0017 §3 layers: views-eng-16's admission runs at the definition-write pipeline's validate stage, before the draft is stored")]
    public async Task AdmissionRunsAtValidateBeforeTheDraftIsStored()
    {
        // ADR 0071 decision 2: admission adds no pipeline stage; it runs at validate. The publish half is pending (see the PR).
        var store = new InMemoryViewDefinitionStore();
        var authoring = new ViewDefinitionAuthoring(
            new ViewDefinitionAdmission(new NoKinds(), new NoRecordTypes(), new NoMeasures(), new ViewExpressionFunctionRegistry(), new NoInteractions()),
            store);
        var draft = new ViewDefinitionDraft(
            new(
                new("work.queue", "1.0.0", "tenant-a", ViewCascadeLayer.Tenant,
                    System.Text.Json.JsonSerializer.SerializeToElement(new { source = "test" }), [], null),
                1, "Work queue", "work-item", ViewOwnershipTier.Public, "work:read",
                new([new("title", 240)], [], null, null, null)),
            new("layout.table", new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" }));

        var refused = await Assert.ThrowsAsync<ViewDefinitionAdmissionException>(async () => await authoring.CreateDraftAsync(draft));

        Assert.Equal("definition.validate", refused.Stage);
        Assert.Contains(refused.Refusals, refusal => refusal.Code == "view_definition.kind_unknown");
        Assert.Empty(await store.ListHistoryAsync("tenant-a", "work.queue"));
    }

    [Theory(DisplayName = "T-486 engine-gaps: every refusal code that was a bare literal is a named constant with its wire spelling unchanged")]
    [InlineData("definition", "NotFound", "view_definition.not_found")]
    [InlineData("definition", "RevisionConflict", "view_definition.revision_conflict")]
    [InlineData("definition", "VersionInvalid", "view_definition.version_invalid")]
    [InlineData("query", "FilterKindUnknown", "view.filter.kind_unknown")]
    [InlineData("query", "FilterOperandUnknown", "view.filter.operand_unknown")]
    [InlineData("query", "FilterFunctionUnknown", "view.filter.function_unknown")]
    public void EveryRefusalCodeIsANamedConstantWithItsWireSpellingUnchanged(string owner, string constant, string wire)
    {
        // The literals are the wire spellings at platform 1a97bc7b, the slice's scope anchor; none is renamed or merged.
        var field = (owner == "query" ? typeof(ViewQueryCodes) : typeof(ViewDefinitionCodes)).GetField(constant);
        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
        Assert.Equal(wire, field.GetValue(null));
    }

    [Fact(DisplayName = "T-486 engine-gaps: the evaluation-time and admission-time unknown-function codes stay distinct")]
    public void TheTwoUnknownFunctionCodesStayDistinct()
    {
        Assert.Equal("view.filter.function_unknown", ViewQueryCodes.FilterFunctionUnknown);
        Assert.Equal("view_definition.filter_function_unknown", ViewDefinitionCodes.FilterFunctionUnknown);
    }

    private static ViewRow Row(string id, string state, int priority) => new(
        id,
        new Dictionary<string, object?> { ["state"] = state, ["priority"] = priority });

    private static IEnumerable<string> EngineSources() =>
        Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "projections", "dotnet", "blocks", "hlp.blocks.entity-views"),
            "*.cs",
            SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("The platform repository root was not found above the test output.");
    }

    private sealed class NoKinds : IViewKindRegistry
    {
        public ValueTask<ViewKindDescriptor?> ResolveAsync(string kind, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewKindDescriptor?>(null);

        public ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ViewKindDescriptor>>([]);
    }

    private sealed class NoRecordTypes : IViewRecordTypeRegistry
    {
        public ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(string recordType, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewRecordTypeDescriptor?>(new(recordType, new Dictionary<string, ViewRecordFieldKind> { ["title"] = ViewRecordFieldKind.Text }));
    }

    private sealed class NoMeasures : IViewMeasureCatalog
    {
        public ValueTask<ViewMeasureDescriptor?> ResolveAsync(string name, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewMeasureDescriptor?>(null);

        public ValueTask<ViewMeasureResult> EvaluateAsync(ViewMeasureBinding binding, IReadOnlyList<ViewRow> rows,
            DateTimeOffset evaluatedAt, string tenant, string principal, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No measure is evaluated at admission.");
    }

    private sealed class NoInteractions : IViewInteractionRegistry
    {
        public ValueTask<ViewWidgetDescriptor?> ResolveWidgetAsync(string widget, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ViewWidgetDescriptor?>(null);

        public ValueTask<bool> HasRowActionAsync(string action, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask<bool> HasWorkflowTransitionAsync(string transition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }
}
