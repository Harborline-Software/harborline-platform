#!/usr/bin/env node

import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {resolveCommand} from '../../../tooling/resolve-command.mjs'
import {resolvePinnedDotnet} from '../../../tooling/resolve-dotnet.mjs'

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..')
const moduleId='hlp.ui.context-menu'
const specification=JSON.parse(readFileSync(path.join(root,'specs/modules/ui/hlp.ui.context-menu/interface.yaml'),'utf8'))
const fixtures=JSON.parse(readFileSync(path.join(root,'conformance/hlp.ui.context-menu/fixtures.yaml'),'utf8'))
const caseIds=specification.cases.map(row=>row.id)
if(JSON.stringify(caseIds)!==JSON.stringify(fixtures.cases.map(row=>row.id))) throw new Error('Context Menu interface and fixture case order differ')
const reactRoot=path.join(root,'projections/react/ui/hlp.ui.button')
const dotnet=resolvePinnedDotnet(root)

if(process.env.HARBORLINE_SHARED_SKIP_BUILD!=='1'){
  const build=spawnSync(dotnet.executable,['build','projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj','--configuration','Release','-v:minimal'],{cwd:root,encoding:'utf8',env:{...process.env,CI:'1',NO_COLOR:'1'}})
  if(build.status!==0) throw new Error(`Context Menu Blazor setup failed\n${build.stdout}\n${build.stderr}`)
}

const results=[]
for(const fixture of fixtures.cases){
  for(const projection of ['react','blazor']){
    const command=projection==='react'
      ? ['npm','run','test:context-menu','--','-t',fixture.id]
      : [dotnet.executable,'test','projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj','--configuration','Release','--no-build','--no-restore','--filter',`ConformanceCase=${fixture.id}`,'-v:minimal']
    const resolved=resolveCommand(command[0],command.slice(1))
    const execution=spawnSync(resolved.executable,resolved.args,{cwd:projection==='react'?reactRoot:root,encoding:'utf8',maxBuffer:64*1024*1024,env:{...process.env,CI:'1',NO_COLOR:'1',HARBORLINE_CONFORMANCE_FIXTURE:JSON.stringify(fixture)}})
    const output=`${execution.stdout??''}\n${execution.stderr??''}`
    const testCount=projection==='react'?Number(/Tests\s+(\d+)\s+passed/.exec(output)?.[1]??0):Number(/Passed:\s+(\d+)/.exec(output)?.[1]??0)
    results.push({moduleId,caseId:fixture.id,projection,testCount,exitCode:execution.status,passed:execution.status===0&&testCount===1,failureOutput:execution.status===0?undefined:output.split('\n').slice(-50).join('\n')})
  }
}
const passed=results.length===caseIds.length*2&&results.every(row=>row.passed)
process.stdout.write(`${JSON.stringify({schemaVersion:1,moduleId,status:passed?'PASS':'FAIL',counts:{interfaceCases:caseIds.length,projections:2,expectedResults:caseIds.length*2,executedResults:results.length,passedResults:results.filter(row=>row.passed).length},caseIds,results},null,2)}\n`)
process.exitCode=passed?0:1
