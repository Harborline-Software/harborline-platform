let nextId=1
const observations=new Map()
export function observe(element,callback,options){
  const id=nextId++
  const horizontal=options.orientation===0
  let timer
  const send=()=>callback.invokeMethodAsync('OnMeasured',{
    rawPosition:horizontal?element.scrollLeft:element.scrollTop,
    scrollSize:horizontal?element.scrollWidth:element.scrollHeight,
    clientSize:horizontal?element.clientWidth:element.clientHeight,
    rightToLeft:horizontal&&getComputedStyle(element).direction==='rtl'
  })
  const measure=()=>{
    clearTimeout(timer)
    timer=setTimeout(send,Math.max(0,options.announceDebounceMilliseconds??300))
  }
  element.addEventListener('scroll',measure,{passive:true})
  const resize=new ResizeObserver(measure); resize.observe(element)
  // T-1135: the load baseline is sent at once, not debounced, so a scroll inside the first debounce window is
  // compared against it (and announced) rather than replacing it.
  window.addEventListener('resize',measure); send()
  observations.set(id,{element,measure,resize,get timer(){return timer}})
  return id
}
export function dispose(id){
  const entry=observations.get(id); if(!entry)return
  entry.element.removeEventListener('scroll',entry.measure)
  entry.resize.disconnect(); window.removeEventListener('resize',entry.measure)
  clearTimeout(entry.timer); observations.delete(id)
}
