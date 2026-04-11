$exe = Get-ChildItem "$PSScriptRoot\..\bin\Release\*\win-x64\publish\WindowsPomodoro.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($exe) {
    & $exe.FullName
} else {
    Write-Error "WindowsPomodoro.exe not found. Run build.ps1 first."
}
