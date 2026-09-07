namespace Harborline.UIAdapters.Blazor.Components.Forms.Inputs;

public enum SelectFieldSize { Sm, Md, Lg }
public sealed record SelectOption(string Value, string Label, bool Disabled = false);
