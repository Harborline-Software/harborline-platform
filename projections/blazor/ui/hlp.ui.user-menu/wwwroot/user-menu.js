let nextId=1
const outside=new Map()
export function observeOutside(root,callback){const id=nextId++;const handler=event=>{if(!root.contains(event.target))callback.invokeMethodAsync('OnOutsidePointer')};document.addEventListener('pointerdown',handler,true);outside.set(id,handler);return id}
export function disposeOutside(id){const handler=outside.get(id);if(handler)document.removeEventListener('pointerdown',handler,true);outside.delete(id)}
export function focusFirst(panel,custom){const selector=custom?'button:not([disabled]),a[href],input,select,textarea,[tabindex]:not([tabindex="-1"])':'[role="menuitem"]:not([disabled]):not([aria-disabled="true"])';panel.querySelector(selector)?.focus()}
// By identity: nth-of-type counted same-tag siblings, so mixed links, buttons and separators moved
// focus to the wrong entry. A null identity is the appended Sign out entry.
export function focusItem(panel,id){const target=id==null?panel.querySelector('.hl-user-menu__sign-out'):[...panel.querySelectorAll('[data-hl-item]')].find(element=>element.dataset.hlItem===id);target?.focus()}
// Escape from inside the surface closes it unless a control there already handled the key. The
// listener is on the document, after Blazor's own, so a Blazor @onkeydown:preventDefault inside
// custom content is respected too. In the menu pattern, navigation keys do not scroll the page.
const keys=new Map()
// Tab out of the menu: focus moves to the trigger before the browser's default Tab, so Tab continues
// from there (the closing render may already have removed the focused item) and Shift+Tab stops on it.
// The closing render is synchronous in WebAssembly but arrives later over a Server circuit, so for a
// forward Tab the panel is inert until the default action has run: Tab passes the still-rendered items
// and lands after the menu. It is restored on the next task, in case the host keeps the menu open.
export function observeKeys(panel,menu,callback,trigger){const id=nextId++;const handler=event=>{if(!panel.contains(event.target)&&!(menu&&event.key==='Tab'&&event.target===document.body))return;if(menu&&event.key==='Tab'){if(!event.shiftKey){panel.inert=true;setTimeout(()=>{panel.inert=false})}trigger?.focus();if(event.shiftKey)event.preventDefault();return}if(menu&&['ArrowUp','ArrowDown','Home','End'].includes(event.key))event.preventDefault();if(event.key==='Escape'&&!event.defaultPrevented)callback.invokeMethodAsync('OnEscapeKey')};document.addEventListener('keydown',handler);keys.set(id,handler);return id}
export function disposeKeys(id){const handler=keys.get(id);if(handler)document.removeEventListener('keydown',handler);keys.delete(id)}
export function focus(element){element?.focus()}
