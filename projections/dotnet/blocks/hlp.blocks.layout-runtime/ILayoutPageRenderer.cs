namespace Harborline.Blocks.LayoutRuntime;

/// <summary>
/// Library-neutral boundary for a consumer that paints already-fragmented Layout pages. The
/// runtime owns flow and page selection; a host adapter may turn this result into pixels or bytes.
/// </summary>
public interface ILayoutPageRenderer
{
    /// <summary>Renders the immutable page fragments without changing their order or geometry.</summary>
    ValueTask<LayoutPageRenderResult> RenderAsync(
        IReadOnlyList<LayoutPageFragment> fragments,
        CancellationToken cancellationToken = default);
}

/// <summary>One host-produced page representation. Its payload format belongs to that host.</summary>
public sealed record LayoutRenderedPage(int Number, ReadOnlyMemory<byte> Payload, string ContentType);

/// <summary>The ordered host result from a page renderer.</summary>
public sealed record LayoutPageRenderResult(IReadOnlyList<LayoutRenderedPage> Pages);
