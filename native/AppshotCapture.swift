import ApplicationServices
import Carbon
import Cocoa
import CoreGraphics
import Foundation

let skipOwners: Set<String> = [
  "Dock",
  "Window Server",
  "Control Center",
  "Control Centre",
  "Notification Center",
  "Notification Centre",
  "Spotlight",
  "universalAccessAuthWarn",
  "SystemUIServer",
  "loginwindow",
  "Item-0",
  "Wallpaper",
  "WindowManager",
]

let leftCommandMask: UInt64 = 0x00000008
let rightCommandMask: UInt64 = 0x00000010

func emit(_ object: [String: Any]) {
  guard JSONSerialization.isValidJSONObject(object),
        let data = try? JSONSerialization.data(withJSONObject: object, options: []),
        let line = String(data: data, encoding: .utf8)
  else { return }
  fputs(line + "\n", stdout)
  fflush(stdout)
}

func fail(_ message: String) -> Never {
  emit(["ok": false, "error": message])
  exit(1)
}

func windowList() -> [[String: Any]] {
  let opts: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
  return (CGWindowListCopyWindowInfo(opts, kCGNullWindowID) as? [[String: Any]]) ?? []
}

func windowArea(_ bounds: [String: Any]?) -> Double {
  let width = (bounds?["Width"] as? NSNumber)?.doubleValue ?? 0
  let height = (bounds?["Height"] as? NSNumber)?.doubleValue ?? 0
  return width * height
}

func isSkippableOwner(_ owner: String, extra: Set<String>) -> Bool {
  extra.contains(owner) || skipOwners.contains(owner)
}

func pickTargetWindow(skipOwners extra: Set<String>) -> (id: CGWindowID, owner: String, name: String, pid: pid_t)? {
  let windows = windowList().compactMap { info -> (id: CGWindowID, owner: String, name: String, pid: pid_t, area: Double)? in
    let layer = info[kCGWindowLayer as String] as? Int ?? -1
    guard layer == 0 else { return nil }
    let owner = info[kCGWindowOwnerName as String] as? String ?? ""
    let name = info[kCGWindowName as String] as? String ?? ""
    let pid = info[kCGWindowOwnerPID as String] as? pid_t ?? -1
    let id = CGWindowID(info[kCGWindowNumber as String] as? UInt32 ?? 0)
    let area = windowArea(info[kCGWindowBounds as String] as? [String: Any])
    guard id != 0, pid > 0, area >= 80 * 80 else { return nil }
    return (id, owner, name, pid, area)
  }

  func largest(for pid: pid_t) -> (id: CGWindowID, owner: String, name: String, pid: pid_t)? {
    windows.filter { $0.pid == pid }.max { $0.area < $1.area }.map { ($0.id, $0.owner, $0.name, $0.pid) }
  }

  if let front = NSWorkspace.shared.frontmostApplication {
    let owner = front.localizedName ?? ""
    if !isSkippableOwner(owner, extra: extra), let picked = largest(for: front.processIdentifier) {
      return picked
    }
  }

  var seen = Set<pid_t>()
  for window in windows {
    if isSkippableOwner(window.owner, extra: extra) { continue }
    if seen.contains(window.pid) { continue }
    seen.insert(window.pid)
    if let picked = largest(for: window.pid) { return picked }
  }
  return windows.max { $0.area < $1.area }.map { ($0.id, $0.owner, $0.name, $0.pid) }
}

func stringAttribute(_ element: AXUIElement, _ name: CFString) -> String? {
  var value: CFTypeRef?
  guard AXUIElementCopyAttributeValue(element, name, &value) == .success else { return nil }
  if let text = value as? String {
    let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
    return trimmed.isEmpty ? nil : trimmed
  }
  return nil
}

let chromeChromeNoise: Set<String> = [
  "Skip to content", "Navigation Menu", "Homepage", "Global", "Platform", "Solutions",
  "Resources", "Open Source", "Enterprise", "Pricing", "Sign in", "Sign up",
  "Appearance settings", "Toby: Tab Management Tool", "自定义 Chrome", "返回", "前进",
  "重新加载", "查看网站信息", "为此标签页添加书签", "扩展程序", "创建二维码 - 已固定",
  "重新启动即可更新", "书签", "已保存的标签页分组", "标签页分组", "分隔符",
  "标签页搜索", "打开 Chrome 中的 Gemini", "新标签页", "关闭", "翻译",
  "安装“GitHub”", "Accessibility help", "Go to Google Home", "Clear",
  "Search by voice", "Search by image", "Search", "Share", "Google apps",
  "AI Mode", "All", "Images", "Videos", "Shopping", "Short videos", "News",
  "More filters", "More", "Tools", "Search Results", "Page Navigation",
  "Footer Links", "Help", "Send feedback", "Privacy", "Terms",
]

