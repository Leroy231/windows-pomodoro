# Windows Pomodoro

A simple 30-minute Pomodoro timer for Windows. Flashes the taskbar icon and plays a beep when the timer completes.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), or install via Visual Studio:

When installing or modifying Visual Studio:

1. Open the **Visual Studio Installer**
2. Click **Modify** on your installation
3. Under the **Workloads** tab, check **.NET desktop development**
4. In the **Installation details** panel on the right, ensure **.NET 10.0 Runtime** is checked
5. Click **Modify** to apply

## Build

```
dotnet build
```

## Run

```
dotnet run
```

## Publish

To produce a single standalone `.exe` with no dependencies:

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The output will be in `bin/Release/net10.0-windows10.0.17763.0/win-x64/publish/WindowsPomodoro.exe`.
