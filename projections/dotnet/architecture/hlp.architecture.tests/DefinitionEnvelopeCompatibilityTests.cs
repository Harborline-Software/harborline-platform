using System.Reflection;
using System.Runtime.Loader;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.LayoutRuntime;
using Harborline.DocumentsEnvelope.BinaryCompatibility;
using Harborline.Foundation.Documents;

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

        // Consumer.csproj's reference to the fixture DLL is Private=false, so the fixture is never deployed
        // beside this test; the call above can only have executed against whichever Harborline.Blocks.BuilderDefinitions
        // this process already has loaded (the real, rebuilt one, ProjectReferenced by this project like every
        // other block it tests). Prove that assembly forwards the type rather than defining it locally -- a
        // broken or missing forwarder fails here, not silently behind the fixture reference.
        var runtimeBuilderDefinitions = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "Harborline.Blocks.BuilderDefinitions");
        Assert.DoesNotContain(runtimeBuilderDefinitions.DefinedTypes,
            type => type.FullName == typeof(DefinitionRefusalException).FullName);
        Assert.Equal(typeof(DefinitionRefusalException),
            runtimeBuilderDefinitions.GetType(typeof(DefinitionRefusalException).FullName!, throwOnError: true));

        // Prove Consumer.csproj really was compiled against the OLD, non-forwarding shape -- not a same-version
        // rebuild masquerading as "previous". Loaded in its own AssemblyLoadContext purely to inspect its
        // metadata, never executed, so it cannot collide with (or be silently satisfied by) the real assembly
        // already running this test above.
        var isolation = new AssemblyLoadContext(
            nameof(Previous_builder_definitions_fixture_runs_against_forwarded_envelope), isCollectible: true);
        try
        {
            var previousBuild = isolation.LoadFromAssemblyPath(Path.Combine(
                TierDirectionArchitectureTests.RepositoryRoot(),
                "tests", "package-consumers", "documents-envelope-binary-compat", "fixtures",
                "Harborline.Blocks.BuilderDefinitions.PREVIOUS.dll"));
            Assert.Contains(previousBuild.DefinedTypes,
                type => type.FullName == typeof(DefinitionRefusalException).FullName);
        }
        finally
        {
            isolation.Unload();
        }
    }

    [Fact]
    public void Layout_calendar_rules_and_documents_share_the_foundation_envelope_identity()
    {
        var expectedException = typeof(DefinitionRefusalException);
        var expectedRefusal = typeof(DefinitionRefusal);
        Assert.Equal("Harborline.Foundation.Definitions", expectedException.Assembly.GetName().Name);

        // Layout: the open gate refuses a reader who may not open the surface (layout-eng-26).
        var layoutRefusal = Assert.Throws<DefinitionRefusalException>(() =>
            LayoutAuthorityGate.RequireOpen("surface.identity-probe", new DenyAllLayoutAccess()));
        AssertSameEnvelopeType(expectedException, layoutRefusal.GetType());

        // Calendar: BookingDefinitionPackage.Export refuses a revision whose registry kind Booking does not own.
        var calendarRevision = new DefinitionRevision(
            new DefinitionDocument(new DefinitionKey("acme", DefinitionKind.Rules, "not-a-booking"), "v1", "1.0.0", "{}"),
            1, DefinitionStatus.Published, "digest");
        var calendarRefusal = Assert.Throws<DefinitionRefusalException>(() => BookingDefinitionPackage.Export(calendarRevision));
        AssertSameEnvelopeType(expectedException, calendarRefusal.GetType());

        // Rules: RuleDefinitionCatalog.Admit is pure and refuses a document of the wrong registry kind.
        var rulesRefusals = RuleDefinitionCatalog.Admit(
            new DefinitionDocument(new DefinitionKey("acme", DefinitionKind.Bookables, "not-a-rule"), "v1", "1.0.0", "{}"),
            DefinitionAdmissionPhase.Publish);
        AssertSameEnvelopeType(expectedRefusal, Assert.Single(rulesRefusals).GetType());

        // Documents: AdmitCatalogueBody refuses malformed body JSON before touching template or surfaces.
        var noopSurfaces = new TemplateSurfaces(_ => null, (_, _) => [], (_, _) => null, PlatformPackageSeed.ContractWindow);
        var documentsRefusals = TemplateDefinitionAdmission.AdmitCatalogueBody(
            "acme", "not-json-probe", "1.0.0", "not json", publishing: false, noopSurfaces);
        AssertSameEnvelopeType(expectedRefusal, Assert.Single(documentsRefusals).GetType());
    }

    private static void AssertSameEnvelopeType(Type expected, Type actual)
    {
        // Assert.Same alone proves reference identity; AssemblyQualifiedName is asserted too so a failure names
        // which assembly each side actually resolved against, rather than just "not the same object".
        Assert.Same(expected, actual);
        Assert.Equal(expected.AssemblyQualifiedName, actual.AssemblyQualifiedName);
    }

    private sealed class DenyAllLayoutAccess : ILayoutAccess
    {
        public bool CanAuthor() => false;
        public bool CanPublish() => false;
        public bool CanRead(LayoutBinding binding) => false;
        public bool CanOpen(string surfaceId) => false;
    }
}
