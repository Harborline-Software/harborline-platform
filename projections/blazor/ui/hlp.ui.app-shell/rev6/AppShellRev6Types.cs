namespace Harborline.UIAdapters.Blazor.Components.Layout;

public sealed record AppShellRev6Owner(string Person, string Tenant, string Workspace);
public sealed record AppShellRev6Tab(string Id, string Kind, string Target, bool Preview, bool Pinned, bool Dirty);
public sealed record AppShellRev6SaveRequest(AppShellRev6Owner Owner, string Id, long Generation, long RequestId);
// This contract contains only the current presentation. Private owner maps stay in the host.
public sealed record AppShellRev6Presentation(AppShellRev6Owner Owner, string Destination,
    AppShellRev6Tab[] Tabs, string? Active, string? PendingClose, AppShellRev6SaveRequest? SaveRequest);
public enum AppShellRev6CloseChoice { Save, Discard, Cancel }
public sealed record AppShellRev6Intent(string Type, string? Id = null, AppShellRev6CloseChoice? Choice = null,
    string? Kind = null, string? Target = null);
