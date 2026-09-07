# dsh-appshots

[![Awesome DSH Plugin](https://awesome-dsh-plugin.com/badge.svg)](https://awesome-dsh-plugin.com)

English | [简体中文](README.zh-CN.md)

Codex-style window capture for [DSH Desktop](https://github.com/anywhere-labs/dsh-desktop) on macOS.

Press both Command keys (or the camera button next to the composer) to grab the frontmost window, attach it to the current chat, and inject available window text as hidden context.

- Captures the frontmost window only, not the whole screen
- Attaches the screenshot to the composer draft without sending it
- Reads available window text via Accessibility and injects it as hidden model context
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

## Permissions

System Settings → Privacy & Security:

- **Screen Recording**: required to capture the frontmost window
- **Accessibility**: required to listen for both Command keys and read window text

Grant these to **DSH Desktop**. If the hotkey does not fire, also allow `appshot-capture`.

## Use

1. Focus the window you want to share.
2. Press both Command keys, or click the camera button in the composer.
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
| `hotkeyMode` | `both-command` | `both-command` / `carbon` / `off` |
| `carbonKeyCode` | `0` | Carbon key code when `hotkeyMode` is `carbon` |
| `carbonModifiers` | `256` | Carbon modifiers; 256 is Command |

## Limits

- macOS only
- Window text comes from the Accessibility tree, so some apps expose only visible copy
- Chrome chrome such as the tab strip and bookmark bar is filtered out of the hidden text

## License

MIT
