using System.Collections.Concurrent;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>
/// One engine's registration of its run kind. The engine keeps its own state (a workflow's position and
/// iteration counter) in its own store; the substrate holds only identity, status, attempts and dead-letter.
/// </summary>
/// <param name="Kind">The run kind the engine produces.</param>
/// <param name="OwningEngine">The engine that owns the kind, for operators reading a run.</param>
public sealed record RunKindRegistration(RunKind Kind, string OwningEngine);

/// <summary>The run kinds engines have registered. A kind is registered once, by one engine.</summary>
public sealed class RunKindRegistry
{
    private readonly ConcurrentDictionary<RunKind, RunKindRegistration> _registrations = new();

    /// <summary>Registers a run kind, refusing a kind that is already registered.</summary>
    public RunKindRegistration Register(RunKind kind, string owningEngine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owningEngine);
        if (kind.Value is null)
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunKindInvalid, "A default run kind cannot be registered.");
        }

        var registration = new RunKindRegistration(kind, owningEngine);
        if (!_registrations.TryAdd(kind, registration))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunKindDuplicate,
                $"Run kind '{kind}' is already registered by '{_registrations[kind].OwningEngine}'.");
        }

        return registration;
    }

    /// <summary>The registration for a kind, refusing a kind no engine registered.</summary>
    public RunKindRegistration Require(RunKind kind) =>
        kind.Value is not null && _registrations.TryGetValue(kind, out var registration)
            ? registration
            : throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunKindUnregistered,
                $"No engine registered run kind '{kind}'.");

    /// <summary>Every registration, ordered by kind.</summary>
    public IReadOnlyList<RunKindRegistration> Registrations =>
        _registrations.Values.OrderBy(registration => registration.Kind.Value, StringComparer.Ordinal).ToList();
}
