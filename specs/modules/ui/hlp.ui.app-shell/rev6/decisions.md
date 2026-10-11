# Harness rev 6 decisions

Grilling record for the domain-agnostic harness → `hlp.ui.app-shell` rev 6, and how it connects to the Harborline API. Status as of 2026-10-11.

Sources used throughout:
- **R-mine**: `reports/Domain agnostic harness benchmark.md`
- **R-yours**: the owner's independent benchmark (pasted 2026-10-11)
- **R-gpt**: `research_notes/ChatGPT web side panel/chatgpt_side_panel.md`
- **DES-0002**: `harborline-control/designs/DES-0002-primitives/design.md`
- **DES-0006**: `harborline-control/designs/DES-0006-api-design/design.md`
- **rev 5**: `harborline-platform/specs/modules/ui/hlp.ui.app-shell/interface.yaml` and `projections/react/ui/hlp.ui.app-shell/src/types.ts`

## 1. The rule that decides where logic lives

**The harness decides how things look and behave on screen. It never decides what is true or allowed.**

Test: if two people could disagree about the answer, or it changes when data changes, it comes from the API. If it is only about this person's screen, the harness owns it.

| Layer | Owns |
|---|---|
| **Harness** (`hlp.ui.app-shell`) | Layout, tabs, split panel, Esc order, shortcuts, focus and announcements, container breakpoints, the user's on-screen state (open tabs, pinned tabs, mode, theme) |
| **App** (harborline-app) | Calls the API and turns answers into harness input (the "pack" adapter); Pilot; refetch on push invalidations |
| **API** (local node + server) | Permissions and reasons, counts, My work, actions and approval rules, settings values and their source, sync recency, conflicts, job states, tenant membership |

Evidence:
- DES-0006 L30–44: standing, availability, refusals, provenance and permissions are "returned, never inferred client-side". L337–369: "The app does no filtering."
- rev 5 says the shell owns no route table, grants, standings, tenant scope, HTTP data, session semantics or copilot behaviour.
- ADR-0056: Pilot lives in the app, acts as the user, and has no privileged API.
- Offline-first does not move logic into the harness. The local node (`/api/local-node/*`) *is* the API on the device. ADR-0053 covers offline writes and conflicts, and ADR-0016 covers sync recency.

## 2. Product model

| # | Decision | Evidence |
|---|---|---|
| M1 | Everything is an event stream (conversations, records, places, runs). Relational views are projections. | Owner's Tessera model (2026-10-07) |
| M2 | **Chat-first architecture**; a navigation-first mode is assembled from the same parts. *Open: Q13b round.* | Owner, 2026-10-11 |
| M3 | Conversations are both free-standing and attachable to a record ("discuss this"). Conversation events about a record appear in its Log. Private chat, public effects. | Owner's Tessera model; R-mine (Rovo context failures) |
| M4 | Mode default is set by tenant or role (pack `defaultForPersonas`). The user's choice wins and is stored per user per workspace. Switch lives in the rail's `mode` zone. | rev 5 `PackNavigationModeSwitch`, rail zone order |
| M6 | Stream + faces: the main area shows a stream's conversation face by default. Lists, boards, Details, Timeline and Files are faces built from Views over streams (inline in chat, as panel tabs, or as pages). Workspace mode is a composition of the same parts and is called **Workspace**, never "legacy". Test: every Workspace-mode feature is built from parts that also exist in chat mode. | Chat beat dashboards only on simple tasks and gave no accuracy gain (arXiv 2605.31287, 2026). An agent cut clicks but not time, and preference explained ~half of use (arXiv 2608.19551, 2026). NN/g articulation barrier. GitHub "When chat is the wrong UI" (2026). |
| M7 | Records, places, runs and conversations have a conversation face. Saved views don't. "Ask about this list" starts a conversation with the view as a context chip. | Salesforce Chatter record feeds; ServiceNow activity stream (Zurich); Slack Salesforce channels (Messages / Record details / Related tabs); Linear issue comments + agent sessions. No product gives a view its own feed. |
| M8 | Structured answers in chat are live, query-bound blocks with "Open in tab / page". Writes arrive as approval cards shown only to approvers. | Teams Loop components; Adaptive Cards up-to-date views and approver-only actions (MS Learn, Sep 2026); Lark record cards; Slackbot panel → full-screen (2026) |
| M9 | A conversation carries explicit context chips in its header. Mentioned records are offered, never silently added. Rejects Microsoft 365 Copilot's silent source choice because of auditability and permission scope. | VS Code Copilot suggested attachments and implicit-context toggle (v1.95); M365 Copilot "/" references (counter-example) |
| M10 | Chat-mode Home: a central composer, My work items as cards above, recent conversations below. | Linear agent home; Gemini centred prompt [S]; ChatGPT Pulse cards. The layout is our synthesis. |
| M11 | Chat-mode rail: Home · My work · Conversations (Pinned, Recent, user sections, filter by People / Agents / Places) · Browse · account. My work keeps assigned work and Updates together (a deliberate departure, A1). | Slack and Teams custom sections; ChatGPT pinned chats and grouped Recents |
| M12 | No phone rule. First use follows the tenant or role default (M4); each device class then remembers its last-used mode. | Microsoft 365 Copilot mobile MC1129128 / MC1266021; Linear mobile navigation (Jan 2026); arXiv 2608.19551 |
| M13 | Build order: spec the stream-and-face model, add the chat composition, refactor the navigation layout into a composition of the same parts, and ship both together. M6's test is the acceptance check. | Slack Block Kit, Loop, and the universal action model as analogies |
| M14 | A mode switch keeps every open tab. The primary stream changes face (conversation ↔ structured), and its other face stays available as a tab. | Follows from M6 and P1/P9 |
| M15 | People and the Assistant share one Conversation kind. Participants can be people and agents, and agents join by @mention or assignment. The Assistant tab is a conversation where Pilot is a participant. | Linear @agent and agent assignment; Rovo agents in chat; M3 |
| M5 | A conversation is a Records type (transactional) whose entries are events. No new primitive. **Dependency for the platform team.** | DES-0002 §3 admission test, §15 record classes |

