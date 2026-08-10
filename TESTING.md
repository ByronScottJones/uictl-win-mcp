# Testing on Windows

Everything in this repo so far (`UICtl.Core`, `UICtl.Ipc`, and `UICtl.Mcp`) was
written on macOS, verified only by cross-compiling against
`net10.0-windows10.0.19041.0` with `EnableWindowsTargeting`. That catches
wrong method names/signatures - it proves nothing about runtime behavior.
**None of this code has ever executed.** This file is the checklist for the
first real Windows session: what to verify, in what order, and which pieces
were flagged as highest-risk while writing them blind.

If you're an agent picking this up on a Windows machine: work through this
checklist, fix what's broken, and update this file (and the "known
highest-risk spots" section especially) as you go so it stays a useful map of
what's actually been proven vs. still assumed.

## Setup

- .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`, or
  https://dotnet.microsoft.com/download).
- Windows 10 build 19041 (2004) or later - matches the TargetFramework.
- For OCR: at least one language's "Optical character recognition" component
  installed (Settings > Time & Language > Language & region > (language) >
  Options > install "Handwriting" is separate - look for the OCR-specific
  component). Without it, `OcrEngine.TryCreateFromUserProfileLanguages()`
  returns null and `uictl_ocr` fails with a clear error - that's expected
  behavior to verify, not a bug to chase.
- A couple of ordinary apps to test against - Notepad and Calculator are
  good default targets (mirrors what the macOS `uictl-mcp` sibling project
  tested against: TextEdit/Calculator). Something with a deeper UI tree
  (e.g. Settings) is worth trying too, for `elements`/tree-walk behavior.

## Step 1: does it even build and run here?

```powershell
git clone https://github.com/byronjones-elsevier/uictl-win-mcp.git
cd uictl-win-mcp
dotnet build
dotnet run --project src/UICtl.Cli
```

Expect: build succeeds (already verified cross-platform, should be
uneventful). As of this writing `UICtl.Cli` has no subcommands wired up yet -
it just prints `uictl: not yet implemented (project scaffold only)` and
exits 1. Check recent commits / `ENGINEERING.md` for whether that's still
true by the time you're reading this.

## Testing before the CLI is wired up

Since `UICtl.Cli` doesn't parse subcommands yet, the fastest way to exercise
`UICtl.Core`/`UICtl.Ipc`/`UICtl.Mcp` directly is one of:

- Add temporary tests to `tests/UICtl.Core.Tests` (currently empty) that call
  `UICtl.Core` methods directly - e.g. `AppSelector.Resolve("notepad")`,
  `AppsAndWindows.ListWindows(null)` - and assert on the result. These are
  cheap to write and, unlike on macOS, will actually *run* here. Worth
  turning into permanent regression tests rather than deleting once things
  work.
- Or write a throwaway console `Program.cs` that calls
  `UICtl.Ipc.CommandDispatcher.Dispatch(command, paramsJsonElement)` directly
  for each command in `MCP_INTERFACE.md`'s table, printing the JSON response
  and eyeballing it against the documented shape - no daemon/pipe needed for
  this, `CommandDispatcher` is a plain static call.
- Once `UICtl.Mcp` has a way to run standalone (see Task 2's work), you can
  also drive it directly through an MCP client without the CLI at all.

## Checklist, roughly in dependency order

1. **Permissions** (`Permissions.Status`). Run unelevated first - confirm
   `elevated: false`. Then from an elevated terminal - confirm
   `elevated: true`. Pass an `app` selector for a known-elevated process
   (e.g. Task Manager run as admin) and confirm `targetProcessElevated`
   reflects it.

2. **App/window enumeration** (`AppsAndWindows.ListApps`/`ListWindows`,
   `AppSelector.Resolve`). Launch Notepad; confirm it appears in `apps.list`
   with default `all=false` (it's a normal foreground window). Confirm some
   background service process only appears with `all=true`. Confirm
   `windows.list --app notepad` returns a window with a frame matching what
   you see on screen.

3. **Activate**. With Notepad behind another window, call
   `activate --app notepad`; confirm it actually comes to front. This
   exercises the `AttachThreadInput`/foreground-lock workaround in
   `AppsAndWindows.BringToFront` - exactly the kind of thing that's easy to
   get subtly wrong and manifests only as "nothing happens" on real Windows.

4. **Screenshot (plain)**. Capture Notepad; open the PNG; confirm it's not
   black/blank. `PrintWindow` with `PW_RENDERFULLCONTENT` is the main risk
   here, especially against anything DWM-composited.

5. **Elements + screenshot --annotate**. Run `elements --app notepad`;
   confirm `role`/`title`/`frame` values look sane (role values should match
   real UI Automation `ControlType` names like `Edit`, `Button`,
   `TitleBar`). Then `screenshot --app notepad --annotate`; confirm the PNG
   has numbered boxes at the right locations and the legend's frames line up
   with what's on screen.

6. **Click / type / key**. Click a numbered element from step 5's legend;
   confirm it lands where expected. **Note: nothing in this process sets
   Per-Monitor-V2 DPI awareness yet** (that's `Cli`/daemon-startup's job, not
   yet implemented - see `ENGINEERING.md`'s DPI section) - if you're on a
   display at anything other than 100% scaling, treat a click/frame mismatch
   as an expected, already-known gap, not a new Core bug, until DPI
   awareness is added. Type into a text field both via `--element` (should
   use `ValuePattern`, method `"valuePattern"` in the response) and via
   plain focus+`SendInput` (method `"synthesizedKeystrokes"`); confirm both
   actually produce the typed text. Send a key combo (e.g. `ctrl+a` in
   Notepad) and confirm it registers.

7. **Move / scroll / pixel**. Confirm the cursor actually moves, a
   scrollable window actually scrolls, and pixel sampling returns a color
   matching what's on screen at that point (compare against any screen
   color-picker tool).

8. **Clipboard**. `clipboard.set` then `clipboard.get` - confirm round-trip.
   Also manually paste (Ctrl+V somewhere) after setting, to confirm it's
   really on the system clipboard and not just cached internally.

9. **wait-for**. Call it against an element that doesn't exist yet; confirm
   it polls for the full timeout and returns `found: false` rather than
   erroring immediately. Then trigger the element to appear mid-poll (e.g.
   open a dialog) and confirm it returns `found: true` promptly.

10. **OCR**. Against a window with visible text (Notepad with some typed
    text is fine), confirm recognized text and bounding boxes look right,
    and that `confidence` is present as an explicit JSON `null` (not
    omitted) - that's intentional per `MCP_INTERFACE.md`, not a bug. Also
    test the `region` parameter against a sub-area and confirm only text
    within it comes back.

11. **Daemon lifecycle**. `uictl daemon start --foreground` (once `Cli`
    wires this to `DaemonServer.RunForegroundAsync`) should create
    `%LOCALAPPDATA%\uictl\daemon.log` and start logging. A client command run
    afterward should reach it via the `\\.\pipe\uictl` named pipe without
    needing to spawn a new daemon. `uictl daemon stop` should cleanly end the
    foreground process's accept loop (check the log for the "daemon stopped"
    line and that the process actually exits).

12. **MCP server**. Once `Cli`'s `mcp` subcommand exists, register it with an
    MCP client (see `README.md`'s Claude Code/Desktop config examples) and
    confirm all 16 `uictl_*` tools show up (`claude mcp list`, `claude mcp
    get uictl`), and that calling e.g. `uictl_screenshot` through the MCP
    client round-trips correctly end to end.

## Known highest-risk spots (from writing this blind)

- `PrintWindow`/`Graphics.GetHdc` in `ScreenCapture.CaptureWindow` - never
  verified against a real DWM-composited window.
- FlaUI's actual tree-walk behavior/timing on a real accessibility tree
  (`Automation.EnumerateElements`) - verified only that the specific members
  used exist, via reflection on the installed NuGet package (see the commit
  that added `Automation.cs`), not that the walk behaves as expected against
  real UI.
- The `INPUT` struct padding assumption in `InputSynthesis.cs` - `SendInput`
  silently no-opping or misbehaving on x64 if the assumed struct layout were
  wrong. See that file's comment and its commit message for the reasoning
  it should be correct.
- The `Windows.Media.Ocr` async/WinRT interop path in `OCR.cs` - compiled
  clean against the projected metadata, but the actual `BitmapDecoder`/
  `SoftwareBitmap` conversion round-trip has never run.
- DPI awareness is not set anywhere in the process yet (see checklist item 6)
  - expect frame/click coordinates to be wrong on non-100%-scaled displays
    until that's added.
- Elevation checks in `Permissions.cs` (`OpenProcessToken`/
  `GetTokenInformation`) - standard pattern, never run.
- Whatever `UICtl.Mcp` turns out to look like by the time you read this -
  check `ENGINEERING.md`/recent commits for its current state, since it may
  have landed after this file was written.

## Reporting back

When you find something wrong, the most useful thing to capture is: which
specific method misbehaved, what you expected vs. got, and whether it's a
plain logic bug or one of the known-risk items above turning out to actually
be wrong (vs. a new risk this checklist didn't anticipate).
