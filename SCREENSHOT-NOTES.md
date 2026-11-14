# Phase 14 Screenshot — Challenge Notes for Research

Date: 2026-11-14
Status: build green, 42/42 regression tests pass, but the frozen capture
contains a "duplicated desktop" (screen tearing) when the main window
minimizes. Reverted to the minimize variant — it behaves best so far.

## What the feature does

Menu button → main window leaves the screen → fullscreen overlay shows a
frozen screenshot of the desktop → user drags a rectangle → release crops
the frozen bitmap → PNG saved to `Desktop\Screenshot\screenshot-*.png` →
main window restores. Escape cancels. Windows-only.

## Architecture (current)

- `IScreenCaptureService` / `ScreenCaptureService`
  (`src/ImageTextify.Platform/Imaging/ScreenCaptureService.cs`) —
  `Graphics.CopyFromScreen` + `GetSystemMetrics` P/Invoke. Produces a PNG
  stream of exact pixels. Proven correct in isolation (spike: 400x300 in,
  400x300 out).
- `ScreenshotViewModel.TakeScreenshot`
  (`src/FileHarvester.App/ViewModels/ScreenshotViewModel.cs`) — creates
  `Desktop\Screenshot`, hides/minimizes the main window via Win32
  `ShowWindow`, sleeps 300 ms, captures the full screen into a
  `System.Drawing.Bitmap`, constructs `ScreenshotOverlayWindow` with the
  frozen bitmap, wires `Completed`/`Cancelled`/`Closed`, calls `Show()`.
- `ScreenshotOverlayWindow`
  (`src/FileHarvester.App/Views/ScreenshotOverlayWindow.axaml[.cs]`) —
  borderless maximized topmost window, frozen bitmap as canvas
  background, drag rectangle, crop on release. `OnOpened` only paints the
  frame now; capture was moved to the ViewModel.

## What works (verified at runtime)

- Build: 0 warnings, 0 errors. Tests: camera 10, conversion 23, TTS 9 —
  all green.
- Menu button present and opens the overlay.
- Overlay: fullscreen, dimmed, crosshair cursor, drag rectangle follows
  the mouse, Escape closes, release closes.
