let nextId=1
const outside=new Map()
export function observeOutside(root,callback){const id=nextId++;const handler=event=>{if(!root.contains(event.target))callback.invokeMethodAsync('OnOutsidePointer')};document.addEventListener('pointerdown',handler,true);outside.set(id,handler);return id}
export function disposeOutside(id){const handler=outside.get(id);if(handler)document.removeEventListener('pointerdown',handler,true);outside.delete(id)}
export function focusFirst(panel,custom){const selector=custom?'button:not([disabled]),a[href],input,select,textarea,[tabindex]:not([tabindex="-1"])':'[role="menuitem"]:not([disabled]):not([aria-disabled="true"])';panel.querySelector(selector)?.focus()}
// By identity: nth-of-type counted same-tag siblings, so mixed links, buttons and separators moved
// focus to the wrong entry. A null identity is the appended Sign out entry.
export function focusItem(panel,id){const target=id==null?panel.querySelector('.hl-user-menu__sign-out'):[...panel.querySelectorAll('[data-hl-item]')].find(element=>element.dataset.hlItem===id);target?.focus()}
// Escape closes the surface unless a control inside it already handled the key.
export function observeEscape(panel,callback){panel.addEventListener('keydown',event=>{if(event.key==='Escape'&&!event.defaultPrevented)callback.invokeMethodAsync('OnEscapeKey')})}
export function focus(element){element?.focus()}
