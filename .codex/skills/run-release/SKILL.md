---
name: run-release
description: Publish and launch the Windows Pomodoro Release executable from the current project checkout. Use when the user asks to build and run release, publish and run, run-release, launch the release exe, or run the published WindowsPomodoro.exe.
---

# Run Release

Use this workflow from the Windows Pomodoro repository root, wherever the project is checked out.

1. Stop any currently running instance of the published executable before publishing, so the existing `.exe` does not lock the output path:

```powershell
$exePath = Join-Path (Get-Location) 'bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\WindowsPomodoro.exe'
$exeName = [System.IO.Path]::GetFileNameWithoutExtension($exePath)
$running = Get-Process -Name $exeName -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath }
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 5
}
```

2. Publish the Release executable from the repo root:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

3. Launch the published executable visibly:

```powershell
$exePath = Join-Path (Get-Location) 'bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\WindowsPomodoro.exe'
Start-Process -FilePath $exePath
```

4. In the final response, state whether any existing process was stopped, whether publish succeeded, and whether the launch command completed without error.

Use the repo root as the shell working directory. If sandboxing blocks either command, rerun it with escalation and a concise approval question.
