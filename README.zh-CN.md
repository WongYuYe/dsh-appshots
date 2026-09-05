# dsh-appshots

[![Awesome DSH Plugin](https://awesome-dsh-plugin.com/badge.svg)](https://awesome-dsh-plugin.com)

[English](README.md) | 简体中文

Codex 风格的 Appshots，用于 macOS 上的 [DSH Desktop](https://github.com/anywhere-labs/dsh-desktop)。

同时按下两个 Command 键（或点输入框旁的相机按钮），捕获最前面的窗口并附加到当前会话。

- 只捕获最前面的窗口，不截整个屏幕
- 截图附加到输入框草稿，不会自动发送
- 通过辅助功能读取可用的窗口文字，作为隐藏的模型上下文注入
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

## 权限

系统设置 → 隐私与安全性：

- **屏幕录制**：捕获最前面窗口所必需
- **辅助功能**：监听两个 Command 键、读取窗口文字所必需

把这两项授予 **DSH Desktop**。如果热键不触发，同时允许 `appshot-capture`。

## 使用

1. 聚焦你想分享的窗口。
2. 同时按下两个 Command 键，或点击输入框里的相机按钮。
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
| `hotkeyMode` | `both-command` | `both-command` / `carbon` / `off` |
| `carbonKeyCode` | `0` | `hotkeyMode` 为 `carbon` 时的 Carbon 键码 |
| `carbonModifiers` | `256` | Carbon 修饰键；256 是 Command |

## 限制

- 仅支持 macOS
- 窗口文字来自辅助功能树，部分应用只暴露可见文本
- Chrome 的标签栏、书签栏等 UI 会从隐藏文本中过滤掉

## License

MIT
