# uictl MCP interface — cross-platform contract

This is the single source of truth for the tool names, CLI verbs, arguments, and
response shape that **both** implementations of `uictl` commit to exposing
identically:

- macOS (Swift): [`uictl-mcp`](https://github.com/byronjones-elsevier/uictl-mcp)
- Windows (.NET/C#): this repo, `uictl-win-mcp`

The goal is that an agent's tool-calling code (or shell script) doesn't need to
branch on OS — `uictl_click`, `uictl_screenshot`, etc. take the same arguments
and return the same shape everywhere. Where a platform genuinely can't support
something, or must add something, that's called out explicitly below rather
than silently diverging. Treat this file as the spec to implement against; if
an implementation and this file disagree, the file wins — fix the code.

Changes to tool names, argument names, or the response envelope must be made
in this file first, then mirrored into both implementations' source
(`Sources/uictl/MCP/MCPServer.swift` on macOS; the MCP tool registration in
this repo's future `UICtl.Mcp` project) in the same change.

## Response envelope

Every command — CLI or MCP — returns exactly one of:

```json
{"ok": true, "data": { /* command-specific */ }}
```
```json
{"ok": false, "error": "<human-readable message>"}
```

The CLI prints this as one JSON object to stdout and exits `0` on `ok: true`,
non-zero otherwise. The MCP tool call returns the same JSON as its text
content, with `isError` set to `!ok`.

## Common types

- **Point** — `{"x": number, "y": number}`. Screen coordinates, top-left
  origin, y increasing downward.
  - macOS: Quartz global point-space (`CGPoint`, "points" not raw pixels —
    divide/multiply by backing scale factor if you need physical pixels).
  - Windows: virtual-screen device pixels (`GetWindowRect`/UI Automation
    `BoundingRectangle` space, in a per-monitor-DPI-aware process). See
    `ENGINEERING.md` in each repo for the platform-specific pixel-vs-point
    discussion — the contract here is just "top-left origin, y-down, and
    self-consistent with every other frame/point this tool returns on that
    platform."
- **Frame/Rect** — `{"x": number, "y": number, "w": number, "h": number}`.
  Same coordinate space as Point.
- **Element id** — an opaque string, scoped to the window it was listed from.
  Callers must treat it as opaque and not parse it.
  - macOS: `"{windowId}-{n}"` (an incrementing counter per window).
  - Windows: same scheme (`"{windowId}-{n}"`) unless implementation finds a
    reason to diverge — if so, update this file and explain why.
  - Both platforms: ids are invalidated the next time that window's elements
    are re-listed (`uictl_elements` / `uictl_screenshot --annotate`). Callers
    must re-list before acting on a stale id.
- **Window id** — an integer window handle.
  - macOS: `CGWindowID` (`UInt32`).
  - Windows: `HWND` value. `HWND` is pointer-sized (64-bit on x64), so
    Windows' `windowId` may exceed 32 bits — callers must not assume it fits
    in a signed 32-bit int. This is the one deliberate type-width divergence;
    everything else in this contract is platform-independent.

## App/window selectors

Every `app` parameter below is a single string matched, in order, against:
process name (substring, case-insensitive) → then a platform identifier →
then numeric pid.

- macOS: name substring → bundle id → pid.
- Windows: name substring (process image name, e.g. `notepad`) → package
  family name (for packaged/MSIX apps, the closest analog to a bundle id) →
  pid.

## Tools

Twenty tools, one per row. "Command" is the internal dispatcher command
string (shared vocabulary between the CLI front end and the MCP front end on
each platform); "CLI" is the subcommand a human/script would type; "MCP tool"
is the name an MCP client calls.

| Command | CLI | MCP tool | Required args | Optional args |
|---|---|---|---|---|
| `permissions.status` | `permissions` | `uictl_permissions` | — | `app: string` |
| `apps.list` | `apps` | `uictl_apps` | — | `all: bool` |
| `displays.list` | `displays` | `uictl_displays` | — | — |
| `windows.list` | `windows` | `uictl_windows` | — | `app: string` |
| `activate` | `activate` | `uictl_activate` | `app: string` | `window: int` |
| `focus.hold` | `focus hold` | `uictl_focus_hold` | — (`app` or `window`) | `app: string`, `window: int` |
| `focus.release` | `focus release` | `uictl_focus_release` | — | — |
| `focus.status` | `focus status` | `uictl_focus_status` | — | — |
| `screenshot` | `screenshot` | `uictl_screenshot` | — | `window: int`, `app: string`, `screen: int`, `out: string`, `annotate: bool`, `role: string` |
| `elements` | `elements` | `uictl_elements` | — | `window: int`, `app: string`, `role: string`, `title: string`, `maxDepth: int`, `maxElements: int` |
| `click` | `click` | `uictl_click` | — (`at` or `element`) | `at: string`, `element: string`, `button: string`, `double: bool`, `count: int` |
| `move` | `move` | `uictl_move` | `at: string` | — |
| `scroll` | `scroll` | `uictl_scroll` | `at: string` | `dx: int`, `dy: int` |
| `type` | `type` | `uictl_type` | `text: string` | `element: string` |
| `key` | `key` | `uictl_key` | `combo: string` | — |
| `waitFor` | `wait-for` | `uictl_wait_for` | — | `window: int`, `app: string`, `role: string`, `title: string`, `timeout: number` |
| `ocr` | `ocr` | `uictl_ocr` | — | `image: string`, `window: int`, `app: string`, `region: string` |
| `pixel` | `pixel` | `uictl_pixel` | `at: string` | — |
| `clipboard.get` | `clipboard get` | `uictl_clipboard_get` | — | — |
| `clipboard.set` | `clipboard set` | `uictl_clipboard_set` | `text: string` | — |
| `feedback.create` | `feedback create` | `uictl_feedback_create` | `category: string`, `title: string`, `body: string` | — |
| `feedback.list` | `feedback list` | `uictl_feedback_list` | — | — |
| `feedback.get` | `feedback get` | `uictl_feedback_get` | `id: int` | — |
| `feedback.update` | `feedback update` | `uictl_feedback_update` | `id: int` | `category: string`, `title: string`, `body: string` |
| `feedback.delete` | `feedback delete` | `uictl_feedback_delete` | `id: int` | — |
| `feedback.checkDuplicates` | `feedback check-duplicates` | `uictl_feedback_check_duplicates` | `id: int` | `repo: string`, `token: string` |
| `feedback.submit` | `feedback submit` | `uictl_feedback_submit` | `id: int` | `repo: string`, `token: string` |

`permissions.request` (CLI-only: `permissions --request`) is deliberately
**not** an MCP tool on either platform — it exists to trigger macOS's TCC
consent dialogs interactively, which only makes sense from a foreground CLI
invocation a human can see, not from an agent-driven MCP call. Windows has no
equivalent OS consent dialog (see below), so `permissions --request` on
Windows is a no-op that returns the same payload as `permissions` plus a
`"requested": false` note — kept for CLI parity, not because it does
anything.

### `uictl_permissions`

Checks the platform-specific preconditions this tool needs to function.
Response `data` shape is intentionally *not* identical between platforms,
since the underlying concepts differ — callers should treat this as
diagnostic/informational, not branch logic:

- macOS: `{"accessibility": bool, "screenRecording": bool}`.
- Windows: `{"elevated": bool, "targetProcessElevated": bool | null}`. UI
  Automation and `SendInput` are blocked by UIPI when the target process runs
  at a higher integrity level than `uictl` itself — there's no consent prompt
  to grant, only "run uictl elevated too, or don't automate elevated apps."
  Pass the optional `app` argument (same app-selector rules as every other
  tool) to populate `targetProcessElevated` for that process; omit it and
  `targetProcessElevated` is `null`.

### `uictl_apps`

`data`: `{"apps": [{"pid": int, "name": string, "bundleId": string}, ...]}`.

- Windows: `bundleId` holds the package family name for packaged apps, or
  `""` for classic Win32 apps (most of them) — same empty-string-for-N/A
  convention macOS uses for apps with no bundle id.

### `uictl_displays`

`data`: `{"displays": [{"index": int, "displayId": int, "frame": Frame, "isMain": bool, "scale": number}, ...]}`.
`index` matches what `screenshot`'s `screen` argument expects. `displayId` is
an opaque per-platform monitor id (macOS: `CGDirectDisplayID`; Windows:
`HMONITOR` value - same 64-bit-may-exceed-32-bits caveat as window ids, see
"Window id" above). `scale` is points-to-pixels on macOS
(`SCContentFilter.pointPixelScale`) and DPI/96 on Windows (both: "how many
physical pixels per this platform's nominal coordinate unit at that
monitor's current setting").

### `uictl_windows`

`data`: `{"windows": [{"windowId": int, "pid": int, "title": string, "frame": Frame, "displayId": int | null}, ...]}`.
`displayId` matches one of `uictl_displays`' `displayId` values, or `null` if
the window's center doesn't fall within any display's bounds (rare, but
possible for a mostly off-screen window).

### `uictl_activate`

`data`: `{"pid": int, ...}` (macOS also echoes back the resolved app info;
Windows should do the same — resolved pid at minimum).

### `uictl_focus_hold` / `uictl_focus_status`

`data`: `{"held": true, "app": string, "pid": int, "windowId": int, "isFrontmost": bool, "restoresTo": string | null}`,
or `{"held": false}` when nothing is held (`focus_status` only — `focus_hold`
always has something held by the time it returns, having just set it).
`restoresTo` is the label `focus_release` will try to restore focus to, or
`null` if `hold` captured nothing (e.g. no foreground window at the moment of
the first `hold` in a hold/[hold...]/release sequence).

### `uictl_focus_release`

`data`: `{"held": false, "restoredFocus": "alreadyFrontmost" | "reactivated" | "failed"}`,
with `restoredFocus` omitted if there was nothing to restore (no prior `hold`
had captured a previous focus).

**Focus-hold integration.** `uictl_click`, `uictl_move`, `uictl_scroll`,
`uictl_type`, and `uictl_key` each additionally carry
`"focusHold": "alreadyFrontmost" | "reactivated" | "failed"` in their
response **only while a hold is active** (omitted entirely otherwise, never
sent as `"notHeld"`) — see each tool's own section below for its base
response shape. Before performing the action, each of these five re-checks
whether the held window's app is foreground and, if not, re-activates and
raises it first, so a human clicking away and back mid-sequence doesn't
derail automation targeting a held window. `"failed"` means the action that
just ran may well have gone to the wrong window (e.g. the held app has
quit) — callers should treat that as a signal to re-check state, not assume
the action landed where aimed.

### `uictl_screenshot`

`data`: `{"path": string, "width": int, "height": int}`, plus
`"elements": [{"number": int, "id": string, "role": string, "title": string, "value": string?, "frame": Frame}, ...]`
when `annotate: true`. `role` is a platform-native string either way (macOS:
`AXRole` values like `AXButton`; Windows: UI Automation `ControlType` names
like `Button`) — see the note under `uictl_elements`.

### `uictl_elements`

`data`: `{"windowId": int, "count": int, "elements": [...]}` (same element
shape as the screenshot legend, minus `number`), plus `"truncated": true` if
`maxElements` cut the walk short.

**Role strings are not unified across platforms.** `role`/`--role` filters
against whatever native role/control-type vocabulary the platform exposes
(macOS AX role constants vs. Windows UI Automation `ControlType` names). A
script written to filter `--role AXButton` will need `--role Button` on
Windows. This is the one place callers must branch on platform, because
inventing a fake shared vocabulary would just be a lossy translation layer
neither implementation's native tooling/docs would match. If a future need
justifies a shared role taxonomy, propose it here first.

### `uictl_click`

`data`: `{"clicked": Point}`, plus `"focusHold"` per the note under `uictl_focus_release` above while a hold is active.

### `uictl_move` / `uictl_scroll`

`data`: `{"moved": true}` / `{"scrolled": true}`, plus `"focusHold"` per the note under `uictl_focus_release` above while a hold is active.

### `uictl_type`

`data`: `{"method": "axValue" | "synthesizedKeystrokes", "element": string?}`, plus `"focusHold"` per the note under `uictl_focus_release` above while a hold is active.

- Windows: `method` values are `"valuePattern"` (UI Automation `ValuePattern.SetValue`,
  the equivalent of macOS's direct AX value set) or `"synthesizedKeystrokes"`
  (`SendInput`, the fallback). Same two-tier strategy, platform-native names.

### `uictl_key`

`data`: `{"sent": string}` (echoes the combo), plus `"focusHold"` per the note under `uictl_focus_release` above while a hold is active. Modifier vocabulary is
platform-native: macOS uses `cmd, shift, alt/option, ctrl/control, fn`;
Windows uses `ctrl, shift, alt, win`. There is no shared modifier name for
"the OS accelerator key" (Cmd vs. Ctrl) — scripts crossing platforms must
translate this themselves.

### `uictl_wait_for`

`data`: `{"found": bool, "element": {...}?}` (element present only when found).

### `uictl_ocr`

`data`: `{"textBlocks": [{"text": string, "frame": Frame, "confidence": number | null}, ...]}`.

- macOS: Vision framework (`VNRecognizeTextRequest`), which reports a real
  per-observation `confidence` (0–1).
- Windows: `Windows.Media.Ocr` (`OcrEngine`), which reports no confidence
  score at all — the API simply doesn't expose one. `confidence` is always
  `null` on Windows; this is a genuine platform limitation, not an
  unimplemented field, and callers must not assume it's populated cross-
  platform. Each block corresponds to one `OcrLine`, with `frame` as the
  union of that line's `OcrWord` bounding rects (rather than a synthetic
  paragraph grouping) — the same granularity macOS's per-observation blocks
  give you.
- Both platforms return per-block bounding boxes in the same global
  coordinate space as `elements` frames — this is a hard requirement, not a
  suggestion, since OCR's main use case is "find a click point for text the
  AX/UIA tree didn't expose."

### `uictl_pixel`

`data`: `{"r": int, "g": int, "b": int, "a": int}` (0–255 each).

### `uictl_clipboard_get` / `uictl_clipboard_set`

`data`: `{"text": string}` / `{"set": true}`.

### Feedback (`uictl_feedback_*`)

Local-first storage for feedback about uictl itself (an issue, error, or
recommendation), stored one JSON file per platform (`feedback.json`) before a
separate `submit` step hands a specific entry off to GitHub by opening a
pre-filled "new issue" page — neither platform's `submit` files the issue
itself; a human still reviews and clicks "Create" there.

A `FeedbackEntry` (returned by `create`/`get`/`update`/`list`'s array
elements) is:
```json
{
  "id": int, "category": "issue" | "error" | "recommendation",
  "title": string, "body": string,
  "createdAt": string, "updatedAt": string,
  "status": "draft" | "submitted",
  "submittedAt": string | null, "submittedUrl": string | null
}
```

- `uictl_feedback_create` — `data`: the new `FeedbackEntry`.
- `uictl_feedback_list` — `data`: `[FeedbackEntry, ...]` (a plain array, not
  wrapped in an object — deliberately unlike `uictl_apps`/`uictl_windows`).
- `uictl_feedback_get` — `data`: the `FeedbackEntry`.
- `uictl_feedback_update` — `data`: the updated `FeedbackEntry`. Only
  `category`/`title`/`body` are editable; `status` only changes via `submit`.
- `uictl_feedback_delete` — `data`: `{"deleted": id}`.
- `uictl_feedback_check_duplicates` — `data`: `{"checked": bool, "usedToken": bool, "duplicates": [{"number": int, "title": string, "url": string, "state": string}, ...]}`
  on a successful check, or `{"checked": false, "reason": string, "duplicates": []}`
  if the check itself couldn't run (no token against a private repo, network
  error, rate limit, ...) — this is *not* surfaced as a tool error; an
  unreachable duplicate check just means "proceed without it." `usedToken` is
  only present when `checked` is true. The duplicate heuristic is a
  deliberately simple case-insensitive equality-or-substring match against
  every open and closed issue in the target repo (default: this platform's
  own repo) — not fuzzy matching.
- `uictl_feedback_submit` — first re-runs the same duplicate check. If a
  match is found, the local entry is deleted and `data` is
  `{"submitted": false, "duplicate": true, "deletedLocally": true, "matchedIssue": {...}}`
  with nothing opened. Otherwise `data` is
  `{"url": string, "opened": true, "duplicateCheck": string, "entry": FeedbackEntry}`
  (`entry.status` is now `"submitted"`).

**MCP elicitation.** `uictl_feedback_submit` is the one tool on both
platforms whose logic isn't a thin forward to the daemon: since an *agent*
is the one initiating something outward-facing on the human's behalf, it
first asks the human to review (and optionally edit) the title/body via
form-mode MCP elicitation, then hands the pre-filled GitHub URL to the
client via a second, url-mode elicitation, rather than silently opening a
browser tab on the daemon's own machine. If the review is declined or
cancelled, `data` is `{"submitted": false, "reason": string}` and nothing is
sent anywhere. If the connected client doesn't support elicitation (or
doesn't respond within a timeout — 120s on both platforms), this falls back
to the same non-interactive open-the-URL-directly behavior as the CLI, with
an added `elicitationFallback: string` field explaining why. The **CLI**
`feedback submit` always uses this non-interactive path directly — running
it from a terminal is itself the human's confirmation, so there is nothing
to elicit.

- Windows: default repo is `byronjones-elsevier/uictl-win-mcp`; token
  resolution (`--token` / `token` param → `$GITHUB_TOKEN` → `gh auth token`)
  and the duplicate-check REST call are the same shape as macOS's, just
  via `HttpClient`/`System.Diagnostics.Process` instead of `URLSession`/
  `Process`.
- macOS: default repo is `byronjones-elsevier/uictl-mcp`.

## Deliberately platform-specific, not part of this contract

- `daemon start|stop|status` (CLI-only, both platforms — see each repo's
  `ENGINEERING.md` for the IPC transport, which differs: Unix domain socket
  on macOS, named pipe on Windows).
- `mcp` (the subcommand that runs the MCP server itself over stdio) — its
  existence is shared, its registration/config-file examples are OS-specific
  (see each repo's README).
- Exact process-elevation / permission-prompt behavior (see `uictl_permissions`
  above).
