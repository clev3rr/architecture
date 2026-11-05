param([switch]$Docker)
Set-Location -LiteralPath $PSScriptRoot
if ($Docker) {
    docker build --target tests -t lab6-tests .
    exit $LASTEXITCODE
}
& "$PSScriptRoot/scripts/dotnet.ps1" test Lab6.slnx -c Release --logger "trx;LogFileName=tests.trx" --results-directory TestResults
