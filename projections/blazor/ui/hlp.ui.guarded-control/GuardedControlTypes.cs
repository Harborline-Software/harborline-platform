namespace Harborline.UIAdapters.Blazor.Components.Buttons;

public enum GuardedControlState { Covered, Armed, Committing }
public enum GuardedControlCommitOutcome { Completed, Rejected }
public enum GuardedControlRecoveryReason { Escape, Timeout, Navigation, DocumentHidden, WindowBlur, BecameDisabled }

public abstract record GuardedControlEvent;
public sealed record GuardedControlTransition(GuardedControlState Previous, GuardedControlState Next, string Cause) : GuardedControlEvent;
public sealed record GuardedControlRecovered(GuardedControlRecoveryReason Reason) : GuardedControlEvent;
public sealed record GuardedControlCommitSettled(GuardedControlCommitOutcome Outcome) : GuardedControlEvent;
