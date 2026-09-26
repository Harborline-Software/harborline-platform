using System.Reflection;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// rules-eng-28 (owner ruling Q32, recorded as T-724 ruling 79): there is no default refusal
/// stage. Every refusal names its <see cref="DefinitionAdmissionPhase"/> explicitly.
/// </summary>
public sealed class RulesEng28Tests
{
    [Fact(DisplayName = "rules-eng-28: no refusal helper or constructor has a default value for its DefinitionAdmissionPhase parameter")]
    public void No_admission_phase_parameter_has_a_default()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = typeof(DefinitionAdmissionPhase).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)));
        var offenders = members
            .SelectMany(member => member.GetParameters(), (member, parameter) => (member, parameter))
            .Where(pair => pair.parameter.ParameterType == typeof(DefinitionAdmissionPhase) && pair.parameter.HasDefaultValue)
            .Select(pair => $"{pair.member.DeclaringType?.FullName}.{pair.member.Name}({pair.parameter.Name} = {pair.parameter.DefaultValue})")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Found a defaulted DefinitionAdmissionPhase parameter (every refusal must name its stage explicitly):\n"
            + string.Join('\n', offenders));
    }

    [Fact(DisplayName = "rules-eng-28: a store read refusal outside any admission still reports the stage the read composes at")]
    public async Task Store_read_refusal_reports_its_explicit_stage()
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>());
        var key = new DefinitionKey("tenant-a", DefinitionKind.Rules, "rule-a");

        // ListHistoryAsync is a plain read, never gated by an admission. Its registry-unknown
        // refusal reports Author because every current caller composes authoring views.
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await store.ListHistoryAsync(key));
        Assert.Equal(DefinitionAdmissionPhase.Author, refusal.Stage);
    }
}
