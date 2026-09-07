let nextId = 1
const observations = new Map()

export function observe(query, callback) {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    throw new Error('media-query-unavailable')
  }
  const media = window.matchMedia(query)
  const id = nextId++
  const handler = event => callback.invokeMethodAsync('OnChanged', event.matches)
  media.addEventListener('change', handler)
  observations.set(id, { media, handler })
  return { id, matches: media.matches }
}

export function dispose(id) {
  const observation = observations.get(id)
  if (!observation) return
  observation.media.removeEventListener('change', observation.handler)
  observations.delete(id)
}
