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
  assert.match(component, /aria-label="@TabName\(tab\)"/);
  assert.match(component, /disabled="@tab.Pinned"/);
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
  const handlers = new Map();
  const root = {
    addEventListener(type, fn) { handlers.set(type, fn); }, removeEventListener(type, fn) { assert.equal(fn, handlers.get(type)); handlers.delete(type); },
    focus() { document.activeElement = this; },
    contains(element) { return tabs.includes(element) || element === closeTrigger; },
    querySelector(selector) { return selector === '[role="dialog"]' ? dialog : visibleTabs[0] ?? null; }, querySelectorAll(selector) { return selector === '[role="tab"]' ? visibleTabs : []; }
  };
  const panel = { id: 'editor-a' };
  const closeTrigger = { ...element(), closest: () => panel };
  const key = (value, target, shiftKey = false) => { let prevented = false; handlers.get('keydown')({ key: value, target, shiftKey, preventDefault() { prevented = true; } }); assert.equal(prevented, true); };
  bind(root);
  assert.deepEqual(tabs[0].scrollOptions, { block: 'nearest', inline: 'nearest' });
  tabs[2].focus(); key('ArrowRight', tabs[2]); assert.equal(document.activeElement, tabs[0]); assert.equal(tabs[0].clicks, 1);
  key('End', tabs[0]); assert.equal(document.activeElement, tabs[2]);
  key('Home', tabs[2]); assert.equal(document.activeElement, tabs[0]);
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[0].disabled ? buttons[1] : buttons[0], querySelectorAll: () => buttons.filter(button => !button.disabled) };
  bind(root); assert.equal(document.activeElement, buttons[0]);
  buttons[0].disabled = true;
  bind(root); assert.equal(document.activeElement, buttons[1]);
  document.activeElement = { tagName: 'BODY' };
  bind(root); assert.equal(document.activeElement, buttons[1]);
  buttons[2].focus();
  key('Tab', buttons[2]); assert.equal(document.activeElement, buttons[1]);
  key('Tab', buttons[1], true); assert.equal(document.activeElement, buttons[2]);
  dialog.querySelectorAll = () => [];
  key('Tab', buttons[2]); assert.equal(document.activeElement, buttons[2]);
  dialog = null; bind(root); assert.equal(document.activeElement, tabs[0]);
  visibleTabs = []; bind(root); assert.equal(document.activeElement, root);
  visibleTabs = tabs;
  closeTrigger.focus();
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[1], querySelectorAll: () => buttons.filter(button => !button.disabled) };
  bind(root, 'alice');
  dialog = null; bind(root, 'alice'); assert.equal(document.activeElement, closeTrigger);
  closeTrigger.focus();
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[1], querySelectorAll: () => buttons.filter(button => !button.disabled) };
  bind(root, 'alice');
  panel.id = 'editor-b'; // Razor reused a surviving section after Discard.
  dialog = null; bind(root, 'alice'); assert.equal(document.activeElement, tabs[0]);
  closeTrigger.focus();
  dialog = { contains: element => buttons.includes(element), querySelector: () => buttons[1], querySelectorAll: () => buttons.filter(button => !button.disabled) };
  bind(root, 'alice');
  dialog = null; bind(root, 'bob'); assert.equal(document.activeElement, tabs[0]);
  unbind(root); assert.equal(handlers.size, 0);
  delete globalThis.document;
});

