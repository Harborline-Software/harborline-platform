# Source-first reusable rev6 frame

Reference the existing aggregate Razor Class Library from a .NET10+ consumer in the same checked-out repository layout. Keep its normal repository toolchain and source closure; do not copy just a generated DLL or gallery code.

Set `HarborlinePlatformSourceRoot` in your consuming app project to the absolute root of the complete Platform checkout, or pass it when building the app:

```sh
dotnet build YourApp.csproj -p:HarborlinePlatformSourceRoot=/absolute/path/to/platform-source
```

The property belongs to the consuming app. Its project reference uses that source root:

```xml
<ItemGroup>
 <ProjectReference Include="$(HarborlinePlatformSourceRoot)/projections/blazor/ui/hlp.ui.button/Harborline.UIAdapters.Blazor.csproj" />
</ItemGroup>
```

```razor
@using Harborline.UIAdapters.Blazor.Components.Layout
@using Harborline.UIAdapters.Blazor.Components.DataDisplay
<link rel="stylesheet" href="./_content/Harborline.UIAdapters.Blazor/data-grid.css" />
<div style="block-size:100dvh">
 <HarborlineAppShellRev6Frame ShellId="workspace" Owner="owner"
                             Presentation="View" Intent="Handle" />
</div>
@code {
 private readonly AppShellRev6Owner owner = new("person", "tenant", "workspace");
 private bool panelOpen;
 private readonly ViewRenderPlan plan = new("example-hash", "example-view", "1",
   "example-pack", "1", "ViewDefinition", new(ViewKindIds.Table,
     new([new("name", "Name")]), []));
 private readonly ViewRuntimeRow[] rows =
   [new("example", new Dictionary<string, object?> { ["name"] = "Example row" })];
 private AppShellRev6FramePresentation View => new(owner,
   AppShellRev6FrameMode.Workspace,
   new("EX", "Example organization", "/", [], "AR", "Example person"),
   new("Records", "Everywhere", []),
   new("Records", "Home / Records", "Search", "Ask or search", []),
   new(owner, AppShellRev6FrameFaceKind.Structured, "records", plan, rows),
   new(owner, "records", [], null, null, null), panelOpen);
 private Task Handle(AppShellRev6FrameIntent request) {
   if (request.Owner != owner) return Task.CompletedTask;
   if (request is AppShellRev6FrameSetPanelOpen panel) panelOpen = panel.Open;
   // Forward navigation/actions/row/tab requests to the application's existing adapter.
   return Task.CompletedTask;
 }
}
```


The outer host supplies the available block size; the reusable frame fills it. Inline size is observed from the root content box. `ShellInlineSize` is only an explicit host-measurement/test input, not a viewport breakpoint. The frame imports `app-shell-rev6-frame.mjs` and `app-shell-rev6-focus.mjs` through the normal `_content/Harborline.UIAdapters.Blazor/` static-web-assets route. The existing DataGrid imports its own component script. Explicitly link `data-grid.css` as shown; do not depend on the aggregate's generic `style.css` filename. Frame chrome and ViewRuntime wrapper/action layout styles travel inside the Razor component. No HeadOutlet, gallery stylesheet, service registration or new project is needed by this frame.

The public frame has exactly ShellId, Owner, Presentation, Intent, ShellInlineSize and TabContent. Caller owns current data, navigation/effects, reducer, permission-filtered rows/counts and save acknowledgment. Requests carry the rendered owner; host handlers must retain their own owner/epoch guards for already-emitted work. Header search is a trigger request, not a local query or search backend. The TabContent fragment is exclusively the existing typed tab-body seam.

Only Structured/ViewDefinition/layout.table with fields and rows selects the existing HarborlineViewRuntime/DataGrid. Zero rows use Empty. Home, Conversation, missing/unsupported plans and Board show `No supported view is available.`; this is not a production Records, arbitrary body renderer or full revision implementation. The example has no simulated save acknowledgment. The gallery's sample records/filter/tools and local700ms acknowledgment stay outside the RCL.

The gallery exposes fixture-only Alice/Bob/sign-out and physical 700px embedding controls outside the reusable frame. The identity control remains available during dirty close, clears the old presentation immediately and cancels caller-owned queued save work. Draft fragments retain their rendered owner and epoch. The embedding control changes the enclosing element's physical width and leaves root observation enabled; it does not supply ShellInlineSize. These controls provide later browser acceptance paths, not browser proof.

The frame owns one main, a non-main tab surface and current-owner phone navigation. Dirty close isolates every background subtree and retains the editor until the caller updates the presentation; geometry does not navigate or steal focus. Panel state remains controlled and hiding preserves mounted editors. The standalone five-parameter TabHost remains supported.

This source delivery has not been compiled, unit/conformance tested or browser qualified by the author. Actual existing-gallery CSS/JS/desktop/narrow acceptance follows a separately admitted exact-source run. No universal host/engine/OS, MAUI, separate-server, publication or full-gate certification is claimed or required for this task's browser-only acceptance.
