const frames = new WeakMap();
const enabled = 'button:not(:disabled),a[href],input:not(:disabled),select:not(:disabled),textarea:not(:disabled),[tabindex]:not([tabindex="-1"])';
function visible(node) {
  return !!node && node.isConnected !== false && !node.closest?.('[hidden],[inert],[aria-hidden="true"]')
    && (!node.getClientRects || node.getClientRects().length > 0);
}
function controls(dialog) { return [...dialog.querySelectorAll(enabled)].filter(visible); }
function focusFirst(dialog) { (controls(dialog)[0] ?? dialog).focus(); }
function hideSearch(root) {
  for (const search of root.querySelectorAll('[data-tab-search]:popover-open')) search.hidePopover();
}
function fallback(root) {
  const selected = root.querySelector('[role="tab"][aria-selected="true"]');
  (visible(selected) ? selected : root.querySelector('main'))?.focus();
}
function rememberPanelReturn(root, binding, dismiss = null) {
  const origin = document.activeElement;
  const losesFocus = root.querySelector('.hl-rev6-panel')?.contains(origin)
    || (dismiss && (dismiss === origin || dismiss.contains?.(origin)));
  binding.panelReturn = losesFocus ? { owner: binding.owner, origin } : null;
}
function invoke(binding, method, ...args) {
  if (binding.disposed) return;
  binding.callback.invokeMethodAsync(method, ...args).catch(error => {
    if (!binding.disposed) throw error;
  });
}
export function bind(root, callback, observe = true, ownerScope = null) {
  let binding = frames.get(root);
  if (!binding) {
    binding = { callback, owner: ownerScope, modal: null, origin: null, editor: null,
      drawerTrigger: null, explicitDrawerDismiss: false, panelReturn: null,
      panelOpen: false, tabCount: 0, cleanClose: null, observer: null, disposed: false };
    binding.captureClick = event => {
      const modal = binding.modal;
      const dismissDrawer = modal?.hasAttribute('data-navigation-dialog') && event.target.closest?.('[data-drawer-dismiss]');
      if (modal && !modal.contains(event.target) && !dismissDrawer) {
        event.preventDefault(); event.stopImmediatePropagation(); focusFirst(modal); return;
      }
      if (modal?.hasAttribute('data-navigation-dialog') && (dismissDrawer || event.target.closest?.('[data-drawer-row]'))) binding.explicitDrawerDismiss = true;
      const close = event.target.closest?.('[data-tab-close]');
      if (close && !close.disabled) binding.cleanClose = { owner: binding.owner, origin: close, editor: close.closest('[role="tabpanel"]')?.id ?? null };
      const dismissPanel = event.target.closest?.('[data-panel-close]');
      if (dismissPanel) rememberPanelReturn(root, binding, dismissPanel);
    };
    binding.captureKey = event => {
      const modal = binding.modal;
      if (!modal) return;
      if (!modal.contains(event.target)) {
        event.preventDefault(); event.stopImmediatePropagation(); focusFirst(modal); return;
      }
      if (event.key === 'Tab') {
        const items = controls(modal);
        const index = items.indexOf(document.activeElement);
        event.preventDefault(); event.stopPropagation();
        (items.length ? items[(index + (event.shiftKey ? -1 : 1) + items.length) % items.length] : modal).focus();
      }
    };
    binding.captureFocus = event => {
      if (binding.panelReturn && event.target !== binding.panelReturn.origin) binding.panelReturn = null;
      if (binding.modal && !binding.modal.contains(event.target)) focusFirst(binding.modal);
    };
    binding.key = event => {
      if (event.defaultPrevented || event.key !== 'Escape') return;
      if (root.querySelector('[data-dirty-dialog]') || root.querySelector('[data-tab-search]:popover-open')) return;
      if (root.querySelector('[data-navigation-dialog]')) {
        binding.explicitDrawerDismiss = true;
        event.preventDefault(); event.stopPropagation(); invoke(binding, 'DismissNavigation', binding.owner);
      } else if (root.getAttribute('data-panel-open') === 'true') {
        rememberPanelReturn(root, binding, document.activeElement?.closest?.('[data-panel-close]'));
        event.preventDefault(); event.stopPropagation(); invoke(binding, 'DismissPanel', binding.owner);
      } else if (root.getAttribute('data-focus-mode') === 'true') {
        event.preventDefault(); event.stopPropagation(); invoke(binding, 'ExitFocus', binding.owner);
      }
    };
    root.addEventListener('click', binding.captureClick, true);
    root.addEventListener('keydown', binding.captureKey, true);
    root.addEventListener('focusin', binding.captureFocus, true);
    root.addEventListener('keydown', binding.key);
    frames.set(root, binding);
  }
  binding.callback = callback;
  const ownerChanged = binding.owner !== ownerScope;
  if (ownerChanged) {
    binding.modal = null; binding.origin = null; binding.editor = null;
    binding.drawerTrigger = null; binding.explicitDrawerDismiss = false; binding.panelReturn = null; binding.cleanClose = null;
  }
  binding.owner = ownerScope;
  const previous = binding.modal;
  const modal = root.querySelector('[data-dirty-dialog]') ?? root.querySelector('[data-navigation-dialog]');
  if (modal) {
    hideSearch(root); binding.cleanClose = null;
    if (modal !== previous) {
      if (modal.hasAttribute('data-dirty-dialog')) {
        const origin = previous?.hasAttribute('data-navigation-dialog') ? null : document.activeElement;
        binding.origin = !ownerChanged && root.contains(origin) ? origin : null;
        binding.editor = binding.origin?.closest('[role="tabpanel"]')?.id ?? null;
        binding.drawerTrigger = null; binding.explicitDrawerDismiss = false;
      } else {
        binding.drawerTrigger = root.querySelector('[data-navigation-trigger]');
        binding.explicitDrawerDismiss = false;
      }
      binding.modal = modal; focusFirst(modal);
    } else if (!modal.contains(document.activeElement) || document.activeElement.disabled) focusFirst(modal);
  } else {
    binding.modal = null;
    if (previous && !ownerChanged) {
      if (previous.hasAttribute('data-dirty-dialog')) {
        const origin = binding.origin;
        if (binding.editor !== null && visible(origin) && root.contains(origin)
          && origin.closest('[role="tabpanel"]')?.id === binding.editor) origin.focus();
        else fallback(root);
      } else if (binding.explicitDrawerDismiss && root.getAttribute('data-form') === 'phone') {
        if (visible(binding.drawerTrigger) && root.contains(binding.drawerTrigger)) binding.drawerTrigger.focus();
        else fallback(root);
      }
    }
    binding.origin = null; binding.editor = null; binding.explicitDrawerDismiss = false;
  }
  const tabCount = root.querySelectorAll('[role="tab"]').length;
  if (!ownerChanged && !modal && binding.cleanClose && tabCount < binding.tabCount) {
    const close = binding.cleanClose;
    if (close.owner === ownerScope && (!visible(close.origin) || close.origin.closest('[role="tabpanel"]')?.id !== close.editor)) fallback(root);
    binding.cleanClose = null;
  }
  binding.tabCount = tabCount;
  const panelOpen = root.getAttribute('data-frame-controlled-panel-open') === 'true';
  if (!ownerChanged && binding.panelOpen && !panelOpen && binding.panelReturn && !modal) {
    const pending = binding.panelReturn;
    const active = document.activeElement;
    const focusLost = active === pending.origin || (!visible(pending.origin) && (active === null || active === document.body));
    if (pending.owner === ownerScope && focusLost) {
      const toggle = root.querySelector('[data-panel-toggle]');
      (visible(toggle) ? toggle : root.querySelector('main'))?.focus();
    }
  }
  if (!panelOpen) binding.panelReturn = null;
  binding.panelOpen = panelOpen;
  if (!observe && binding.observer) { binding.observer.disconnect(); binding.observer = null; }
  if (observe && !binding.observer && globalThis.ResizeObserver) {
    const observer = new ResizeObserver(entries => {
      if (binding.disposed || frames.get(root) !== binding || binding.observer !== observer) return;
      for (const entry of entries) {
        if (entry.target !== root) continue;
        const box = entry.contentBoxSize;
        const width = (Array.isArray(box) ? box[0]?.inlineSize : box?.inlineSize) ?? entry.contentRect.width;
        if (Number.isFinite(width) && width >= 0) invoke(binding, 'MeasureInlineSize', width);
      }
    });
    binding.observer = observer; observer.observe(root);
  }
}
export function unbind(root) {
  const binding = frames.get(root);
  if (!binding) return;
  binding.disposed = true; binding.observer?.disconnect(); hideSearch(root);
  root.removeEventListener('click', binding.captureClick, true);
  root.removeEventListener('keydown', binding.captureKey, true);
  root.removeEventListener('focusin', binding.captureFocus, true);
  root.removeEventListener('keydown', binding.key);
  frames.delete(root);
}
