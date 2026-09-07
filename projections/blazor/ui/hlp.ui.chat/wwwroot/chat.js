export function focusMessage(root, index) { root?.querySelector(`[data-hl-index="${index}"]`)?.focus(); }
export function scrollLogToEnd(log) { if (log) log.scrollTop = log.scrollHeight; }
export function isNearEnd(log, threshold = 48) { return !log || log.scrollHeight - log.scrollTop - log.clientHeight <= threshold; }
