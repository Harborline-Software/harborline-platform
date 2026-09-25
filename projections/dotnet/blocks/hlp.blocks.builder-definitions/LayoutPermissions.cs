using Harborline.Contracts.Authorization;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The capabilities Layout declares to Access (DES-0052 §6, T-724 ruling 76). There is no
/// <c>layout:submit</c>: a submit is subject to the surface's submit gate plus the existing Records
/// write check, never a capability the surface declares for itself (DES-0032 §4).
/// </summary>
public static class LayoutPermissions
{
    /// <summary>Author a surface: a configurer act, over the author's required Access port (T-724 ruling 75).</summary>
    public const string Author = "layout:author";

    /// <summary>Publish a surface, gated the same way.</summary>
    public const string Publish = "layout:publish";

    /// <summary>Open a published surface: the authorization gate <c>layout-eng-26</c>.</summary>
    public const string Open = "layout:open";

    /// <summary>Layout's declarations for Access's capability register, each at version 1.</summary>
    public static IReadOnlyList<AuthorizationCapabilityDefinition> Declarations { get; } =
        [new(new(Author), 1), new(new(Publish), 1), new(new(Open), 1)];
}
