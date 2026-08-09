# Using uictl as an agent

This tool exists so an agent (Claude Code or similar) can drive a GUI app
during development without a human moving the mouse. This file is the
practical playbook — see `README.md` for setup and `ENGINEERING.md` for
implementation details. It mirrors the macOS
[`uictl-mcp`](https://github.com/byronjones-elsevier/uictl-mcp) playbook
verb-for-verb; see `MCP_INTERFACE.md` for the exact cross-platform contract.

## The core loop

1. **Find the window.**
   ```powershell
   uictl windows --app <AppName>
   ```
   If the app isn't running yet, launch it yourself (`Start-Process
   AppName`, or however your workflow starts it), then retry — there's no
   "launch" command in uictl on purpose; that's app-specific and you already
   have a shell.

2. **Bring it to front.**
   ```powershell
   uictl activate --app <AppName>
   ```
   Do this before screenshotting or clicking — otherwise you may be looking
   at (or clicking into) a window that's behind something else.

3. **Look at it — with element numbers, not a blank screenshot.**
   ```powershell
   uictl screenshot --app <AppName> --annotate --out C:\temp\shot.png
   ```
   Read the returned JSON `elements` legend (each has a `number`, `id`,
   `role`, `title`, `frame`) alongside the image. This is deliberately *not*
   just a plain screenshot — a vision model reading raw pixels has to guess
   coordinates, which is where most GUI-automation flakiness comes from.
   With the overlay, you can say "click number 7" and then act on that
   entry's `id`.

4. **Act using the element id, not raw coordinates.**
   ```powershell
   uictl click --element <id>
   uictl type --element <id> "some text"
   ```
   Prefer `--element` over `--at x,y` whenever you have an id: it's
   resilient to the window having moved or resized since you looked at it,
   and `type --element` tries to set the field's value directly (UI
   Automation `ValuePattern`) before falling back to synthesized keystrokes
   (`SendInput`) — faster, and doesn't fight with autocomplete the way
   keystroke-by-keystroke typing can.

5. **Confirm the result, don't assume it.**
   ```powershell
   uictl elements --app <AppName> --title "expected label"
   ```
   or `wait-for` if the UI updates asynchronously:
   ```powershell
   uictl wait-for --app <AppName> --title "Done" --timeout 10
   ```
   or re-screenshot and OCR/look at it again. Don't chain five actions
   blind — check after anything that might fail silently (a disabled
   button, a modal that didn't open, focus that landed somewhere else).

## When accessibility elements aren't enough

Some UIs (canvas-drawn, game engines, custom-rendered text — Electron/Qt/Unity
apps that don't implement UI Automation providers properly) don't expose
useful nodes via `elements`. Fall back to:

- `uictl ocr --app <AppName>` — reads on-screen text (`Windows.Media.Ocr`)
  and returns each block's bounding box in the same global coordinate space
  as `elements` frames, so you can compute a click point from OCR'd text you
  can't find in the UI Automation tree.
- `uictl pixel --at x,y` — cheap state checks (is this toggle's indicator
  lit? did a progress bar finish?) without a full screenshot+vision-model
  round trip.

## Gotchas specific to this tool (Windows)

- **Element ids expire.** Every `elements`/`screenshot --annotate` call
  re-walks the tree and re-numbers it. An id from three steps ago may now
  point at nothing, or at a different element if the UI changed shape.
  Re-list before acting if any time (or any other action) has passed.
- **`click --element` uses the element's *center point* via `SendInput`,
  not UI Automation's `InvokePattern`.** This is intentional, mirroring the
  macOS tool's stance — `InvokePattern` isn't implemented by many
  custom-drawn controls, so this tool always does a real synthesized mouse
  click at the element's on-screen center. If clicks aren't landing, sanity
  check `uictl windows --app X` against what's actually visible, and confirm
  the target app isn't elevated relative to `uictl` (see below).
- **Elevation mismatches silently block automation.** If `uictl` is running
  unelevated and the target app is elevated (ran via "Run as
  administrator", or itself launched something elevated), UI Automation
  calls and `SendInput` into it will fail or silently no-op due to UIPI.
  Run `uictl permissions` to check; if the target is elevated, you need to
  run `uictl` elevated too.
- **Per-monitor DPI matters.** Frames and points from this tool are in
  physical-pixel virtual-screen coordinates. If a window is on a monitor
  with a non-100% DPI setting, a value you compute by hand from "logical"
  units (e.g. from a design doc in DIPs) needs converting first — don't
  assume 1 DIP = 1 pixel. See `ENGINEERING.md` for the conversion.
- **`type` without `--element`** sends keystrokes to whatever currently has
  keyboard focus, system-wide — make sure you've clicked into the right
  field first (or passed `--element`, which handles focusing for you).
- **The daemon caches state.** If you rebuild `uictl` during development,
  run `uictl daemon stop` before your next command — otherwise you'll keep
  talking to the old binary running in the background.
- **One window per HWND, no correlation heuristic needed.** Unlike macOS
  (which has no public API mapping a `CGWindowID` to an `AXUIElement` and
  has to match by title+frame), Windows UI Automation can resolve a window's
  automation element directly from its `HWND`
  (`AutomationElement.FromHandle`). If `elements --window <id>` ever behaves
  unexpectedly, that's a bug in this tool, not an inherent platform
  ambiguity — file it as such.

## MCP mode

If your harness supports MCP tools directly, prefer that over shelling out:

```powershell
claude mcp add uictl -- C:\path\to\uictl.exe mcp
```

Then call `uictl_screenshot`, `uictl_elements`, `uictl_click`, etc. as
structured tool calls instead of parsing CLI stdout. Same daemon, same
element-id cache, same everything underneath — just a shorter round trip.
