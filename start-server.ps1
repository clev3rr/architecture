Set-Location -LiteralPath $PSScriptRoot
& "$PSScriptRoot/scripts/dotnet.ps1" run --project src/Lab6.Server --urls http://localhost:5080
