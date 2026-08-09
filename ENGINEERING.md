# Engineering notes

This mirrors the structure of the macOS
[`uictl-mcp`](https://github.com/byronjones-elsevier/uictl-mcp) `ENGINEERING.md`
so the two implementations stay easy to compare. Where Windows genuinely
differs from macOS (IPC transport, window↔element correlation, DPI), that's
called out explicitly — everything else should be assumed to work the same
way unless noted.

## Planned process architecture

```
CLI subcommand ──┐
                  ├──▶ DaemonClient (Named Pipe, \\.\pipe\uictl) ──▶ DaemonServer ──▶ CommandDispatcher ──▶ Core/*
MCP tool call  ───┘
```

- **CLI** (`src/UICtl.Cli`, `System.CommandLine`): parses flags, builds a
  `{"command": ..., "params": ...}` JSON dict, sends it via `DaemonClient`,
  prints the JSON response, exits 0/1 based on the response's `ok` field.
- **MCP server** (`src/UICtl.Mcp`, `uictl mcp`, official
  `ModelContextProtocol` C# SDK): same thing, one layer up — each MCP tool
  call also just calls `DaemonClient.Send`. This is why CLI and MCP usage
  share state transparently (see "element ids" below). Tool definitions here
  must match `MCP_INTERFACE.md` exactly.
- **DaemonClient/DaemonServer** (`src/UICtl.Ipc`): a length-prefixed JSON
  protocol over a **named pipe** (`\\.\pipe\uictl`), one request/response
  per connection — same protocol shape as macOS's Unix domain socket
  version, different transport (Windows has no Unix domain sockets in the
  general case pre-Windows-10-1803, and named pipes are the idiomatic local
  IPC primitive here). `DaemonClient` auto-spawns the daemon (`uictl daemon
  start --foreground`, stdio redirected to a log file under
  `%LOCALAPPDATA%\uictl\`) if the pipe isn't reachable, and waits up to 5s
  for it to come up.
- **CommandDispatcher** (`src/UICtl.Ipc`): the one switch/pattern-match
  every request goes through, regardless of front end. Add a new capability
  by adding a case here plus a CLI subcommand and/or MCP tool that calls it
  — the dispatcher is the only place that needs to know how to actually
  perform it.
- **Core** (`src/UICtl.Core`): the actual Windows API calls — Win32
  (`user32.dll`: `EnumWindows`, `SetForegroundWindow`, `SendInput`), UI
  Automation (`UIAutomationClient`, via `System.Windows.Automation` or the
  `FlaUI` wrapper), screen capture (`Windows.Graphics.Capture` or GDI
  `BitBlt`), OCR (`Windows.Media.Ocr`), clipboard (`System.Windows.Clipboard`
  or the raw Win32 clipboard API if avoiding a WPF dependency).

## Why a daemon at all

Same rationale as macOS: state that needs to outlive one CLI invocation has
to live somewhere. Concretely on Windows:

- The **element-id cache** (`elements`/`screenshot --annotate` hand out
  synthetic ids that a later `click --element <id>` resolves back to a live
  `AutomationElement`) needs a process that outlives one invocation, exactly
  like macOS's `AXUIElement` cache — even though Windows *can* re-resolve an
  element more directly than macOS in some cases (see "Window ↔
  AutomationElement correlation" below), the id scheme and its cache are
  kept for architectural parity and because UI Automation element references
  can still become stale as the tree changes.
- **Elevation/UIPI state** is a per-process concern the same way
  Accessibility/Screen-Recording grants are on macOS — keeping one long-lived
  daemon means that state (and any future caching of UI Automation
  `CacheRequest` results) is consistent regardless of which front end
  (CLI or MCP) triggered a given call.

## Coordinate spaces

Fewer distinct spaces than macOS, but one added wrinkle (per-monitor DPI)
that macOS's Retina scaling handles differently:

1. **Virtual screen space** — origin at the top-left of the *virtual screen*
   (the bounding box of all monitors, which can have negative coordinates
   for monitors positioned above/left of the primary), y increasing
   downward, in **physical pixels**. This is what `GetWindowRect`, UI
   Automation's `BoundingRectangle`, and `SendInput`'s pixel-coordinate mode
   all use once the process is marked Per-Monitor-V2 DPI-aware (required —
   see below). Every `"frame"`/`"clicked"` point in this tool's JSON output
   is in this space.
2. **Capture-local pixel space** — a captured bitmap's own `(0,0)` at the
   top-left of *that capture* (a window or a display). Converting a virtual-
   screen frame into this space is `global - capture.origin` (no additional
   scale factor needed here, unlike macOS's Retina `pointPixelScale`,
   *because* both spaces are already physical pixels — see next point).
3. **DPI awareness is a process-wide manifest setting, not automatic.** A
   .NET app must declare itself Per-Monitor-V2 DPI-aware (via
   `app.manifest` or `SetProcessDpiAwarenessContext` at startup) or the OS
   will lie to it: `GetWindowRect` and friends return values pre-scaled to
   whatever DPI the *primary* monitor has, not the monitor a given window is
   actually on. `uictl` must set Per-Monitor-V2 awareness at process start,
   before any window/monitor enumeration, so every coordinate this tool
   reports is a real physical pixel on whichever monitor it came from — this
   is the direct Windows analog of the macOS Calculator AX-vs-window-server
   frame mismatch bug class, and getting DPI awareness wrong is the most
   likely way to reintroduce that class of bug here.

There is no Vision-framework-style normalized/bottom-left-origin space to
worry about on the Windows OCR path — `Windows.Media.Ocr` already returns
`Rect`s in the same top-left-origin pixel space as everything else, once the
source bitmap's own origin offset is added back in (same "capture-local →
global" addition as above).

## Window ↔ AutomationElement correlation

**This is easier on Windows than macOS, and worth calling out as a
deliberate simplification relative to the macOS implementation.** macOS has
no public API mapping a `CGWindowID` to an `AXUIElement`, so it matches by
title+frame heuristically (see macOS `ENGINEERING.md`). Windows UI Automation
provides `AutomationElement.FromHandle(HWND)` directly — an exact, non-
heuristic resolution from the same `HWND` that `EnumWindows`/`GetWindowRect`
already gave you. `WindowResolver` on this platform should be a thin wrapper
around `FromHandle`, with no title/frame matching fallback needed. If a
future edge case forces a heuristic here too (e.g. some UWP/XAML window
shapes exposing multiple automation roots per HWND), document it here the
way macOS documents its Calculator exception.

## Adding a new capability

1. Implement the actual Windows API call in `src/UICtl.Core`.
2. Add a case to `CommandDispatcher.Dispatch`.
3. Add a CLI subcommand (`src/UICtl.Cli`) that builds the params dict and
   calls `DaemonClient.Send`.
4. Add a matching tool definition in `src/UICtl.Mcp` if it should also be
   MCP-callable — **and update `MCP_INTERFACE.md` in the same change**,
   since that file is the contract the macOS implementation is expected to
   match too.
5. Rebuild (`dotnet build`), restart the daemon (`uictl daemon stop`; it
   auto-restarts on the next command) so it picks up the new binary.

## Planned project layout

Not yet scaffolded — this is the intended shape once implementation starts,
kept here so early PRs have a target to converge on rather than each
inventing a structure:

```
uictl-win-mcp/
  src/
    UICtl.Cli/        # entry point, System.CommandLine subcommands
    UICtl.Core/       # Win32 / UI Automation / capture / OCR / clipboard
    UICtl.Ipc/         # DaemonClient, DaemonServer, CommandDispatcher, named-pipe protocol
    UICtl.Mcp/         # MCP server (ModelContextProtocol SDK), tool definitions
  tests/
    UICtl.Core.Tests/
  uictl.sln
```