- Save pipeline: PNG lands in `Desktop\Screenshot\` at exact dragged
  pixel size (verified earlier when the app window was still visible).
- `ShowWindow(SW_MINIMIZE)` on the main window HWND **does** minimize the
  app — Avalonia's `WindowState = WindowState.Minimized` alone did
  nothing at runtime (defect F-056).
- `ShowWindow(SW_RESTORE)` + restoring the saved `WindowState` brings
  the window back maximized.

## What failed — the open problem

### F-057 (fixed): async capture raced the drag

`OnOpened` was `async void` + `await`; `_frozen` was still null when the
user released the drag → `Cancelled` fired → no save. Fixed twice:
first by making `OnOpened` synchronous, then (current) by capturing in
the ViewModel before `Show()` and passing the bitmap into the window.

### F-056 (fixed): Avalonia WindowState.Minimized is a no-op here

Setting `desktop.MainWindow.WindowState = WindowState.Minimized` did not
minimize the window. `ShowWindow(hwnd, SW_MINIMIZE)` via P/Invoke works.

### THE OPEN BUG: "duplicated desktop" / screen tearing

Symptom: after clicking Screenshot, the frozen frame shows **two**
desktop views stacked — the pre-minimize frame (app window visible,
desktop contents shifted up) over the post-minimize frame. It looks like
the app duplicated itself. The saved PNG contains the same artifact.

Diagnosis: `CopyFromScreen` reads the screen while the DWM minimize
animation is still running. The capture catches a half-composited
frame — top of screen shows the old buffer, bottom shows the new one.
This is classic screen tearing during a window animation, not a
duplicate window.

Attempted fixes and results:

| Attempt | Result |
|---|---|
| `WindowState.Minimized` only | Window did not minimize at all |
| `ShowWindow(SW_MINIMIZE)` + `WindowState.Minimized` | Minimizes correctly, but tearing remains |
| + `Thread.Sleep(300)` before capture | Tearing remains (300 ms not enough, or sleep doesn't sync with DWM) |
| `ShowWindow(SW_HIDE)` instead of minimize | **Worse** — window did not hide; it appeared to turn transparent/flicker, and the duplicate was still present. Reverted. |
| Capture inside overlay `OnOpened` | Worked when app stayed visible; races/tears once minimize is real |

## Why this is hard in Avalonia

- Avalonia has no built-in "freeze screen / click-through overlay"
  primitive. The overlay is a normal topmost window showing a bitmap we
  captured ourselves — there is no OS-level "screen snip" surface.
- `Graphics.CopyFromScreen` reads the **composited** screen at call
  time. Any window animation in flight (minimize, restore, DWM
  transitions) can be caught mid-frame. There is no "wait until the
  desktop is stable" API in GDI.
- `SW_HIDE` behaved unexpectedly (window looked transparent rather than
  gone) — possibly because Avalonia renders via its own composition
  path and the HWND hide interacts badly with `ExtendClientAreaToDecorationsHint`
  / layered presentation. Needs investigation.
- Windows' own Snipping Tool avoids this by using the **Desktop
  Duplication API** / DWM thumbnails and by capturing *after* the UI
  transition completes, not by racing a GDI screen read.

## Research questions to answer

1. **Is there a reliable "desktop settled" signal?** e.g. wait for
   `IsIconic(hwnd)` + a DWM frame-present event, or poll until two
   consecutive `CopyFromScreen` frames are identical.
2. **Desktop Duplication API** (`IDXGIOutputDuplication`) — the modern
   path used by Snipping Tool/OBS. Acquire the desktop image *after*
   minimize completes; immune to GDI tearing. Requires a small native
   interop or a library (e.g. `Vortice.Windows`, `SharpDX`).
3. **`PrintWindow` / `PW_RENDERFULLCONTENT`** on the desktop or on
   individual windows — captures a window's content without the
   animation, but does not capture the whole desktop.
4. **Magnification API** (`MagSetWindowFilter`/`MagImageScalingCallback`)
   — can grab screen regions; used by some snipping tools.
5. **DWM thumbnail / `DwmRegisterThumbnail`** — live preview of a
   window; not obviously useful for full-desktop freeze.
6. **Does Avalonia expose a hide/minimize-complete callback?**
   `Window.StateChanged` event fires on `WindowStateChanged` — maybe
   waiting for the *event* (not a fixed sleep) plus one more frame is
   enough.
7. **Alternative UX**: skip minimizing entirely — capture first, then
   show overlay on top of the still-visible app, and exclude the app
   window from the frozen image by capturing *before* the overlay but
   *after* hiding only the overlay region? (Probably not viable — the
   app window would be in the shot.)
8. **Alternative UX**: capture the screen, then *subtract* the app
   window region? Fragile.
9. **Why did `SW_HIDE` look transparent?** Check whether Avalonia's
   `Window.Hide()` (managed API) behaves differently from raw
   `ShowWindow(SW_HIDE)` — Avalonia may handle visibility through its
   own layer and the raw Win32 call desynced it.
10. **Timing**: is 300 ms actually enough on this machine? Try 600–1000
    ms to confirm the tearing is timing vs. a structural GDI issue.
    If a longer sleep fixes it, the fix is "wait for minimize
    animation" (there may be `SPI_GETMENUANIMATION`/`ANIMATION` metrics
    or `DwmGetWindowAttribute(DWMWA_*)` to compute the real duration).

## Files involved

- `src/FileHarvester.App/ViewModels/ScreenshotViewModel.cs` — flow,
  minimize/restore, save.
- `src/FileHarvester.App/Views/ScreenshotOverlayWindow.axaml` + `.cs` —
  overlay, drag, crop.
- `src/ImageTextify.Core/Abstractions/IScreenCaptureService.cs` —
  contract.
- `src/ImageTextify.Platform/Imaging/ScreenCaptureService.cs` —
  `CopyFromScreen` implementation.
- `src/ImageTextify.Platform/Composition/ImageTextifyServiceCollectionExtensions.cs`
  — Windows-guarded DI registration (do not touch the TTS block above it).

## Hard rules still in force

- No git commands; tree stays uncommitted.
- No full-solution test per edit; `dotnet build src\FileHarvester.App\FileHarvester.App.csproj --nologo`.
- Do not touch the Edge TTS files or the TTS DI block.
- No `_spike_*` folders in repo root; throwaway projects go to `%TEMP%`.
