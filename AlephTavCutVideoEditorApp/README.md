# AlephTavCut Video Editor ✅

A simple Windows desktop app (WPF) to load recorded videos and remove parts by specifying timestamp ranges to cut out. Designed to work offline — ship `ffmpeg.exe` in the app folder or point to a local ffmpeg binary.

## Features
- Load a video and preview it.
- Enter timestamp ranges (Start / End) to remove any part, including start and end of file.
- Add multiple cuts; they are merged and applied in order.
- Exports an edited video using FFmpeg (cut & concat method).

## Build & Run (Windows)
Prerequisites:
- .NET 7 SDK (or later) installed on your machine
- `ffmpeg.exe` available (either put it in the same folder as the built exe or set the path in the UI)

Steps:
1. Open a terminal in this folder and run:
   dotnet build
2. To run from the build output (debug):
   dotnet run --project AlephTavCutVideoEditorApp

Packaging for offline use (single EXE):
- Publish a single-file self-contained app (example for win-x64):
  dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true --self-contained true -o publish
- Copy `ffmpeg.exe` to the `publish` folder so the exe can run without internet.

## Usage
1. Start the app.
2. Click "Load Video" and pick your file.
3. Optionally set `ffmpeg.exe` path (defaults to `ffmpeg.exe` — i.e., use PATH or copy ffmpeg to the app folder).
4. Enter Start and End timestamps for a segment to remove (formats: `hh:mm:ss` or seconds like `15.5`) and click `Add`.
5. Repeat to add more cuts. Use `Remove` and `Clear All` as needed.
6. Click `Export Edited Video` and pick an output filename. The app will create temporary parts using ffmpeg and then concat them into the final video.

## Notes & Limitations
- The current implementation uses `-ss`/`-t` plus copying with `-c copy` for speed; for exact frame-accurate cutting without re-encoding you may need ffmpeg re-encode options or keyframe-aware handling.
- For very large numbers of parts or very short segments, the concat approach will still work but may require re-encoding in some cases.

## License
MIT — adapt as you like.

---
AlephTavCut Video Editor — simple, offline-ready trimming by timestamp. 🔧