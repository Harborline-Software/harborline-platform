const bindings = new WeakMap();
export function bind(root, ownerScope = null) {
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
      } else if (!dialog && event.target.matches('[role="tab"]') && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
        const tabs = [...root.querySelectorAll('[role="tab"]')];
        const index = tabs.indexOf(event.target);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowLeft' ? -1 : 1) + tabs.length) % tabs.length;
        event.preventDefault(); tabs.forEach((tab, i) => tab.tabIndex = i === next ? 0 : -1); tabs[next]?.focus(); tabs[next]?.click();
      }
    } };
    root.addEventListener('keydown', binding.handler); bindings.set(root, binding);
  }
  const dialog = root.querySelector('[role="dialog"]');
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
  if (!dialog && (binding.dialog || tabCount < binding.tabCount)) {
    const triggerSurvives = binding.dialog && binding.trigger?.isConnected && root.contains(binding.trigger)
      && binding.triggerOwner === ownerScope && binding.triggerPanel !== null
      && binding.trigger.closest('[role="tabpanel"]')?.id === binding.triggerPanel;
    (triggerSurvives ? binding.trigger : root.querySelector('[role="tab"][aria-selected="true"]') ?? root).focus();
    binding.trigger = null;
  }
  binding.dialog = dialog;
  binding.tabCount = tabCount;
  root.querySelector('[role="tab"][aria-selected="true"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}
export function unbind(root) { const binding = bindings.get(root); if (binding) root.removeEventListener('keydown', binding.handler); bindings.delete(root); }
