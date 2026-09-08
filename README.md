# dsh-appshots

[![Awesome DSH Plugin](https://awesome-dsh-plugin.com/badge.svg)](https://awesome-dsh-plugin.com)

简体中文 | [English](README.en.md)

给 [DSH Desktop](https://github.com/anywhere-labs/dsh-desktop) 用的窗口截图，支持 macOS 和 Windows。

macOS 同时按两边 Command，Windows 同时按两边 Ctrl，或点输入框旁的相机，捕获当前前台窗口，附加到会话草稿，并读取窗口文字作为隐藏上下文。

- 只捕获最前面的窗口，不截整个屏幕
- 截图附加到输入框草稿，不会自动发送
- 读取可用的窗口文字（macOS 辅助功能 / Windows UI Automation），作为隐藏的模型上下文注入
- 跳过 DSH Desktop 自身，避免截到插件自己的窗口
- 截图后自动把 DSH Desktop 调到前台

## 安装

```sh
dsh plugin --profile desktop add dsh-appshots
```

安装后重启 DSH Desktop。

从源码安装：

```sh
git clone https://github.com/WongYuYe/dsh-appshots.git
cd dsh-appshots
dsh plugin --profile desktop add .
```

Windows 会在第一次截图时用系统自带的 `csc.exe`（.NET Framework 4.x）编译助手，不需要安装 Visual Studio。

## 权限

### macOS

系统设置 → 隐私与安全性：

- **屏幕录制**：捕获最前面窗口所必需
- **辅助功能**：监听两个 Command 键、读取窗口文字所必需

把这两项授予 **DSH Desktop**。如果热键不触发，同时允许 `appshot-capture`。

### Windows

- 截图走 `CopyFromScreen`，系统可能弹一次屏幕访问授权
- 窗口文字走 UI Automation，部分提权或受保护的应用几乎读不到文字
- 默认热键是同时按两边 Ctrl。如果不触发，可改成 `hotkeyMode: win-hotkey`

## 使用

1. 聚焦你想分享的窗口。
2. macOS 同时按两个 Command，Windows 同时按两个 Ctrl，或点击输入框里的相机按钮。
3. DSH Desktop 来到前台，截图已附加。
4. 输入提示词并发送。

如果当前有打开的会话，Appshot 会附加到那里；如果没有，会新建一个会话。连续截图会进入同一个会话。

## 设置

命名空间 `dsh-appshots`，位于 `~/.dsh/settings.yaml`：

| 字段 | 默认值 | 含义 |
|---|---|---|
| `skipSelf` | `true` | 跳过 DSH Desktop 自身 |
| `attachText` | `true` | 发送时把清洗后的窗口文字作为隐藏模型上下文注入 |
| `recentWindowMs` | `60000` | 没有打开会话时，复用这个时间窗口内截图过的会话 |
| `hotkeyMode` | `auto` | `auto` / `both-command` / `both-control` / `carbon` / `win-hotkey` / `off` |
| `carbonKeyCode` | `0` | `hotkeyMode` 为 `carbon` 时的 Carbon 键码 |
| `carbonModifiers` | `256` | Carbon 修饰键；256 是 Command |
| `winVk` | `44` | `hotkeyMode` 为 `win-hotkey` 时的虚拟键码（44 是 PrintScreen） |
| `winModifiers` | `3` | `win-hotkey` 的 Win32 修饰键；3 是 Ctrl+Alt |

`auto` 在 macOS 上是两边 Command，在 Windows 上是两边 Ctrl。

## 限制

- 仅支持 macOS 和 Windows
- 窗口文字来自系统辅助功能树，部分应用只暴露可见文本
- Chrome 的标签栏、书签栏等 UI 会从隐藏文本中过滤掉
- Windows 截的是窗口矩形的屏幕像素，被挡住的窗口会带上挡住它的内容

## License

MIT
