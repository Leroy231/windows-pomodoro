Push-Location "$PSScriptRoot\.."
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
Pop-Location
