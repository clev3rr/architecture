Set-Location -LiteralPath $PSScriptRoot
& "$PSScriptRoot/scripts/dotnet.ps1" test Lab6.slnx -c Release --logger "trx;LogFileName=tests.trx" --results-directory TestResults