func roleOf(_ element: AXUIElement) -> String {
  stringAttribute(element, kAXRoleAttribute as CFString) ?? ""
}

func shouldSkipChromeNoise(_ line: String) -> Bool {
  if chromeChromeNoise.contains(line) { return true }
  if line.hasPrefix("关闭") { return true }
  if line.contains("内存用量") { return true }
  if line.contains("闲置标签页") { return true }
  if line.hasPrefix("要获取缺失的图片说明") { return true }
  return false
}

func collectText(from element: AXUIElement, deadline: Date, budget: inout Int, depth: Int, into lines: inout [String]) {
  if Date() > deadline || budget <= 0 || depth > 18 { return }
  budget -= 1
  let role = roleOf(element)
  if ["AXToolbar", "AXTabGroup", "AXMenuBar", "AXMenu", "AXSplitter"].contains(role) { return }

  if let value = stringAttribute(element, kAXValueAttribute as CFString), !shouldSkipChromeNoise(value) {
    if lines.last != value { lines.append(value) }
  } else if let title = stringAttribute(element, kAXTitleAttribute as CFString), !shouldSkipChromeNoise(title) {
    if title.count >= 2, lines.last != title { lines.append(title) }
  }

  var childrenRef: CFTypeRef?
  guard AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &childrenRef) == .success,
        let children = childrenRef as? [AXUIElement]
  else { return }
  for child in children.prefix(80) {
    collectText(from: child, deadline: deadline, budget: &budget, depth: depth + 1, into: &lines)
  }
}

func cleanWindowText(_ raw: String) -> String {
  var seen = Set<String>()
  var kept: [String] = []
  for line in raw.split(separator: "\n", omittingEmptySubsequences: false).map({ $0.trimmingCharacters(in: .whitespacesAndNewlines) }) {
    if line.isEmpty { continue }
    if shouldSkipChromeNoise(line) { continue }
    if line.count < 2 { continue }
    if seen.contains(line) { continue }
    seen.insert(line)
    kept.append(line)
    if kept.count >= 400 { break }
  }
  return kept.joined(separator: "\n")
}

func windowText(pid: pid_t, maxChars: Int) -> (text: String, truncated: Bool, trusted: Bool) {
  let trusted = AXIsProcessTrusted()
  guard trusted else { return ("", false, false) }
  let app = AXUIElementCreateApplication(pid)
  var focused: CFTypeRef?
  var root: AXUIElement = app
  if AXUIElementCopyAttributeValue(app, kAXFocusedWindowAttribute as CFString, &focused) == .success,
     let focused,
     CFGetTypeID(focused) == AXUIElementGetTypeID() {
    root = (focused as! AXUIElement)
  }
  var lines: [String] = []
  var budget = 900
  collectText(from: root, deadline: Date().addingTimeInterval(1.2), budget: &budget, depth: 0, into: &lines)
  let text = cleanWindowText(lines.joined(separator: "\n"))
  if text.count > maxChars {
    let end = text.index(text.startIndex, offsetBy: maxChars)
    return (String(text[..<end]), true, true)
  }
  return (text, false, true)
}

func activateDshDesktop() {
  let named = NSWorkspace.shared.runningApplications.filter { app in
    (app.localizedName ?? "") == "DSH Desktop"
  }
  let bundled = NSRunningApplication.runningApplications(withBundleIdentifier: "ai.deepseek.dsh-desktop")
    + NSRunningApplication.runningApplications(withBundleIdentifier: "com.deepseek.dsh-desktop")
  guard let target = named.first ?? bundled.first else { return }
  if #available(macOS 14.0, *) {
    target.activate()
  } else {
    target.activate(options: [.activateIgnoringOtherApps])
  }
  let src = """
  tell application "System Events"
    set procs to every process whose name is "DSH Desktop"
    if (count of procs) > 0 then
      set frontmost of item 1 of procs to true
    end if
  end tell
  """
  if let script = NSAppleScript(source: src) {
    var error: NSDictionary?
    _ = script.executeAndReturnError(&error)
  }
}

func extraSkip(skipSelf: Bool) -> Set<String> {
  skipSelf ? ["DSH Desktop", "DSH Desktop Helper"] : []
}

func frontPayload(skipSelf: Bool, maxChars: Int) -> [String: Any] {
  guard let target = pickTargetWindow(skipOwners: extraSkip(skipSelf: skipSelf)) else {
    return ["ok": false, "error": "no on-screen window to capture"]
  }
  let extracted = windowText(pid: target.pid, maxChars: maxChars)
  return [
    "ok": true,
    "owner": target.owner,
    "title": target.name,
    "pid": Int(target.pid),
    "windowId": Int(target.id),
    "text": extracted.text,
    "textTruncated": extracted.truncated,
    "axTrusted": extracted.trusted,
  ]
}

