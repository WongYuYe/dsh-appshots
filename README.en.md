# dsh-appshots

[![Awesome DSH Plugin](https://awesome-dsh-plugin.com/badge.svg)](https://awesome-dsh-plugin.com)

[简体中文](README.md) | English

Codex-style window capture for [DSH Desktop](https://github.com/anywhere-labs/dsh-desktop) on macOS and Windows.

Press both Command keys on macOS, both Ctrl keys on Windows, or the camera button next to the composer to grab the frontmost window, attach it to the current chat, and inject available window text as hidden context.

- Captures the frontmost window only, not the whole screen
- Attaches the screenshot to the composer draft without sending it
- Reads available window text (Accessibility on macOS, UI Automation on Windows) and injects it as hidden model context
- Skips DSH Desktop itself so the plugin does not capture its own window
- After capture, brings DSH Desktop to the front

## Install

```sh
dsh plugin --profile desktop add dsh-appshots
```

Restart DSH Desktop after installing.

From source:

```sh
git clone https://github.com/WongYuYe/dsh-appshots.git
cd dsh-appshots
dsh plugin --profile desktop add .
```

On Windows the helper compiles on first capture with the system `csc.exe` (.NET Framework 4.x). Visual Studio is not required.

Release: bump `package.json` to `X.Y.Z` and push a `vX.Y.Z` tag. GitHub Actions opens the GitHub Release and publishes to npm with Trusted Publisher (OIDC).

## Permissions

### macOS

System Settings → Privacy & Security:

- **Screen Recording**: required to capture the frontmost window
- **Accessibility**: required to listen for both Command keys and read window text

Grant these to **DSH Desktop**. If the hotkey does not fire, also allow `appshot-capture`.

### Windows

- Screen capture uses `CopyFromScreen`; Windows may prompt once for screen access
- Window text uses UI Automation; some elevated or protected apps expose little or no text
- The default hotkey is both Ctrl keys. If it does not fire, try `hotkeyMode: win-hotkey`

## Use

1. Focus the window you want to share.
2. Press both Command keys (macOS) or both Ctrl keys (Windows), or click the camera button in the composer.
3. DSH Desktop comes to the front with the screenshot attached.
4. Add a prompt and send.

If a session is currently open, the appshot goes there. If none is open, a new session is created. Consecutive captures go to the same session.

## Settings

Namespace `dsh-appshots` in `~/.dsh/settings.yaml`:

| Field | Default | Meaning |
|---|---|---|
| `skipSelf` | `true` | Skip DSH Desktop itself |
| `attachText` | `true` | Inject cleaned window text as hidden model context on send |
| `recentWindowMs` | `60000` | If no session is open, reuse the session captured within this window |
| `hotkeyMode` | `auto` | `auto` / `both-command` / `both-control` / `carbon` / `win-hotkey` / `off` |
| `carbonKeyCode` | `0` | Carbon key code when `hotkeyMode` is `carbon` |
| `carbonModifiers` | `256` | Carbon modifiers; 256 is Command |
| `winVk` | `44` | Virtual-key code when `hotkeyMode` is `win-hotkey` (44 is PrintScreen) |
| `winModifiers` | `3` | Win32 modifiers for `win-hotkey`; 3 is Ctrl+Alt |

`auto` is both Command keys on macOS and both Ctrl keys on Windows.

## Limits

- macOS and Windows only
- Window text comes from the platform accessibility tree, so some apps expose only visible copy
- Chrome chrome such as the tab strip and bookmark bar is filtered out of the hidden text
- Windows capture copies the on-screen pixels of the window rectangle; occluded windows include whatever is visible

## License

MIT