// P8 search addresses an existing presented tab; it never owns the host's active
// selection. This DOM double checks event/focus calls, not browser rendering.
test('Search tabs closes its disclosure, focuses the addressed tab and Escape returns to its trigger', () => {
  globalThis.document = { activeElement: null };
  const handlers = new Map();
  let adds = 0;
  let dialog = null;
  const element = id => ({ id, clicks: 0, focus() { document.activeElement = this; },
    click() { this.clicks++; }, scrollIntoView(value) { this.scrollOptions = value; } });
  const tabs = [element('tab-details'), element('tab-files')];
  const trigger = { ...element('search-trigger'), getAttribute: name => name === 'popovertarget' ? 'search-layer' : null };
  const modalButton = element('save');
  const search = { id: 'search-layer', open: true, hides: 0,
    matches(selector) { return selector === ':popover-open' && this.open; },
    hidePopover() { assert.equal(this.open, true); this.open = false; this.hides++; } };
  const result = { closest: selector => selector === '[data-tab-search]' ? search : result,
    getAttribute: name => name === 'data-tab-search-target' ? 'tab-files' : null };
  const nestedIcon = { closest: () => result };
  const root = {
    addEventListener(type, fn) { adds++; handlers.set(type, fn); },
    removeEventListener(type, fn) { assert.equal(handlers.get(type), fn); handlers.delete(type); },
    contains(node) { return node === result; },
    querySelector(selector) {
      if (selector === '[role="dialog"]') return dialog;
      if (selector === '[data-tab-search]:popover-open') return search.open ? search : null;
      if (selector === '[role="tab"][aria-selected="true"]') return tabs[0];
      return null;
    },
    querySelectorAll(selector) {
      if (selector === '[role="tab"]') return tabs;
      if (selector === '[data-tab-search-trigger]') return [trigger];
      if (selector === '[data-tab-search]:popover-open') return search.open ? [search] : [];
      return [];
    },
  };
  try {
    bind(root, 'alice'); bind(root, 'alice');
    assert.equal(adds, 2);
    handlers.get('click')({ target: nestedIcon });
    assert.equal(search.open, false); assert.equal(search.hides, 1);
    assert.equal(document.activeElement, tabs[1]);
    assert.deepEqual(tabs[1].scrollOptions, { block: 'nearest', inline: 'nearest' });
    assert.equal(tabs[0].clicks + tabs[1].clicks, 0);
    search.open = true;
    let prevented = false; let stopped = false;
    handlers.get('keydown')({ key: 'Escape', target: trigger,
      preventDefault() { prevented = true; }, stopPropagation() { stopped = true; } });
    assert.equal(prevented, true); assert.equal(stopped, true);
    assert.equal(search.open, false); assert.equal(search.hides, 2);
    assert.equal(document.activeElement, trigger);
    search.open = true;
    dialog = { contains: node => node === modalButton, querySelector: () => modalButton };
    bind(root, 'alice');
    assert.equal(search.open, false); assert.equal(search.hides, 3);
    assert.equal(document.activeElement, modalButton);
    handlers.get('click')({ target: nestedIcon });
    assert.equal(document.activeElement, modalButton); assert.equal(search.hides, 3);
    dialog = null; bind(root, 'alice');
    search.open = true;
    const foreignResult = { closest: () => foreignResult };
    handlers.get('click')({ target: { closest: () => foreignResult } });
    assert.equal(search.open, true); assert.equal(document.activeElement, tabs[0]);
    unbind(root); assert.equal(search.open, false); assert.equal(search.hides, 4);
    assert.equal(handlers.size, 0);
  } finally { unbind(root); delete globalThis.document; }
});

test('tab traversal wraps left, scrolls the new tab and ignores editable or detached targets', () => {
  globalThis.document = { activeElement: null };
  const handlers = new Map();
  const element = () => ({ clicks: 0, matches: selector => selector === '[role="tab"]',
    focus() { document.activeElement = this; }, click() { this.clicks++; },
    scrollIntoView(value) { this.scrollOptions = value; } });
  const tabs = [element(), element(), element()];
  let visibleTabs = tabs;
  const root = {
    addEventListener(type, fn) { handlers.set(type, fn); },
    removeEventListener(type, fn) { assert.equal(handlers.get(type), fn); handlers.delete(type); },
    querySelector(selector) { return selector === '[role="tab"][aria-selected="true"]' ? visibleTabs[0] ?? null : null; },
    querySelectorAll(selector) { return selector === '[role="tab"]' ? visibleTabs : []; },
    contains() { return false; }, focus() { document.activeElement = this; },
  };
  const key = target => {
    let prevented = false;
    handlers.get('keydown')({ key: 'ArrowLeft', target, preventDefault() { prevented = true; } });
    return prevented;
  };
  try {
    bind(root);
    assert.equal(key(tabs[0]), true);
    assert.equal(document.activeElement, tabs[2]); assert.equal(tabs[2].clicks, 1);
    assert.deepEqual(tabs[2].scrollOptions, { block: 'nearest', inline: 'nearest' });
    assert.equal(key({ matches: () => false }), false);
    assert.equal(key(element()), false);
    visibleTabs = []; bind(root);
    assert.equal(document.activeElement, root);
    assert.equal(key(tabs[0]), false);
    unbind(root); assert.equal(handlers.size, 0);
  } finally { unbind(root); delete globalThis.document; }
});

