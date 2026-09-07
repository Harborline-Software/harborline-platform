let nextId = 1
const registrations = new Map()

function attach(registration) {
  if (registration.attached || !registration.enabled) return
  document.addEventListener(registration.eventType, registration.handler)
  registration.attached = true
}

function detach(registration) {
  if (!registration.attached) return
  document.removeEventListener(registration.eventType, registration.handler)
  registration.attached = false
}

export function observe(elements, callback, enabled = true, eventType = 'mousedown') {
  if (eventType !== 'mousedown' && eventType !== 'pointerdown') throw new Error('unsupported-event-type')
  const id = nextId++
  const registration = { elements, callback, enabled, eventType, attached: false }
  registration.handler = event => {
    const inside = registration.elements.some(element => element && element.contains(event.target))
    if (inside) return
    callback.invokeMethodAsync('OnOutside', {
      eventType: event.type,
      button: event.button ?? 0,
      pointerType: event.pointerType ?? null,
      altKey: !!event.altKey,
      controlKey: !!event.ctrlKey,
      metaKey: !!event.metaKey,
      shiftKey: !!event.shiftKey,
    })
  }
  registrations.set(id, registration)
  attach(registration)
  return id
}

export function setEnabled(id, enabled) {
  const registration = registrations.get(id)
  if (!registration) return
  registration.enabled = enabled
  if (enabled) attach(registration)
  else detach(registration)
}

export function dispose(id) {
  const registration = registrations.get(id)
  if (!registration) return
  detach(registration)
  registrations.delete(id)
}
