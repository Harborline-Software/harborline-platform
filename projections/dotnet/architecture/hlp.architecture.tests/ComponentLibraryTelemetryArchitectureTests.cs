using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class ComponentLibraryTelemetryArchitectureTests
{
    private static readonly string[] Roots = ["react", "blazor", "shared"];
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs", ".cs", ".razor" };
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "tests", "test", "__tests__", "stories" };
    // Raw source text, no comment or string stripping: a mention in a comment or string is a finding too,
    // and only an exact allow-list row admits it. Bare console/Console/sendBeacon tokens keep a comment
    // between the receiver and the member (console /* x */ .log) from slipping past. The browser network sinks
    // (fetch, fetchLater, XMLHttpRequest, WebSocket, WebTransport, RTCPeerConnection, EventSource, sendBeacon, an Image
    // or src/srcset beacon, a form submit, a navigation through location or open, a CSS url() that is not one literal),
    // the .NET network types, and a URL attribute (src, href, action, ...) whose JSX {...} or Razor @ value is not one
    // string literal count as emits too.
    private static readonly Regex Emit = new(@"\b(?:console\b|Console\b|ILogger(?:Factory)?\b|LoggerMessage\b|ActivitySource\b|System\s*\.\s*Diagnostics\s*\.\s*Metrics\b|new\s+Meter\s*\(|Debug\s*\.\s*(?:Write\w*|Print)\b|Trace\s*\.\s*(?:Write\w*|Trace\w*)\b|EventSource\b|sendBeacon\b|fetch\b|fetchLater\b|XMLHttpRequest\b|WebSocket\b|WebTransport\b|RTCPeerConnection\b|requestSubmit\b|submit\s*\(|Image\b|using\s+static\s+System\s*\.\s*Diagnostics\s*\.\s*(?:Debug|Trace)\b"
        + @"|location\s*(?:\??\.\s*[\w$]+\s*)?(?:=(?!=)|\[)|location\s*\??\.\s*(?:assign|replace)\b|open\s*\(|(?:window|self|globalThis)\s*\??\.\s*open\b|url\s*\(\s*(?!(?<q>['""]?)[^'""$@{}()+\r\n]*\k<q>\s*\))"
        + @"|System\s*\.\s*Net\s*\.\s*(?:Http|Sockets|WebSockets)\b|JSImport\b|JSHost\b)|Http(?:Client|MessageHandler)|WebClient|WebRequest|Socket|TcpClient|UdpClient|HttpListener"
        + @"|@opentelemetry/|\.\s*src(?:set)?\s*=(?!=)|setAttribute\s*\(\s*['""`]src(?:set)?['""`]"
        + @"|(?<![\w$.\-])(?i:src|srcset|href|action|formaction|poster|data|ping|background)\s*=\s*(?:\{(?!\s*(?:'[^'\\\r\n]*'|""[^""\\\r\n]*"")\s*\})|""[^""\r\n]*@|'[^'\r\n]*@|@)", RegexOptions.Compiled);
    // Every import, export, require or dynamic import in a JS-family file reads one string-literal specifier from the
    // comment-blanked text; a site that is neither one of these forms nor a plain declaration is a finding.
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs" };
    // A keyword followed by $ or - (data-export, export$) is part of another name; export, a statement keyword, cannot follow a -.
    private static readonly Regex ImportSite = new(@"(?:(?<![\w$.'""`])(?:import|require)|(?<![\w$.'""`\-])export)(?![\w$\-])", RegexOptions.Compiled);
    private const string Literal = @"(?:'(?<spec>[^'\\\r\n]*)'|""(?<spec>[^""\\\r\n]*)""|`(?<spec>[^`$\\]*)`)";
    // The type group marks a type-only import or re-export, which esbuild erases.
    private static readonly Regex ImportForm = new(@"\G(?:import(?<type>\s+type(?=\s*[{*]|\s+(?!from\b)[\w$]))?\s*(?:[\w$]+\s*,?\s*)?(?:\*\s*as\s+[\w$]+\s*|\{[^}]*\}\s*)?from\s*" + Literal
        + @"|import\s*" + Literal + @"|import\s*\(\s*" + Literal + @"\s*[,)]|export(?<type>\s+type)?\s*(?:\*(?:\s*as\s+[\w$]+)?|\{[^}]*\})\s*from\s*" + Literal + @"|require\s*\(\s*" + Literal + @"\s*\))", RegexOptions.Compiled);
    private static readonly Regex NotAnImport = new(@"\G(?:import\s*\.\s*meta\b|import(?:\s+type)?\s+[\w$]+\s*=\s*(?=require\b)|export\s+(?:(?:default|const|let|var|function|class|async|abstract|declare|enum|interface|namespace|module)\b|type\s+[\w$]+\s*[=<])|export\s*=|export(?:\s+type)?\s*\{[^}]*\}(?!\s*from\b)|require\b(?!\s*\())", RegexOptions.Compiled);
    // A Blazor JS interop call: its first argument names the JS function, or is not a literal at all.
    private static readonly Regex Interop = new(@"\bInvoke(?:Void(?:Async)?|Async|\s*(?=<))\s*(?:<(?:[^<>()]|<[^<>()]*>)*>)?\s*\(\s*(?:""(?<name>[^""\\\r\n]*)""|(?<arg>[^\s)]))", RegexOptions.Compiled);
    private static readonly Regex GlobalSink = new(@"\.|^(?:open|eval|Function|import|importScripts|setTimeout|setInterval|postMessage|location|fetch|fetchLater|sendBeacon|XMLHttpRequest|WebSocket|EventSource|WebTransport|RTCPeerConnection|Image|Reflect|globalThis|window|self|document|navigator|top|parent|opener|frames)$", RegexOptions.Compiled);
    private static readonly Regex ContentModule = new(@"\G""\s*,\s*(?:[\w.]+\s*,\s*)?""\./_content/[^""\\\r\n]*""\s*\)", RegexOptions.Compiled);
    private static readonly Regex LiteralCall = new("""^[A-Za-z_][\w.]*\s*\(\s*(?:'(?:[^'\\\r\n]|\\.)*'|"(?:[^"\\\r\n]|\\.)*")(?:\s*,\s*(?:'(?:[^'\\\r\n]|\\.)*'|"(?:[^"\\\r\n]|\\.)*"))*\s*\)\s*;?$""", RegexOptions.Compiled);

    private static readonly string[] ExpectedFindings = [
        "projections/blazor/Directory.Build.targets:2",
        "projections/blazor/Directory.Build.targets:3",
        "projections/blazor/cond/Cond.test.cs:1",
        "projections/blazor/emit.ts:1",
        "projections/blazor/imported/Targets.test.cs:1",
        "projections/blazor/imported/ValueLeak.test.cs:1",
        "projections/blazor/late/Late.test.cs:1",
        "projections/blazor/member/Conditional.test.cs:1",
        "projections/blazor/member/ContentRemoved.test.cs:1",
        "projections/blazor/member/CrossType.test.cs:1",
        "projections/blazor/member/GroupConditional.test.cs:1",
        "projections/blazor/member/ValueLeak.test.cs:1",
        "projections/blazor/member/WhenRemoved.test.cs:1",
        "projections/blazor/pkg/Default.test.cs:1",
        "projections/react/activity.cs:1",
        "projections/react/after-hole.ts:1",
        "projections/react/anchors.ts:1",
        "projections/react/anchors.ts:2",
        "projections/react/apostrophe.tsx:2",
        "projections/react/block.ts:1",
        "projections/react/brace-text.ts:1",
        "projections/react/bypasses.ts:1",
        "projections/react/bypasses.ts:2",
        "projections/react/bypasses.ts:3",
        "projections/react/bypasses.ts:4",
        "projections/react/bypasses.ts:6",
        "projections/react/bypasses.ts:8",
        "projections/react/comment-gap.ts:1",
        "projections/react/comment-gap.ts:2",
        "projections/react/comment-gap.ts:3",
        "projections/react/comment-lines.ts:4",
        "projections/react/console.cs:1",
        "projections/react/console.ts:1",
        "projections/react/console.ts:2",
        "projections/react/debug.razor:1",
        "projections/react/debug.razor:2",
        "projections/react/division-comment.ts:1",
        "projections/react/division-regex.ts:1",
        "projections/react/division-regex.ts:2",
        "projections/react/division.ts:1",
        "projections/react/dot-split.ts:1",
        "projections/react/dup.ts:1",
        "projections/react/dup.ts:2",
        "projections/react/error.cjs:1",
        "projections/react/error.cjs:2",
        "projections/react/event.ts:1",
        "projections/react/factory.tsx:1",
        "projections/react/html.razor:1",
        "projections/react/inert.ts:1",
        "projections/react/interop.razor:5",
        "projections/react/interop.razor:6",
        "projections/react/interop.razor:7",
        "projections/react/interop.razor:8",
        "projections/react/interpolated.cs:1",
        "projections/react/interpolation-hole.cs:1",
        "projections/react/logger.cs:1",
        "projections/react/message.js:1",
        "projections/react/meter.mjs:1",
        "projections/react/metrics.jsx:1",
        "projections/react/module.cts:2",
        "projections/react/module.mts:2",
        "projections/react/nested-follow.ts:1",
        "projections/react/nested-interpolation.cs:1",
        "projections/react/nested-template.ts:1",
        "projections/react/network.ts:1",
        "projections/react/network.ts:2",
        "projections/react/network.ts:3",
        "projections/react/network.ts:4",
        "projections/react/network.ts:5",
        "projections/react/network.ts:6",
        "projections/react/otel-dynamic.mjs:1",
        "projections/react/otel-from.ts:1",
        "projections/react/otel-require.cjs:1",
        "projections/react/otel.js:1",
        "projections/react/regex.ts:1",
        "projections/react/sinks.cs:1",
        "projections/react/sinks.cs:2",
        "projections/react/sinks.cs:3",
        "projections/react/sinks.cs:4",
        "projections/react/sinks.cs:5",
        "projections/react/sinks.cs:6",
        "projections/react/sinks.ts:1",
        "projections/react/sinks.ts:2",
        "projections/react/sinks.ts:3",
        "projections/react/sinks.ts:4",
        "projections/react/sinks.ts:5",
        "projections/react/sinks.ts:6",
        "projections/react/sinks.ts:7",
        "projections/react/sinks.ts:8",
        "projections/react/sinks.ts:9",
        "projections/react/sinks.ts:10",
        "projections/react/sinks.ts:11",
        "projections/react/sinks.ts:12",
        "projections/react/source.cs:1",
        "projections/react/static.cs:1",
        "projections/react/static.cs:2",
        "projections/react/static.cs:3",
        "projections/react/strings.ts:1",
        "projections/react/strings.ts:2",
        "projections/react/strings.ts:3",
        "projections/react/template-hole.ts:1",
        "projections/react/trace.cs:1",
        "projections/react/trace.cs:2",
        "projections/react/url-attr.razor:1",
        "projections/react/url-attr.razor:3",
        "projections/react/url-attr.razor:4",
        "projections/react/url-attr.tsx:1",
        "projections/react/url-attr.tsx:4",
        "projections/react/variable.ts:1",
        "projections/react/variable.ts:2",
        "projections/react/variable.ts:3",
        "projections/react/variable.ts:4",
        "projections/react/verbatim-interpolation.cs:1",
        "projections/react/verbatim.cs:1",
        "projections/shared/emit.ts:1",
        "projections/typescript/foundation/lib/src/comment-dynamic.ts:1",
        "projections/typescript/foundation/lib/src/comment-from.ts:1",
        "projections/typescript/foundation/lib/src/index.ts:5",
        "projections/typescript/foundation/lib/src/index.ts:6",
        "projections/typescript/foundation/lib/src/index.ts:7",
        "projections/typescript/foundation/lib/src/leak.ts:1",
    ];

    private static readonly string[] ExpectedAllowListErrors = [
        "projections/react/console.ts: stale allow-list row, no line reads: console.log('comment')",
        "projections/react/console.ts:1: // console.log('comment')",
        "projections/react/dup.ts:2: console.log('same')",
        "projections/react/interpolated.cs: allow-list row passes more than plain string literals: Console.WriteLine($\"{value}\");",
        "projections/react/interpolated.cs:1: Console.WriteLine($\"{value}\");",
        "projections/react/missing.ts: stale allow-list row, no line reads: console.log('gone')",
        "projections/react/url-attr.tsx: stale allow-list row, no line reads: <img src={value} />",
        "projections/react/url-attr.tsx:4: <img srcSet={`${base}/a.png 2x`} />",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log('a' + value)",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log(`value ${value}`)",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log(value)",
        "projections/react/variable.ts: allow-list row passes more than plain string literals: console.log(value.name)",
        "projections/react/variable.ts:1: console.log(value)",
        "projections/react/variable.ts:2: console.log(value.name)",
        "projections/react/variable.ts:3: console.log(`value ${value}`)",
        "projections/react/variable.ts:4: console.log('a' + value)",
    ];

    // Each row admits exactly one line: the file, its exact trimmed text, and why it carries no rendered value.
    private static readonly Dictionary<(string Path, string Line), string> AllowList = new()
    {
        [("projections/react/ui/hlp.ui.text-area/src/TextArea.tsx", "console.warn('TextArea: the `invalid` prop is deprecated. Use `error` instead.')")] = "fixed deprecation sentence, no prop value",
    };

    // Each row admits exactly one line that is not a literal call: the file, its exact trimmed text, and why no value leaves through it.
    private static readonly Dictionary<(string Path, string Line), string> Admitted = new()
    {
        [("Directory.Build.props", "<Import Project=\"$(MSBuildThisFileDirectory)artifacts/packages/nuget/packed-version.props\" Condition=\"Exists('$(MSBuildThisFileDirectory)artifacts/packages/nuget/packed-version.props')\" />")] = "imports packed-version.props, which tooling/package-version.mjs generates with one version PropertyGroup and no items",
        [("Directory.Build.props", "<BeforeMicrosoftNETSdkTargets>$(MSBuildThisFileDirectory)projections/dotnet/Harborline.PackageTargetFramework.props</BeforeMicrosoftNETSdkTargets>")] = "names Harborline.PackageTargetFramework.props, which the scan reads: a TargetFramework and package items, no Compile, Content or RazorComponent",
        [("projections/blazor/ui/hlp.ui.app-shell/HarborlineAppShell.razor", "<section class=\"hl-app-shell__zone\" data-shell-zone=\"workspaces\" data-zone-cap=\"@WorkspaceCap\" aria-label=\"@WorkspacesLabel\"><div class=\"hl-app-shell__zone-label\">@WorkspacesLabel</div>@foreach(var workspace in (showAllWorkspaces?Workspaces:Workspaces.Take(WorkspaceCap))){<div class=\"hl-app-shell__rail-row\" data-shell-row><a class=\"hl-app-shell__rail-link\" href=\"@ShellChromeContract.Address(\"workspaces\",workspace.Id)\" aria-current=\"@(workspace.Id==Workspace?.Id?\"page\":null)\" data-enhance-nav=\"false\" @onclick=\"()=>SetWorkspace(workspace.Id)\" @onclick:preventDefault=\"true\"><span class=\"hl-app-shell__rail-label\">@workspace.Label</span>@if(workspace.Count is not null){<span class=\"hl-app-shell__count\" data-shell-count>@workspace.Count</span>}</a></div>}@if(!showAllWorkspaces&&Workspaces.Count>WorkspaceCap){<button type=\"button\" class=\"hl-app-shell__show-more\" @onclick=\"()=>showAllWorkspaces=true\">Show @(Workspaces.Count-WorkspaceCap) more</button>}</section>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.app-shell/HarborlineAppShell.razor", "<section class=\"hl-app-shell__rail-footer\" data-shell-zone=\"footer\" aria-label=\"Account and system\">@if(FooterIdentity is not null){<div class=\"hl-app-shell__identity\" data-shell-row><span class=\"hl-app-shell__rail-label\">@FooterIdentity.Label</span><span class=\"hl-app-shell__identity-role\">@FooterIdentity.Role</span></div>}@foreach(var item in SystemItems){<a class=\"hl-app-shell__rail-link\" href=\"@ShellChromeContract.Address(\"system\",item.Id)\" data-enhance-nav=\"@(item.Invoke is not null?\"false\":null)\" @onclick=\"()=>InvokeSystem(item)\" @onclick:preventDefault=\"@(item.Invoke is not null)\"><span class=\"hl-app-shell__rail-label\">@item.Label</span></a>}@RailFooter</section>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.app-shell/HarborlineAppShell.razor", "private RenderFragment SimpleRow(ShellNavItem item)=>@<div class=\"hl-app-shell__rail-row\" data-shell-row><a class=\"hl-app-shell__rail-link\" href=\"@ShellChromeContract.Address(item.Kind??\"workspaces\",item.Id)\" data-enhance-nav=\"@(Navigated.HasDelegate?\"false\":null)\" @onclick=\"()=>Navigated.InvokeAsync(item)\" @onclick:preventDefault=\"@Navigated.HasDelegate\"><span class=\"hl-app-shell__rail-label\">@item.Label</span>@if(item.Count is string count){<span class=\"hl-app-shell__count\" data-shell-count>@count</span>}</a></div>;")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.app-shell/rev6/HarborlineAppShellRev6Frame.razor", "<a href=\"@shown.Rail.HomeHref\" aria-label=\"@shown.Rail.TenantLabel\" data-nav-id=\"home\" data-drawer-row @onclick='() => Navigate(shown.Owner, \"home\")' @onclick:preventDefault=\"@(Intent.HasDelegate || shown.Tabs.PendingClose is not null)\">@shown.Rail.TenantText</a>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.app-shell/rev6/HarborlineAppShellRev6Frame.razor", "<a @key=\"item.Link.Id\" href=\"@item.Link.Href\" aria-label=\"@LinkName(item.Link)\" title=\"@LinkName(item.Link)\" aria-current=\"@(item.Link.Current ? \"page\" : null)\" data-nav-id=\"@item.Link.Id\" data-drawer-row @onclick=\"() => Navigate(shown.Owner, item.Link.Id)\" @onclick:preventDefault=\"@(Intent.HasDelegate || shown.Tabs.PendingClose is not null)\">")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.app-shell/rev6/HarborlineAppShellRev6Frame.razor", "<a @key=\"link.Id\" href=\"@link.Href\" aria-label=\"@LinkName(link)\" aria-current=\"@(link.Current ? \"page\" : null)\" data-nav-id=\"@link.Id\" data-drawer-row @onclick=\"() => Navigate(shown.Owner, link.Id)\" @onclick:preventDefault=\"@(Intent.HasDelegate || shown.Tabs.PendingClose is not null)\">@link.Label@if (link.Count is { } count) { <span class=\"hl-rev6-count\"> @count</span> }</a>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.breadcrumb/HarborlineBreadcrumb.razor", "<a class=\"hl-breadcrumb__link\" href=\"@item.Href\">@item.Label</a>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.chat/HarborlineChat.razor", "<img class=\"hl-chat__avatar\" src=\"@message.Author.AvatarUrl\" alt=\"@message.Author.Name\" />")] = "avatar image URL the consumer supplies; the component adds no value of its own to it",
        [("projections/blazor/ui/hlp.ui.data-exchange/HarborlineDataExchangeAuthoringEditor.razor", "@if (AuthoringRefusals.Count > 0) { <fieldset><legend>Authoring refusals</legend><ul>@foreach (var refusal in AuthoringRefusals) { <li><span>@refusal.Stage</span>: <a href=\"@refusal.TargetHref\">@refusal.Code</a></li> }</ul></fieldset> }")] = "refusal target link from the authoring model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.popover/wwwroot/popover.js", "// the content carries data-hl-popover-open (set last in connect(), removed first in dispose()).")] = "comment naming the data-hl-popover-open attribute",
        [("projections/blazor/ui/hlp.ui.schema-form/HarborlineSchemaCollection.razor", "<HarborlineSchemaItems Action=\"@Action\" ByField=\"@ByField\" Disabled=\"@Disabled\" FieldFocused=\"@FieldFocused\"")] = "Action is an EventCallback<SchemaFormAction> component parameter, not an HTML action attribute",
        [("projections/blazor/ui/hlp.ui.schema-form/HarborlineSchemaForm.razor", "<HarborlineSchemaSection @key=\"section.Id\" Action=\"@HandleAction\" ByField=\"@byField\"")] = "Action is an EventCallback<SchemaFormAction> component parameter, not an HTML action attribute",
        [("projections/blazor/ui/hlp.ui.schema-form/HarborlineSchemaItems.razor", "<HarborlineSchemaItems Action=\"@Action\" ByField=\"@ByField\" Disabled=\"@Disabled\" FieldFocused=\"@FieldFocused\"")] = "Action is an EventCallback<SchemaFormAction> component parameter, not an HTML action attribute",
        [("projections/blazor/ui/hlp.ui.schema-form/HarborlineSchemaItems.razor", "<HarborlineSchemaCollection @key=\"collection.Key\" Action=\"@Action\" ByField=\"@ByField\"")] = "Action is an EventCallback<SchemaFormAction> component parameter, not an HTML action attribute",
        [("projections/blazor/ui/hlp.ui.schema-form/HarborlineSchemaSection.razor", "<HarborlineSchemaItems Action=\"@Action\" ByField=\"@ByField\" Disabled=\"@Disabled\" FieldFocused=\"@FieldFocused\"")] = "Action is an EventCallback<SchemaFormAction> component parameter, not an HTML action attribute",
        [("projections/blazor/ui/hlp.ui.side-nav/HarborlineSideNavItems.razor", "<a href=\"@item.Href\" class=\"@ControlClass(current)\" aria-current=\"@(current ? \"page\" : null)\" aria-disabled=\"@(item.Disabled ? \"true\" : null)\"")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/blazor/ui/hlp.ui.text-box/HarborlineTextBox.razor", "await InvokeModuleAsync(js => js.InvokeVoidAsync(call, ResolvedId));")] = "call is the literal focusInput or focusInputIfLost, set two lines up, on the text-box module",
        [("projections/blazor/ui/hlp.ui.user-menu/HarborlineUserMenu.razor", "@if (!avatarFailed && !string.IsNullOrWhiteSpace(Identity.AvatarUri)) { <img src=\"@Identity.AvatarUri\" alt=\"\" @onerror=\"() => avatarFailed = true\" /> }")] = "avatar image URL the consumer supplies; the component adds no value of its own to it",
        [("projections/blazor/ui/hlp.ui.user-menu/HarborlineUserMenu.razor", "<a role=\"@(HasCustom ? null : \"menuitem\")\" href=\"@(item.Disabled ? null : item.Destination)\" aria-disabled=\"@(item.Disabled ? \"true\" : null)\" tabindex=\"@(MenuTabIndex(pair.index,item))\"")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.app-shell/src/AppShell.tsx", "<LimitedZone label={workspacesLabel} zone=\"workspaces\" cap={workspaceCap} items={workspaces} render={candidate => <a className=\"hl-app-shell__rail-link\" href={shellAddress('workspaces', candidate.id)} aria-current={candidate.id === workspace?.id ? 'page' : undefined} onClick={event => { event.preventDefault(); activateWorkspace(workspaces.indexOf(candidate)) }}>{candidate.icon}<span className=\"hl-app-shell__rail-label\">{candidate.label}</span>{candidate.count !== undefined ? <span className=\"hl-app-shell__count\" data-shell-count>{candidate.count}</span> : null}</a>} />")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.app-shell/src/AppShell.tsx", "{systemItems.map(item => <a key={item.id} className=\"hl-app-shell__rail-link\" href={shellAddress('system', item.id)} onClick={event => { if (item.invoke) { event.preventDefault(); item.invoke() } }}>{item.icon}<span className=\"hl-app-shell__rail-label\">{item.label}</span></a>)}")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.app-shell/src/AppShell.tsx", "function RailLink({ item, activeItemId, onNavigate }: { item: ShellNavItem; activeItemId?: string; onNavigate?: (item: ShellNavItem) => void }) { return <a className=\"hl-app-shell__rail-link\" href={shellAddress(item.kind ?? 'workspaces', item.id)} aria-current={item.id === activeItemId ? 'page' : undefined} onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate(item) } }}>{item.icon}<span className=\"hl-app-shell__rail-label\">{item.label}</span>{item.count !== undefined ? <span className=\"hl-app-shell__count\" data-shell-count>{item.count}</span> : null}</a> }")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.app-shell/src/ShellRail.tsx", "<a className=\"hl-app-shell__rail-link\" href={shellAddress(item.kind ?? 'workspaces', item.id)} aria-current={item.id === activeItemId ? 'page' : undefined} onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate(item) } }}>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.booking-authoring/src/BookingDraftTypes.ts", "* is offered against (booking-auth-6). resources: admitted Resources a Bookable may require (booking-auth-7). maintenanceSources:")] = "doc comment: the word require in prose",
        [("projections/react/ui/hlp.ui.breadcrumb/src/Breadcrumb.tsx", "<a className=\"hl-breadcrumb__link\" href={item.href}>{item.label}</a>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.button/scripts/build.mjs", "// `formulaCallOps`, a value import; the compiled authoring bridge is bundled like the contracts.")] = "comment: the word import in prose",
        [("projections/react/ui/hlp.ui.chat/src/Chat.tsx", "<img className=\"hl-chat__avatar\" src={participant.avatarUrl} alt={participant.name} />")] = "avatar image URL the consumer supplies; the component adds no value of its own to it",
        [("projections/react/ui/hlp.ui.chat/src/Chat.tsx", "const requestSubmit = React.useCallback((rawText: string) => {")] = "a local useCallback named requestSubmit that calls the consumer's onSubmit, not HTMLFormElement.requestSubmit",
        [("projections/react/ui/hlp.ui.chat/src/Chat.tsx", "onClick={() => requestSubmit(suggestion.prompt)}")] = "a local useCallback named requestSubmit that calls the consumer's onSubmit, not HTMLFormElement.requestSubmit",
        [("projections/react/ui/hlp.ui.chat/src/Chat.tsx", "requestSubmit(currentInput)")] = "a local useCallback named requestSubmit that calls the consumer's onSubmit, not HTMLFormElement.requestSubmit",
        [("projections/react/ui/hlp.ui.chip/src/Chip.tsx", "{avatar ? <img alt=\"\" className=\"hl-chip__avatar\" src={avatar} /> : null}")] = "avatar image URL the consumer supplies; the component adds no value of its own to it",
        [("projections/react/ui/hlp.ui.context-menu/src/ContextMenu.tsx", "open({x: bounds?.left ?? 8, y: bounds?.bottom ?? 8})")] = "a local useCallback named open that positions the menu, not window.open",
        [("projections/react/ui/hlp.ui.context-menu/src/ContextMenu.tsx", "open({x: event.clientX, y: event.clientY})")] = "a local useCallback named open that positions the menu, not window.open",
        [("projections/react/ui/hlp.ui.data-exchange/src/DataExchangeAuthoringEditor.tsx", "{authoringRefusals.length > 0 && <Section name=\"Authoring refusals\"><ul>{authoringRefusals.map(refusal => <li key={`${refusal.stage}:${refusal.code}`}><span>{refusal.stage}</span>: <a href={refusal.targetHref}>{refusal.code}</a></li>)}</ul></Section>}")] = "refusal target link from the authoring model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.detail-panel/src/index.ts", "// query identity import MASTER_DETAIL_RAIL_QUERY from hlp.ui.use-can-show-master-detail.")] = "comment: the word import in prose",
        [("projections/react/ui/hlp.ui.side-nav/src/SideNav.tsx", "href={disabled ? undefined : item.href}")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/react/ui/hlp.ui.user-menu/src/UserMenu.tsx", "{identity.avatarUri && !avatarFailed ? <img src={identity.avatarUri} alt=\"\" onError={() => setAvatarFailed(true)}/> : userInitials(identity.name)}")] = "avatar image URL the consumer supplies; the component adds no value of its own to it",
        [("projections/react/ui/hlp.ui.user-menu/src/UserMenu.tsx", "if (item.kind === 'link') return <a {...common} key={item.id} href={item.disabled ? undefined : item.destination} onClick={event => { if (item.disabled) event.preventDefault(); else closeAfterSelection(item.closeOnSelect) }}>{content}</a>")] = "link destination from the consumer's nav model; the browser requests it only when the user follows the link",
        [("projections/typescript/contracts/hlp.contracts.forms/src/authorization.ts", "require(role: RoleReference): RoleDefinition {")] = "a registry method named require, not CommonJS require",
        [("projections/typescript/contracts/hlp.contracts.forms/src/authorization.ts", "require(capability: AuthorizationCapabilityReference): AuthorizationCapabilityDefinition {")] = "a registry method named require, not CommonJS require",
    };

    [Fact]
    public void Component_library_emit_calls_match_the_commented_allow_list_exactly()
    {
        var root = RepositoryRoot();
        foreach (var lane in Roots) Assert.NotEmpty(Files(root, lane));
        Assert.All(AllowList.Values.Concat(Admitted.Values), reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        var errors = Violations(Scan(root), AllowList, Admitted);
        Assert.True(errors.Count == 0, "The component library emits no telemetry (telemetry-cc-16); a member's own emit path belongs in the member's engine.\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Component_library_fence_reports_every_planted_emit_call()
    {
        var root = Path.Combine(Path.GetTempPath(), "t502-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "projections", "react");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "console.ts"), "// console.log('comment')\nconsole.log('fixed')\n");
            File.WriteAllText(Path.Combine(dir, "logger.cs"), "ILogger logger;\n");
            File.WriteAllText(Path.Combine(dir, "activity.cs"), "ActivitySource source;\n");
            File.WriteAllText(Path.Combine(dir, "console.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(dir, "event.ts"), "navigator.sendBeacon(url);\n");
            File.WriteAllText(Path.Combine(dir, "variable.ts"), "console.log(value)\nconsole.log(value.name)\nconsole.log(`value ${value}`)\nconsole.log('a' + value)\n");
            File.WriteAllText(Path.Combine(dir, "interpolated.cs"), "Console.WriteLine($\"{value}\");\n");
            File.WriteAllText(Path.Combine(dir, "ignored.test.ts"), "console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "bypasses.ts"), "console?.log(value)\nconsole.log?.(value)\nconsole['log'](value)\nconsole\n .log(value)\nconsole.log\n(value)\n/* comment */ console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "static.cs"), "using static System.Console;\nusing static System.Diagnostics.Debug;\nusing static System.Diagnostics.Trace;\nWriteLine(value);\nWrite(value);\n");
            File.WriteAllText(Path.Combine(dir, "factory.tsx"), "ILoggerFactory factory;\n");
            File.WriteAllText(Path.Combine(dir, "message.js"), "LoggerMessage message;\n");
            File.WriteAllText(Path.Combine(dir, "metrics.jsx"), "System.Diagnostics.Metrics metrics;\n");
            File.WriteAllText(Path.Combine(dir, "meter.mjs"), "new Meter(name);\n");
            File.WriteAllText(Path.Combine(dir, "module.cts"), "import value = require('./value');\nconsole.log(value);\n");
            File.WriteAllText(Path.Combine(dir, "module.mts"), "import { value } from './value.js';\nconsole.log(value);\n");
            File.WriteAllText(Path.Combine(dir, "error.cjs"), "Console.Error.Write(value);\nConsole.Out.Write(value);\n");
            File.WriteAllText(Path.Combine(dir, "debug.razor"), "Debug.Print(value);\nDebug.Write(value);\n");
            File.WriteAllText(Path.Combine(dir, "trace.cs"), "Trace.TraceError(value);\nTrace.Write(value);\n");
            File.WriteAllText(Path.Combine(dir, "source.cs"), "EventSource source;\n");
            File.WriteAllText(Path.Combine(dir, "otel.js"), "import '@opentelemetry/api';\n");
            File.WriteAllText(Path.Combine(dir, "apostrophe.tsx"), "<p>Don't</p>\nconst u = 'http://x'; console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "otel-from.ts"), "import { trace } from '@opentelemetry/api';\n");
            File.WriteAllText(Path.Combine(dir, "otel-require.cjs"), "const api = require('@opentelemetry/api');\n");
            File.WriteAllText(Path.Combine(dir, "otel-dynamic.mjs"), "await import('@opentelemetry/api');\n");
            File.WriteAllText(Path.Combine(dir, "dot-split.ts"), "console.\nlog(value)\n");
            File.WriteAllText(Path.Combine(dir, "block.ts"), "/* console.log(value) */\n");
            File.WriteAllText(Path.Combine(dir, "html.razor"), "<!-- @{ Console.WriteLine(Value); } -->\n");
            File.WriteAllText(Path.Combine(dir, "comment-lines.ts"), "/*\n\n*/\nconsole.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "strings.ts"), "const u = 'http://x'; console.log(value)\nconst u = \"http://x\"; console.log(value)\nconst u = `http://x`; console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "verbatim.cs"), "var p = @\"a\\\"; var u = \"//\"; Console.WriteLine(p);\n");
            File.WriteAllText(Path.Combine(dir, "anchors.ts"), "console.warn('fixed'); console.log(value)\nconsole.log(value); console.warn('fixed')\n");
            File.WriteAllText(Path.Combine(dir, "regex.ts"), "const pattern = /https?:\\/\\//; console.log(value);\n");
            File.WriteAllText(Path.Combine(dir, "inert.ts"), "const help = \"console.log(value)\";\n");
            File.WriteAllText(Path.Combine(dir, "division.ts"), "const ratio = a / b; console.log(value); // comment\n");
            File.WriteAllText(Path.Combine(dir, "template-hole.ts"), "const text = `value ${console.log(value)}`;\n");
            File.WriteAllText(Path.Combine(dir, "interpolation-hole.cs"), "var text = $\"value {Console.WriteLine(value)}\";\n");
            File.WriteAllText(Path.Combine(dir, "nested-template.ts"), "const text = `${a ? `x${console.log(v)}` : b}`;\n");
            File.WriteAllText(Path.Combine(dir, "nested-follow.ts"), "const text = `${f(`inner ${x}`, console.log(v))}`;\n");
            File.WriteAllText(Path.Combine(dir, "nested-interpolation.cs"), "var text = $\"{(x ? $\"{Console.WriteLine(v)}\" : y)}\";\n");
            File.WriteAllText(Path.Combine(dir, "verbatim-interpolation.cs"), "var text = $@\"value {Console.WriteLine(v)}\";\n");
            File.WriteAllText(Path.Combine(dir, "division-regex.ts"), "const ratio = a / b; console.log(v) // x\nconst re = /['\"]/; console.log(v)\n");
            File.WriteAllText(Path.Combine(dir, "division-comment.ts"), "const ratio = a / b; // console.log(value)\n");
            File.WriteAllText(Path.Combine(dir, "brace-text.ts"), "const help = `Example { console.log(value) }`;\n");
            File.WriteAllText(Path.Combine(dir, "after-hole.ts"), "const help = `${value} console.log(value)`;\n");
            File.WriteAllText(Path.Combine(dir, "comment-gap.ts"), "console /* x */ .log(value)\nConsole /* x */ .WriteLine(value);\nnavigator /* x */ .sendBeacon(url);\n");
            File.WriteAllText(Path.Combine(dir, "dup.ts"), "console.log('same')\nconsole.log('same')\n");
            foreach (var name in new[] { "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "tests", "test", "__tests__", "stories", "example.tests" })
            {
                var skipped = Path.Combine(dir, name);
                Directory.CreateDirectory(skipped);
                File.WriteAllText(Path.Combine(skipped, "ignored.ts"), "console.log(value);\n");
            }
            File.WriteAllText(Path.Combine(dir, "ignored.spec.ts"), "console.log(value);\n");
            File.WriteAllText(Path.Combine(dir, "ignored.stories.ts"), "console.log(value);\n");
            foreach (var lane in new[] { "blazor", "shared" })
            {
                var laneDir = Path.Combine(root, "projections", lane);
                Directory.CreateDirectory(laneDir);
                File.WriteAllText(Path.Combine(laneDir, "emit.ts"), "console.log(value);\n");
            }
            // A packable project compiles whatever its globs match, test-like name or not; a non-packable one ships nothing.
            var blazor = Path.Combine(root, "projections", "blazor");
            foreach (var name in new[] { "pkg", "member", "pkg.tests", "member.tests" }) Directory.CreateDirectory(Path.Combine(blazor, name));
            File.WriteAllText(Path.Combine(blazor, "pkg", "Pkg.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><ItemGroup><Compile Include=\"../member/**/*.cs\" Exclude=\"../member/Excluded.test.cs\" /><Compile Include=\"../member/Removed.test.cs\" /><Compile Remove=\"../member/Removed.test.cs\" /><Content Include=\"../member/CrossType.test.cs\" /><Compile Remove=\"../member/CrossType.test.cs\" /><Content Remove=\"../member/ContentRemoved.test.cs\" /></ItemGroup></Project>\n");
            File.WriteAllText(Path.Combine(blazor, "pkg", "Default.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "ValueLeak.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "Excluded.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "Removed.test.cs"), "Console.WriteLine(value);\n");
            // MSBuild keeps one list per item type: a Remove on one type leaves the same path in another.
            File.WriteAllText(Path.Combine(blazor, "member", "CrossType.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "member", "ContentRemoved.test.cs"), "Console.WriteLine(value);\n");
            File.WriteAllText(Path.Combine(blazor, "pkg.tests", "Pkg.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><Compile Include=\"../member.tests/**/*.cs\" /></ItemGroup></Project>\n");
            File.WriteAllText(Path.Combine(blazor, "member.tests", "Probe.test.cs"), "Console.WriteLine(value);\n");
            // A Remove, or an IsPackable, under a Condition (on itself, its group, or a Choose branch) may not apply.
            File.WriteAllText(Path.Combine(blazor, "pkg", "Pkg.csproj"), File.ReadAllText(Path.Combine(blazor, "pkg", "Pkg.csproj")).Replace("</ItemGroup></Project>", "<Compile Remove=\"../member/Conditional.test.cs\" Condition=\"false\" /></ItemGroup><ItemGroup Condition=\"'$(Undefined)' == 'yes'\"><Compile Remove=\"../member/GroupConditional.test.cs\" /></ItemGroup><Choose><When Condition=\"false\"><ItemGroup><Compile Remove=\"../member/WhenRemoved.test.cs\" /></ItemGroup></When></Choose></Project>", StringComparison.Ordinal));
            foreach (var name in new[] { "Conditional", "GroupConditional", "WhenRemoved" }) File.WriteAllText(Path.Combine(blazor, "member", name + ".test.cs"), "Console.WriteLine(value);\n");
            Directory.CreateDirectory(Path.Combine(blazor, "cond"));
            File.WriteAllText(Path.Combine(blazor, "cond", "Cond.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup Condition=\"false\"><IsPackable>false</IsPackable></PropertyGroup></Project>\n");
            File.WriteAllText(Path.Combine(blazor, "cond", "Cond.test.cs"), "Console.WriteLine(value);\n");
            // A React package's build.mjs bundles what its entry points import, through esbuild aliases, into other projections.
            File.WriteAllText(Path.Combine(dir, "network.ts"), "fetch('https://x.invalid', { method: 'POST', body: value });\nconst request = new XMLHttpRequest();\nconst socket = new WebSocket(url);\nnew Image().src = url + value;\npixel.src = url + value;\npixel.setAttribute('src', url + value);\n");
            Directory.CreateDirectory(Path.Combine(dir, "pkg", "scripts"));
            File.WriteAllText(Path.Combine(dir, "pkg", "scripts", "build.mjs"), "import { build } from 'esbuild'\nconst root = resolve(import.meta.dirname, '..')\nconst libRoot = resolve(root, '../../typescript/foundation/lib')\ncompile(libRoot, ['-p', 'tsconfig.build.json'])\nconst external = [\n  'react',\n]\nconst alias = {\n  // the compiled library\n  '@x/lib': resolve(libRoot, 'dist/index.js'),\n}\nawait build({ entryPoints: [resolve(root, 'scripts/entry.ts')], bundle: true, external, alias, format: 'esm' })\n");
            File.WriteAllText(Path.Combine(dir, "pkg", "scripts", "entry.ts"), "import 'react';\nexport { leak } from '@x/lib';\n");
            var lib = Path.Combine(root, "projections", "typescript", "foundation", "lib");
            Directory.CreateDirectory(Path.Combine(lib, "src"));
            File.WriteAllText(Path.Combine(lib, "tsconfig.json"), "{\n  // shared options\n  \"compilerOptions\": { \"outDir\": \"dist\", },\n}\n");
            File.WriteAllText(Path.Combine(lib, "tsconfig.build.json"), "{ \"extends\": \"./tsconfig.json\", \"compilerOptions\": { \"rootDir\": \"src\" } }\n");
            // A comment inside an import is blanked before the specifier is read; an import whose specifier is not a string literal is a finding.
            File.WriteAllText(Path.Combine(lib, "src", "index.ts"), "import type { Shape } from '@x/types';\nexport { leak } from './leak.js';\nexport { gap } from /* gap */ './comment-from.js';\nconst late = import(/* x */ './comment-dynamic.js');\nconst named = import(name);\nconst other = require(name);\n// then import the rest\nexport { late }; export default named; const u = import.meta.url;\n");
            File.WriteAllText(Path.Combine(lib, "src", "leak.ts"), "export const leak = (draft: string) => console.log(draft);\n");
            File.WriteAllText(Path.Combine(lib, "src", "comment-from.ts"), "export const gap = (draft: string) => console.log(draft);\n");
            File.WriteAllText(Path.Combine(lib, "src", "comment-dynamic.ts"), "console.log(draft);\n");
            File.WriteAllText(Path.Combine(lib, "src", "unused.ts"), "console.log(draft);\n");
            // Directory.Build.props and .targets up to the root add items too; an Import other than an Sdk one, or a property naming one, is a finding.
            File.WriteAllText(Path.Combine(blazor, "pkg", "Directory.Build.props"), "<Project>\n  <Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\" />\n  <ItemGroup><Compile Include=\"../imported/ValueLeak.test.cs\" /></ItemGroup>\n</Project>\n");
            File.WriteAllText(Path.Combine(blazor, "Directory.Build.targets"), "<Project>\n  <Import Project=\"extra.targets\" />\n  <PropertyGroup><CustomAfterMicrosoftCommonTargets>$(MSBuildThisFileDirectory)after.targets</CustomAfterMicrosoftCommonTargets></PropertyGroup>\n</Project>\n");
            File.WriteAllText(Path.Combine(blazor, "extra.targets"), "<Project><ItemGroup><Compile Include=\"../imported/Targets.test.cs\" /></ItemGroup></Project>\n");
            Directory.CreateDirectory(Path.Combine(blazor, "imported"));
            foreach (var name in new[] { "ValueLeak", "Targets" }) File.WriteAllText(Path.Combine(blazor, "imported", name + ".test.cs"), "Console.WriteLine(value);\n");
            // A .targets file that sets IsPackable runs after the project, so the project's own false no longer holds.
            Directory.CreateDirectory(Path.Combine(blazor, "late"));
            File.WriteAllText(Path.Combine(blazor, "late", "Late.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>\n");
            File.WriteAllText(Path.Combine(blazor, "late", "Directory.Build.targets"), "<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>\n");
            File.WriteAllText(Path.Combine(blazor, "late", "Late.test.cs"), "Console.WriteLine(value);\n");
            // A URL attribute whose value is not one string literal can carry a value off the page.
            File.WriteAllText(Path.Combine(dir, "url-attr.tsx"), "<img src={'https://x.invalid/p?v=' + value} />\n<img src={'https://x.invalid/fixed.png'} />\n<a href=\"/fixed\">x</a>\n<img srcSet={`${base}/a.png 2x`} />\n");
            File.WriteAllText(Path.Combine(dir, "url-attr.razor"), "<img src=\"https://x.invalid/p?v=@Value\" />\n<img src=\"/fixed.png\" />\n<a href=@Value>x</a>\n<form action=\"@Target\"></form>\n");
            File.WriteAllText(Path.Combine(dir, "sinks.ts"), "fetchLater(url, { method: 'POST', body: value });\nconst transport = new WebTransport(url + value);\nconst peer = new RTCPeerConnection(config);\nconst stream = new EventSource(url + value);\nform.submit();\nform.requestSubmit();\nlocation = url + value;\nwindow.location.href = url + value;\nlocation.assign(url + value);\nlocation.replace(url + value);\nwindow.open(url + value);\nconst style = { backgroundImage: `url(${url + value})` };\nconst path = location.pathname; const css = 'url(/fixed.png)';\n");
            File.WriteAllText(Path.Combine(dir, "sinks.cs"), "using var client = new HttpClient();\nvar web = new WebClient();\nvar request = HttpWebRequest.Create(url);\nvar socket = new Socket(family, type, protocol);\nvar tcp = new TcpClient(host, port);\nusing System.Net.Http;\n");
            File.WriteAllText(Path.Combine(dir, "interop.razor"), "@inject IJSRuntime JS\n@code {\n  private IJSObjectReference? module;\n  async Task A() { module = await JS.InvokeAsync<IJSObjectReference>(\"import\", \"./_content/Harborline.UIAdapters.Blazor/a.js\"); }\n  async Task B() => await JS.InvokeVoidAsync(\"open\", url + value);\n  async Task C() => await module!.InvokeVoidAsync(name, value);\n  async Task D() => await JS.InvokeAsync<IJSObjectReference>(\"import\", \"https://x.invalid/m.js?v=\" + value);\n  async Task E() => await InvokeModuleAsync(js => js.InvokeVoidAsync(call, value));\n  async Task F() => await OnChange.InvokeAsync(value);\n  [Parameter] public EventCallback<string> OnChange { get; set; }\n}\n");

            var findings = Scan(root);
            Assert.Equal(ExpectedFindings,findings.Select(row => $"{row.Path}:{row.Line}"));

            string[] rowsUnderTest = ["projections/react/console.ts", "projections/react/dup.ts", "projections/react/variable.ts", "projections/react/interpolated.cs", "projections/react/url-attr.tsx"];
            var admitted = new Dictionary<(string Path, string Line), string>
            {
                [("projections/react/url-attr.tsx", "<img src={'https://x.invalid/p?v=' + value} />")] = "fixture",
                [("projections/react/url-attr.tsx", "<img src={value} />")] = "fixture",
            };
            var allowed = new Dictionary<(string Path, string Line), string>
            {
                [("projections/react/console.ts", "console.log('fixed')")] = "fixture",
                [("projections/react/console.ts", "console.log('comment')")] = "fixture",
                [("projections/react/dup.ts", "console.log('same')")] = "fixture",
                [("projections/react/missing.ts", "console.log('gone')")] = "fixture",
                [("projections/react/variable.ts", "console.log(value)")] = "fixture",
                [("projections/react/variable.ts", "console.log(value.name)")] = "fixture",
                [("projections/react/variable.ts", "console.log(`value ${value}`)")] = "fixture",
                [("projections/react/variable.ts", "console.log('a' + value)")] = "fixture",
                [("projections/react/interpolated.cs", "Console.WriteLine($\"{value}\");")] = "fixture",
            };
            var errors = Violations(findings.Where(row => rowsUnderTest.Contains(row.Path)).ToArray(), allowed, admitted);
            Assert.Equal(ExpectedAllowListErrors, errors.Order(StringComparer.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed record Finding(string Path, int Line, string Text);

    private static List<string> Violations(Finding[] findings, Dictionary<(string Path, string Line), string> allowList, Dictionary<(string Path, string Line), string> admitted)
    {
        var errors = allowList.Keys.Where(key => !LiteralCall.IsMatch(key.Line))
            .Select(key => $"{key.Path}: allow-list row passes more than plain string literals: {key.Line}").ToList();
        var unused = allowList.Keys.Where(key => LiteralCall.IsMatch(key.Line)).Concat(admitted.Keys).ToHashSet();
        errors.AddRange(findings.Where(row => !unused.Remove((row.Path, row.Text))).Select(row => $"{row.Path}:{row.Line}: {row.Text}"));
        errors.AddRange(unused.Select(key => $"{key.Path}: stale allow-list row, no line reads: {key.Line}"));
        return errors;
    }

    // The walk skips test-like names, so every file a packable project compiles is added back from its own items and
    // its Directory.Build chain, and every file a React package's bundle pulls in, from inside the lanes or out of them,
    // is added from its build. An MSBuild import or an import specifier the scan cannot read is itself a finding.
    private static Finding[] Scan(string root)
    {
        var findings = new List<Finding>();
        var files = Roots.SelectMany(lane => Files(root, lane).Concat(PackageInputs(root, Path.Combine(root, "projections", lane), findings))).ToList();
        findings.AddRange(files.Concat(BundleInputs(root)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(file => ScanFile(root, file)));
        return findings.Distinct().OrderBy(row => row.Path, StringComparer.Ordinal).ThenBy(row => row.Line).ToArray();
    }

    private static IEnumerable<Finding> ScanFile(string root, string file)
    {
        var source = File.ReadAllText(file);
        var lines = source.Split('\n');
        var path = Path.GetRelativePath(root, file).Replace('\\', '/');
        var offsets = Emit.Matches(source).Select(match => match.Index);
        var extension = Path.GetExtension(file);
        if (ScriptExtensions.Contains(extension)) offsets = offsets.Concat(Imports(source).Unread);
        if (extension is ".cs" or ".razor") offsets = offsets.Concat(InteropCalls(file, source));
        return offsets.Select(index => source.AsSpan(0, index).Count('\n') + 1).Distinct().Order()
            .Select(line => new Finding(path, line, lines[line - 1].Trim()));
    }

    // Every specifier read as a string literal, and the offset of every import site that is not one.
    private static (List<(string Spec, bool Type)> Specs, List<int> Unread) Imports(string source)
    {
        var stripped = StripComments(source);
        var (specs, unread) = (new List<(string Spec, bool Type)>(), new List<int>());
        foreach (Match site in ImportSite.Matches(source))
        {
            var form = ImportForm.Match(stripped, site.Index);
            if (form.Success) specs.Add((form.Groups["spec"].Value, form.Groups["type"].Success));
            else if (!NotAnImport.IsMatch(stripped, site.Index)) unread.Add(site.Index);
        }
        return (specs, unread);
    }

    // Blanks // and /* */ comments to spaces, keeping newlines so offsets and lines hold. Strings end at a newline,
    // template text resumes after each ${ } hole, and a slash after an operator, an opening bracket or a keyword starts a regex.
    // ponytail: a tokenizer, not a parser; a misread can only blank or keep text, and either way an import it breaks is a finding.
    private static string StripComments(string source)
    {
        var text = source.ToCharArray();
        var holes = new Stack<int>();
        var (depth, i, regexAllowed) = (0, 0, true);
        void Blank(int from, int to) { for (var k = from; k < to; k++) if (text[k] is not ('\n' or '\r')) text[k] = ' '; }
        int Quoted(int start, char quote)
        {
            var j = start + 1;
            while (j < text.Length && text[j] != quote && text[j] != '\n') j += text[j] == '\\' ? 2 : 1;
            return Math.Min(j + 1, text.Length);
        }
        int Template(int j)
        {
            regexAllowed = false;
            while (j < text.Length)
            {
                if (text[j] == '\\') { j += 2; continue; }
                if (text[j] == '`') return j + 1;
                if (text[j] == '$' && j + 1 < text.Length && text[j + 1] == '{') { holes.Push(depth); regexAllowed = true; return j + 2; }
                j++;
            }
            return text.Length;
        }
        int RegexLiteral(int start)
        {
            var (j, inClass) = (start + 1, false);
            while (j < text.Length && text[j] != '\n')
            {
                if (text[j] == '\\') { j += 2; continue; }
                if (text[j] == '[') inClass = true;
                else if (text[j] == ']') inClass = false;
                else if (text[j] == '/' && !inClass) { j++; while (j < text.Length && char.IsLetter(text[j])) j++; return j; }
                j++;
            }
            return start + 1;
        }
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/') { var end = Array.IndexOf(text, '\n', i); end = end < 0 ? text.Length : end; Blank(i, end); i = end; }
            else if (c == '/' && next == '*') { var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal); end = end < 0 ? text.Length : end + 2; Blank(i, end); i = end; }
            else if (c is '\'' or '"') { i = Quoted(i, c); regexAllowed = false; }
            else if (c == '`') i = Template(i + 1);
            else if (c == '}' && holes.Count > 0 && holes.Peek() == depth) { holes.Pop(); i = Template(i + 1); }
            else if (c == '/' && regexAllowed) { i = RegexLiteral(i); regexAllowed = false; }
            else if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$')) i++;
                regexAllowed = source[start..i] is "return" or "typeof" or "case" or "do" or "else" or "in" or "of" or "new" or "delete" or "void" or "throw" or "instanceof" or "yield" or "await";
            }
            else
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
                if (!char.IsWhiteSpace(c)) regexAllowed = c is not (')' or ']' or '}');
                i++;
            }
        }
        return new string(text);
    }

    // A JS interop call that names a global (window.open, a dotted path, import of anything but a _content module), or
    // whose function name is not a literal, unless it is ComponentBase's bare InvokeAsync or an EventCallback declared beside it.
    private static IEnumerable<int> InteropCalls(string file, string source)
    {
        foreach (Match call in Interop.Matches(source))
        {
            var name = call.Groups["name"];
            if (name.Success)
            {
                if (GlobalSink.IsMatch(name.Value) && !(name.Value == "import" && ContentModule.IsMatch(source, name.Index + name.Length))) yield return call.Index;
                continue;
            }
            var before = source[..call.Index].TrimEnd();
            if (before.Length == 0 || before[^1] != '.') continue;
            var receiver = Regex.Match(before, @"(\w+)\s*!?\s*\??\.$");
            var callbacks = Directory.EnumerateFiles(Path.GetDirectoryName(file)!).Where(path => Path.GetExtension(path) is ".cs" or ".razor").Select(File.ReadAllText);
            if (!receiver.Success || !callbacks.Any(text => Regex.IsMatch(text, $@"\bEventCallback\b(?:\s*<[^;{{]*?>)?\s+{receiver.Groups[1].Value}\b"))) yield return call.Index;
        }
    }

    private static IEnumerable<string> Files(string root, string lane)
    {
        var scanRoot = Path.Combine(root, "projections", lane);
        if (!Directory.Exists(scanRoot)) yield break;
        var pending = new Stack<string>();
        pending.Push(scanRoot);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(child);
                if (!Skip.Contains(name) && !name.EndsWith(".tests", StringComparison.OrdinalIgnoreCase)) pending.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(dir))
                if (Extensions.Contains(Path.GetExtension(file)) && !Regex.IsMatch(Path.GetFileName(file), @"\.(test|spec|stories)\.", RegexOptions.IgnoreCase)) yield return file;
        }
    }

    // Compile, RazorComponent and Content items of every packable project (IsPackable is not false), read with
    // MSBuild's semantics: Include and Exclude are ;-separated globs relative to the project, Remove applies in
    // document order to its own item type, and the SDK defaults (Compile **/*.cs; Content **/*.razor and wwwroot/**;
    // minus bin and obj) come first. Conditions are not evaluated: a Remove or an IsPackable under one (on itself,
    // an ancestor, or a Choose branch) is ignored, so the scan set only ever grows. Every Directory.Build.props,
    // Directory.Build.targets and Directory.Packages.props from the project up to the repository root, and every file
    // they or the project import, adds its Compile, Content and RazorComponent Includes too, and its Removes are ignored.
    // ponytail: no MSBuild evaluation; an Import other than an Sdk one, or a property naming a .props or .targets file,
    // is a finding an allow-list row must admit, and an Include that needs a property fails the test instead of being skipped.
    private static List<string> PackageInputs(string root, string scanRoot, List<Finding> findings)
    {
        if (!Directory.Exists(scanRoot)) return [];
        var projects = Directory.EnumerateFiles(scanRoot, "*.csproj", new EnumerationOptions { RecurseSubdirectories = true })
            .Where(path => !path.Replace('\\', '/').Split('/').Any(part => part is "node_modules" or "bin" or "obj"));
        return projects.SelectMany(project => ProjectInputs(root, project, findings)).Where(file => Extensions.Contains(Path.GetExtension(file))).ToList();
    }

    private static readonly string[] ChainFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    private static IEnumerable<string> ProjectInputs(string root, string project, List<Finding> findings)
    {
        var dir = Path.GetDirectoryName(project)!;
        static bool Conditional(XElement element) => element.AncestorsAndSelf().Any(e => e.Attribute("Condition") is not null || e.Name.LocalName is "When" or "Otherwise");
        static bool SetsIsPackable(IEnumerable<XElement> elements) => elements.Any(e => e.Name.LocalName == "IsPackable");
        var chain = new List<string>();
        for (var up = dir; up is not null; up = Path.GetDirectoryName(up))
        {
            chain.AddRange(ChainFiles.Select(name => Path.Combine(up, name)).Where(File.Exists));
            if (string.Equals(Path.GetFullPath(up).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
        }
        // The project file, then the chain and whatever any of them imports, each read once.
        var files = new List<(string Path, XElement[] Elements)>();
        var pending = new Queue<string>(chain.Prepend(project));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unread = new List<Finding>();
        while (pending.TryDequeue(out var file))
        {
            if (!seen.Add(Path.GetFullPath(file))) continue;
            var elements = XDocument.Load(file, LoadOptions.SetLineInfo).Descendants().ToArray();
            files.Add((file, elements));
            var fileDir = Path.GetDirectoryName(file)!;
            var lines = File.ReadAllLines(file);
            var imports = elements.Where(e => e.Name.LocalName == "Import" && e.Attribute("Sdk") is null).Select(e => (Element: e, Paths: new[] { (string?)e.Attribute("Project") ?? "" }))
                .Concat(elements.Where(e => e.Parent?.Name.LocalName == "PropertyGroup" && !e.HasElements)
                    .Select(e => (Element: e, Paths: e.Value.Split(';', StringSplitOptions.TrimEntries)))
                    .Where(property => property.Paths.Any(value => value.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))));
            foreach (var (element, paths) in imports)
            {
                var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
                unread.Add(new Finding(Path.GetRelativePath(root, file).Replace('\\', '/'), line, lines[line - 1].Trim()));
                foreach (var target in paths.Select(value => value.Replace("$(MSBuildThisFileDirectory)", fileDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    if (target.Length > 0 && !target.Contains("$(", StringComparison.Ordinal) && File.Exists(Path.Combine(fileDir, target))) pending.Enqueue(Path.Combine(fileDir, target));
            }
        }
        var items = files[0].Elements;
        // Directory.Build.props and Directory.Packages.props come before the project; any other chain or imported file may come after it.
        var after = files.Skip(1).Where(file => Path.GetFileName(file.Path) is not ("Directory.Build.props" or "Directory.Packages.props")).SelectMany(file => file.Elements);
        if (items.Any(e => e.Name.LocalName == "IsPackable" && !Conditional(e) && e.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)) && !SetsIsPackable(after)) return [];
        findings.AddRange(unread);
        // MSBuild keeps one list per item type, so a Remove drops paths only from its own type's list.
        HashSet<string> Defaults(string globs) => Globs(dir, globs).Except(Globs(dir, "bin/**;obj/**"), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inputs = new Dictionary<string, HashSet<string>>
        {
            ["Compile"] = Defaults("**/*.cs"),
            ["Content"] = Defaults("**/*.razor;wwwroot/**"),
            ["RazorComponent"] = new(StringComparer.OrdinalIgnoreCase),
        };
        foreach (var item in items.Where(e => inputs.ContainsKey(e.Name.LocalName)))
        {
            var list = inputs[item.Name.LocalName];
            var include = (string?)item.Attribute("Include");
            var remove = (string?)item.Attribute("Remove");
            if (include is not null)
            {
                Assert.False(Regex.IsMatch(include, @"[$@%]\("), $"{project}: evaluate this Include before scanning: {include}");
                var excluded = Globs(dir, (string?)item.Attribute("Exclude")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                list.UnionWith(Globs(dir, include).Where(file => !excluded.Contains(file)));
            }
            if (remove is not null && !Conditional(item)) list.ExceptWith(Globs(dir, remove));
        }
        foreach (var (file, elements) in files.Skip(1))
            foreach (var item in elements.Where(e => inputs.ContainsKey(e.Name.LocalName) && e.Attribute("Include") is not null))
            {
                var include = (string)item.Attribute("Include")!;
                Assert.False(Regex.IsMatch(include, @"[$@%]\("), $"{file}: evaluate this Include before scanning: {include}");
                inputs[item.Name.LocalName].UnionWith(Globs(dir, include));
            }
        return inputs.Values.SelectMany(list => list);
    }

    // Each React package's build.mjs bundles its entry points with esbuild, which follows imports out of the lanes
    // (into projections/typescript, say). This walks the same graph: an alias applies first, an external stays out,
    // a relative or aliased path resolves with esbuild's default extensions, its .js-to-.ts rewrite and index files,
    // and a path into a tsconfig outDir the script compiles maps back to that tsconfig's rootDir.
    // ponytail: regex reads of build.mjs and of comment-blanked import sites, not a JS parser; what it cannot read fails the test.
    private static List<string> BundleInputs(string root)
    {
        var react = Path.Combine(root, "projections", "react");
        if (!Directory.Exists(react)) return [];
        var reached = new List<string>();
        var scripts = Directory.EnumerateFiles(react, "build.mjs", new EnumerationOptions { RecurseSubdirectories = true })
            .Where(path => !path.Replace('\\', '/').Split('/').Contains("node_modules"));
        foreach (var script in scripts)
        {
            var text = File.ReadAllText(script);
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal) { ["import.meta.dirname"] = Path.GetDirectoryName(script)! };
            string Evaluate(string call)
            {
                var m = Regex.Match(call, @"^resolve\(\s*([\w.]+)\s*,\s*'([^']*)'\s*\)$");
                Assert.True(m.Success && bindings.ContainsKey(m.Groups[1].Value), $"{script}: evaluate {call} before scanning");
                return Path.GetFullPath(Path.Combine(bindings[m.Groups[1].Value], m.Groups[2].Value));
            }
            foreach (Match m in Regex.Matches(text, @"(?m)^const\s+(\w+)\s*=\s*(resolve\([^)]*\))\s*$")) bindings[m.Groups[1].Value] = Evaluate(m.Groups[2].Value);
            var outDirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text, @"\bcompile\(\s*(\w+)\s*,\s*\[\s*'-p'\s*,\s*'([^']+)'\s*\]\s*\)"))
            {
                if (!bindings.TryGetValue(m.Groups[1].Value, out var cwd)) continue;
                var tsconfig = Path.Combine(cwd, m.Groups[2].Value);
                var (outDir, rootDir) = (CompilerOption(tsconfig, "outDir"), CompilerOption(tsconfig, "rootDir"));
                Assert.True(outDir is not null && rootDir is not null, $"{tsconfig}: set outDir and rootDir before scanning");
                outDirs[Path.GetFullPath(Path.Combine(outDir.Value.Dir, outDir.Value.Value.GetString()!))] = Path.GetFullPath(Path.Combine(rootDir.Value.Dir, rootDir.Value.Value.GetString()!));
            }
            string FromOutDir(string path) => outDirs.Where(pair => path.StartsWith(pair.Key + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Select(pair => Path.Combine(pair.Value, Path.GetRelativePath(pair.Key, path))).FirstOrDefault(path);
            string[] Rows(string block, string pattern) => block.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
                .Select(line => { var m = Regex.Match(line, pattern); Assert.True(m.Success, $"{script}: read this row before scanning: {line}"); return m.Value; }).ToArray();
            var aliasBlock = Regex.Match(text, @"(?ms)^const alias = \{(.*?)^\}");
            Assert.True(aliasBlock.Success || !Regex.IsMatch(text, @"\balias\b"), $"{script}: read its alias before scanning");
            var alias = Rows(aliasBlock.Groups[1].Value, @"^'[^']+'\s*:\s*resolve\([^)]*\),?$")
                .Select(row => Regex.Match(row, @"^'([^']+)'\s*:\s*(resolve\([^)]*\))")).ToDictionary(m => m.Groups[1].Value, m => Evaluate(m.Groups[2].Value), StringComparer.Ordinal);
            var external = Rows(Regex.Match(text, @"(?ms)^const external = \[(.*?)^\]").Groups[1].Value, @"^'[^']+',?$").Select(row => row.Trim(',', '\'')).ToArray();
            var builds = Regex.Matches(text, @"\bbuild\(\s*\{([^}]*)\}\s*\)");
            Assert.True(builds.Count == Regex.Count(text, @"\bbuild\("), $"{script}: read every build() call before scanning");
            var pending = new Stack<string>();
            foreach (Match build in builds)
            {
                var options = build.Groups[1].Value;
                Assert.False(Regex.IsMatch(options, @"\b(?:alias|external|plugins|tsconfig\w*|stdin|resolveExtensions|nodePaths|packages|mainFields|conditions|inject)\s*:"), $"{script}: read these build options before scanning: {options}");
                var entries = Regex.Match(options, @"\bentryPoints:\s*\[((?:\s*resolve\([^)]*\)\s*,?)+)\s*\]");
                Assert.True(entries.Success, $"{script}: read these entry points before scanning: {options}");
                foreach (Match call in Regex.Matches(entries.Groups[1].Value, @"resolve\([^)]*\)")) pending.Push(Evaluate(call.Value));
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pending.TryPop(out var target))
            {
                var file = ResolveModule(FromOutDir(target));
                Assert.True(file is not null, $"{script}: resolve {target} before scanning");
                if (!seen.Add(file) || !Extensions.Contains(Path.GetExtension(file))) continue;
                reached.Add(file);
                foreach (var (spec, type) in Imports(File.ReadAllText(file)).Specs)
                {
                    var key = alias.Keys.FirstOrDefault(name => spec == name || spec.StartsWith(name + "/", StringComparison.Ordinal));
                    if (key is not null) pending.Push(Path.GetFullPath(alias[key] + spec[key.Length..]));
                    else if (spec.StartsWith('.', StringComparison.Ordinal)) pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, spec)));
                    else if (!type &&!external.Any(name => spec == name || spec.StartsWith(name + "/", StringComparison.Ordinal)))
                        Assert.False(TsconfigPathsMap(file, spec), $"{file}: esbuild resolves {spec} through tsconfig paths; follow it before scanning");
                }
            }
        }
        return reached;
    }

    // esbuild's default resolveExtensions, then its TypeScript rewrite of a .js-family specifier, then directory index files.
    private static string? ResolveModule(string path)
    {
        string[] extensions = [".tsx", ".ts", ".jsx", ".js", ".css", ".json"];
        var stem = Path.ChangeExtension(path, null);
        string[] rewrites = Path.GetExtension(path).ToUpperInvariant() switch { ".JS" or ".JSX" => [stem + ".ts", stem + ".tsx"], ".MJS" => [stem + ".mts"], ".CJS" => [stem + ".cts"], _ => [] };
        return new[] { path }.Concat(extensions.Select(ext => path + ext)).Concat(rewrites).Concat(extensions.Select(ext => Path.Combine(path, "index" + ext))).FirstOrDefault(File.Exists);
    }

    // Before node_modules, esbuild resolves a bare specifier through the paths of the importing file's nearest tsconfig.json.
    private static bool TsconfigPathsMap(string file, string spec)
    {
        for (var dir = Path.GetDirectoryName(file); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var tsconfig = Path.Combine(dir, "tsconfig.json");
            if (!File.Exists(tsconfig)) continue;
            return CompilerOption(tsconfig, "paths") is { } paths && paths.Value.EnumerateObject()
                .Any(entry => entry.Name == spec || (entry.Name.EndsWith('*', StringComparison.Ordinal) &&spec.StartsWith(entry.Name[..^1], StringComparison.Ordinal)));
        }
        return false;
    }

    // A compilerOptions value and the directory of the tsconfig that sets it, following relative extends.
    private static (JsonElement Value, string Dir)? CompilerOption(string tsconfig, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(tsconfig), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var dir = Path.GetDirectoryName(tsconfig)!;
        if (document.RootElement.TryGetProperty("compilerOptions", out var options) && options.TryGetProperty(name, out var value)) return (value.Clone(), dir);
        if (!document.RootElement.TryGetProperty("extends", out var parent)) return null;
        Assert.True(parent.ValueKind == JsonValueKind.String && parent.GetString()!.StartsWith('.', StringComparison.Ordinal), $"{tsconfig}: follow this extends before scanning");
        return CompilerOption(Path.GetFullPath(Path.Combine(dir, parent.GetString()!)), name);
    }

    private static IEnumerable<string> Globs(string dir, string? list) =>
        (list ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SelectMany(glob => Glob(dir, glob));

    private static IEnumerable<string> Glob(string dir, string glob)
    {
        var parts = glob.Replace('\\', '/').Split('/');
        var wild = Array.FindIndex(parts, part => part.IndexOfAny(['*', '?']) >= 0);
        if (wild < 0)
        {
            var file = Path.GetFullPath(Path.Combine(dir, glob));
            return File.Exists(file) ? [file] : [];
        }
        var baseDir = Path.GetFullPath(Path.Combine([dir, .. parts[..wild]]));
        if (!Directory.Exists(baseDir)) return [];
        var pattern = new Regex("^" + Regex.Escape(string.Join('/', parts[wild..]))
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal).Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase);
        return Directory.EnumerateFiles(baseDir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Where(file => pattern.IsMatch(Path.GetRelativePath(baseDir, file).Replace('\\', '/')))
            .Select(Path.GetFullPath);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
