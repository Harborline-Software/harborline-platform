using System.IO;
using System.Text.RegularExpressions;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0143 SC2 — the .NET no-side-door fence, engine-core half. Two structural guards prove a workflow
/// step CANNOT reach a real CP side-effect except THROUGH the broker-PEP:
///
/// <list type="number">
/// <item><description>
///   <b>The engine core is financial-free.</b> <c>blocks-workflow</c> (the durable engine + the
///   interpreter that lands on it + the broker) references NO financial-cluster assembly and imports NO
///   financial type (<c>IJournalPostingService</c> / <c>JournalEntry</c> / <c>JournalEntryStatus</c> /
///   <c>Harborline.Blocks.FinancialLedger</c>). So an interpreter/handler physically cannot construct or post
///   a JE — it can only produce an OPAQUE <see cref="WorkflowEffect"/> via the abstract
///   <see cref="IWorkflowEffectFactory"/> seam, whose concrete impl lives in the host. (Same mold as
///   <c>EngineDependencyDirectionArchitectureTests</c> in blocks-financial-ledger.)
/// </description></item>
/// <item><description>
///   <b>The broker is the sole consumer of the effect-factory abstraction.</b> The only engine-core files
///   that reference <see cref="IWorkflowEffectFactory"/> are its definition, the registry, the broker, and
///   the DI wiring — never a step handler or the interpreter. A step obtains an effect ONLY by asking
///   <see cref="IWorkflowEffectBroker"/> (which, for a CP effect, means a human confirm that passes SoD).
///   When the A1 interpreter lands it must go through the broker, not the factory — this test enforces it.
/// </description></item>
/// </list>
/// </summary>
public sealed class WorkflowEngineEnforcementSeamArchitectureTests
{
    // ── Guard 1: the engine core is financial-free ──────────────────────────────

