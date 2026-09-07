using System.Reflection;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Interpreter.Tests;

/// <summary>
/// ADR 0143 SC2 <b>SF-1 GATE</b> (the F-2 analog of the F-1 effect-factory containment test). The seam-review
/// verdict flagged that F-2's execution-store fence is DISCIPLINE-level (the authoring face
/// <see cref="IWorkflowDefinitionStore"/> + the concrete <see cref="InMemoryWorkflowDefinitionStore"/> are
/// public + DI-registered, so nothing STRUCTURALLY prevents an execution-side type from injecting the lenient
/// face) — and required PR (b) to add, with the interpreter, an arch test binding every execution-path type to
/// the re-validating <see cref="IWorkflowDefinitionExecutionStore"/> and FORBIDDING the authoring face /
/// concrete on execution paths.
/// <para>
/// This is that gate: it reflects over the interpreter assembly (the execution path) and asserts NO type there
/// takes a constructor parameter or holds a field of the lenient authoring face / concrete store — an executor
/// can only be HANDED the re-validating <see cref="IWorkflowDefinitionExecutionStore"/>, so "load a definition
/// for execution" can only traverse the fail-closed re-admitting reads (the F-2 "safe path is the only path").
/// </para>
/// </summary>
public sealed class WorkflowExecutionStoreBindingArchitectureTests
{
    private static readonly Assembly ExecutionAssembly = typeof(DeclarativeWorkflowInterpreter).Assembly;

    [Fact]
    public void Execution_path_types_do_not_bind_the_lenient_authoring_store_or_concrete()
    {
        var forbidden = new[] { typeof(IWorkflowDefinitionStore), typeof(InMemoryWorkflowDefinitionStore) };
        var offenders = new List<string>();

        foreach (var type in ExecutionAssembly.GetTypes())
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var p in ctor.GetParameters())
                {
                    if (Array.IndexOf(forbidden, p.ParameterType) >= 0)
                    {
                        offenders.Add($"{type.FullName} ctor parameter '{p.Name}' : {p.ParameterType.Name}");
                    }
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (Array.IndexOf(forbidden, field.FieldType) >= 0)
                {
                    offenders.Add($"{type.FullName} field '{field.Name}' : {field.FieldType.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "ADR 0143 SC2 SF-1 VIOLATED: an execution-path type binds the LENIENT authoring workflow-definition " +
            "store (IWorkflowDefinitionStore) or the concrete InMemoryWorkflowDefinitionStore. Execution paths " +
            "must inject ONLY IWorkflowDefinitionExecutionStore (the fail-closed re-admitting reads), so a " +
            "definition loaded for execution is always re-validated. Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_interpreter_binds_the_re_validating_execution_store()
    {
        // Non-vacuity: the interpreter DOES depend on the execution face — the fence is real, not trivially true
        // because the interpreter loads no definition at all.
        var ctor = typeof(DeclarativeWorkflowInterpreter).GetConstructors().Single();
        Assert.Contains(
            ctor.GetParameters(),
            p => p.ParameterType == typeof(IWorkflowDefinitionExecutionStore));

        Assert.DoesNotContain(
            ctor.GetParameters(),
            p => p.ParameterType == typeof(IWorkflowDefinitionStore));
    }
}
