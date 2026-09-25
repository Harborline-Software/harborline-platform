
using Harborline.Foundation.RuleEngine.Compilation;

namespace Harborline.Foundation.RuleEngine.Standings;

/// <summary>A domain-declared position computed from one record type's own fields. A fact, never a role.</summary>
public readonly record struct StandingReference
{
    /// <summary>Creates a standing name.</summary>
    public StandingReference(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The domain-declared standing name.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The set-facing Rules shape (DES-0018 <c>rules-ck-21</c>): an ordinary versioned definition that computes a
/// standing from declared fields of one record type. Its predicate is a schema-scoped
/// <c>harborline-jsonlogic/v1</c> validation rule, admitted by the compiler when the definition is made, and
/// its declared inputs are exactly the fields that predicate reads.
/// </summary>
public sealed record StandingRuleDefinition
{
    /// <summary>Creates and validates a standing rule; refuses by <see cref="ArgumentException"/>.</summary>
    public StandingRuleDefinition(string ruleId, string ruleVersion, StandingReference standing, string recordType,
        IReadOnlyList<string> inputFields, RuleDefinition predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentNullException.ThrowIfNull(inputFields);
        ArgumentNullException.ThrowIfNull(predicate);
        if (standing == default) throw new ArgumentException("A standing name is required.", nameof(standing));
        if (inputFields.Count == 0 || inputFields.Any(string.IsNullOrWhiteSpace)
            || inputFields.Distinct(StringComparer.Ordinal).Count() != inputFields.Count)
            throw new ArgumentException("Input fields must be one or more unique, non-empty names.", nameof(inputFields));
        if (predicate.Tier != RuleTier.JsonLogic || predicate.Action != RuleActionKind.Validate || predicate.Scope != RuleScope.Schema)
            throw new ArgumentException("A standing predicate is a schema-scoped harborline-jsonlogic/v1 validation rule.", nameof(predicate));
        if (!string.Equals(predicate.Id, ruleId, StringComparison.Ordinal))
            throw new ArgumentException("The predicate id must equal the standing rule id.", nameof(predicate));

        var references = RuleCompiler.Compile([predicate]).Rules.Single().References;
        if (references.Any(reference => reference is not FieldRef))
            throw new ArgumentException("A standing predicate reads only top-level record fields.", nameof(predicate));
        if (!references.OfType<FieldRef>().Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal).SetEquals(inputFields))
            throw new ArgumentException("Input fields must name exactly the record fields the predicate reads.", nameof(inputFields));

        (RuleId, RuleVersion, Standing, RecordType, InputFields, Predicate) =
            (ruleId, ruleVersion, standing, recordType, inputFields.ToArray(), predicate);
    }

    /// <summary>The stable rule identity evidence and traces name.</summary>
    public string RuleId { get; }

    /// <summary>The installed rule version evidence and traces name.</summary>
    public string RuleVersion { get; }

    /// <summary>The standing carried when the predicate passes.</summary>
    public StandingReference Standing { get; }

    /// <summary>The record type this rule is declared on.</summary>
    public string RecordType { get; }

    /// <summary>The complete set of record fields the predicate reads.</summary>
    public IReadOnlyList<string> InputFields { get; }

    /// <summary>The closed-grammar predicate.</summary>
    public RuleDefinition Predicate { get; }
}
