export function sameOwner(owner, presentationOwner) {
  return !!owner && !!presentationOwner && ['person', 'tenant', 'workspace'].every(axis =>
    typeof owner[axis] === 'string' && owner[axis].length > 0 && owner[axis] === presentationOwner[axis])
}
export function tabFocusIndex(key, index, count) {
  if (!count) return -1
  switch (key) {
    case 'Home': return 0
    case 'End': return count - 1
    case 'ArrowRight': return (index + 1) % count
    case 'ArrowLeft': return (index + count - 1) % count
    default: return -1
  }
}
