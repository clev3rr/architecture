Set-Location -LiteralPath $PSScriptRoot
& "$PSScriptRoot/scripts/dotnet.ps1" run --project src/Lab6.Client -- http://localhost:5080
