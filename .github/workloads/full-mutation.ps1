dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
node tooling/stryker.mjs full
exit $LASTEXITCODE
