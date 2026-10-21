const bindings = new WeakMap();
function searchTrigger(root, search) {
  return [...root.querySelectorAll('[data-tab-search-trigger]')]
    .find(trigger => trigger.getAttribute('popovertarget') === search.id);
}
function hideSearch(root) {
  for (const search of root.querySelectorAll('[data-tab-search]:popover-open')) search.hidePopover();
}
export function bind(root, ownerScope = null, emptyFallback = root, manageCloseRecovery = true) {
  let binding = bindings.get(root);
  if (!binding) {
    binding = { dialog: null, tabCount: 0, trigger: null, triggerPanel: null, triggerOwner: null, handler: event => {
      const dialog = root.querySelector('[role="dialog"]');
      if (dialog && event.key === 'Tab') {
        const buttons = [...dialog.querySelectorAll('button:not(:disabled)')];
        if (!buttons.length) { event.preventDefault(); return; }
        const index = buttons.indexOf(document.activeElement);
        event.preventDefault();
        buttons[(index + (event.shiftKey ? -1 : 1) + buttons.length) % buttons.length]?.focus();
      } else if (!dialog && event.key === 'Escape' && root.querySelector('[data-tab-search]:popover-open')) {
        const search = root.querySelector('[data-tab-search]:popover-open');
        event.preventDefault(); event.stopPropagation();
        search.hidePopover(); searchTrigger(root, search)?.focus();
      } else if (!dialog && event.target.matches('[role="tab"]') && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
        const tabs = [...root.querySelectorAll('[role="tab"]')];
        const index = tabs.indexOf(event.target);
        if (index < 0 || tabs.length === 0) return;
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowLeft' ? -1 : 1) + tabs.length) % tabs.length;
        event.preventDefault(); tabs.forEach((tab, i) => tab.tabIndex = i === next ? 0 : -1); tabs[next]?.focus(); tabs[next]?.click();
        tabs[next]?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
      }
    } };
    binding.clickHandler = event => {
      if (root.querySelector('[role="dialog"]')) return;
      const result = event.target.closest?.('[data-tab-search-target]');
      if (!result || !root.contains(result)) return;
      const search = result.closest('[data-tab-search]');
      if (!search) return;
      if (search.matches(':popover-open')) search.hidePopover();
      const target = result.getAttribute('data-tab-search-target');
      const tab = [...root.querySelectorAll('[role="tab"]')].find(item => item.id === target);
      (tab ?? searchTrigger(root, search) ?? emptyFallback).focus();
      tab?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    };
    root.addEventListener('keydown', binding.handler);
    root.addEventListener('click', binding.clickHandler);
    bindings.set(root, binding);
  }
  const dialog = root.querySelector('[role="dialog"]');
  if (dialog) hideSearch(root);
  const tabCount = root.querySelectorAll('[role="tab"]').length;
  if (dialog && dialog !== binding.dialog) {
    const trigger = document.activeElement;
    binding.trigger = root.contains(trigger) ? trigger : null;
    binding.triggerPanel = binding.trigger?.closest('[role="tabpanel"]')?.id ?? null;
    binding.triggerOwner = ownerScope;
  }
  if (dialog && (dialog !== binding.dialog || !dialog.contains(document.activeElement) || document.activeElement.disabled)) {
    (dialog.querySelector('button:not(:disabled)') ?? dialog.querySelector('[role="status"]'))?.focus();
  }
  // Cancel/Escape returns to the original Close control. Destruction may remove
  // or reuse its section; verify owner and editor identity before restoring it.
  if (manageCloseRecovery && !dialog && (binding.dialog || tabCount < binding.tabCount)) {
    const triggerSurvives = binding.dialog && binding.trigger?.isConnected && root.contains(binding.trigger)
      && binding.triggerOwner === ownerScope && binding.triggerPanel !== null
      && binding.trigger.closest('[role="tabpanel"]')?.id === binding.triggerPanel;
    (triggerSurvives ? binding.trigger : root.querySelector('[role="tab"][aria-selected="true"]') ?? emptyFallback).focus();
    binding.trigger = null;
  }
  binding.dialog = dialog;
  binding.tabCount = tabCount;
  root.querySelector('[role="tab"][aria-selected="true"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}
export function unbind(root) {
  const binding = bindings.get(root);
  if (binding) {
    hideSearch(root);
    root.removeEventListener('keydown', binding.handler);
    root.removeEventListener('click', binding.clickHandler);
  }
  bindings.delete(root);
}
