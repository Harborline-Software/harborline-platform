const connections = new WeakMap()

const scrollHost = root => root?.querySelector('.hl-scheduler__scroll') ?? null
const nowLine = host => host.querySelector('[data-now-indicator]')

// Mirrors the React lane's onScroll/measure: the Now action exists only while a now indicator is
// rendered and sits outside the scroll viewport.
export function connect(root, callback) {
  const host = scrollHost(root)
  if (!host) return
  const measure = () => {
    const line = nowLine(host)
    const offscreen = !!line && (line.offsetTop < host.scrollTop || line.offsetTop > host.scrollTop + host.clientHeight)
    callback.invokeMethodAsync('OnNowVisibilityAsync', offscreen)
  }
  const resize = new ResizeObserver(measure)
  host.addEventListener('scroll', measure, { passive: true })
  window.addEventListener('resize', measure)
  resize.observe(host)
  measure()
  connections.set(root, {
    measure,
    stop: () => {
      host.removeEventListener('scroll', measure)
      window.removeEventListener('resize', measure)
      resize.disconnect()
    },
  })
}

// The view and the date change the rendered grid without scrolling or resizing the host, so the
// lane asks for a fresh measurement there, exactly as the React lane's two effects do.
export function measure(root) {
  connections.get(root)?.measure()
}

// The React lane's date/view layout effect does not measure: in day and week it moves the scroll
// host onto the now line (or onto the work-day start when there is none) and clears the flag, so a
// date change back onto today never leaves a Now action behind. This mirrors it.
export function anchor(root, hourRow) {
  const host = scrollHost(root)
  if (!host) return
  const line = nowLine(host)
  const target = line ?? host.querySelector(`[data-hour-row="${hourRow}"]`)
  if (!target) return
  host.scrollTop = Math.max(0, target.offsetTop - (line ? host.clientHeight / 3 : 0))
}

export function scrollToNow(root) {
  const host = scrollHost(root)
  const line = host && nowLine(host)
  if (!host || !line) return
  const top = Math.max(0, line.offsetTop - host.clientHeight / 3)
  const reduced = !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
  if (typeof host.scrollTo === 'function') host.scrollTo({ top, behavior: reduced ? 'auto' : 'smooth' })
  else host.scrollTop = top
}

export function disconnect(root) {
  const connection = connections.get(root)
  if (!connection) return
  connection.stop()
  connections.delete(root)
}
