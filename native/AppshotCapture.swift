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

let skipRoles: Set<String> = [
  "AXMenuBar", "AXMenu", "AXMenuItem", "AXHelpTag", "AXImage",
]

let editableRoles: Set<String> = [
  "AXTextField", "AXTextArea", "AXComboBox", "AXSearchField", "AXStaticText",
]

func roleOf(_ element: AXUIElement) -> String {
  stringAttribute(element, kAXRoleAttribute as CFString) ?? ""
}

func boolAttribute(_ element: AXUIElement, _ name: CFString) -> Bool {
  var value: CFTypeRef?
  guard AXUIElementCopyAttributeValue(element, name, &value) == .success else { return false }
  if let flag = value as? Bool { return flag }
  if let number = value as? NSNumber { return number.boolValue }
  return false
}

func axPoint(_ element: AXUIElement) -> CGPoint? {
  var ref: CFTypeRef?
  guard AXUIElementCopyAttributeValue(element, kAXPositionAttribute as CFString, &ref) == .success,
        let raw = ref, CFGetTypeID(raw) == AXValueGetTypeID()
  else { return nil }
  var point = CGPoint.zero
  guard AXValueGetValue(raw as! AXValue, .cgPoint, &point) else { return nil }
  return point
}

func axSize(_ element: AXUIElement) -> CGSize? {
  var ref: CFTypeRef?
  guard AXUIElementCopyAttributeValue(element, kAXSizeAttribute as CFString, &ref) == .success,
        let raw = ref, CFGetTypeID(raw) == AXValueGetTypeID()
  else { return nil }
  var size = CGSize.zero
  guard AXValueGetValue(raw as! AXValue, .cgSize, &size) else { return nil }
  return size
}

func elementFrame(_ element: AXUIElement) -> CGRect? {
  guard let origin = axPoint(element), let size = axSize(element), size.width >= 1, size.height >= 1 else { return nil }
  return CGRect(origin: origin, size: size)
}

func isBrowserOwner(_ owner: String) -> Bool {
  ["Google Chrome", "Chromium", "Microsoft Edge", "Arc", "Brave Browser", "Vivaldi"].contains(owner)
}

func isPrivateUse(_ scalar: Unicode.Scalar) -> Bool {
  (0xE000...0xF8FF).contains(scalar.value)
    || (0xF0000...0xFFFFD).contains(scalar.value)
    || (0x100000...0x10FFFD).contains(scalar.value)
}

func isIconHeavy(_ line: String) -> Bool {
  let scalars = Array(line.unicodeScalars)
  guard !scalars.isEmpty else { return true }
  if scalars.count <= 2, scalars.allSatisfy({ $0.properties.isEmoji || isPrivateUse($0) }) { return true }
  let icons = scalars.filter { isPrivateUse($0) || $0.properties.isEmoji }.count
  return icons * 2 >= scalars.count
}

func isLowValue(_ line: String) -> Bool {
  let folded = line.lowercased()
  if ["on", "off", "true", "false", "yes", "no", "0", "1"].contains(folded) { return true }
  if line.count <= 3, line.allSatisfy({ $0.isNumber || $0 == "." }) { return true }
  return false
}

func shouldSkipChromeNoise(_ line: String) -> Bool {
  if chromeChromeNoise.contains(line) { return true }
  if line.hasPrefix("关闭") { return true }
  if line.contains("内存用量") { return true }
  if line.contains("闲置标签页") { return true }
  if line.hasPrefix("要获取缺失的图片说明") { return true }
  return false
}

