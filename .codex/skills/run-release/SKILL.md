---
name: run-release
description: Publish and launch the Windows Pomodoro Release executable from the current project checkout. Use when the user asks to build and run release, publish and run, run-release, launch the release exe, or run the published WindowsPomodoro.exe.
---

# Run Release

Use the bundled script for the deterministic release workflow. It stops any currently running published executable, publishes Release, verifies the published `.exe`, and launches it visibly.

From the Windows Pomodoro repository root, run:

```powershell
& .\.codex\skills\run-release\scripts\run-release.ps1
```

In the final response, summarize the script output: `StoppedProcessCount`, `PublishSucceeded`, `LaunchedProcessId`, and `ExecutablePath`.

Use the repo root as the shell working directory. If sandboxing blocks either command, rerun it with escalation and a concise approval question.
