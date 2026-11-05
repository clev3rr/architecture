param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
$root = Split-Path -Parent $PSScriptRoot
$local = Join-Path $root '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $local) { & $local @Arguments; exit $LASTEXITCODE }
& dotnet @Arguments
exit $LASTEXITCODE
