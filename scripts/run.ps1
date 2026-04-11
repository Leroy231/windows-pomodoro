& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Get-ChildItem "$PSScriptRoot\..\bin\Release\net*\win-x64\publish\WindowsPomodoro.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($exe) {
    & $exe.FullName
} else {
    Write-Error "WindowsPomodoro.exe not found."
}
