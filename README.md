# AlephTavCut Video Editor

A lightweight offline WPF tool to cut and remove arbitrary ranges from videos using ffmpeg.

## Features

- Load and preview videos (.mp4, .mov, .mkv, .avi, .wmv)
- Add timestamp ranges to remove
- Two trimming modes: **Fast (stream-copy)** and **Precise (re-encode)**
- Progress parsing from ffmpeg stderr
- Integration test that runs when ffmpeg is available

## How to run (development)

1. Install .NET 8 SDK
2. Build: `dotnet build`
3. Run from the project folder: `dotnet run --project AlephTavCutVideoEditorApp` or double-click `AlephTavCutVideoEditorApp/bin/Debug/net8.0-windows/AlephTavCutVideoEditorApp.exe`

## How to get a runnable (self-contained) build

A self-contained Windows build is placed on your Desktop by the helper script (published by the local build):

- `C:\Users\<you>\Desktop\AlephTavCutVideoEditor_publish\AlephTavCutVideoEditorApp.exe`

To publish yourself:

```bash
dotnet publish AlephTavCutVideoEditorApp/AlephTavCutVideoEditorApp.csproj -c Release -r win-x64 --self-contained true -o "$env:USERPROFILE\Desktop\AlephTavCutVideoEditor_publish"
```

## Notes about ffmpeg

- ffmpeg is required for exports; either install ffmpeg on PATH or set its path in the UI.
- For frame-accurate trimming, enable "Precise trimming (re-encode)"; this re-encodes segments using libx264 and aac.
- To remove a cut: select an item in the list and click **Remove**, or enter `Start` and `End` and click **Remove** to target a specific range. You can also press the **Delete** key to remove the selected list item.

## Contribution

- Tests: unit tests run with `dotnet test`. Integration test requires `FFMPEG_PATH` env var or presence of ffmpeg in the workspace `tools` area.

