import { resolve } from 'node:path'
const aggregateRoot=resolve(import.meta.dirname,'../hlp.ui.button')
export default {resolve:{alias:{'@testing-library/jest-dom':resolve(aggregateRoot,'node_modules/@testing-library/jest-dom')}},test:{globals:true,environment:'jsdom',setupFiles:['./src/test-setup.ts'],css:false}}
