using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.DocumentsEnvelope.BinaryCompatibility;

/// <summary>Compiled only against the previous BuilderDefinitions binary fixture.</summary>
public static class PreviousBuilderDefinitionsConsumer
{
    /// <summary>Constructs and reads the pre-forwarding envelope contract.</summary>
    public static string ConstructAndRead()
    {
        var refusal = new DefinitionRefusal("documents.fixture", "/fixture");
        var exception = new DefinitionRefusalException(DefinitionAdmissionPhase.Publish, [refusal]);
        return $"{exception.Stage}:{exception.Refusals.Single().Code}:{exception.Refusals.Single().Pointer}";
    }
}
