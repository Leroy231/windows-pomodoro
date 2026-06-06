param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$TargetFramework = 'net10.0-windows10.0.17763.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$exePath = Join-Path $RepoRoot "bin\$Configuration\$TargetFramework\$Runtime\publish\WindowsPomodoro.exe"
$exeName = [System.IO.Path]::GetFileNameWithoutExtension($exePath)
$stoppedCount = 0

$running = Get-Process -Name $exeName -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -eq $exePath
    }
    catch {
        $false
    }
}

if ($running) {
    $stoppedCount = @($running).Count
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 5
}

Push-Location $RepoRoot
try {
    & dotnet publish -c $Configuration -r $Runtime --self-contained -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Published executable was not found at $exePath."
}

$process = Start-Process -FilePath $exePath -PassThru

Write-Output "RepoRoot: $RepoRoot"
Write-Output "StoppedProcessCount: $stoppedCount"
Write-Output "PublishSucceeded: True"
Write-Output "LaunchedProcessId: $($process.Id)"
Write-Output "ExecutablePath: $exePath"
