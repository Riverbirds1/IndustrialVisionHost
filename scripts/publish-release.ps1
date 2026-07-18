param(
    [string]$Version = "1.0.0",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')
{
    throw "Invalid version: $Version. Examples: 1.0.0 or 1.1.0-beta.1."
}

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $workspaceRoot "IndustrialVisionHost.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $workspaceRoot "artifacts\release"
}
elseif (-not [IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot = Join-Path $workspaceRoot $OutputRoot
}

$packageName = "IndustrialVisionHost-v$Version-win-x64"
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $OutputRoot $packageName))

if (Test-Path -LiteralPath $publishDirectory)
{
    $resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
    if (-not $publishDirectory.StartsWith(
            $resolvedOutputRoot + '\',
            [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean a path outside the output root: $publishDirectory"
    }

    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet publish $project `
    -c Release `
    -p:PublishProfile=WinX64SelfContained `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:PublishDir="$publishDirectory\"

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$requiredFiles = @(
    "IndustrialVisionHost.exe",
    "IndustrialVisionHost.dll",
    "OpenCvSharp.dll",
    "OpenCvSharpExtern.dll",
    "Microsoft.Data.Sqlite.dll",
    "e_sqlite3.dll"
)

$missingFiles = $requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $publishDirectory $_))
}

if ($missingFiles.Count -gt 0)
{
    throw "Required publish files are missing: $($missingFiles -join ', ')"
}

$versionText = @"
Industrial Vision Host
Version: $Version
Platform: Windows x64
Deployment: .NET 6 self-contained folder
Executable: IndustrialVisionHost.exe
Data directory: %LOCALAPPDATA%\IndustrialVisionHost
"@
Set-Content -LiteralPath (Join-Path $publishDirectory "VERSION.txt") `
    -Value $versionText `
    -Encoding utf8

$checksums = Get-ChildItem -LiteralPath $publishDirectory -File |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash  $($_.Name)"
    }
Set-Content -LiteralPath (Join-Path $publishDirectory "SHA256SUMS.txt") `
    -Value $checksums `
    -Encoding ascii

$totalBytes = (Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    Measure-Object -Property Length -Sum).Sum
$totalMegabytes = [Math]::Round($totalBytes / 1MB, 2)

Write-Output "Publish completed: $publishDirectory"
Write-Output "Version: $Version"
Write-Output "File count: $((Get-ChildItem -LiteralPath $publishDirectory -File -Recurse).Count)"
Write-Output "Directory size: $totalMegabytes MB"
