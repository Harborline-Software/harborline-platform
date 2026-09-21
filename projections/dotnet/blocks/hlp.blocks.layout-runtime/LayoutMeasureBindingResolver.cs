using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.MeasureCatalogue;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>
/// Places one observe-measure result without taking over measure resolution, authorization,
/// narrowing, dispatch, aggregation, or typed-result ownership from the shared catalogue.
/// </summary>
public sealed class LayoutMeasureBindingResolver
{
    private readonly IMeasureCatalogue _catalogue;

    /// <summary>Creates the bounded adapter over the one shared measure evaluation path.</summary>
    public LayoutMeasureBindingResolver(IMeasureCatalogue catalogue)
    {
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    /// <summary>
    /// Resolves one measure block using its effective intent, the caller's trusted request,
    /// and binding pointer.
    /// </summary>
    public async ValueTask<LayoutMeasureBindingResolution> ResolveAsync(
        LayoutBlock block,
        LayoutIntent surfaceDefaultIntent,
        string bindingPointer,
        MeasureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingPointer);
        ArgumentNullException.ThrowIfNull(request);

        var bindingKind = KindOf(block.Binding);
        var effectiveIntent = block.Intent ?? surfaceDefaultIntent;
        if (effectiveIntent != LayoutIntent.Observe || block.Binding is not LayoutMeasureBinding measure)
        {
            return new LayoutBindingRefusal(
                block.Id,
                bindingKind,
                bindingPointer,
                LayoutBindingCodes.Unsupported);
        }

        try
        {
            var reference = new MeasureRef(measure.MeasurePath);
            var result = await _catalogue
                .EvaluateAsync(reference, request, cancellationToken)
                .ConfigureAwait(false);
            return new LayoutMeasureBindingResult(block.Id, bindingKind, result);
        }
        catch (MeasureException exception)
        {
            return new LayoutBindingRefusal(
                block.Id,
                bindingKind,
                bindingPointer,
                LayoutBindingCodes.Unresolvable,
                exception.Code,
                exception.Pointer);
        }
    }

    private static string KindOf(LayoutBinding binding) => binding switch
    {
        LayoutRecordFieldBinding => "record_field",
        LayoutQueryBinding => "query",
        LayoutMeasureBinding => "measure",
        LayoutTemplateBinding => "template",
        LayoutStaticBinding => "static",
        _ => "unknown",
    };
}
