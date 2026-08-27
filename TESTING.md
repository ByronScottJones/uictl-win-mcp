# Testing on Windows

Everything in this repo (`UICtl.Core`, `UICtl.Ipc`, `UICtl.Mcp`, `UICtl.Cli`)
was originally written on macOS, verified only by cross-compiling against
`net10.0-windows10.0.19041.0` with `EnableWindowsTargeting`.

**Update (2026-08-26): a first full live pass on real Windows (build
`net10.0-windows10.0.19041.0`, .NET 10.0.400 SDK) is done — see "Known
highest-risk spots" below for what's now confirmed vs. still open.** In that
session, `dotnet build` was clean, and every command below was exercised live
against a real Notepad window: `permissions` (unelevated + `--app`),
`apps`/`apps --all`, `windows --app`, `activate` (incl. `--window`),
`screenshot` (visually confirmed non-blank against a DWM-composited window),
`elements` and `screenshot --annotate` (visually confirmed numbered boxes
line up with real UI), `click --element`, `type --element` (both the
`valuePattern` and `synthesizedKeystrokes` methods, confirmed via re-reading
the element's value), `key` (`ctrl+a`, `ctrl+end`), `move`, `scroll`,
`pixel`, `clipboard set`/`get` (round-tripped), `wait-for` (both the
found-immediately and the full-timeout-then-`false` paths), `ocr` (real text
recognized with correct bounding boxes, `confidence` confirmed `null` on
every block per the platform limitation `MCP_INTERFACE.md` documents),
daemon lifecycle (`start --foreground`, `status`, `stop`, and cold
auto-spawn timed at ~1s), `uictl mcp` (a real JSON-RPC `initialize` →
`tools/list` → `tools/call` round trip via stdio, confirming all 16 tools
register with schemas matching `MCP_INTERFACE.md` and that a real tool call
returns the same envelope the CLI does), and all five help-flag aliases
(`-h`/`-H`/`--help`/`--HELP`/`-?`) plus per-subcommand `--help`. One real bug
was found and fixed this way: `GetCurrentThreadId` was declared under
`user32.dll` in `NativeMethods.cs` (it's a `kernel32.dll` export), which
broke `activate` outright. Per-Monitor-V2 DPI awareness was also added
(previously entirely unset) - see `DpiAwareness.cs`.

**Still not exercised**: `targetProcessElevated: true` (needs an actual
elevated target process; not attempted since deliberately triggering a UAC
prompt wasn't part of that pass), and everything in the "not yet built"
category tracked separately from this file (`displays`, `focus`, `feedback`,
`log`/activity GUI - see the project's feature-parity plan, not this
checklist, for those).

If you're an agent picking this up on a Windows machine for further work:
work through whatever's still unchecked below, fix what's broken, and update
this file (and the "known highest-risk spots" section especially) as you go
so it stays a useful map of what's actually been proven vs. still assumed.

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
   confirm it lands where expected. **Note: Per-Monitor-V2 DPI awareness is
   now set** (see `DpiAwareness.cs`) - correctness on a display at anything
   other than 100% scaling still hasn't been confirmed on real hardware
   (see "Known highest-risk spots"), so treat a click/frame mismatch there as
   worth investigating, not an already-known gap. Type into a text field both via `--element`
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
    `README.md`'s Claude Code/Desktop config examples) and confirm all
    `uictl_*` tools in `MCP_INTERFACE.md`'s table show up (`claude mcp list`,
    `claude mcp get uictl`), and that calling e.g. `uictl_screenshot` through the MCP client round-trips
    correctly end to end.

13. **Help flags** (`uictl --help`, `-h`, `-H`, `--HELP`, `-?`, and
    `uictl <subcommand> --help`). All should print usage and exit 0 - now
    confirmed against this repo's actual command tree (see the note at the
    top of this file), not just `System.CommandLine` in isolation.

## Known highest-risk spots

Confirmed working in the 2026-08-26 live pass (see the note at the top of
this file):

- `PrintWindow`/`Graphics.GetHdc` in `ScreenCapture.CaptureWindow` - captured
  a real DWM-composited Notepad window correctly (visually confirmed, not
  black/blank).
- The UI Automation tree-walk (`Automation.cs`) - walked a real Notepad
  window's tree (47 elements), roles/titles/frames all sane (`Document`,
  `Button`, `MenuBar`, `TitleBar`, etc., matching real UI Automation
  `ControlType` names as expected).
- The `INPUT` struct padding assumption in `InputSynthesis.cs` - `SendInput`
  round-tripped real keystrokes correctly (confirmed via `type` without
  `--element`, verified the typed text landed).
- The `Windows.Media.Ocr` async/WinRT interop path in `OCR.cs` - recognized
  real on-screen text with correct bounding boxes; `confidence` came back
  `null` on every block as documented.
- The daemon auto-spawn path (`DaemonClient.Connect`) - cold start (no
  daemon running) to first successful response timed at ~1s.
- `System.CommandLine` wiring end to end, not just in isolation - all
  commands exercised dispatch the right command string with the right
  params (see the full list in the note at the top of this file). All five
  help-flag aliases confirmed.
- `uictl mcp`'s stdio transport (`ModelContextProtocol` SDK 2.1.0) - a real
  client handshake (`initialize` → `tools/list` → `tools/call`) round-tripped
  correctly; all 16 tools registered with schemas matching
  `MCP_INTERFACE.md`.

