import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { bind, unbind } from '../../../projections/blazor/ui/hlp.ui.app-shell/rev6/wwwroot/app-shell-rev6-focus.mjs';
const read = path => readFileSync(new URL('../../../' + path, import.meta.url), 'utf8');

test('native contract exposes current owner presentation without reducer owners map', () => {
  const types = read('projections/blazor/ui/hlp.ui.app-shell/rev6/AppShellRev6Types.cs');
  const component = read('projections/blazor/ui/hlp.ui.app-shell/rev6/HarborlineAppShellRev6TabHost.razor');
  assert.match(types, /AppShellRev6Presentation\(AppShellRev6Owner Owner/);
  assert.doesNotMatch(types + component, /\bOwners\b|\bowners\b|initialState|transition\(/);
  assert.match(component, /Presentation\?\.Owner == Owner/);
  assert.match(component, /previousOwner != Owner\) \{ current = null/);
  assert.match(component, /role="tablist"/);
  assert.match(component, /role="dialog" aria-modal="true"/);
  assert.match(component, /inert=/);
  assert.match(component, /EditorRequired\] public string ShellId/);
  assert.match(component, /data-shell-id="@ShellId"/);
  assert.match(component, /Kind: tab.Kind, Target: tab.Target/);
  assert.match(component, /aria-label="Workspace tab host" tabindex="-1"/);
  assert.doesNotMatch(component, /\u0007/);
  assert.match(component, /• Unsaved/);
  assert.match(component, /tab.Pinned \? "Pinned"/);
});

test('story retains shared reducer and neutral fixtures behind a guarded host boundary', () => {
  const story = read('gallery/projections/blazor/Stories/AppShellRev6NativeScenario.razor');
  const gallery = read('gallery/projections/blazor/Harborline.Gallery.Blazor.csproj');
  const packageProject = read('projections/blazor/ui/hlp.ui.button/Harborline.UIAdapters.Blazor.csproj');
  assert.match(story, /"runScenario"/);
  assert.match(story, /"transition"/);
  assert.match(story, /"presentation"/);
  assert.match(story, /requestEpoch != epoch/);
  assert.match(story, /requestId = request.RequestId, generation = request.Generation/);
  assert.match(story, /Owner="owner" Presentation="view"/);
  assert.match(gallery, /LogicalName="app-shell-rev6-state-scenarios.json"/);
  assert.match(gallery, /Link="wwwroot\\rev6\\tab-identity-state.mjs"/);
  assert.match(packageProject, /hlp.ui.app-shell\/rev6\/wwwroot\/\*\*" Link="wwwroot\/%\(Filename\)%\(Extension\)"/);
});

// APG keyboard contract is the oracle: Right wraps, Home/End activate endpoints,
// modal Tab wraps, and closing focuses a surviving selected tab or the main host.
test('focus module activates tabs, traps modal focus and recovers after save and close', () => {
  globalThis.document = { activeElement: null };
  const element = () => ({ isConnected: true, clicks: 0, matches: () => true, closest: () => null, focus() { document.activeElement = this; }, click() { this.clicks++; }, scrollIntoView(options) { this.scrollOptions = options; } });
  const tabs = [element(), element(), element()];
  const buttons = [element(), element(), element()];
  let dialog = null;
  let visibleTabs = tabs;
  let handler;
  const root = {
    addEventListener(_, fn) { handler = fn; }, removeEventListener(_, fn) { assert.equal(fn, handler); handler = null; },
    focus() { document.activeElement = this; },
    contains(element) { return tabs.includes(element) || element === closeTrigger; },
    querySelector(selector) { return selector === '[role="dialog"]' ? dialog : visibleTabs[0] ?? null; }, querySelectorAll() { return visibleTabs; }
  };
  const panel = { id: 'editor-a' };
  const closeTrigger = { ...element(), closest: () => panel };
  const key = (value, target, shiftKey = false) => { let prevented = false; handler({ key: value, target, shiftKey, preventDefault() { prevented = true; } }); assert.equal(prevented, true); };
  bind(root);
  assert.deepEqual(tabs[0].scrollOptions, { block: 'nearest', inline: 'nearest' });
  tabs[2].focus(); key('ArrowRight', tabs[2]); assert.equal(document.activeElement, tabs[0]); assert.equal(tabs[0].clicks, 1);
  key('End', tabs[0]); assert.equal(document.activeElement, tabs[2]);
  key('Home', tabs[2]); assert.equal(document.activeElement, tabs[0]);
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[0].disabled ? buttons[1] : buttons[0], querySelectorAll: () => buttons };
  bind(root); assert.equal(document.activeElement, buttons[0]);
  buttons[0].disabled = true;
  bind(root); assert.equal(document.activeElement, buttons[1]);
  document.activeElement = { tagName: 'BODY' };
  bind(root); assert.equal(document.activeElement, buttons[1]);
  buttons[2].focus();
  key('Tab', buttons[2]); assert.equal(document.activeElement, buttons[0]);
  key('Tab', buttons[0], true); assert.equal(document.activeElement, buttons[2]);
  dialog.querySelectorAll = () => [];
  key('Tab', buttons[2]); assert.equal(document.activeElement, buttons[2]);
  dialog = null; bind(root); assert.equal(document.activeElement, tabs[0]);
  visibleTabs = []; bind(root); assert.equal(document.activeElement, root);
  visibleTabs = tabs;
  closeTrigger.focus();
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[1], querySelectorAll: () => buttons };
  bind(root, 'alice');
  dialog = null; bind(root, 'alice'); assert.equal(document.activeElement, closeTrigger);
  closeTrigger.focus();
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[1], querySelectorAll: () => buttons };
  bind(root, 'alice');
  panel.id = 'editor-b'; // Razor reused a surviving section after Discard.
  dialog = null; bind(root, 'alice'); assert.equal(document.activeElement, tabs[0]);
  closeTrigger.focus();
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[1], querySelectorAll: () => buttons };
  bind(root, 'alice');
  dialog = null; bind(root, 'bob'); assert.equal(document.activeElement, tabs[0]);
  unbind(root); assert.equal(handler, null);
  delete globalThis.document;
});
