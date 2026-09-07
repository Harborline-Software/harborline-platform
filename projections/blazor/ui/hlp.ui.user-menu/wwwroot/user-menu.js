let nextId=1
const outside=new Map()
export function observeOutside(root,callback){const id=nextId++;const handler=event=>{if(!root.contains(event.target))callback.invokeMethodAsync('OnOutsidePointer')};document.addEventListener('pointerdown',handler,true);outside.set(id,handler);return id}
export function disposeOutside(id){const handler=outside.get(id);if(handler)document.removeEventListener('pointerdown',handler,true);outside.delete(id)}
export function focusFirst(panel,custom){const selector=custom?'button:not([disabled]),a[href],input,select,textarea,[tabindex]:not([tabindex="-1"])':'[role="menuitem"]:not([disabled]):not([aria-disabled="true"])';panel.querySelector(selector)?.focus()}
export function focusItem(panel,index){panel.querySelector(`[data-hl-item]:nth-of-type(${index+1})`)?.focus()}
export function focus(element){element?.focus()}