## 3. Search and keys

| # | Decision | Evidence |
|---|---|---|
| S1 | Enter acts on the highlighted row. "Ask the Assistant" is a row (last, or first when the query reads as a question). Mod+Enter always asks. Tab only moves focus. | WAI-ARIA APG modal dialog; WCAG 2.4.3 (R-mine); GitHub palette mode prefixes (R-yours) |
| S2 | Context bar: Filter · Sort · View · New. No second search box. Filter → "text contains" narrows in place. Views are pack-declared (Drawing is a Facilities view). | R-mine one-box rule. *Counter-evidence:* SLDS 2 keeps a local search (R-yours), handled by filter. |
| S3 | No single-letter shell shortcuts. Panel = Mod+Shift+I (rev 5 inspector key). Single letters only inside a focused component (J/K), remappable, listed on a generated "?" sheet. "Start inspection" is a pack command with no default key. | WCAG 2.1.4 Level A (both reports); Linear/GitHub scoped single letters |
| S4 | Tab-strip keys use Mod+Alt combinations. Browsers reserve Ctrl+T, W and N. | R-gpt: ChatGPT desktop uses Ctrl+T/W, which works only in a desktop app |

## 4. Side panel = tab host

| # | Decision | Evidence |
|---|---|---|
| P1 | A tab is **kind + target** (Details·WO-1042, Assistant·conversation 7). Opening what is already open focuses it. Multiple Assistant conversations are multiple tabs. | R-gpt (tab vs page vs output); M365/Notion/Gemini chat history (R-yours) |
| P2 | Tab kinds are consistent but not always applicable: Details, Related, Log, **Timeline** (time axis: spans and future items, the History component), Files, Page, Measures, Layout, Assistant. The old chronological "Timeline" tab stays merged into Log. | Owner; DES-0002 Layout/Views/content store; HistoryTimeline component |
| P3 | Log has filter chips: All · People · System · Agents. Agent actions show "proposed by / approved by". | R-yours (SAP Joule traceability, audit vs discussion) |
| P4 | A **preview tab** follows list selection. Editing, double-click or pin makes it stay. | VS Code preview tabs |
| P5 | New-tab page: **Tools** (applicable kinds plus shortcuts), **Suggested** (from selection and place), **Recents** (incl. reopen closed). Inapplicable kinds are hidden. | Codex desktop screenshot; R-gpt Space Suggested/Recents; rev 5 "no disabled affordance" |
| P6 | The panel auto-opens only from a user action. A job the user started may open its output tab on completion when "Open results when ready" is on (default on) and the panel is visible or the user is on that page. Otherwise: badge plus My work. Never moves focus. | R-gpt (ChatGPT desktop auto-preview setting; Canvas retired); HAX guidelines; WCAG 3.2.1/3.2.2 |
| P7 | A tab that stops applying stays, with a reason. Restored tabs reload through the permission check. | DES-0006 refusal envelope |
| P8 | Unlimited tabs: shrink, then scroll, plus "Search tabs". Pinned tabs show as icons. Two split groups replace the hard-coded "Details + Assistant at ≥1600". Phone uses a tab grid. Tab strip uses the APG tablist pattern. | Chrome; Codex split; R-yours capacity-based panes |
| P9 | Tabs persist per user per workspace via rev 5's injectable storage. Preview tabs reset on destination change. Pinned tabs survive. | rev 5 storage seam |
| P10 | **Pages** (lasting documents in a tree, from Documents and rev 5 `documentSpine`) and **Outputs** (what a conversation or job produced, in the content store) both open as tabs. An output is a draft until saved to a record or page. | R-gpt (Space Pages vs Outputs vs tabs) |
| P11 | Assistant page edits = inline before/after proposal + accept. Attribution comes from the event stream. | R-gpt "Ask for change", "Show attribution"; R-mine diff/undo/audit gap |
| P12 | Panel docks only when the main area keeps its minimum width plus the panel fits. At 1024px, rail + sidebar + panel leave only about 348px. | R-yours space-budget calculation; Fluent inline vs overlay drawer |

