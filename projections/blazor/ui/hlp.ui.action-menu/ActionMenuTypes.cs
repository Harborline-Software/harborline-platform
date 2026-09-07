using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Buttons;

public abstract record ActionMenuEntry;

/// <summary>
/// disclosure-model.md conformance/hlp.ui.action-menu/disclosure-v1.json. <paramref name="ShortcutHint"/>
/// is declared as data and rendered by the shared token (right aligned, monospace); declaring
/// <paramref name="Checked"/> makes the entry a toggle that carries a check mark and reports
/// through OnSelect, the one callback every entry reports through. The visible hint is decorative
/// (aria-hidden); <paramref name="KeyShortcuts"/> carries the same chord to assistive technology in
/// the WAI-ARIA 1.2 §6.6.7 aria-keyshortcuts format (UI Events KeyboardEvent.key names).
/// </summary>
public sealed record ActionMenuItem(string Id, string Label, bool Disabled = false, bool Destructive = false, RenderFragment? Icon = null, string? ShortcutHint = null, bool? Checked = null, string? KeyShortcuts = null) : ActionMenuEntry;
public sealed record ActionMenuSeparator : ActionMenuEntry;
public enum ActionMenuAlignment { Left, Right }

/// <summary>
/// disclosure-model.md §5 - one home per scope. The claim table is keyed on the shell instance
/// itself (the circuit's IJSRuntime: one per Blazor circuit, one per bUnit context), so the fence
/// needs no service registration and cannot be missed by a route that forgot to register one.
/// </summary>
public static class ActionMenuScopes
{
    /// <summary>
    /// The name a scope host cascades its scope id under: a window, a panel, a detail-panel section
    /// wraps its content in <c>&lt;CascadingValue Name="@ActionMenuScopes.CascadingName" Value="..."&gt;</c>.
    /// A menu resolves its scope from its own ScopeId, else the nearest host, else <see cref="RootScopeId"/>.
    /// </summary>
    public const string CascadingName = "HarborlineActionMenuScope";

    /// <summary>
    /// The shell's own root surface. The derivation chain terminates here, so every composed menu
    /// carries a scope and the one-home fence can never be bypassed by omitting the parameter.
    /// </summary>
    public const string RootScopeId = "window";

    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<string, byte>> Shells = new();

    public static IDisposable Claim(object shell, string scopeId)
    {
        var claimed = Shells.GetOrCreateValue(shell);
        if (!claimed.TryAdd(scopeId, 0)) throw new InvalidOperationException("action-menu-scope-taken");
        return new Release(claimed, scopeId);
    }

    private sealed class Release(ConcurrentDictionary<string, byte> claimed, string scopeId) : IDisposable
    {
        public void Dispose() => claimed.TryRemove(scopeId, out _);
    }
}
