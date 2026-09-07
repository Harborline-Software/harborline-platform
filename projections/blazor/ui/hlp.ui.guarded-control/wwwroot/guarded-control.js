export function observe(root, dotnet, blurGraceMs) {
  let blurTimer = 0
  const hidden = () => {
    if (document.hidden) dotnet.invokeMethodAsync('RecoverFromHostAsync', 'document-hidden')
    else dotnet.invokeMethodAsync('RestoreDeferredFocusAsync')
  }
  const blur = () => { blurTimer = window.setTimeout(() => dotnet.invokeMethodAsync('RecoverFromHostAsync', 'window-blur'), blurGraceMs) }
  const focusWindow = () => { window.clearTimeout(blurTimer); dotnet.invokeMethodAsync('RestoreDeferredFocusAsync') }
  const navigation = () => dotnet.invokeMethodAsync('RecoverFromHostAsync', 'navigation')
  document.addEventListener('visibilitychange', hidden)
  window.addEventListener('blur', blur)
  window.addEventListener('focus', focusWindow)
  window.addEventListener('popstate', navigation)
  window.addEventListener('pagehide', navigation)
  return {
    dispose() {
      window.clearTimeout(blurTimer)
      document.removeEventListener('visibilitychange', hidden)
      window.removeEventListener('blur', blur)
      window.removeEventListener('focus', focusWindow)
      window.removeEventListener('popstate', navigation)
      window.removeEventListener('pagehide', navigation)
    }
  }
}

export function focus(element) { element?.focus({ preventScroll: true }) }
