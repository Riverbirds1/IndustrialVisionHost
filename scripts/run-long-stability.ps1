param(
    [ValidateRange(1, 10080)]
    [int]$DurationMinutes = 10,

    [ValidateRange(1, 3600)]
    [int]$SampleSeconds = 5,

    [ValidateRange(0, 1000000)]
    [int]$FaultEveryCycles = 500,

    [ValidateRange(0, 60000)]
    [int]$CycleDelayMilliseconds = 20,

    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot `
    "..\tools\IndustrialVisionHost.StabilityRunner\IndustrialVisionHost.StabilityRunner.csproj"

$arguments = @(
    "run",
    "--project", $project,
    "-c", "Release",
    "--",
    "--duration-seconds", ($DurationMinutes * 60),
    "--sample-seconds", $SampleSeconds,
    "--cycle-delay-ms", $CycleDelayMilliseconds,
    "--fault-every-cycles", $FaultEveryCycles
)

if (-not [string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $arguments += @("--output", $OutputDirectory)
}

& dotnet @arguments
exit $LASTEXITCODE
