$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot `
    "..\tests\IndustrialVisionHost.Tests\IndustrialVisionHost.Tests.csproj"

dotnet test $project `
    -c Release `
    --filter "Category=Stability" `
    --logger "console;verbosity=detailed"

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}