## 5. Attention, colour, sync

| # | Decision | Evidence |
|---|---|---|
| A1 | My work count = things that need you only (assigned tasks plus runs waiting for your input or review). Notifications (DES-0055) show inside as "Updates" with no count. No bell. | Linear My Issues vs Inbox, Asana My tasks vs Inbox (R-yours) |
| A2 | Needs-you colour is indigo (attention). Red is reserved for failed, error and destructive states. Number always present. | Not researched; WCAG 1.4.1 only |
| A3 | Counts are numbers everywhere. Packs can't choose a dot. | rev 5 counts rule; WCAG 4.1.3 announcements |
| A4 | Sync strip appears when offline, queued, in conflict, or stale beyond a threshold. Reads `/sync-status` recency. Never says "Synced". A conflict creates a My work item. | ADR-0016, ADR-0053 |

## 6. Actions and the assistant

| # | Decision | Evidence |
|---|---|---|
| C1 | The domain actions catalogue comes from Workflows (effect, permission, approval, offline policy, server verdict). The shell owns only chrome commands. One catalogue drives search, New, selection bar, More and the Assistant's "/". | DES-0002 "Workflows is the only effecting kind"; ADR-0059; VS Code contributions (both reports) |
| C2 | Pilot plans app-side. Approving a proposal submits a command or Workflow run, which the execution runtime makes durable. The job centre shows runs. **Dependency: a runs read API.** | ADR-0056; DES-0002 §21 execution runtime; Sidekick, Linear sessions (both reports) |
| C3 | Denied navigation is hidden. Denied actions render as the API declares (hidden, or disabled with the server's reason). `user.admin` becomes capabilities from `/me`. | DES-0002 §14 rule 3; DES-0006 L300–308, L381–404; R-yours trust boundary |

## 7. Connecting to the API

| # | Decision | Evidence |
|---|---|---|
| X1 | The harness "pack" becomes the app adapter's output (`/me` + `/navigation/workspaces` + Views results). Pack JSON survives only as gallery and test fixtures. | rev 5 boundary; `PackNavigationRoutes.cs` "no second navigation store" |
| X2 | Place = a pack-declared record type plus a hierarchy (relationship or taxonomy). The scope chip passes the place to Views as a parameter. **Dependency: Views parameters.** | DES-0002 §14 (filter folded into the query), §15 (locations are master records) |

## 8. Tenancy and account

| # | Decision | Evidence |
|---|---|---|
| T1 | Tenant switcher exists in the harness even though R1 has one tenant. It lives in the **avatar menu**, and the tenant mark is always Home. | Owner; Azure portal "Switch directory", VS Code Accounts |
| T2 | Avatar menu: identity header (name · role · tenant), inline tenant list with role and count ("All tenants…" dialog past about five), Settings Ctrl+, , Sync status line, Help & shortcuts ›, Sign out last. Profile, notifications, theme and language move into Settings. | Owner's four menu examples (Codex, Claude, VS Code, a credits-based app) |
| T3 | Switching tenant is a hard boundary: per-tenant tabs, mode and place memory. Jobs stay in their tenant. Nothing carries across. | DES-0006 L291–293 (tenancy ambient) |
| T4 | Tenant and workspace switching are two levels with two homes (avatar menu vs rail `workspaces` or `mode` zone). | rev 5 zones |
| T5 | Avatar badge = account actions only (re-sign-in, device lease, invitations), as a number. Other tenants' counts appear only inside the menu. | VS Code Accounts badge |
| T6 | Rail avatar: 56px icon-only, 80px adds first name. Accessible name includes tenant. APG menu button pattern. | Codex vs Claude triggers |

## 9. Settings (presentation only; behaviour belongs to the API)

- **One Settings** surface with scope sections (You · This device · Workspace · Organization). The API returns which settings exist for this user, each with value, source, lock and scope; the harness only renders it. Evidence: DES-0002 `CascadeDefaults`; Linear and Notion single settings.
- Operational admin (members, grants, audit, packs) is an Admin destination built like any other, not part of Settings.
- Sign-out, local data, shared devices and device removal are **local-node behaviour**. The harness shows a dialog only when the API says there is something to warn about. The app is offline-first, so queued writes are normal and sign-out is silent by default.

## 9a. Extensibility and releases

| # | Decision | Evidence |
|---|---|---|
| E1 | Only the platform adds renderers (tab kinds, block kinds), compiled into both React and Blazor. Packs compose from them, with Layout tabs and blocks for custom needs. Packs never ship UI code. | DES-0002 §3 transport rule, §10–11; HLP-0002; VS Code contribution points; Shopify App Bridge intent model |
| E2 | No third-party extensions in R1. Ids, versioning and conditions are designed so they could be added later. | R-yours P3 "Full extension/plugin runtime" (isolation, versioning, supply chain); R-mine contract section |
| E3 | Contributions come from packs. Tenant admins may hide, reorder and choose defaults (tabs, Home cards) within the same budgets, through the settings cascade (pack → organization → workspace → user). Admin-authored Layout tabs wait until Layout authoring exists. | Atlassian admin vs per-user navigation; DES-0002 §16 Navigation composition; `CascadeDefaults` |
| E4 | Twelve contribution points: destinations, sidebar sections, tab kinds per record kind, block kinds, actions with placements, Home cards, context providers, search sources, suggestions, modes, shortcuts, look and words. Fixed system slots, budgets, conditions as data that never grant, stable ids, versioned schema, unknown contributions skipped with a diagnostic. Every rev 5 extension point maps onto one. | VS Code `when`; Backstage `if`; Carbon fixed slots; SAP overflow priorities; Material 3–7 rail; DES-0002 §10 rule 2 |
| V0 | Core behaviour (modes, tab host, docking, keyboard and focus, search surface, menus, Esc order, auto-open rules) is harness code, changed only by new revisions of `hlp.ui.app-shell`. Configuration covers only what legitimately varies by tenant, role or person. | HLP-0001 deep modules; the spec's per-revision invariants and cases; VS Code workbench parts are core |
| V1 | Built-ins go through the same internal registry packs feed. Chat and Workspace are registered mode compositions, and every built-in tab kind is a registration. | VS Code built-in extensions (Git, Markdown) on the public API; M6 test |
| V2 | Each new harness behaviour ships behind a per-tenant flag: off → opt-in → default on with opt-out → required. The flag is removed at the next revision. Flags resolve through OpenFeature. Packs declare a minimum harness revision, and breaking changes get a deprecation window. | Atlassian navigation rollout (2.6% opt-out); Linear staged redesign; OpenFeature; DES-0002 kernel contract window |

## 10. Handed to the API and platform side

1. Views query parameters (place scope, X2)
2. Conversation storage as a Records type (M5)
3. Runs read API over the execution runtime (C2)
4. Settings cascade read API with source and lock per value (§9)
5. Local-node sign-out and device-data rules (shared devices, lease expiry, remote wipe)
6. Pack and destination-level declaration of panel tab kinds per record kind
7. Fix: .NET client sends `X-Harborline-Tenant`, but DES-0006 L292 says passing a tenant is a 400

## 11. Still open

- Nothing. The Q13b round is closed (M6–M15). Notes: `research_notes/Chat first shell patterns/chat_first_shell.md`.
- **Six Tessera contradictions:** all resolved. Search keys = S1, panel tabs = P2/P3, colour = A2, context bar = S2, I key = S3, inbox signal = A3.
