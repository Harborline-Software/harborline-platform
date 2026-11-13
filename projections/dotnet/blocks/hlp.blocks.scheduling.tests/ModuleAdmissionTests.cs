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
                Assert.Equal("MODULE_ADMISSION_SKIP: modules must advance one rung at a time.", Assert.Throws<UnsupportedProfileException>(() => module.Advance(next, backend)).Message);
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
            Assert.Equal("MODULE_CERTIFICATE_UNDEFINED: DES-0022 open ruling 3 defines no certificate.", Assert.Throws<UnsupportedProfileException>(
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
        Assert.Equal("MODULE_UNSUPPORTED: invented; required: an admitted module.", unknown.Message);
        var missing = Assert.Throws<UnsupportedProfileException>(() => compiler.Compile(facts,
            Definition(new ScheduleConstraintModule("no_overlap")), backend with { Modules = backend.Modules.Where(kind => kind != ConstraintModuleKind.ResourceNoOverlap).ToHashSet() },
            SolverGuarantee.FeasibleProof, 10));
        Assert.Equal("MODULE_UNSUPPORTED: no_overlap; required: support from backend prototype.exhaustive-proof.v2.", missing.Message);
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
            (Definition() with { ProblemClass = ScheduleProblemClass.Mobility }, proof, SolverGuarantee.FeasibleProof, 1, "PROBLEM_CLASS_UNSUPPORTED: Mobility; backend prototype.exhaustive-proof.v2; required: a supported problem class (Project, JobShop)."),
            (Definition(), heuristic, SolverGuarantee.FeasibleProof, 1, "GUARANTEE_UNSUPPORTED: FeasibleProof; backend prototype.greedy-first-fit.v1; required: an offered guarantee (BoundedHeuristic)."),
            (Definition(), proof with { MaximumWorkBudget = 2 }, SolverGuarantee.FeasibleProof, 3, "WORK_BUDGET_UNSUPPORTED: 3; backend prototype.exhaustive-proof.v2; required: budget in 0..2."),
        })
        {
            var refusal = Assert.Throws<UnsupportedProfileException>(() => compiler.Compile(Profile(), invalid.Item1,
                invalid.Item2, invalid.Item3, invalid.Item4));
            Assert.Equal(invalid.Item5, refusal.Message);
        }
    }

    [Theory(DisplayName = "scheduling-bound-2: unsupported parameter shapes refuse rather than being ignored")]
    [InlineData("no_overlap", "{\"enabled\":false}", "MODULE_UNSUPPORTED: no_overlap; required: valid typed parameters.")]
    [InlineData("precedence", "{}", "MODULE_UNSUPPORTED: precedence; required: valid typed parameters.")]
    [InlineData("precedence", "{\"constraints\":null}", "MODULE_UNSUPPORTED: precedence; required: valid typed parameters.")]
    [InlineData("precedence", "{\"constraints\":[null]}", "MODULE_UNSUPPORTED: precedence; required: distinct activity ids and non-negative gaps.")]
    [InlineData("precedence", "{\"constraints\":[{\"id\":\"x\",\"beforeActivityId\":\"a\",\"afterActivityId\":\"a\",\"minimumGapSlots\":0}]}", "MODULE_UNSUPPORTED: precedence; required: distinct activity ids and non-negative gaps.")]
    [InlineData("precedence", "{\"constraints\":[],\"extra\":true}", "MODULE_UNSUPPORTED: precedence; required: valid typed parameters.")]
    public void InvalidModuleParametersRefuse(string name, string json, string expected)
    {
        var module = ConstraintModuleAdmission.Elicit(new(name, JsonSerializer.Deserialize<JsonElement>(json)));
        Assert.Equal(expected, Assert.Throws<UnsupportedProfileException>(
            () => module.Advance(ConstraintModuleStage.ShapeCandidate)).Message);
    }

    [Fact(DisplayName = "scheduling-bound-2: omitted minimumGapSlots refuses typed precedence admission")]
    public void MissingMinimumGapRefuses()
    {
        var module = ConstraintModuleAdmission.Elicit(new("precedence", JsonSerializer.Deserialize<JsonElement>(
            """{"constraints":[{"id":"x","beforeActivityId":"a","afterActivityId":"b"}]}""")));
        Assert.Equal("MODULE_UNSUPPORTED: precedence; required: valid typed parameters.",
            Assert.Throws<UnsupportedProfileException>(() => module.Advance(ConstraintModuleStage.ShapeCandidate)).Message);
    }

    [Theory(DisplayName = "scheduling-bound-3: maximum integer precedence gap cannot wrap in either assignment order")]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumGapCannotWrap(bool reverse)
    {
        var facts = Profile() with
        {
            TimeWindows = [new("a", 0, 0), new("b", 1, 1)],
            Precedence = [reverse ? new("x", "b", "a", 2147483647) : new("x", "a", "b", 2147483647)],
        };
        var problem = new FiniteCandidateCompiler().Compile(facts);
        var proof = new DeterministicExhaustiveProofSolver().Solve(problem, 100);
        Assert.Equal(SolveStatus.ProvenInfeasible, proof.Status);
        var estimate = new GreedyFirstFitSolver().Solve(problem, 100);
        Assert.Equal(SolveStatus.Partial, estimate.Status);
    }

    [Fact(DisplayName = "scheduling-eng-2: solve requires every module to be backend-supported by this backend")]
    public void SolveRequiresChosenBackendSupport()
    {
        foreach (var (solver, solverId) in new (IPlanningSolver, string)[]
        {
            (new DeterministicExhaustiveProofSolver(), "prototype.exhaustive-proof.v2"),
            (new GreedyFirstFitSolver(), "prototype.greedy-first-fit.v1"),
        })
        {
            var module = ConstraintModuleAdmission.Elicit(new("no_overlap"));
            var invalid = new List<ConstraintModuleAdmission> { module };
            module = module.Advance(ConstraintModuleStage.ShapeCandidate);
            invalid.Add(module);
            module = module.Advance(ConstraintModuleStage.Admitted);
            invalid.Add(module);
            invalid.Add(module.Advance(ConstraintModuleStage.BackendSupported, solver.Backend with { Id = "other" }));
            var supported = module.Advance(ConstraintModuleStage.BackendSupported, solver.Backend);
            var problem = new FiniteCandidateCompiler().Compile(Profile());
            foreach (var admission in invalid)
            {
                // Invalid activities prove refusal precedes any solver enumeration, including a zero budget.
                var refusal = Assert.Throws<UnsupportedProfileException>(() => solver.Solve(problem with
                {
                    Activities = null!, Modules = [supported, admission],
                }, 0));
                Assert.Equal($"MODULE_UNSUPPORTED: no_overlap; required: backend-supported admission for backend {solverId}.", refusal.Message);
            }
            Assert.Equal(SolveStatus.Feasible, solver.Solve(problem, 100).Status);
        }
    }

    [Fact(DisplayName = "scheduling-bound-3: independent checker rejects the maximum integer gap without overflow")]
    public void CheckerMaximumGapCannotWrap()
    {
        Assert.False(Harborline.Experiments.SchedulingDecisionModel.Oracle.PlanChecker.PrecedenceHolds(
            new("a", 0, 1, ["r"]), new("b", 1, 2, ["r"]), 2147483647));
        Assert.True(Harborline.Experiments.SchedulingDecisionModel.Oracle.PlanChecker.PrecedenceHolds(
            new("a", 0, 1, ["r"]), new("b", 1, 2, ["r"]), 0));
    }

    [Theory(DisplayName = "scheduling-bound-3: proposal identity includes the compiled definition tenant, id and version")]
    [InlineData("t", "job", "1.0.0", ":t:job:job%401.0.0")]
    [InlineData("other", "job", "1.0.0", ":other:job:job%401.0.0")]
    [InlineData("t", "other", "1.0.0", ":t:other:other%401.0.0")]
    [InlineData("t", "job", "2.0.0", ":t:job:job%402.0.0")]
    [InlineData("t:job", "job", "1.0.0", ":t%3Ajob:job:job%401.0.0")]
    public void ProposalIdentityIncludesDefinitionPin(string tenant, string id, string version, string suffix)
    {
        var definition = Definition() with
        {
            Envelope = new(id, version, tenant, Harborline.Blocks.BuilderDefinitions.LayoutCascadeLayer.DomainPackage,
                JsonSerializer.SerializeToElement(new { pack = "general" }), "standard", false, [], new(1, 0)),
        };
        foreach (var (solver, solverId) in new (IPlanningSolver, string)[]
        {
            (new DeterministicExhaustiveProofSolver(), "prototype.exhaustive-proof.v2"),
            (new GreedyFirstFitSolver(), "prototype.greedy-first-fit.v1"),
        })
        {
            var problem = new FiniteCandidateCompiler().Compile(Profile(), definition, solver.Backend, solver.Guarantee, 100);
            Assert.Equal(solverId + ":p:v1:Feasible" + suffix, solver.Solve(problem, 100).ProposalId);
            Assert.Equal(solverId + ":p:v1:Feasible", solver.Solve(new FiniteCandidateCompiler().Compile(Profile()), 100).ProposalId);
        }
    }

    internal static ScheduleDefinition Definition(params ScheduleConstraintModule[] modules) => new(null, "Job shop",
        new(ScheduleStartKind.Form, "form"), ProblemClass: ScheduleProblemClass.JobShop, Modules: modules);

    internal static SchedulingProfile Profile() => new("p", "v1", [new("a", 1), new("b", 1)],
        [new("a", 0, 0), new("b", 1, 2)], [new("r", new HashSet<string> { "skill" }, new HashSet<int> { 0, 1, 2 })],
        [new("ra", "a", "skill", 1), new("rb", "b", "skill", 1)], [],
        PlanningFactSets.Required.Select(name => new PlanningFactSetPin(name, "v1", true)).ToArray());
}
