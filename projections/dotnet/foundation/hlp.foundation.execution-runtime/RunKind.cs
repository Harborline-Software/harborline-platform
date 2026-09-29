using System.Text.RegularExpressions;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>
/// The kind of run an engine registers with the substrate, for example <c>workflow-run</c> or
/// <c>plan-run</c> (DES-0056 <c>execution-runtime-cc-5</c>). A lower-case kebab token.
/// </summary>
public readonly partial record struct RunKind
{
    private const int MaxLength = 64;

    /// <summary>Creates a run kind, refusing anything but a lower-case kebab token of at most 64 characters.</summary>
    public RunKind(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength || !KebabToken().IsMatch(value))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunKindInvalid,
                $"'{value}' is not a lower-case kebab run kind.");
        }

        Value = value;
    }

    /// <summary>The registered name.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KebabToken();
}
