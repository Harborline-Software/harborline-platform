let nextId=1
const outside=new Map()
export function observeOutside(root,callback){const id=nextId++;const handler=event=>{if(!root.contains(event.target))callback.invokeMethodAsync('OnOutsidePointer')};document.addEventListener('pointerdown',handler,true);outside.set(id,handler);return id}
export function disposeOutside(id){const handler=outside.get(id);if(handler)document.removeEventListener('pointerdown',handler,true);outside.delete(id)}
export function focusOption(root,index){root.querySelectorAll('[role=option]')[index]?.focus()}
export function focus(element){element?.focus()}
