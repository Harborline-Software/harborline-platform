# Experimental rev6 first slice

The pure browser/Node module lives in `projections/shared/ui/hlp.ui.app-shell/rev6` and is shared by React Storybook and Blazor Blazing Story through JS interop. Both render the same neutral event corpus. The standalone slice catalog links them without changing the live rev5 catalog, fixtures, public package exports or default shell.

Run `node --test conformance/hlp.ui.app-shell/rev6/tab-identity-state.test.mjs`.

Oracles: literal target lists and stable addresses from P1/P4/P9; explicit dirty-editor and owner-boundary properties from the approved first-slice clarification in staged rev6. Tests execute the reducer; fixture observations are not their own expected results. No mutation score is claimed.

Scope: tab dedupe, preview promotion and closure, dirty destruction requests, owner presentation isolation and retained work. Save exposes `saveRequest` with the original owner, unique requestId, tab id and draft generation. The adapter must echo those fields in `saved` after success. A failed save has no success event and retains the editor. Stale acknowledgments cannot close another owner's editor or a later draft generation. Cancel invalidates the request. `retain` models opaque owner-bound host data; it does not execute or authorize queued actions. Identity addresses use tuple serialization to avoid delimiter collisions. Sign-out removes only the active presentation pointer.

This is an in-memory model, not persistence or a security boundary. The host must own private state and API authorization. Native projection integration, real editor prompts, focus, tabs accessibility, persistence, mode/group/docking behavior, full registry binding, gallery visual parity and native builds remain later slices. Existing ViewKindRegistry already supplies Kind/Renderer/RequiredRoles; this slice adds no alternate renderer registry. Its closed tab-kind list is only tab host validation and accepts no executable pack contribution.

The stories are state observations, not interactive native shell implementations. Native build and browser verification are intentionally deferred under the no-heavy-build instruction.
