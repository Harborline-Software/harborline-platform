namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>Tri-state checkbox value; user activation always emits a Boolean next value.</summary>
public enum CheckBoxState { Unchecked, Checked, Mixed }

/// <summary>Logical label placement around the checkbox.</summary>
public enum CheckBoxLabelPlacement { Before, After }

/// <summary>Presentation-only checkbox size.</summary>
public enum CheckBoxSize { Small, Medium, Large }
