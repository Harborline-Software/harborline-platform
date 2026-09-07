let trapped=null
function focusables(root){return [...root.querySelectorAll('button:not([disabled]),a[href],input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])')]}
export function focusAndTrap(drawer){release();const handler=event=>{if(event.key!=='Tab')return;const nodes=focusables(drawer);if(nodes.length===0)return;const first=nodes[0],last=nodes[nodes.length-1];if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus()}else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus()}};drawer.addEventListener('keydown',handler);trapped={drawer,handler};const nodes=focusables(drawer);(nodes.find(node=>!node.hasAttribute('data-hl-drawer-close'))??nodes[0])?.focus()}
export function releaseAndFocus(trigger){release();trigger?.focus()}
function release(){if(!trapped)return;trapped.drawer.removeEventListener('keydown',trapped.handler);trapped=null}
