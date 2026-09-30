namespace Harborline.UIAdapters.Blazor.Components.Buttons;

/// <summary>The interaction stage of a guarded control: covered, armed, or committing.</summary>
public enum GuardedControlState
{
    /// <summary>The control is covered and cannot be committed yet.</summary>
    Covered,
    /// <summary>The control is uncovered and ready for the confirming action.</summary>
    Armed,
    /// <summary>The guarded change is being committed.</summary>
    Committing
}
/// <summary>How a guarded control's commit ended.</summary>
public enum GuardedControlCommitOutcome
{
    /// <summary>The guarded change was committed.</summary>
    Completed,
    /// <summary>The guarded change was refused and not applied.</summary>
    Rejected
}
/// <summary>Why an armed guarded control fell back to its covered state.</summary>
public enum GuardedControlRecoveryReason
{
    /// <summary>The user pressed Escape and the control re-covered.</summary>
    Escape,
    /// <summary>The arming window expired and the control re-covered.</summary>
    Timeout,
    /// <summary>The user navigated away and the control re-covered.</summary>
    Navigation,
    /// <summary>The page was hidden, for example a tab switch, and the control re-covered.</summary>
    DocumentHidden,
    /// <summary>The window lost focus and the control re-covered.</summary>
    WindowBlur,
    /// <summary>The control became disabled and re-covered.</summary>
    BecameDisabled
}

/// <summary>Base type for events raised by a guarded control.</summary>
public abstract record GuardedControlEvent;
/// <summary>Raised when a guarded control moves between states, with the previous state, the next state and the cause.</summary>
public sealed record GuardedControlTransition(GuardedControlState Previous, GuardedControlState Next, string Cause) : GuardedControlEvent;
/// <summary>Raised when a guarded control recovers, with the reason.</summary>
public sealed record GuardedControlRecovered(GuardedControlRecoveryReason Reason) : GuardedControlEvent;
/// <summary>Raised when the commit of a guarded control settles, with its outcome.</summary>
public sealed record GuardedControlCommitSettled(GuardedControlCommitOutcome Outcome) : GuardedControlEvent;
