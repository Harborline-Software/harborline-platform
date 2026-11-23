using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleModuleStageProofTests
{
    private static SolverBackend Backend(bool supports) => new("limited",
        supports ? new HashSet<ConstraintModuleKind> { ConstraintModuleKind.ResourceNoOverlap } : new HashSet<ConstraintModuleKind>(),
        new HashSet<ScheduleProblemClass> { ScheduleProblemClass.JobShop },
        new HashSet<SolverGuarantee> { SolverGuarantee.FeasibleProof }, 10);

    private static ConstraintModuleAdmission Elicit() => ConstraintModuleAdmission.Elicit(new("no_overlap"));
    private static ConstraintModuleAdmission Admitted() => Elicit().Advance(ConstraintModuleStage.ShapeCandidate).Advance(ConstraintModuleStage.Admitted);

    [Fact(DisplayName = "scheduling-bound-2 scheduling-eng-2 module-stage: stages preserve prior instances")]
    public void StagesPreservePriorInstances()
    {
        var elicited = Elicit();
        var shape = elicited.Advance(ConstraintModuleStage.ShapeCandidate);
        var admitted = shape.Advance(ConstraintModuleStage.Admitted);
        var supported = admitted.Advance(ConstraintModuleStage.BackendSupported, Backend(true));
        Assert.Equal("Elicited", elicited.Stage.ToString());
        Assert.Equal("ShapeCandidate", shape.Stage.ToString());
        Assert.Equal("Admitted", admitted.Stage.ToString());
        Assert.Equal("BackendSupported", supported.Stage.ToString());
        Assert.Equal("ResourceNoOverlap", shape.Kind.ToString());
        Assert.Null(elicited.BackendId);
        Assert.Null(shape.BackendId);
        Assert.Null(admitted.BackendId);
        Assert.Equal("limited", supported.BackendId);
    }

    [Fact(DisplayName = "scheduling-bound-2 scheduling-eng-2 module-stage: skipped stages refuse")]
    public void SkipsRefuse()
    {
        var module = Elicit();
        foreach (var next in new[] { ConstraintModuleStage.Admitted, ConstraintModuleStage.Elicited, (ConstraintModuleStage)99 })
            Assert.Equal("MODULE_ADMISSION_SKIP: modules must advance one rung at a time.",
                Assert.Throws<UnsupportedProfileException>(() => module.Advance(next)).Message);
    }

    [Fact(DisplayName = "scheduling-bound-2 scheduling-eng-2 module-stage: backend support is required and retryable")]
    public void UnsupportedBackendRefuses()
    {
        var module = Admitted();
        Assert.Throws<UnsupportedProfileException>(() => module.Advance(ConstraintModuleStage.BackendSupported));
        Assert.Equal("MODULE_UNSUPPORTED: no_overlap; required: support from backend limited.",
            Assert.Throws<UnsupportedProfileException>(() => module.Advance(ConstraintModuleStage.BackendSupported, Backend(false))).Message);
        Assert.Equal("BackendSupported", module.Advance(ConstraintModuleStage.BackendSupported, Backend(true)).Stage.ToString());
    }

    [Fact(DisplayName = "scheduling-bound-2 scheduling-eng-2 module-stage: certification remains undefined")]
    public void CertificateRefuses()
    {
        var supported = Admitted().Advance(ConstraintModuleStage.BackendSupported, Backend(true));
        Assert.Equal("MODULE_CERTIFICATE_UNDEFINED: DES-0022 open ruling 3 defines no certificate.",
            Assert.Throws<UnsupportedProfileException>(() => supported.Advance(ConstraintModuleStage.Certified)).Message);
        Assert.Equal("BackendSupported", supported.Stage.ToString());
        Assert.Equal("limited", supported.BackendId);
    }
}