// The neutral scrollbar fixture is the literal oracle. This source guard does
// not measure browser scrollbars, theme contrast or rendered Razor behavior.
test('both native scroll surfaces carry the isolated themed grab contract', () => {
  const component = read('projections/blazor/ui/hlp.ui.app-shell/rev6/HarborlineAppShellRev6TabHost.razor');
  const caller = read('gallery/projections/blazor/Stories/AppShellRev6ComposedScenario.razor');
  const hostCss = read('gallery/projections/blazor/Stories/AppShellRev6ComposedScenario.razor.css');
  assert.match(caller, /<div class="hl-r6-native">\s*<HarborlineAppShellRev6TabHost /);
  const hostStrip = hostCss.match(/\.hl-r6-native ::deep \[role="tablist"\]\s*\{([^}]*)\}/)?.[1];
  assert.ok(hostStrip);
  assert.match(hostStrip, /height:\s*36px;/);
  assert.match(hostStrip, /scrollbar-width:\s*thin;/);
  assert.match(hostStrip, /border-bottom:\s*1px solid/);
  assert.doesNotMatch(hostStrip, /(?:height|scrollbar-width)\s*:[^;]*!important/);
  const strip = component.match(/<div role="tablist"[^>]*>/)?.[0];
  const search = component.match(/<div id="@searchResultsId"[^>]*>/)?.[0];
  assert.ok(strip?.includes('data-shell-scroll-region'));
  assert.ok(search?.includes('data-shell-scroll-region'));
  const stripStyle = strip.match(/style="([^"]*)"/)?.[1];
  assert.ok(stripStyle);
  assert.match(stripStyle, /(?:^|;)scrollbar-width:auto;/);
  assert.match(stripStyle, /(?:^|;)height:auto;/);
  assert.ok(stripStyle.includes('box-sizing:border-box;'));
  assert.ok(stripStyle.includes('min-height:calc(34px + 24px + 4px + 1px);'));
  assert.ok(stripStyle.includes('padding:2px'));
  assert.ok(component.includes('block-size:34px;'));
  assert.doesNotMatch(component, /scrollbar-width\s*:\s*thin/);
  const css = component.match(/<style>([\s\S]*?)<\/style>/)?.[1];
  assert.ok(css);
  const region = '.hl-app-shell-rev6-tab-host [data-shell-scroll-region]';
  const rule = (text, selector) => {
    const start = text.indexOf(selector + ' {');
    assert.notEqual(start, -1, 'Missing scroll rule: ' + selector);
    const body = text.slice(start + selector.length + 2);
    return body.slice(0, body.indexOf('}'));
  };
  assert.ok(rule(css, region).includes('scrollbar-color: var(--color-border, var(--hl-border, #595959)) transparent;'));
  assert.ok(rule(css, region + ':hover').includes('scrollbar-color: var(--color-muted-foreground, var(--hl-muted-foreground, #595959)) transparent;'));
  const grab = rule(css, region + '::-webkit-scrollbar');
  assert.ok(grab.includes('width: 24px;')); assert.ok(grab.includes('height: 24px;'));
  assert.ok(css.includes(region + '::-webkit-scrollbar-track,'));
  assert.ok(rule(css, region + '::-webkit-scrollbar-corner').includes('background: transparent;'));
  const thumb = rule(css, region + '::-webkit-scrollbar-thumb');
  assert.ok(thumb.includes('border: 3px solid transparent;'));
  assert.ok(thumb.includes('background-color: var(--color-border, var(--hl-border, #595959));'));
  assert.ok(thumb.includes('background-clip: padding-box;'));
  assert.ok(rule(css, region + '::-webkit-scrollbar-thumb:hover').includes('background-color: var(--color-muted-foreground, var(--hl-muted-foreground, #595959));'));
  const webkit = css.slice(css.indexOf('@@supports selector(::-webkit-scrollbar)'));
  assert.ok(rule(webkit, region + ':hover').includes('scrollbar-color: auto;'));
  const forced = css.slice(css.indexOf('@@media (forced-colors: active)'));
  assert.ok(rule(forced, region + ':hover').includes('forced-color-adjust: auto;'));
  assert.ok(rule(forced, region + '::-webkit-scrollbar-thumb').includes('background-color: ButtonText;'));
  assert.ok(rule(forced, region + '::-webkit-scrollbar-thumb:hover').includes('background-color: Highlight;'));
  assert.ok(rule(forced, region + ' :focus-visible').includes('outline-color: Highlight;'));
  assert.doesNotMatch(css, /\.hl-app-shell \[/);
});
