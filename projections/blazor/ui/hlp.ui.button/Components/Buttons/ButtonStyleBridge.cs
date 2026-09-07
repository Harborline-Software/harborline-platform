using Harborline.Foundation.Enums;

namespace Harborline.UIAdapters.Blazor.Components.Buttons;

internal interface IButtonStyleBridge
{
    string ButtonClass(ButtonVariant variant, ButtonSize size, FillMode fillMode, RoundedMode rounded, bool isInert);
    string LoadingIndicatorClass();
}

internal sealed class CanonicalButtonStyleBridge : IButtonStyleBridge
{
    public string ButtonClass(ButtonVariant variant, ButtonSize size, FillMode fillMode, RoundedMode rounded, bool isInert) =>
        string.Join(" ", new[]
        {
            "hl-button",
            $"hl-button--{variant.ToString().ToLowerInvariant()}",
            $"hl-button--size-{size.ToString().ToLowerInvariant()}",
            $"hl-button--fill-{fillMode.ToString().ToLowerInvariant()}",
            $"hl-button--rounded-{rounded.ToString().ToLowerInvariant()}",
            isInert ? "hl-button--inert" : null,
        }.Where(value => value is not null));

    public string LoadingIndicatorClass() => "hl-button__loading-indicator--small";
}
