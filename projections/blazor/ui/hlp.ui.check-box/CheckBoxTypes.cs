namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>Tri-state checkbox value; user activation always emits a Boolean next value.</summary>
public enum CheckBoxState
{
    /// <summary>The box is not selected.</summary>
    Unchecked,
    /// <summary>The box is selected.</summary>
    Checked,
    /// <summary>The box shows an indeterminate state, for a partly selected group.</summary>
    Mixed
}

/// <summary>Logical label placement around the checkbox.</summary>
public enum CheckBoxLabelPlacement
{
    /// <summary>Places the label before the box.</summary>
    Before,
    /// <summary>Places the label after the box.</summary>
    After
}

/// <summary>Presentation-only checkbox size.</summary>
public enum CheckBoxSize
{
    /// <summary>Renders the compact checkbox size for dense layouts.</summary>
    Small,
    /// <summary>Renders the standard checkbox size.</summary>
    Medium,
    /// <summary>Renders the large checkbox size for easier targeting.</summary>
    Large
}
