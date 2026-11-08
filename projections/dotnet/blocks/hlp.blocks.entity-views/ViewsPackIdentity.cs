namespace Harborline.Blocks.EntityViews;

/// <summary>
/// Views' pack wire identities (DES-0017 <c>views-ck-1</c>): <see cref="ViewDefinition"/> is content kind 10 under the Views
/// primitive bucket 9. Both are the registry's existing wire values and are never renumbered (DES-0017 board F1).
/// </summary>
public static class ViewsPackIdentity
{
    /// <summary>The content kind a <see cref="ViewDefinition"/> travels as.</summary>
    public const int ContentKind = 10;

    /// <summary>The Views primitive bucket.</summary>
    public const int Primitive = 9;
}
