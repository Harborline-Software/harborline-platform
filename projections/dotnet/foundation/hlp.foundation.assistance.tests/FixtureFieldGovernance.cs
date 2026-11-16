using Harborline.Contracts.Fields;
using Harborline.Foundation.Assistance;

namespace Harborline.Foundation.Assistance.Tests;

// A host's field store in miniature: records.customer.status is ungoverned, email is personal, credit_limit
// confidential and tax_id masked. Each governed field also carries a classification id, which must never admit it.
internal sealed class FixtureFieldGovernance(params (string Definition, string Field, FieldGovernanceDefinition Governance)[] extra) : IAssistanceFieldGovernance
{
    private readonly Dictionary<(string, string), FieldGovernanceDefinition> fields = new[]
    {
        ("records.customer", "status", new FieldGovernanceDefinition(false, false, false, null)),
        ("records.customer", "email", new FieldGovernanceDefinition(true, false, false, "restricted")),
        ("records.customer", "credit_limit", new FieldGovernanceDefinition(false, true, false, "restricted")),
        ("records.customer", "tax_id", new FieldGovernanceDefinition(false, false, true, "restricted")),
    }.Concat(extra).ToDictionary(item => (item.Item1, item.Item2), item => item.Item3);

    public FieldGovernanceDefinition? Resolve(string definition, string field) => fields.GetValueOrDefault((definition, field));
}
