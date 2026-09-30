using System.Reflection;

using Harborline.Foundation.Definitions;

using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// Owner ruling (T-572 Q1, ck-8 proposal): every envelope-bearing kind carries the contract, and every
/// future <c>*DefinitionEnvelope</c> type must too. This fence loads every shipped production assembly
/// and refuses a type matching that name pattern which lacks a required, no-default, nullable
/// <see cref="DefinitionContractVersion"/> <c>Contract</c> member.
/// </summary>
public sealed class DefinitionEnvelopeContractArchitectureTests
{
    // The kinds that carry the contract as a typed *DefinitionEnvelope* record today: the ruling's seven, plus Reports
    // (T-489 S4a), which arrived after it and must carry the contract like any future envelope. Booking's
    // Resource/Bookable kind is the ruling's eighth kind, but it has no typed envelope -- its shape is a
    // JSON allow-list (BookingDefinitionAdmission.EnvelopeMembers) that already carries "contract" -- so it
    // is outside this reflection scan by construction, not by oversight.
    private static readonly string[] KnownKinds =
    [
        "LayoutDefinitionEnvelope",
        "ViewDefinitionEnvelope",
        "DataExchangeDefinitionEnvelope",
        "TemplateDefinitionEnvelope",
        "RuleDefinitionEnvelope",
        "AssistanceDefinitionEnvelope",
        "TaxonomyDefinitionEnvelope",
        "ReportDefinitionEnvelope",
    ];

    [Fact]
    public void EveryDefinitionEnvelopeTypeHasARequiredNoDefaultContract()
    {
        var envelopeTypes = ScanForDefinitionEnvelopeTypes(AppContext.BaseDirectory);

        Assert.NotEmpty(envelopeTypes);
        var names = envelopeTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var kind in KnownKinds)
            Assert.True(names.Contains(kind), $"Expected known envelope kind {kind} to still be found by the scan.");

        foreach (var type in envelopeTypes)
        {
            var parameter = RequiredContractParameter(type);
            Assert.True(parameter is not null,
                $"{type.FullName} is named *DefinitionEnvelope but has no positional constructor parameter " +
                "named Contract of type DefinitionContractVersion?.");
            Assert.False(parameter!.HasDefaultValue,
                $"{type.FullName}.Contract must be a required positional parameter with no default.");
        }
    }

    private static ParameterInfo? RequiredContractParameter(Type type)
    {
        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();
        return constructor?.GetParameters().FirstOrDefault(parameter =>
            parameter.Name == "Contract" && parameter.ParameterType == typeof(DefinitionContractVersion));
    }

    private static Type[] ScanForDefinitionEnvelopeTypes(string binDirectory) =>
        Directory.EnumerateFiles(binDirectory, "Harborline.*.dll")
            .Where(file => !Path.GetFileNameWithoutExtension(file).EndsWith(".Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsPublic && type.Name.EndsWith("DefinitionEnvelope", StringComparison.Ordinal))
            .ToArray();
}