func isStaleFindWidget(_ line: String) -> Bool {
  line.range(of: #"\d+\s+of\s+\d+\s+found"#, options: .regularExpression) != nil
    || line.contains(" found for '")
    || line.hasPrefix("found for '")
}

func isJunkLine(_ line: String) -> Bool {
  if shouldSkipChromeNoise(line) { return true }
  if isStaleFindWidget(line) { return true }
  if isIconHeavy(line) { return true }
  if line.contains("command:") { return true }
  if line.contains("gitlens.") { return true }
  if line.contains("$(") { return true }
  if line.contains("utm_source=") { return true }
  if line.lowercased().contains("screen reader") { return true }
  if line.contains("YesNoLearn More") || line == "Learn More" { return true }
  if line.hasPrefix("Open in Agents") { return true }
  if line.count > 280 { return true }
  return false
}

func axElementList(_ element: AXUIElement, _ name: CFString) -> [AXUIElement] {
  var ref: CFTypeRef?
  guard AXUIElementCopyAttributeValue(element, name, &ref) == .success, let ref else { return [] }
  if let list = ref as? [AXUIElement] { return list }
  if CFGetTypeID(ref) == AXUIElementGetTypeID() { return [ref as! AXUIElement] }
  return []
}

func uniqueChildren(_ element: AXUIElement, _ names: [CFString]) -> [AXUIElement] {
  var seen = Set<ObjectIdentifier>()
  var out: [AXUIElement] = []
  for name in names {
    for child in axElementList(element, name) {
      let key = ObjectIdentifier(child)
      if seen.insert(key).inserted { out.append(child) }
    }
  }
  return out
}

func structuralChildren(_ element: AXUIElement) -> [AXUIElement] {
  uniqueChildren(element, [kAXChildrenAttribute as CFString, kAXVisibleChildrenAttribute as CFString])
}

func contentChildren(_ element: AXUIElement) -> [AXUIElement] {
  let mixed = uniqueChildren(element, [
    kAXContentsAttribute as CFString,
    kAXVisibleChildrenAttribute as CFString,
    kAXChildrenAttribute as CFString,
  ])
  return mixed.isEmpty ? structuralChildren(element) : mixed
}

enum Visibility {
  case onScreen
  case unknown
  case offScreen
}

func visibility(of element: AXUIElement, windowFrame: CGRect?) -> Visibility {
  guard let windowFrame else { return .onScreen }
  guard let frame = elementFrame(element) else { return .unknown }
  if frame.width < 4 || frame.height < 4 { return .unknown }
  let visible = windowFrame.intersection(frame)
  if visible.isNull || visible.width < 2 || visible.height < 2 { return .offScreen }
  return .onScreen
}

func isChromeTitle(_ line: String) -> Bool {
  let folded = line.lowercased()
  if folded.hasSuffix(" actions") || folded.hasSuffix(" action") { return true }
  if line.hasSuffix("...") { return true }
  if folded.contains("view switcher") { return true }
  if folded.hasPrefix("toggle ") { return true }
  if folded.contains("gitlens") || folded.contains("gitpod") || folded.contains("copilot") { return true }
  if folded.contains("synchronize") || folded.contains("search editor") { return true }
  if folded.contains("submit search") || folded.contains("view as tree") { return true }
  if ["update", "refresh", "manage", "accounts", "remote", "notifications", "containers",
      "python", "ports", "node", "maximize panel", "kill terminal", "open quick access",
      "agent status", "check-all prettier", "collapse all", "clear search results",
      "open settings", "debug console"].contains(folded) { return true }
  return false
}

func hasShortcutChrome(_ line: String) -> Bool {
  line.contains("⌘") || line.contains("⇧") || line.contains("⌃") || line.contains("⌥")
    || line.contains("Ctrl+") || line.contains("Cmd+") || line.contains("Shift+")
}

func stripShortcutChrome(_ line: String) -> String {
  let pattern = #"(⌘|⇧|⌃|⌥|Ctrl\+|Cmd\+|Shift\+|Alt\+)+[A-Za-z0-9]?"#
  return line.replacingOccurrences(of: pattern, with: "\n", options: .regularExpression)
}

func parameterizedAttribute(_ element: AXUIElement, _ name: String, _ argument: CFTypeRef) -> CFTypeRef? {
  var ref: CFTypeRef?
  guard AXUIElementCopyParameterizedAttributeValue(element, name as CFString, argument, &ref) == .success else { return nil }
  return ref
}

func markerString(_ element: AXUIElement) -> String? {
  var startRef: CFTypeRef?
  var endRef: CFTypeRef?
  guard AXUIElementCopyAttributeValue(element, "AXStartTextMarker" as CFString, &startRef) == .success,
        AXUIElementCopyAttributeValue(element, "AXEndTextMarker" as CFString, &endRef) == .success,
        let startRef, let endRef,
        CFGetTypeID(startRef) == AXTextMarkerGetTypeID(),
        CFGetTypeID(endRef) == AXTextMarkerGetTypeID()
  else { return nil }
  let range = AXTextMarkerRangeCreate(kCFAllocatorDefault, startRef as! AXTextMarker, endRef as! AXTextMarker)
  guard let text = parameterizedAttribute(element, "AXStringForTextMarkerRange", range) as? String else { return nil }
  let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
  return trimmed.isEmpty ? nil : trimmed
}

func splitMarkerBody(_ text: String) -> [String] {
  var current = ""
  var parts: [String] = []
  func flush() {
    let piece = current.trimmingCharacters(in: .whitespacesAndNewlines)
    current = ""
    guard piece.count >= 2 else { return }
    for raw in stripShortcutChrome(piece).split(whereSeparator: \.isNewline) {
      let line = raw.trimmingCharacters(in: .whitespacesAndNewlines)
      if line.count >= 2 { parts.append(line) }
    }
  }
  for scalar in text.unicodeScalars {
    if scalar == "\u{FFFC}" || isPrivateUse(scalar) || scalar.properties.isEmoji {
      flush()
      continue
    }
    if scalar == "\n" || scalar == "\r" {
      flush()
      continue
    }
    current.unicodeScalars.append(scalar)
  }
  flush()
  return parts
}

func appendText(_ text: String, rank: Int, into lines: inout [(rank: Int, text: String)]) {
  let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
  guard trimmed.count >= 2, !isLowValue(trimmed), !isJunkLine(trimmed) else { return }
  if lines.last?.text == trimmed { return }
  lines.append((rank, trimmed))
}

func appendBody(_ text: String, rank: Int, into lines: inout [(rank: Int, text: String)]) {
  if text.count <= 280 {
    appendText(text, rank: rank, into: &lines)
    return
  }
  for raw in text.split(whereSeparator: { $0.isNewline }) {
    appendText(String(raw), rank: rank, into: &lines)
  }
}

func isLeafish(_ role: String) -> Bool {
  ["AXButton", "AXPopUpButton", "AXCheckBox", "AXRadioButton", "AXDisclosureTriangle", "AXSlider", "AXStaticText", "AXImage"].contains(role)
}

func isListLike(_ role: String) -> Bool {
  role == "AXList" || role == "AXOutline" || role == "AXTable" || role == "AXMenu"
}

func extractNode(
  _ element: AXUIElement,
  windowFrame: CGRect?,
  browserChrome: Bool,
  depth: Int,
  into lines: inout [(rank: Int, text: String)]
) -> Bool {
  let role = roleOf(element)
  if skipRoles.contains(role) { return false }
  if browserChrome, role == "AXTabGroup" || role == "AXToolbar" { return false }
  if visibility(of: element, windowFrame: windowFrame) == .offScreen { return false }
  if boolAttribute(element, kAXHiddenAttribute as CFString) { return true }

  if let selected = stringAttribute(element, kAXSelectedTextAttribute as CFString) {
    appendText("Selected: \(selected)", rank: 0, into: &lines)
  }
  let editable = editableRoles.contains(role) || role.hasSuffix("Field")
  if let value = stringAttribute(element, kAXValueAttribute as CFString) {
    if role == "AXSearchField" || role.hasSuffix("SearchField") {
      appendText("Search: \(value)", rank: 1, into: &lines)
    } else if editable || role == "AXStaticText" || value.count >= 6 {
      appendBody(value, rank: editable ? 1 : 2, into: &lines)
    }
  }
  if !browserChrome, role == "AXWebArea" || role == "AXDocument" {
    if let marker = markerString(element) {
      for part in splitMarkerBody(marker).prefix(80) {
        appendText(part, rank: 2, into: &lines)
      }
    }
  }
  if let title = stringAttribute(element, kAXTitleAttribute as CFString), !hasShortcutChrome(title), !isChromeTitle(title) {
    let buttonLike = ["AXButton", "AXPopUpButton", "AXCheckBox", "AXRadioButton", "AXDisclosureTriangle"].contains(role)
    if !buttonLike || title.contains(" ") {
      appendText(title, rank: buttonLike ? 4 : 3, into: &lines)
    }
  }
  if let description = stringAttribute(element, kAXDescriptionAttribute as CFString), !hasShortcutChrome(description), !isChromeTitle(description) {
    appendText(description, rank: 3, into: &lines)
  }
  if let placeholder = stringAttribute(element, kAXPlaceholderValueAttribute as CFString) {
    appendText(placeholder, rank: 2, into: &lines)
  }
  return true
}

func walk(
  roots: [AXUIElement],
  windowFrame: CGRect?,
  browserChrome: Bool,
  deadline: Date,
  budget: inout Int,
  maxDepth: Int,
  children: (AXUIElement) -> [AXUIElement],
  into lines: inout [(rank: Int, text: String)]
) {
  var queue: [(AXUIElement, Int)] = roots.map { ($0, 0) }
  var index = 0
  while index < queue.count, budget > 0, Date() <= deadline {
    let (node, depth) = queue[index]
    index += 1
    if depth > maxDepth { continue }
    budget -= 1
    let keep = extractNode(node, windowFrame: windowFrame, browserChrome: browserChrome, depth: depth, into: &lines)
    guard keep else { continue }
    let role = roleOf(node)
    if isLeafish(role) { continue }
    let kids = children(node)
    let cap = isListLike(role) ? 16 : (role == "AXToolbar" ? 48 : 80)
    if role == "AXToolbar" || isListLike(role) {
      for child in kids.prefix(cap) {
        _ = extractNode(child, windowFrame: windowFrame, browserChrome: browserChrome, depth: depth + 1, into: &lines)
      }
      continue
    }
    for child in kids.prefix(cap) {
      queue.append((child, depth + 1))
    }
  }
}

func collectText(
  from element: AXUIElement,
  windowFrame: CGRect?,
  browserChrome: Bool,
  deadline: Date,
  budget: inout Int,
  depth: Int = 0,
  descend: Bool = true,
  into lines: inout [(rank: Int, text: String)]
) {
  if Date() > deadline || budget <= 0 { return }
  if !descend {
    _ = extractNode(element, windowFrame: windowFrame, browserChrome: browserChrome, depth: depth, into: &lines)
    return
  }
  var chromeBudget = min(budget, 1800)
  walk(
    roots: [element],
    windowFrame: windowFrame,
    browserChrome: browserChrome,
    deadline: deadline,
    budget: &chromeBudget,
    maxDepth: 12,
    children: structuralChildren,
    into: &lines
  )
  budget -= (min(budget, 1800) - chromeBudget)
  var bodyBudget = min(max(budget, 0), 1600)
  walk(
    roots: [element],
    windowFrame: windowFrame,
    browserChrome: browserChrome,
    deadline: deadline,
    budget: &bodyBudget,
    maxDepth: 18,
    children: contentChildren,
    into: &lines
  )
  budget -= (min(max(budget, 0), 1600) - bodyBudget)
}

func cleanWindowText(_ rows: [(rank: Int, text: String)], windowName: String, owner: String) -> String {
  var seen = Set<String>()
  var kept: [String] = []
  let skipExact = Set([windowName, owner, "\(windowName) - \(owner)", "\(owner) - \(windowName)"].filter { !$0.isEmpty })
  for row in rows.sorted(by: { $0.rank < $1.rank }) {
    for raw in row.text.split(separator: "\n", omittingEmptySubsequences: false) {
      let line = raw.trimmingCharacters(in: .whitespacesAndNewlines)
      if line.count < 2 { continue }
      if skipExact.contains(line) { continue }
      if hasShortcutChrome(line) { continue }
      if isChromeTitle(line) { continue }
      if isJunkLine(line) { continue }
      if seen.contains(line) { continue }
      seen.insert(line)
      kept.append(line)
      if kept.count >= 180 { return kept.joined(separator: "\n") }
    }
  }
  return kept.joined(separator: "\n")
}

func windowRoot(app: AXUIElement, windowName: String) -> AXUIElement {
  AXUIElementSetMessagingTimeout(app, 3.0)
  var focused: CFTypeRef?
  if AXUIElementCopyAttributeValue(app, kAXFocusedWindowAttribute as CFString, &focused) == .success,
     let focused,
     CFGetTypeID(focused) == AXUIElementGetTypeID() {
    return focused as! AXUIElement
  }
  let windows = axElementList(app, kAXWindowsAttribute as CFString)
  if !windowName.isEmpty,
     let match = windows.first(where: { stringAttribute($0, kAXTitleAttribute as CFString) == windowName }) {
    return match
  }
  return windows.first ?? app
}

func windowText(pid: pid_t, owner: String, windowName: String, maxChars: Int) -> (text: String, truncated: Bool, trusted: Bool) {
  let trusted = AXIsProcessTrusted()
  guard trusted else { return ("", false, false) }
  let app = AXUIElementCreateApplication(pid)
  let root = windowRoot(app: app, windowName: windowName)
  var rows: [(rank: Int, text: String)] = []
  let deadline = Date().addingTimeInterval(2.8)
  var focusedRef: CFTypeRef?
  if AXUIElementCopyAttributeValue(root, kAXFocusedUIElementAttribute as CFString, &focusedRef) == .success,
     let focusedRef,
     CFGetTypeID(focusedRef) == AXUIElementGetTypeID() {
    let focusedElement = focusedRef as! AXUIElement
    let focusedRole = roleOf(focusedElement)
    if focusedRole != "AXWindow", focusedRole != "AXApplication" {
      var focusBudget = 120
      collectText(
        from: focusedElement,
        windowFrame: nil,
        browserChrome: false,
        deadline: deadline,
        budget: &focusBudget,
        depth: 0,
        descend: false,
        into: &rows
      )
    }
  }
  var budget = 4500
  collectText(
    from: root,
    windowFrame: isBrowserOwner(owner) ? elementFrame(root) : nil,
    browserChrome: isBrowserOwner(owner),
    deadline: deadline,
    budget: &budget,
    depth: 0,
    into: &rows
  )
  let text = cleanWindowText(rows, windowName: windowName, owner: owner)
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
  let extracted = windowText(pid: target.pid, owner: target.owner, windowName: target.name, maxChars: maxChars)
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
