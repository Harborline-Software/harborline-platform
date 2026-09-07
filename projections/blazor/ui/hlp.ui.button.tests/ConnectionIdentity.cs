using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// The property every JavaScript-connected element in a Blazor projection obeys: a connection is
/// keyed to the ELEMENT it was handed, never to the boolean that decides whether that element is
/// rendered. Asserted after every render of every render-branch transition a component has:
///   * while the element is rendered, the newest connection holds the element rendered NOW,
///     compared by the captured <see cref="ElementReference.Id"/> — by value, not by reference;
///   * every superseded connection was disposed, and none is left live once the element is gone.
/// The render predicate is only a proxy for element identity, and a branch swap is where that proxy
/// lies: the boolean stays put while the whole subtree is replaced.
/// Shared by the four connection-identity properties (window, sheet, popover, data-export-button)
/// and modelled on <c>DetailPanelConnectionIdentityTests</c>, which fixed this defect first.
/// </summary>
internal static class ConnectionIdentity
{
    /// <summary>The id of the element a component currently holds in a private field or property.</summary>
    public static string? ElementId(object component, string member)
    {
        var type = component.GetType();
        var value = type.GetField(member, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component)
            ?? type.GetProperty(member, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component);
        if (value is null) throw new InvalidOperationException($"{type.Name} has no member '{member}'");
        return ((ElementReference)value).Id;
    }

    /// <summary>
    /// The invariant, for one connected element (identified by its position in the connect call)
    /// after one render. <paramref name="renderedElementId"/> is null when the element is not rendered.
    /// </summary>
    public static void Assert(BunitJSInterop module, int argument, string? renderedElementId, string where)
    {
        var connects = module.Invocations["connect"]
            .Select(invocation => ((ElementReference)invocation.Arguments[argument]!).Id).ToList();
        var disposes = module.Invocations["dispose"].Count();

        // Deliberately NOT asserted: that every connect is handed a distinct element. Unlike the
        // detail panel, these modules keep an element across some transitions and legitimately
        // reconnect it (the sheet portal survives a modal flip; the export root survives a
        // close/reopen), so distinctness would be false for correct code. What the leak clause below
        // actually needs is that no two connections to it are live at once, which the dispose count
        // states directly; "does it reconnect when nothing moved?" is a separate per-module test.
        if (renderedElementId is not null)
        {
            Xunit.Assert.True(connects.Count > 0, $"{where}: the rendered element was never connected");
            Xunit.Assert.True(renderedElementId == connects[^1],
                $"{where}: the live connection holds a replaced element, not the one rendered now");
            Xunit.Assert.True(connects.Count - 1 == disposes, $"{where}: a superseded connection was not disposed");
        }
        else
        {
            Xunit.Assert.True(connects.Count == disposes, $"{where}: a connection outlived its element");
        }
    }
}
