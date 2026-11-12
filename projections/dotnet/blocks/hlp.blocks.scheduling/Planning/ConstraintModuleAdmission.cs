using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Frozen;
using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling.Planning;

/// <summary>The finite constraints implemented by the compiler and both built-in solvers.</summary>
public enum ConstraintModuleKind
{
    /// <summary>Start bounds and activity duration.</summary>
    TimeWindows,
    /// <summary>Capability and quantity demand.</summary>
    ResourceRequirements,
    /// <summary>Exclusive use of each assigned resource.</summary>
    ResourceNoOverlap,
    /// <summary>Finish-to-start precedence with a minimum gap.</summary>
    Precedence,
}

/// <summary>ADR 0048's ordered module admission states.</summary>
public enum ConstraintModuleStage
{
    /// <summary>Named, without any admission claim.</summary>
    Elicited,
    /// <summary>Its parameter shape has been checked.</summary>
    ShapeCandidate,
    /// <summary>Its semantics are implemented by this engine.</summary>
    Admitted,
    /// <summary>The selected backend implements it.</summary>
    BackendSupported,
    /// <summary>Reserved pending DES-0022 open ruling 3; unreachable.</summary>
    Certified,
}

/// <summary>A module whose state can advance only through the admission boundary.</summary>
public sealed class ConstraintModuleAdmission
{
    private static readonly JsonSerializerOptions ParametersOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };
    private ConstraintModuleAdmission(ScheduleConstraintModule declaration, ConstraintModuleStage stage,
        ConstraintModuleKind? kind = null, IReadOnlyList<PrecedenceConstraint>? precedence = null, string? backendId = null)
    {
        Declaration = declaration;
        Stage = stage;
        Kind = kind;
        Precedence = precedence ?? [];
        BackendId = backendId;
    }

    /// <summary>The authored module and parameters.</summary>
    public ScheduleConstraintModule Declaration { get; }
    /// <summary>The last successfully traversed rung.</summary>
    public ConstraintModuleStage Stage { get; }
    /// <summary>The checked constraint kind, present after shape admission.</summary>
    public ConstraintModuleKind? Kind { get; }
    /// <summary>Additional typed precedence constraints supplied by this module.</summary>
    public IReadOnlyList<PrecedenceConstraint> Precedence { get; }
    /// <summary>The backend for which support was checked.</summary>
    public string? BackendId { get; }

    /// <summary>Names a module without granting it any capability.</summary>
    public static ConstraintModuleAdmission Elicit(ScheduleConstraintModule declaration)
        => new(declaration ?? throw new ArgumentNullException(nameof(declaration)), ConstraintModuleStage.Elicited);

    /// <summary>Advances one rung, refusing skips, unsupported shapes, and undefined certification.</summary>
    public ConstraintModuleAdmission Advance(ConstraintModuleStage next, SolverBackend? backend = null)
    {
        if ((int)next != (int)Stage + 1 || !Enum.IsDefined(next))
            throw new UnsupportedProfileException("MODULE_ADMISSION_SKIP: modules must advance one rung at a time.");
        if (next == ConstraintModuleStage.Certified)
            throw new UnsupportedProfileException("MODULE_CERTIFICATE_UNDEFINED: DES-0022 open ruling 3 defines no certificate.");
        if (next == ConstraintModuleStage.ShapeCandidate)
        {
            var kind = Declaration.Module switch
            {
                "time_windows" => ConstraintModuleKind.TimeWindows,
                "resource_requirements" => ConstraintModuleKind.ResourceRequirements,
                "no_overlap" => ConstraintModuleKind.ResourceNoOverlap,
                "precedence" => ConstraintModuleKind.Precedence,
                _ => throw new UnsupportedProfileException($"MODULE_UNSUPPORTED: {Declaration.Module}; required: an admitted module."),
            };
            IReadOnlyList<PrecedenceConstraint> precedence = [];
            if (Declaration.Parameters is { } parameters)
            {
                try
                {
                    if (kind == ConstraintModuleKind.Precedence)
                        precedence = parameters.Deserialize<PrecedenceParameters>(ParametersOptions)?.Constraints
                            ?? throw new JsonException();
                    else if (parameters.ValueKind != JsonValueKind.Object || parameters.EnumerateObject().Any())
                        throw new JsonException();
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    throw new UnsupportedProfileException($"MODULE_UNSUPPORTED: {Declaration.Module}; required: valid typed parameters.");
                }
            }
            if (precedence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.BeforeActivityId) || string.IsNullOrWhiteSpace(item.AfterActivityId)
                    || item.BeforeActivityId == item.AfterActivityId || item.MinimumGapSlots < 0))
                throw new UnsupportedProfileException("MODULE_UNSUPPORTED: precedence; required: distinct activity ids and non-negative gaps.");
            return new(Declaration, next, kind, precedence);
        }
        if (next == ConstraintModuleStage.BackendSupported && (backend is null || !backend.Modules.Contains(Kind!.Value)))
            throw new UnsupportedProfileException($"MODULE_UNSUPPORTED: {Declaration.Module}; required: support from backend {backend?.Id ?? "<missing>"}.");
        return new(Declaration, next, Kind, Precedence, next == ConstraintModuleStage.BackendSupported ? backend?.Id : null);
    }

    /// <summary>Checks authoring against the same admitted set used at compilation.</summary>
    public static bool IsAdmitted(ScheduleConstraintModule declaration)
    {
        try
        {
            _ = Elicit(declaration).Advance(ConstraintModuleStage.ShapeCandidate).Advance(ConstraintModuleStage.Admitted);
            return true;
        }
        catch (UnsupportedProfileException) { return false; }
    }

    private sealed record PrecedenceParameters(IReadOnlyList<PrecedenceConstraint> Constraints);
}

/// <summary>A backend's finite capability declaration; problem-class names are Harborline's ruling.</summary>
public sealed record SolverBackend(string Id, IReadOnlySet<ConstraintModuleKind> Modules,
    IReadOnlySet<ScheduleProblemClass> ProblemClasses, IReadOnlySet<SolverGuarantee> Guarantees, int MaximumWorkBudget)
{
    internal static SolverBackend Finite(string id, SolverGuarantee guarantee) => new(id,
        Enum.GetValues<ConstraintModuleKind>().ToFrozenSet(),
        new[] { ScheduleProblemClass.Project, ScheduleProblemClass.JobShop }.ToFrozenSet(),
        new[] { guarantee }.ToFrozenSet(), int.MaxValue);

    /// <summary>Refuses a request beyond the backend's declared problem, guarantee or work budget.</summary>
    public void Validate(ScheduleProblemClass problemClass, SolverGuarantee guarantee, int budget)
    {
        if (!ProblemClasses.Contains(problemClass))
            throw new UnsupportedProfileException($"PROBLEM_CLASS_UNSUPPORTED: {problemClass}; backend {Id}; required: a supported problem class ({string.Join(", ", ProblemClasses.Order())}).");
        if (!Guarantees.Contains(guarantee))
            throw new UnsupportedProfileException($"GUARANTEE_UNSUPPORTED: {guarantee}; backend {Id}; required: an offered guarantee ({string.Join(", ", Guarantees.Order())}).");
        if (budget < 0 || budget > MaximumWorkBudget)
            throw new UnsupportedProfileException($"WORK_BUDGET_UNSUPPORTED: {budget}; backend {Id}; required: budget in 0..{MaximumWorkBudget}.");
    }
}
