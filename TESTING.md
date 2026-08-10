# Testing on Windows

Everything in this repo (`UICtl.Core`, `UICtl.Ipc`, `UICtl.Mcp`, `UICtl.Cli`)
was written on macOS, verified only by cross-compiling against
`net10.0-windows10.0.19041.0` with `EnableWindowsTargeting`. That catches
wrong method names/signatures - it proves nothing about runtime behavior.
**None of the Windows-specific code (anything touching Win32/COM/WinRT) has
ever executed.** The one exception is `System.CommandLine` itself (a portable
library with no Windows dependency) - its option/argument binding, required-
option errors, and the custom `-H`/`--HELP` help aliases were smoke-tested in
isolation in a scratch console app and confirmed to behave as expected. That
only proves the parsing *library* works as assumed, not that this repo's
~17 command definitions are wired to it and to `DaemonClient` correctly. This
file is the checklist for the first real Windows session: what to verify, in
what order, and which pieces were flagged as highest-risk while writing them
blind.

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
dotnet run --project src/UICtl.Cli -- apps
```

Expect: build succeeds (already verified cross-platform, should be
uneventful). `uictl apps` should auto-spawn the daemon and print a JSON
`{"ok":true,"data":{"apps":[...]}}` listing your running processes. If it
instead throws, hangs, or prints a raw .NET exception instead of a JSON
`{"ok":false,...}`, that's the very first thing to fix - `CliRunner` is
supposed to catch everything and always emit one JSON object.

If for some reason you need to bypass the CLI's argument parsing to isolate
whether a bug is in `System.CommandLine` wiring vs. the actual capability, you
can still exercise `UICtl.Core`/`UICtl.Ipc` directly:

- Add temporary tests to `tests/UICtl.Core.Tests` (currently empty) that call
  `UICtl.Core` methods directly - e.g. `AppSelector.Resolve("notepad")` - and
  assert on the result. Cheap to write and will actually *run* here, unlike
  on macOS. Worth turning into permanent regression tests rather than
  deleting once things work.
- Or call `UICtl.Ipc.CommandDispatcher.Dispatch(command, paramsJsonElement)`
  directly for a given command in `MCP_INTERFACE.md`'s table - no daemon/pipe
  needed, it's a plain static call.

## Checklist, roughly in dependency order

1. **Permissions** (`uictl permissions`). Run unelevated first - confirm
   `elevated: false`. Then from an elevated terminal - confirm
   `elevated: true`. Run `uictl permissions --app <name>` for a known-elevated
   process (e.g. Task Manager run as admin) and confirm `targetProcessElevated`
   reflects it. Also try `uictl permissions --request` - should behave
   identically to plain `permissions` plus `"requested": false` (it's a
   deliberate no-op on Windows, kept only for CLI parity with macOS).

2. **App/window enumeration** (`uictl apps`, `uictl windows`). Launch
   Notepad; confirm it appears in `uictl apps` with default (no `--all`) -
   it's a normal foreground window. Confirm some background service process
   only appears with `uictl apps --all`. Confirm `uictl windows --app
   notepad` returns a window with a frame matching what you see on screen.

3. **Activate** (`uictl activate --app notepad`). With Notepad behind
   another window, confirm it actually comes to front. This exercises the
   `AttachThreadInput`/foreground-lock workaround in
   `AppsAndWindows.BringToFront` - exactly the kind of thing that's easy to
   get subtly wrong and manifests only as "nothing happens" on real Windows.
   Also confirm the missing-required-option error path works as expected:
   `uictl activate` with no `--app` should print a clear
   `--app is required` error and exit non-zero, not a crash.

4. **Screenshot (plain)** (`uictl screenshot --app notepad`). Open the PNG;
   confirm it's not black/blank. `PrintWindow` with `PW_RENDERFULLCONTENT` is
   the main risk here, especially against anything DWM-composited.

5. **Elements + screenshot --annotate** (`uictl elements --app notepad`,
   `uictl screenshot --app notepad --annotate`). Confirm `role`/`title`/
   `frame` values look sane (role values should match real UI Automation
   `ControlType` names like `Edit`, `Button`, `TitleBar`). Then confirm the
   annotated PNG has numbered boxes at the right locations and the legend's
   frames line up with what's on screen.

6. **Click / type / key** (`uictl click`, `uictl type`, `uictl key`). Click a
   numbered element from step 5's legend (`uictl click --element <id>`);
   confirm it lands where expected. **Note: nothing in this process sets
   Per-Monitor-V2 DPI awareness yet** (see `ENGINEERING.md`'s DPI section) -
   if you're on a display at anything other than 100% scaling, treat a
   click/frame mismatch as an expected, already-known gap, not a new bug,
   until DPI awareness is added. Type into a text field both via `--element`
   (should use `ValuePattern`, method `"valuePattern"` in the response) and
   via plain focus+`SendInput` (method `"synthesizedKeystrokes"`); confirm
   both actually produce the typed text. Send `uictl key "ctrl+a"` in
   Notepad and confirm select-all registers.

7. **Move / scroll / pixel** (`uictl move`, `uictl scroll`, `uictl pixel`).
   Confirm the cursor actually moves, a scrollable window actually scrolls,
   and pixel sampling returns a color matching what's on screen at that point
   (compare against any screen color-picker tool).

8. **Clipboard** (`uictl clipboard set "hello"`, `uictl clipboard get`).
   Confirm round-trip. Also manually paste (Ctrl+V somewhere) after setting,
   to confirm it's really on the system clipboard and not just cached
   internally.

9. **wait-for** (`uictl wait-for`). Call it against an element that doesn't
   exist yet; confirm it polls for the full timeout and returns
   `found: false` rather than erroring immediately. Then trigger the element
   to appear mid-poll (e.g. open a dialog) and confirm it returns
   `found: true` promptly.

10. **OCR** (`uictl ocr --app notepad`). Against a window with visible text
    (Notepad with some typed text is fine), confirm recognized text and
    bounding boxes look right, and that `confidence` is present as an
    explicit JSON `null` (not omitted) - that's intentional per
    `MCP_INTERFACE.md`, not a bug. Also test `--region` against a sub-area
    and confirm only text within it comes back.

11. **Daemon lifecycle** (`uictl daemon start --foreground`, `uictl daemon
    status`, `uictl daemon stop`). `start --foreground` should create
    `%LOCALAPPDATA%\uictl\daemon.log` and start logging. In another terminal,
    `uictl daemon status` should report `running: true` without spawning a
    second daemon (check there's still only one `uictl.exe` process). Any
    other command run afterward should reach the existing daemon via the
    `\\.\pipe\uictl` named pipe. `uictl daemon stop` should cleanly end the
    foreground process's accept loop (check the log for "daemon stopped" and
    that the process actually exits). Also confirm the *auto-spawn* path:
    kill any running daemon, then run `uictl apps` directly - it should
    transparently spawn the daemon and still return a normal result within
    a few seconds.

12. **MCP server** (`uictl mcp`). Register it with an MCP client (see
    `README.md`'s Claude Code/Desktop config examples) and confirm all 16
    `uictl_*` tools show up (`claude mcp list`, `claude mcp get uictl`), and
    that calling e.g. `uictl_screenshot` through the MCP client round-trips
    correctly end to end.

13. **Help flags** (`uictl --help`, `-h`, `-H`, `--HELP`, `-?`, and
    `uictl <subcommand> --help`). All should print usage and exit 0 - this
    was smoke-tested in isolation (see the note at the top of this file) but
    never against this repo's actual command tree.

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
- The daemon auto-spawn path (`DaemonClient.Connect` relaunching
  `Environment.ProcessPath` with `daemon start --foreground` when the pipe
  isn't reachable) - the retry/timeout loop and process-spawn logic were
  never exercised end to end; a first `uictl <anything>` with no daemon
  running is the actual first real test of it.
- The full `UICtl.Cli` command tree (17 commands across
  `Commands/*.cs`) - `System.CommandLine`'s option/argument binding
  mechanics were verified in isolation (see the top of this file), but
  whether each command builds the *right* params dictionary and dispatches
  the *right* command string has only been checked by re-reading the code
  against `MCP_INTERFACE.md`, not by running it.

## Reporting back

When you find something wrong, the most useful thing to capture is: which
specific method misbehaved, what you expected vs. got, and whether it's a
plain logic bug or one of the known-risk items above turning out to actually
be wrong (vs. a new risk this checklist didn't anticipate).
