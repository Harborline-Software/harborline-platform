import * as React from 'react'

function acquireMediaQuery(query: string): MediaQueryList | null {
  if (typeof window === 'undefined') return null
  if (typeof window.matchMedia !== 'function') throw new Error('media-query-unavailable')
  return window.matchMedia(query)
}

export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = React.useState(() => acquireMediaQuery(query)?.matches ?? false)

  React.useEffect(() => {
    const mediaQuery = acquireMediaQuery(query)
    if (!mediaQuery) return

    const handleChange = (event: MediaQueryListEvent) => setMatches(event.matches)

    // Re-read after commit so a host change between render and effect cannot be
    // lost before the listener becomes active.
    setMatches(mediaQuery.matches)
    mediaQuery.addEventListener('change', handleChange)
    return () => mediaQuery.removeEventListener('change', handleChange)
  }, [query])

  return matches
}

export interface MediaQueryProps {
  query: string
  children: (matches: boolean) => React.ReactNode
}

export function MediaQuery({ query, children }: MediaQueryProps) {
  const matches = useMediaQuery(query)
  return <>{children(matches)}</>
}
