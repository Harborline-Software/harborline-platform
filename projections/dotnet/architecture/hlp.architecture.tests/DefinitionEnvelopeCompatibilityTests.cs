using System.Reflection;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.DocumentsEnvelope.BinaryCompatibility;

using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>Q48/T-724 ruling 116: the public envelope has one foundation home and legacy callers bind through forwarding.</summary>
public sealed class DefinitionEnvelopeCompatibilityTests
{
    [Fact]
    public void Previous_builder_definitions_fixture_runs_against_forwarded_envelope()
    {
        var result = PreviousBuilderDefinitionsConsumer.ConstructAndRead();

        Assert.Equal("Publish:documents.fixture:/fixture", result);
        Assert.Equal("Harborline.Foundation.Definitions", typeof(DefinitionRefusalException).Assembly.GetName().Name);
    }

    [Fact]
    public void Layout_calendar_rules_and_documents_share_the_foundation_envelope_identity()
    {
        var expected = typeof(DefinitionRefusalException);
        Assert.Equal("Harborline.Foundation.Definitions", expected.Assembly.GetName().Name);

        foreach (var producer in new[]
        {
            typeof(Harborline.Blocks.BuilderDefinitions.LayoutDefinitionAdmission).Assembly,
            typeof(Harborline.Blocks.Calendar.Booking.BookingDefinitionPackage).Assembly,
            typeof(Harborline.Blocks.BuilderDefinitions.RuleDefinitionCatalog).Assembly,
            typeof(Harborline.Foundation.Documents.TemplateDefinitionAdmission).Assembly,
        })
        {
            Assert.Same(expected, ResolveEnvelopeType(producer));
        }

        Assert.Same(typeof(DefinitionAdmissionPhase), Type.GetType(
            "Harborline.Blocks.BuilderDefinitions.DefinitionAdmissionPhase, Harborline.Foundation.Definitions", throwOnError: true));
    }

    private static Type ResolveEnvelopeType(Assembly producer)
    {
        var envelopeAssemblyName = Assert.Single(producer.GetReferencedAssemblies(), reference =>
            string.Equals(reference.Name, "Harborline.Foundation.Definitions", StringComparison.Ordinal));
        var envelopeAssembly = Assembly.Load(envelopeAssemblyName);
        return envelopeAssembly.GetType(typeof(DefinitionRefusalException).FullName!, throwOnError: true)!;
    }
}
