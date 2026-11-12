using System.Text.Json;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ModuleAdmissionTests
{
    [Fact(DisplayName = "scheduling-bound-2: naming skips no rung and every nonadjacent transition refuses")]
    public void LadderRejectsEverySkip()
    {
        var module = ConstraintModuleAdmission.Elicit(new("no_overlap"));
        var backend = new DeterministicExhaustiveProofSolver().Backend;
        foreach (var stage in new[] { ConstraintModuleStage.Elicited, ConstraintModuleStage.ShapeCandidate,
                     ConstraintModuleStage.Admitted, ConstraintModuleStage.BackendSupported })
        {
            Assert.Equal(stage, module.Stage);
            foreach (var next in Enum.GetValues<ConstraintModuleStage>().Where(next => (int)next != (int)stage + 1))
                Assert.StartsWith("MODULE_ADMISSION_SKIP:", Assert.Throws<UnsupportedProfileException>(() => module.Advance(next, backend)).Message);
            if (stage != ConstraintModuleStage.BackendSupported) module = module.Advance(stage + 1, backend);
        }
        Assert.Equal("prototype.exhaustive-proof.v2", module.BackendId);
    }

    [Fact(DisplayName = "scheduling-bound-2: the undefined certificate refuses with a stable reason")]
    public void CertificationRefusesWithStableReason()
    {
        var backend = new DeterministicExhaustiveProofSolver().Backend;
        foreach (var name in new[] { "time_windows", "resource_requirements", "no_overlap", "precedence" })
        {
            var module = ConstraintModuleAdmission.Elicit(new(name))
                .Advance(ConstraintModuleStage.ShapeCandidate).Advance(ConstraintModuleStage.Admitted)
                .Advance(ConstraintModuleStage.BackendSupported, backend);
            Assert.NotEqual(ConstraintModuleStage.Certified, module.Stage);
            Assert.StartsWith("MODULE_CERTIFICATE_UNDEFINED:", Assert.Throws<UnsupportedProfileException>(
                () => module.Advance(ConstraintModuleStage.Certified, backend)).Message);
            Assert.Equal(ConstraintModuleStage.BackendSupported, module.Stage);
        }
    }

    [Fact(DisplayName = "scheduling-bound-2: compiled modules never reach certified and proposal guarantees remain independent")]
    public void NoModuleCanBeCertified()
    {
        var guarantees = new List<SolverGuarantee>();
        foreach (var solver in new IPlanningSolver[] { new DeterministicExhaustiveProofSolver(), new GreedyFirstFitSolver() })
        {
            var problem = new FiniteCandidateCompiler().Compile(Profile(), Definition(), solver.Backend, solver.Guarantee, 100);
            Assert.Equal(4, problem.Modules.Count);
            Assert.All(problem.Modules, module => Assert.Equal(ConstraintModuleStage.BackendSupported, module.Stage));
            var proposal = solver.Solve(problem, 100);
            Assert.Equal(SolveStatus.Feasible, proposal.Status);
            guarantees.Add(proposal.Guarantee);
        }
        Assert.Equal(new[] { SolverGuarantee.FeasibleProof, SolverGuarantee.BoundedHeuristic }, guarantees);
    }

    [Fact(DisplayName = "scheduling-eng-2: an unknown or backend-unsupported module refuses before enumeration")]
    public void RefusesBeforeEnumeration()
    {
        var backend = new DeterministicExhaustiveProofSolver().Backend;
        var facts = Profile() with { Activities = null! };
        var compiler = new FiniteCandidateCompiler();
        var unknown = Assert.Throws<UnsupportedProfileException>(() => compiler.Compile(facts,
            Definition(new ScheduleConstraintModule("invented")), backend, SolverGuarantee.FeasibleProof, 10));
        Assert.StartsWith("MODULE_UNSUPPORTED:", unknown.Message);
        var missing = Assert.Throws<UnsupportedProfileException>(() => compiler.Compile(facts,
            Definition(new ScheduleConstraintModule("no_overlap")), backend with { Modules = backend.Modules.Where(kind => kind != ConstraintModuleKind.ResourceNoOverlap).ToHashSet() },
            SolverGuarantee.FeasibleProof, 10));
        Assert.Contains("no_overlap", missing.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "scheduling-eng-9, scheduling-bound-3: a declared job shop compiles typed precedence parameters and enforces them")]
    public void DeclaredClassCompilesTypedModules()
    {
        var solver = new DeterministicExhaustiveProofSolver();
        var definition = Definition(new ScheduleConstraintModule("precedence", JsonSerializer.SerializeToElement(new
        {
            constraints = new[] { new { id = "after", beforeActivityId = "a", afterActivityId = "b", minimumGapSlots = 1 } },
        })));
        var problem = new FiniteCandidateCompiler().Compile(Profile(), definition, solver.Backend, SolverGuarantee.FeasibleProof, 100);
        Assert.Equal(new[] { ConstraintModuleKind.TimeWindows, ConstraintModuleKind.ResourceRequirements,
            ConstraintModuleKind.ResourceNoOverlap, ConstraintModuleKind.Precedence }, problem.Modules.Select(module => module.Kind!.Value));
        Assert.All(problem.Modules, module => Assert.Equal(ConstraintModuleStage.BackendSupported, module.Stage));
        var proposal = solver.Solve(problem, 100);
        Assert.Equal(SolveStatus.Feasible, proposal.Status);
        Assert.Equal(0, proposal.Assignments.Single(item => item.ActivityId == "a").StartSlot);
        Assert.Equal(2, proposal.Assignments.Single(item => item.ActivityId == "b").StartSlot);
        Assert.Equal(SolverGuarantee.FeasibleProof, proposal.Guarantee);
    }

    [Fact(DisplayName = "scheduling-bound-1, scheduling-bound-4: backend declares support, guarantees and budget; missing requirements refuse")]
    public void BackendDeclaresAndEnforcesCapabilities()
    {
        var proof = new DeterministicExhaustiveProofSolver().Backend;
        var heuristic = new GreedyFirstFitSolver().Backend;
        Assert.Equal(4, proof.Modules.Count);
        Assert.Contains(ScheduleProblemClass.JobShop, proof.ProblemClasses);
        Assert.Contains(SolverGuarantee.FeasibleProof, proof.Guarantees);
        Assert.Contains(SolverGuarantee.BoundedHeuristic, heuristic.Guarantees);
        Assert.Equal(int.MaxValue, proof.MaximumWorkBudget);
        var compiler = new FiniteCandidateCompiler();
        foreach (var invalid in new[]
        {
            (Definition() with { ProblemClass = ScheduleProblemClass.Mobility }, proof, SolverGuarantee.FeasibleProof, 1, "problem class"),
            (Definition(), heuristic, SolverGuarantee.FeasibleProof, 1, "guarantee"),
            (Definition(), proof with { MaximumWorkBudget = 2 }, SolverGuarantee.FeasibleProof, 3, "budget"),
        })
        {
            var refusal = Assert.Throws<UnsupportedProfileException>(() => compiler.Compile(Profile(), invalid.Item1,
                invalid.Item2, invalid.Item3, invalid.Item4));
            Assert.Contains(invalid.Item5, refusal.Message, StringComparison.Ordinal);
            Assert.Contains("required:", refusal.Message, StringComparison.Ordinal);
        }
    }

    [Theory(DisplayName = "scheduling-bound-2: unsupported parameter shapes refuse rather than being ignored")]
    [InlineData("no_overlap", "{\"enabled\":false}")]
    [InlineData("precedence", "{}")]
    [InlineData("precedence", "{\"constraints\":null}")]
    [InlineData("precedence", "{\"constraints\":[null]}")]
    [InlineData("precedence", "{\"constraints\":[{\"id\":\"x\",\"beforeActivityId\":\"a\",\"afterActivityId\":\"a\",\"minimumGapSlots\":0}]}")]
    [InlineData("precedence", "{\"constraints\":[],\"extra\":true}")]
    public void InvalidModuleParametersRefuse(string name, string json)
    {
        var module = ConstraintModuleAdmission.Elicit(new(name, JsonSerializer.Deserialize<JsonElement>(json)));
        Assert.StartsWith("MODULE_UNSUPPORTED:", Assert.Throws<UnsupportedProfileException>(
            () => module.Advance(ConstraintModuleStage.ShapeCandidate)).Message);
    }

    internal static ScheduleDefinition Definition(params ScheduleConstraintModule[] modules) => new(null, "Job shop",
        new(ScheduleStartKind.Form, "form"), ProblemClass: ScheduleProblemClass.JobShop, Modules: modules);

    internal static SchedulingProfile Profile() => new("p", "v1", [new("a", 1), new("b", 1)],
        [new("a", 0, 0), new("b", 1, 2)], [new("r", new HashSet<string> { "skill" }, new HashSet<int> { 0, 1, 2 })],
        [new("ra", "a", "skill", 1), new("rb", "b", "skill", 1)], [],
        PlanningFactSets.Required.Select(name => new PlanningFactSetPin(name, "v1", true)).ToArray());
}