    [Theory]
    [InlineData("Harborline.Blocks.FinancialLedger")]
    [InlineData("Harborline.Blocks.FinancialAr")]
    [InlineData("Harborline.Blocks.FinancialAp")]
    [InlineData("Harborline.Blocks.FinancialPayments")] // SC2 F-5: was omitted; the source-scan already lists it.
    [InlineData("Harborline.Blocks.FinancialPeriods")]  // SC2 F-5: was omitted; the source-scan already lists it.
    [InlineData("Harborline.Blocks.Payroll")]
    public void Engine_assembly_references_no_financial_cluster_assembly(string forbiddenAssembly)
    {
        var referenced = typeof(WorkflowEffectBroker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(forbiddenAssembly, referenced, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Engine_assembly_reference_list_is_non_empty()
    {
        // Non-vacuity: the probe above cannot pass by reading an empty list.
        var referenced = typeof(WorkflowEffectBroker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();
        Assert.NotEmpty(referenced);
    }

    [Fact]
    public void Engine_source_imports_no_financial_type()
    {
        // A leaked `using` of the financial cluster, or a bare mention of a financial effect type, is the
        // earliest symptom of the interpreter reaching around the broker to post a JE directly.
        var forbiddenUsing = new Regex(
            @"^\s*using\s+Harborline\.Blocks\.(FinancialLedger|FinancialAr|FinancialAp|FinancialPayments|FinancialPeriods|Payroll)\b",
            RegexOptions.Compiled);
        // The financial effect TYPES the engine must never name (even fully-qualified). Word-bounded so a
        // comment substring does not false-positive; matched on non-comment code lines only. `JournalEntry`
        // is bare-word-bounded (SC2 F-5: the docstring claimed it but the regex omitted it) — it does NOT
        // match `JournalEntryStatus` (no word boundary between `JournalEntry` and `Status`), so the two
        // remain distinct alternatives.
        var forbiddenType = new Regex(
            @"\b(IJournalPostingService|JournalEntry|JournalEntryStatus|JournalPostingService)\b",
            RegexOptions.Compiled);

        var srcDir = EnginePackageDir();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (norm.Contains("/bin/") || norm.Contains("/obj/"))
            {
                continue;
            }

            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("///", StringComparison.Ordinal))
                {
                    continue; // skip comment lines (the doc references the types by name deliberately).
                }

                if (forbiddenUsing.IsMatch(raw) || forbiddenType.IsMatch(line))
                {
                    violations.Add($"{norm}: {raw.Trim()}");
                    break;
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR 0143 SC2 VIOLATED: the workflow engine core (blocks-workflow) imports a financial type — the " +
            "engine must reach a CP side-effect ONLY through the abstract IWorkflowEffectFactory/WorkflowEffect " +
            "seam (the concrete JE-posting factory lives in the host, behind the broker-PEP). Violations:\n" +
            string.Join("\n", violations));
    }

    // ── Guard 2: the broker is the sole consumer of the effect-factory abstraction ──

    /// <summary>
    /// The engine-core files allowed to reference the INTERNAL effect-factory resolve+build surface
    /// (<see cref="IWorkflowEffectFactory"/> / <c>IWorkflowEffectFactoryResolver</c>): the definition file
    /// (+ the registry, same file), the broker (the sole invoker of <c>.Build</c>), and the DI wiring (which
    /// composes them). A step handler / the A1 interpreter must NOT reference these — they go through the
    /// public <see cref="IWorkflowEffectBroker"/> (to obtain an effect) or <c>IWorkflowEffectCatalog</c> (for
    /// metadata). This source-scan is now a DEFENSE-IN-DEPTH regression guard: the PRIMARY fence is type-level
    /// (both surfaces are <see langword="internal"/>, so an untrusted assembly cannot even name them — SC2
    /// F-1). <c>IWorkflowEffectCatalog</c> (the PUBLIC metadata seam) is intentionally NOT banned.
    /// </summary>
    private static readonly HashSet<string> FactoryReferenceAllowlist = new(StringComparer.Ordinal)
    {
        "WorkflowEffectFactory.cs",                       // the interface + resolver + registry definition
        "WorkflowEffectBroker.cs",                        // the sole invoker of factory.Build(...)
        "DurableWorkflowServiceCollectionExtensions.cs",  // DI: composes IEnumerable<IWorkflowEffectFactory>
        "WorkflowEffectFactoryRegistration.cs",           // DI: the public delegate registration wraps the
                                                          //     internal DelegateWorkflowEffectFactory (SC2 F-1
                                                          //     Owed #2 — host registers a factory without
                                                          //     naming the internal surface; still broker-only Build)
    };

    [Fact]
    public void Only_the_broker_and_its_wiring_reference_the_effect_factory_abstraction()
    {
        // Match the factory interface AND the internal resolver. `\bIWorkflowEffectFactory\b` alone does NOT
        // match `IWorkflowEffectFactoryResolver` (no word boundary before `Resolver`) — the exact gap the
        // reviewer used to slip the registry past the old scan (SC2 F-1). `IWorkflowEffectCatalog` (public
        // metadata) is deliberately excluded — it is the safe seam the interpreter/admission validator use.
        var token = new Regex(@"\bIWorkflowEffectFactory(Resolver)?\b", RegexOptions.Compiled);
        var srcDir = EnginePackageDir();
        var referencing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (norm.Contains("/bin/") || norm.Contains("/obj/"))
            {
                continue;
            }

            // Non-comment code lines only (SC2 F-1: consistent comment-stripping across all three scans).
            if (NonCommentLines(file).Any(line => token.IsMatch(line)))
            {
                referencing.Add(Path.GetFileName(file));
            }
        }

        var offenders = referencing.Where(f => !FactoryReferenceAllowlist.Contains(f)).ToList();
        Assert.True(
            offenders.Count == 0,
            "ADR 0143 SC2 VIOLATED: an engine-core file other than the broker/registry/DI references the " +
            "internal IWorkflowEffectFactory / IWorkflowEffectFactoryResolver surface — a step handler / the " +
            "A1 interpreter must obtain effects through IWorkflowEffectBroker (or metadata via the public " +
            "IWorkflowEffectCatalog); the broker is the ONLY code that invokes a factory. Offenders:\n" +
            string.Join("\n", offenders));

        // Non-vacuity: the broker + the factory definition ARE in the referencing set (the scan is real).
        Assert.Contains("WorkflowEffectBroker.cs", referencing);
        Assert.Contains("WorkflowEffectFactory.cs", referencing);
    }

    [Fact]
    public void The_broker_is_the_only_engine_core_invoker_of_factory_build()
    {
        // The concrete invocation `factory.Build(` (an IWorkflowEffectFactory being invoked) appears ONLY in
        // the broker — the effect-execution boundary. (The interface DECLARES Build; only the broker CALLS it.)
        // This is now a DEFENSE-IN-DEPTH regression guard: IWorkflowEffectFactory.Build is `internal` (SC2
        // F-1), so an untrusted assembly cannot invoke it under ANY variable name — the compiler is the
        // primary fence; this scan just catches an in-package regression early.
        var invoke = new Regex(@"\bfactory\.Build\s*\(", RegexOptions.Compiled);
        var srcDir = EnginePackageDir();
        var invokers = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (norm.Contains("/bin/") || norm.Contains("/obj/"))
            {
                continue;
            }

            // Non-comment code lines only (SC2 F-1: consistent comment-stripping across all three scans).
            if (NonCommentLines(file).Any(line => invoke.IsMatch(line)))
            {
                invokers.Add(Path.GetFileName(file));
            }
        }

        Assert.Equal(["WorkflowEffectBroker.cs"], invokers);
    }

    // ── Helpers ──

    /// <summary>
    /// The non-comment code lines of a source file — lines whose first non-whitespace is <c>//</c> or
    /// <c>///</c> are dropped (the docs deliberately name the forbidden types). Shared by all three source
    /// scans so their comment handling is consistent (SC2 F-1: the reviewer flagged "one strips, two don't").
    /// </summary>
    private static IEnumerable<string> NonCommentLines(string file)
    {
        foreach (var raw in File.ReadLines(file))
        {
            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            yield return raw;
        }
    }

    private static string EnginePackageDir()
    {
        // The engine projection is the test project's SIBLING: walk up from the test bin directory to
        // the blocks/ directory that contains hlp.blocks.workflow/Harborline.Blocks.Workflow.csproj.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "hlp.blocks.workflow", "Harborline.Blocks.Workflow.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the hlp.blocks.workflow projection directory from " + AppContext.BaseDirectory);
    }
}
