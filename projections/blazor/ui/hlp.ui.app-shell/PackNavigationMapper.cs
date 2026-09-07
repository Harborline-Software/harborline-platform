using Harborline.Contracts.Authorization;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

public static class PackNavigationMapper
{
    /// <summary>The only Blazor projection mapper from the api#58 declaration into shell render state.</summary>
    public static ShellNavigationViewModel Map(PackNavigationDeclaration declaration, Func<string, string>? resolveLabel, ShellNavigationState? state, RoleVocabulary vocabulary, HeldRoleSet heldRoles)
    {
        var label = resolveLabel ?? (key => key);
        state ??= new();
        ShellActionViewModel Action(PackNavigationAction entry) => new(entry.Id, label(entry.VerbKey), entry.Icon, entry.Binding, entry.Shortcut);
        bool Permitted(PackNavigationAction entry)
        {
            if (entry.PermittedRoles.Count == 0) return true;
            var roles = entry.PermittedRoles.Select(ParseRole).ToArray();
            return roles.All(role => role is not null) && RoleGateResolver.Allows(new(roles!), vocabulary, heldRoles);
        }
        var workspaces = declaration.SeedWorkspaces.Select(workspace =>
        {
            var declared = workspace.CreateActions ?? [];
            var allowed = declared.Where(Permitted).Select(Action).ToArray();
            var guidance = declared.Where(entry => !Permitted(entry)).Select(entry => state.CapabilityGuidanceByBinding?.GetValueOrDefault(entry.Binding)).FirstOrDefault(value => value is not null);
            return new ShellWorkspaceViewModel(workspace.Id, label(workspace.LabelKey), (workspace.Groups ?? []).Select(group => new ShellGroupViewModel(group.Id, label(group.LabelKey), group.ItemIds.Select(id => state.Items?.GetValueOrDefault(id) ?? new ShellNavItem(id, id)).ToArray(), group.AddAction is not null && Permitted(group.AddAction) ? Action(group.AddAction) : null)).ToArray(), workspace.CountQueryRef is null ? null : state.Counts?.GetValueOrDefault(workspace.CountQueryRef), allowed, state.DefaultCreateActionByWorkspace?.GetValueOrDefault(workspace.Id), guidance, workspace.DocumentSpine ?? [], state.RecentByWorkspace?.GetValueOrDefault(workspace.Id) ?? [], state.SuggestedByWorkspace?.GetValueOrDefault(workspace.Id));
        }).ToArray();
        var modes = (declaration.ModeSwitch?.Modes ?? []).Select(mode => (mode.Id, label(mode.LabelKey), mode.WorkspaceIds)).ToArray();
        return new(workspaces, modes, declaration.PanelSet ?? []);
    }

    private static RoleReference? ParseRole(string value)
    {
        var separator = value.LastIndexOf('/');
        if (separator <= 0 || separator == value.Length - 1) return null;
        var vocabulary = value[..separator];
        return vocabulary is RoleVocabularies.Platform or RoleVocabularies.Domain ? new(vocabulary, value[(separator + 1)..]) : null;
    }
}