Fixed as a result of this pass:

- `GetCurrentThreadId` was misdeclared under `user32.dll` instead of
  `kernel32.dll`, breaking `activate` outright.
- DPI awareness was never set at process startup; added
  `DpiAwareness.EnsurePerMonitorAware()`, called from both `Program.cs` and
  `DaemonServer.RunForegroundAsync`.

Still open / not exercised:

- Elevation checks in `Permissions.cs` (`OpenProcessToken`/
  `GetTokenInformation`) - the unelevated path (`elevated: false`) and a
  known-unelevated target (`targetProcessElevated: false`) were confirmed;
  the `true` cases (running `uictl` itself elevated, or checking a target
  that's actually elevated, e.g. Task Manager run as admin) still need a
  deliberate elevated-terminal session.
- Everything in the macOS tool that Windows doesn't implement yet at all
  (`log`/activity GUI) - tracked in the project's feature-parity plan, not
  this checklist. (`focus hold/release/status` and `feedback` were confirmed
  live in Phases 2 and 3 respectively - see the update notes below.)

**Update (Phase 1, `uictl displays`): per-monitor DPI correctness on a
non-100%-scaled display is now confirmed** - this test machine's monitor
reports `scale: 1.5` (150%) via the new `uictl displays`, and `windows`/
`screenshot --screen` frames came back as plausible physical-pixel values
consistent with the display's native 2560x1600 resolution, not pre-scaled or
otherwise distorted - closing the one item the DPI-awareness fix above
couldn't confirm on the machine available for that earlier pass.

**Update (Phase 5, automated tests): `tests/UICtl.Core.Tests` is no longer
empty.** 31 xunit tests now run live on this machine (`dotnet test`,
~2-3s), covering `AppSelector.Resolve`, `WindowResolver`, `ElementStore`
(via an `InternalsVisibleTo` from `UICtl.Core`), `PixelSampler`, `Clipboard`
round-trip, the `INPUT` struct's marshaled layout (`InputSynthesisTests`),
`Displays.List`, `FocusHold` (including the HWND-vs-pid `Reactivate` bug
fixed after PR #3), and `CommandDispatcher.Dispatch`-level coverage of the
Phase 1/2 commands (`displays.list`, `windows.list`, `focus.hold/release/
status`) - all real Win32/UIA calls against a live-launched Notepad/Paint,
no mocks. See `tests/UICtl.Core.Tests/TestSupport/NotepadFixture.cs`.

Discovered along the way: this machine's Notepad is the newer Windows App
SDK build with AI writing tools, and its rich-text editor's UI Automation
peer (role `Document`, no `ValuePattern`) isn't created until the window
has been interacted with at least once after launch - `elements`/
`screenshot --annotate` called immediately after launch (before any click)
won't show it. Not a uictl bug; just click or otherwise interact with the
window first if you hit this live. Also: this Notepad build is
single-instance - launching a second `notepad.exe` refocuses the existing
window rather than opening a new one, so tests/scripts needing two
independent windows to switch focus between should use two different apps
(this suite uses Notepad + Paint).

**Update (Phase 3, feedback): the full local CRUD + GitHub duplicate-check +
submit + MCP elicitation-fallback flow is confirmed live**, all against the
real `byronjones-elsevier/uictl-win-mcp` repo with `gh` already
authenticated on this machine:

- `feedback create/list/get/update/delete` round-tripped correctly via the
  CLI, including validation (`"category" must be one of..."`) and
  not-found errors (`"no feedback entry with id 99"`), and `%LOCALAPPDATA%\
  uictl\feedback.json` was confirmed well-formed (`{nextId, entries}`) after
  a delete left it empty.
- `feedback check-duplicates` against a real GitHub issue: created a
  throwaway issue (`#6`), confirmed the title-substring heuristic found it
  (`checked: true, usedToken: true`), then closed it. `usedToken` was `true`
  via `gh auth token`, confirming the token-resolution fallback chain works
  end to end.
- `feedback submit`'s duplicate path: matched entry was deleted locally and
  nothing was opened, as designed. The non-duplicate path opened a real
  browser tab at GitHub's pre-filled new-issue URL and correctly transitioned
  the local entry to `status: "submitted"` with `submittedAt`/`submittedUrl`
  populated - confirmed without ever clicking "Create", so no issue was
  actually filed.
- MCP round trip (`uictl mcp` over stdio, real JSON-RPC via a PowerShell
  client): all 7 `uictl_feedback_*` tools are advertised by `tools/list`
  with schemas matching `MCP_INTERFACE.md` (27 tools total now). Calling
  `uictl_feedback_submit` from a client that declares no elicitation
  capability returned in ~2s with `elicitationFallback: "Client does not
  support elicitation requests."` rather than hanging for the full 120s
  timeout - the SDK checks `ClientCapabilities` up front and fails
  `ElicitAsync` fast, so the timeout only matters against a client that
  claims elicitation support but then never responds. The actual
  form-mode/url-mode elicitation prompts (an MCP client that *does* support
  elicitation) are still unexercised - no such client was available on this
  machine for this pass.

## Reporting back

When you find something wrong, the most useful thing to capture is: which
specific method misbehaved, what you expected vs. got, and whether it's a
plain logic bug or one of the known-risk items above turning out to actually
be wrong (vs. a new risk this checklist didn't anticipate).