func status() {
  let extras = extraSkip(skipSelf: true)
  let target = pickTargetWindow(skipOwners: extras)
  let namesPresent = windowList().contains { info in
    let owner = info[kCGWindowOwnerName as String] as? String ?? ""
    let name = info[kCGWindowName as String] as? String ?? ""
    return !isSkippableOwner(owner, extra: extras) && !name.isEmpty
  }
  emit([
    "ok": true,
    "axTrusted": AXIsProcessTrusted(),
    "windowNamesVisible": namesPresent,
    "frontOwner": target?.owner ?? "",
    "frontTitle": target?.name ?? "",
  ])
}

func promptAccessibilityIfNeeded() {
  if AXIsProcessTrusted() { return }
  let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
  _ = AXIsProcessTrustedWithOptions(options)
}

func runCarbonHotkey(keyCode: UInt32, modifiers: UInt32) {
  var hotKeyRef: EventHotKeyRef?
  var handlerRef: EventHandlerRef?
  let hotKeyID = EventHotKeyID(signature: OSType(0x41505348), id: 1)
  var eventType = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
  let status = InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
    emit(["event": "hotkey"])
    return noErr
  }, 1, &eventType, nil, &handlerRef)
  guard status == noErr else { fail("failed to install hotkey handler (\(status))") }
  let registered = RegisterEventHotKey(keyCode, modifiers, hotKeyID, GetApplicationEventTarget(), 0, &hotKeyRef)
  guard registered == noErr else { fail("failed to register hotkey (\(registered)). It may already be in use.") }
  emit(["event": "ready", "mode": "carbon", "keyCode": Int(keyCode), "modifiers": Int(modifiers)])
  RunLoop.main.run()
}

private var bothCommandArmed = true
private var bothCommandTap: CFMachPort?

func bothCommandTapCallback(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent, refcon: UnsafeMutableRawPointer?) -> Unmanaged<CGEvent>? {
  if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
    if let tap = bothCommandTap {
      CGEvent.tapEnable(tap: tap, enable: true)
    }
    return Unmanaged.passUnretained(event)
  }
  let flags = event.flags.rawValue
  let left = (flags & leftCommandMask) != 0
  let right = (flags & rightCommandMask) != 0
  if left && right {
    if bothCommandArmed {
      bothCommandArmed = false
      emit(["event": "hotkey"])
    }
  } else {
    bothCommandArmed = true
  }
  return Unmanaged.passUnretained(event)
}

func runBothCommandHotkey() {
  promptAccessibilityIfNeeded()
  bothCommandArmed = true
  let mask = CGEventMask(1 << CGEventType.flagsChanged.rawValue)
  guard let tap = CGEvent.tapCreate(
    tap: .cgSessionEventTap,
    place: .headInsertEventTap,
    options: .listenOnly,
    eventsOfInterest: mask,
    callback: bothCommandTapCallback,
    userInfo: nil
  ) else {
    fail("failed to create event tap. Grant Accessibility to DSH Desktop and appshot-capture.")
  }
  bothCommandTap = tap
  let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
  CFRunLoopAddSource(CFRunLoopGetCurrent(), source, .commonModes)
  CGEvent.tapEnable(tap: tap, enable: true)
  emit(["event": "ready", "mode": "both-command"])
  CFRunLoopRun()
}

func argValue(_ name: String) -> String? {
  guard let index = CommandLine.arguments.firstIndex(of: name),
        index + 1 < CommandLine.arguments.count
  else { return nil }
  return CommandLine.arguments[index + 1]
}

func hasFlag(_ name: String) -> Bool {
  CommandLine.arguments.contains(name)
}

let command = CommandLine.arguments.dropFirst().first ?? "front"
let skipSelf = !hasFlag("--include-self")
let maxChars = Int(argValue("--max-chars") ?? "12000") ?? 12_000

switch command {
case "front":
  promptAccessibilityIfNeeded()
  let payload = frontPayload(skipSelf: skipSelf, maxChars: maxChars)
  emit(payload)
  if payload["ok"] as? Bool != true { exit(1) }
case "activate":
  activateDshDesktop()
  emit(["ok": true, "event": "activated"])
case "status":
  promptAccessibilityIfNeeded()
  status()
case "hotkey":
  let mode = argValue("--mode") ?? "both-command"
  if mode == "carbon" {
    let keyCode = UInt32(argValue("--key-code") ?? "0") ?? 0
    let modifiers = UInt32(argValue("--modifiers") ?? "768") ?? 768
    runCarbonHotkey(keyCode: keyCode, modifiers: modifiers)
  } else {
    runBothCommandHotkey()
  }
default:
  fail("unknown command \(command)")
}
