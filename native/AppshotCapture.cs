using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const uint GA_ROOT = 2;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int SW_RESTORE = 9;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const int VK_MENU = 0x12;
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_CONTROL = 0x11;
    public const int VK_SNAPSHOT = 0x2C;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const int WM_HOTKEY = 0x0312;
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);
}

internal static class Program
{
    static readonly HashSet<string> SkipOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "SearchHost",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "TextInputHost",
        "LockApp",
        "dwm",
        "ApplicationFrameHost",
        "SystemSettings",
        "SecurityHealthSystray",
    };

    static Native.LowLevelKeyboardProc HookProc;
    static IntPtr Hook = IntPtr.Zero;
    static bool BothControlArmed = true;

    static void Main(string[] args)
    {
        string command = args.Length > 0 ? args[0] : "front";
        bool skipSelf = !HasFlag(args, "--include-self");
        int maxChars = ParseInt(ArgValue(args, "--max-chars"), 12000);
        try
        {
            switch (command)
            {
                case "front":
                    Emit(FrontPayload(skipSelf, maxChars));
                    break;
                case "capture":
                    Capture(skipSelf, maxChars, ArgValue(args, "--out") ?? FailOut());
                    break;
                case "activate":
                    ActivateDshDesktop();
                    Emit(Ok(new Dictionary<string, object> { { "event", "activated" } }));
                    break;
                case "status":
                    Status(skipSelf);
                    break;
                case "hotkey":
                    RunHotkey(ArgValue(args, "--mode") ?? "both-control", args);
                    break;
                default:
                    Fail("unknown command " + command);
                    break;
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    static string FailOut()
    {
        Fail("missing --out");
        return "";
    }

    static void Capture(bool skipSelf, int maxChars, string outPath)
    {
        var front = FrontPayload(skipSelf, maxChars);
        if (!(front.ContainsKey("ok") && front["ok"] is bool ok && ok))
        {
            Emit(front);
            Environment.Exit(1);
            return;
        }
        IntPtr hwnd = new IntPtr(Convert.ToInt64(front["windowId"]));
        CaptureWindow(hwnd, outPath);
        FileInfo info = new FileInfo(outPath);
        if (!info.Exists || info.Length < 32)
        {
            Fail("screenshot was empty. Grant Screen capture permission if prompted.");
        }
        front["imagePath"] = outPath;
        front["bytes"] = info.Length;
        Emit(front);
    }

    static Dictionary<string, object> FrontPayload(bool skipSelf, int maxChars)
    {
        IntPtr hwnd;
        string owner;
        string title;
        uint pid;
        if (!PickTargetWindow(skipSelf, out hwnd, out owner, out title, out pid))
        {
            return Err("no on-screen window to capture");
        }
        string text;
        bool truncated;
        WindowText(hwnd, maxChars, out text, out truncated);
        return new Dictionary<string, object>
        {
            { "ok", true },
            { "owner", owner ?? "" },
            { "title", title ?? "" },
            { "pid", (int)pid },
            { "windowId", hwnd.ToInt64() },
            { "text", text ?? "" },
            { "textTruncated", truncated },
            { "axTrusted", true },
        };
    }

    static void Status(bool skipSelf)
    {
        IntPtr hwnd;
        string owner;
        string title;
        uint pid;
        bool found = PickTargetWindow(skipSelf, out hwnd, out owner, out title, out pid);
        Emit(Ok(new Dictionary<string, object>
        {
            { "axTrusted", true },
            { "windowNamesVisible", found && !string.IsNullOrEmpty(title) },
            { "frontOwner", found ? owner : "" },
            { "frontTitle", found ? title : "" },
        }));
    }

    static bool PickTargetWindow(bool skipSelf, out IntPtr hwnd, out string owner, out string title, out uint pid)
    {
        hwnd = Native.GetForegroundWindow();
        hwnd = RootWindow(hwnd);
        if (hwnd != IntPtr.Zero && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && WindowArea(hwnd) >= 80 * 80)
        {
            Describe(hwnd, out owner, out title, out pid);
            if (!IsSkippable(owner, title, skipSelf))
            {
                return true;
            }
        }

        IntPtr picked = IntPtr.Zero;
        double best = 0;
        Native.EnumWindows((candidate, _) =>
        {
            if (!Native.IsWindowVisible(candidate) || Native.IsIconic(candidate)) return true;
            if ((Native.GetWindowLong(candidate, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0) return true;
            double area = WindowArea(candidate);
            if (area < 80 * 80 || area <= best) return true;
            string candOwner, candTitle;
            uint candPid;
            Describe(candidate, out candOwner, out candTitle, out candPid);
            if (IsSkippable(candOwner, candTitle, skipSelf)) return true;
            best = area;
            picked = candidate;
            return true;
        }, IntPtr.Zero);

        if (picked == IntPtr.Zero)
        {
            hwnd = IntPtr.Zero;
            owner = "";
            title = "";
            pid = 0;
            return false;
        }
        hwnd = picked;
        Describe(hwnd, out owner, out title, out pid);
        return true;
    }

    static IntPtr RootWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return hwnd;
        IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;
    }

    static void Describe(IntPtr hwnd, out string owner, out string title, out uint pid)
    {
        Native.GetWindowThreadProcessId(hwnd, out pid);
        title = WindowTitle(hwnd);
        owner = ProcessName(pid);
    }

    static string ProcessName(uint pid)
    {
        try
        {
            Process process = Process.GetProcessById((int)pid);
            string name = process.MainWindowTitle;
            try
            {
                string module = process.MainModule != null ? Path.GetFileNameWithoutExtension(process.MainModule.FileName) : process.ProcessName;
                return string.IsNullOrEmpty(module) ? process.ProcessName : module;
            }
            catch
            {
                return process.ProcessName;
            }
        }
        catch
        {
            return "";
        }
    }

    static string WindowTitle(IntPtr hwnd)
    {
        StringBuilder buffer = new StringBuilder(1024);
        Native.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    static bool IsSkippable(string owner, string title, bool skipSelf)
    {
        if (string.IsNullOrEmpty(owner)) return true;
        if (SkipOwners.Contains(owner)) return true;
        if (!skipSelf) return false;
        return IsDshProcess(owner, title);
    }

    static bool IsDshProcess(string owner, string title)
    {
        string ownerKey = (owner ?? "").ToLowerInvariant();
        string titleKey = (title ?? "").ToLowerInvariant();
        if (ownerKey == "dsh desktop" || ownerKey == "dsh desktop helper") return true;
        if (titleKey.Contains("dsh desktop")) return true;
        return ownerKey.Contains("dsh-desktop");
    }

    static double WindowArea(IntPtr hwnd)
    {
        Native.RECT rect;
        if (!TryWindowBounds(hwnd, out rect)) return 0;
        return Math.Max(0, rect.Right - rect.Left) * (double)Math.Max(0, rect.Bottom - rect.Top);
    }

    static bool TryWindowBounds(IntPtr hwnd, out Native.RECT rect)
    {
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(Native.RECT))) == 0)
        {
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
        return Native.GetWindowRect(hwnd, out rect);
    }

    static void CaptureWindow(IntPtr hwnd, string outPath)
    {
        Native.RECT rect;
        if (!TryWindowBounds(hwnd, out rect)) throw new Exception("failed to read window bounds");
        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
            bitmap.Save(outPath, ImageFormat.Png);
        }
    }

    static readonly HashSet<string> ChromeNoise = new HashSet<string>(StringComparer.Ordinal)
    {
        "Skip to content", "Navigation Menu", "Homepage", "Global", "Platform", "Solutions",
        "Resources", "Open Source", "Enterprise", "Pricing", "Sign in", "Sign up",
        "Appearance settings", "自定义 Chrome", "返回", "前进", "重新加载", "查看网站信息",
        "为此标签页添加书签", "扩展程序", "书签", "标签页搜索", "新标签页", "关闭", "翻译",
        "Accessibility help", "Search", "Share", "Google apps", "AI Mode", "All",
        "Images", "Videos", "Shopping", "News", "More", "Tools", "Help", "Privacy", "Terms",
    };

    sealed class RankedLine
    {
        public int Rank;
        public string Text;
        public RankedLine(int rank, string text)
        {
            Rank = rank;
            Text = text;
        }
    }

    static void WindowText(IntPtr hwnd, int maxChars, out string text, out bool truncated)
    {
        List<RankedLine> rows = new List<RankedLine>();
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(1800);
        int budget = 2200;
        string owner, title;
        uint pid;
        Describe(hwnd, out owner, out title, out pid);
        double wl = 0, wt = 0, wr = 0, wb = 0;
        bool hasWindowRect = false;
        try
        {
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            if (root != null)
            {
                try
                {
                    System.Windows.Rect bounds = root.Current.BoundingRectangle;
                    if (!bounds.IsEmpty)
                    {
                        wl = bounds.Left; wt = bounds.Top; wr = bounds.Right; wb = bounds.Bottom;
                        hasWindowRect = true;
                    }
                }
                catch { }
                CollectText(root, wl, wt, wr, wb, hasWindowRect, IsBrowserOwner(owner), deadline, ref budget, 0, rows);
            }
        }
        catch
        {
        }
        string cleaned = CleanWindowText(rows, title, owner);
        if (string.IsNullOrWhiteSpace(cleaned) && !string.IsNullOrWhiteSpace(title)) cleaned = title.Trim();
        truncated = cleaned.Length > maxChars;
        text = truncated ? cleaned.Substring(0, maxChars) : cleaned;
    }

    static bool IsBrowserOwner(string owner)
    {
        return owner == "Google Chrome" || owner == "Microsoft Edge" || owner == "Chromium"
            || owner == "Brave" || owner == "Vivaldi" || owner == "Arc";
    }

    static bool IsPrivateUse(char ch)
    {
        int value = ch;
        return (value >= 0xE000 && value <= 0xF8FF);
    }

    static bool IsIconHeavy(string line)
    {
        if (line.Length <= 2)
        {
            bool allIcons = true;
            foreach (char ch in line)
            {
                if (!char.IsSurrogate(ch) && !IsPrivateUse(ch)) { allIcons = false; break; }
            }
            if (allIcons) return true;
        }
        int icons = 0;
        foreach (char ch in line) if (IsPrivateUse(ch)) icons++;
        return icons * 2 >= line.Length;
    }

    static bool IsLowValue(string line)
    {
        string folded = line.Trim().ToLowerInvariant();
        if (folded == "on" || folded == "off" || folded == "true" || folded == "false" || folded == "yes" || folded == "no") return true;
        if (folded == "0" || folded == "1") return true;
        return false;
    }

    static bool ShouldSkipChromeNoise(string line)
    {
        if (ChromeNoise.Contains(line)) return true;
        if (line.StartsWith("关闭", StringComparison.Ordinal)) return true;
        if (line.Contains("内存用量")) return true;
        if (line.Contains("闲置标签页")) return true;
        return false;
    }

    static bool IsStaleFindWidget(string line)
    {
        return Regex.IsMatch(line, @"\d+\s+of\s+\d+\s+found") || line.Contains(" found for '");
    }

    static bool IsJunkLine(string line)
    {
        if (ShouldSkipChromeNoise(line)) return true;
        if (IsStaleFindWidget(line)) return true;
        if (IsIconHeavy(line)) return true;
        if (line.Contains("command:")) return true;
        if (line.Contains("gitlens.")) return true;
        if (line.Contains("$(")) return true;
        if (line.Contains("utm_source=")) return true;
        if (line.ToLowerInvariant().Contains("screen reader")) return true;
        if (line.Contains("YesNoLearn More") || line == "Learn More") return true;
        if (line.StartsWith("Open in Agents", StringComparison.Ordinal)) return true;
        if (line.Length > 280) return true;
        return false;
    }

    static bool IntersectsWindow(double wl, double wt, double wr, double wb, bool hasWindowRect, System.Windows.Rect frame)
    {
        if (!hasWindowRect || frame.IsEmpty) return true;
        double l = Math.Max(wl, frame.Left);
        double t = Math.Max(wt, frame.Top);
        double r = Math.Min(wr, frame.Right);
        double b = Math.Min(wb, frame.Bottom);
        return (r - l) >= 2 && (b - t) >= 2;
    }

    static List<AutomationElement> ChildrenOf(AutomationElement element)
    {
        List<AutomationElement> children = new List<AutomationElement>();
        try
        {
            AutomationElement child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            int count = 0;
            while (child != null && count < 80)
            {
                children.Add(child);
                child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                count++;
            }
        }
        catch { }
        return children;
    }

    static bool ExtractNode(AutomationElement element, double wl, double wt, double wr, double wb, bool hasWindowRect, bool browserChrome, int depth, List<RankedLine> rows)
    {
        try
        {
            ControlType type = null;
            try { type = element.Current.ControlType; } catch { }
            if (type == ControlType.MenuBar || type == ControlType.Menu || type == ControlType.MenuItem || type == ControlType.Separator || type == ControlType.Image)
            {
                return false;
            }
            if (browserChrome && (type == ControlType.Tab || type == ControlType.ToolBar)) return false;
            bool visible = true;
            try
            {
                if (element.Current.IsOffscreen) visible = false;
                else
                {
                    System.Windows.Rect frame = element.Current.BoundingRectangle;
                    if (frame.Width >= 4 && frame.Height >= 4 && !IntersectsWindow(wl, wt, wr, wb, hasWindowRect, frame)) visible = false;
                }
            }
            catch { }
            if (!visible) return true;

            try
            {
                object textPattern;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out textPattern))
                {
                    TextPatternRange[] selected = ((TextPattern)textPattern).GetSelection();
                    if (selected != null && selected.Length > 0)
                    {
                        string selectedText = selected[0].GetText(800);
                        if (!string.IsNullOrWhiteSpace(selectedText) && selectedText.Trim().Length >= 2 && !IsJunkLine(selectedText.Trim()))
                        {
                            rows.Add(new RankedLine(0, "Selected: " + selectedText.Trim()));
                        }
                    }
                }
            }
            catch { }

            string value = null;
            try
            {
                object pattern;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                {
                    value = ((ValuePattern)pattern).Current.Value;
                }
                else if (!browserChrome && element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                {
                    value = ((TextPattern)pattern).DocumentRange.GetText(4000);
                }
            }
            catch { }

            bool editable = type == ControlType.Edit || type == ControlType.ComboBox || type == ControlType.Document;
            if (!string.IsNullOrWhiteSpace(value) && !IsLowValue(value.Trim()) && !IsJunkLine(value.Trim()))
            {
                if (editable || value.Trim().Length >= 6) rows.Add(new RankedLine(editable ? 1 : 2, value.Trim()));
            }
            string name = null;
            try { name = element.Current.Name; } catch { }
            if (!string.IsNullOrWhiteSpace(name) && name.Trim().Length >= 4 && !IsJunkLine(name.Trim()))
            {
                bool buttonLike = type == ControlType.Button || type == ControlType.SplitButton || type == ControlType.CheckBox || type == ControlType.RadioButton;
                if (!buttonLike || name.Trim().Contains(" ") || name.Trim().Length >= 10)
                {
                    rows.Add(new RankedLine(buttonLike ? 4 : 3, name.Trim()));
                }
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    static void CollectText(AutomationElement element, double wl, double wt, double wr, double wb, bool hasWindowRect, bool browserChrome, DateTime deadline, ref int budget, int depth, List<RankedLine> rows)
    {
        if (DateTime.UtcNow > deadline || budget <= 0 || element == null) return;
        budget -= 1;
        if (!ExtractNode(element, wl, wt, wr, wb, hasWindowRect, browserChrome, depth, rows)) return;
        Queue<KeyValuePair<AutomationElement, int>> queue = new Queue<KeyValuePair<AutomationElement, int>>();
        foreach (AutomationElement child in ChildrenOf(element))
        {
            queue.Enqueue(new KeyValuePair<AutomationElement, int>(child, depth + 1));
        }
        while (queue.Count > 0 && budget > 0 && DateTime.UtcNow <= deadline)
        {
            KeyValuePair<AutomationElement, int> current = queue.Dequeue();
            if (current.Value > 24) continue;
            budget -= 1;
            if (!ExtractNode(current.Key, wl, wt, wr, wb, hasWindowRect, browserChrome, current.Value, rows)) continue;
            ControlType currentType = null;
            try { currentType = current.Key.Current.ControlType; } catch { }
            if (currentType == ControlType.Button || currentType == ControlType.SplitButton
                || currentType == ControlType.CheckBox || currentType == ControlType.RadioButton)
            {
                continue;
            }
            foreach (AutomationElement child in ChildrenOf(current.Key))
            {
                queue.Enqueue(new KeyValuePair<AutomationElement, int>(child, current.Value + 1));
            }
        }
    }

    static string CleanWindowText(List<RankedLine> rows, string windowName, string owner)
    {
        HashSet<string> skipExact = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(windowName)) skipExact.Add(windowName.Trim());
        if (!string.IsNullOrWhiteSpace(owner)) skipExact.Add(owner.Trim());
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        List<string> kept = new List<string>();
        rows.Sort(delegate(RankedLine a, RankedLine b) { return a.Rank.CompareTo(b.Rank); });
        foreach (RankedLine row in rows)
        {
            string[] parts = Regex.Split(row.Text ?? "", @"\r\n|\n|\r");
            foreach (string raw in parts)
            {
                string line = raw.Trim();
                if (line.Length < 2) continue;
                if (skipExact.Contains(line)) continue;
                if (IsJunkLine(line)) continue;
                if (!seen.Add(line)) continue;
                kept.Add(line);
                if (kept.Count >= 180) return string.Join("\n", kept.ToArray());
            }
        }
        return string.Join("\n", kept.ToArray());
    }

    static void ActivateDshDesktop()
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            string owner, title;
            uint pid;
            Describe(hwnd, out owner, out title, out pid);
            if (!IsDshProcess(owner, title)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) return;
        ForceForeground(found);
    }

    static void ForceForeground(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
        IntPtr foreground = Native.GetForegroundWindow();
        uint thisThread = Native.GetCurrentThreadId();
        uint targetThread = Native.GetWindowThreadProcessId(hwnd, out _);
        uint foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        Native.AttachThreadInput(thisThread, foregroundThread, true);
        Native.AttachThreadInput(targetThread, foregroundThread, true);
        Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
        Native.BringWindowToTop(hwnd);
        Native.SetForegroundWindow(hwnd);
        Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Native.AttachThreadInput(thisThread, foregroundThread, false);
        Native.AttachThreadInput(targetThread, foregroundThread, false);
    }

    static void RunHotkey(string mode, string[] args)
    {
        if (mode == "carbon" || mode == "win-hotkey")
        {
            int vk = ParseInt(ArgValue(args, "--vk"), Native.VK_SNAPSHOT);
            uint modifiers = (uint)ParseInt(ArgValue(args, "--modifiers"), (int)(Native.MOD_CONTROL | Native.MOD_ALT));
            RunRegisteredHotkey((uint)vk, modifiers);
            return;
        }
        RunBothControlHotkey();
    }

    static void RunBothControlHotkey()
    {
        HookProc = BothControlHook;
        using (Process process = Process.GetCurrentProcess())
        using (ProcessModule module = process.MainModule)
        {
            IntPtr handle = Native.GetModuleHandle(module.ModuleName);
            Hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, HookProc, handle, 0);
        }
        if (Hook == IntPtr.Zero) Fail("failed to install keyboard hook. Run DSH Desktop as a normal desktop user.");
        Emit(new Dictionary<string, object> { { "event", "ready" }, { "mode", "both-control" } });
        Application.Run();
        if (Hook != IntPtr.Zero) Native.UnhookWindowsHookEx(Hook);
    }

    static IntPtr BothControlHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            bool change = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN || msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
            if (change)
            {
                bool leftDown = KeyDown(Native.VK_LCONTROL);
                bool rightDown = KeyDown(Native.VK_RCONTROL);
                if (leftDown && rightDown)
                {
                    if (BothControlArmed)
                    {
                        BothControlArmed = false;
                        Emit(new Dictionary<string, object> { { "event", "hotkey" } });
                    }
                }
                else
                {
                    BothControlArmed = true;
                }
            }
        }
        return Native.CallNextHookEx(Hook, nCode, wParam, lParam);
    }

    static bool KeyDown(int vk)
    {
        return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    class HotkeyForm : Form
    {
        public HotkeyForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Emit(new Dictionary<string, object> { { "event", "hotkey" } });
            }
            base.WndProc(ref m);
        }
    }

    static void RunRegisteredHotkey(uint vk, uint modifiers)
    {
        HotkeyForm form = new HotkeyForm();
        if (!Native.RegisterHotKey(form.Handle, 1, modifiers, vk))
        {
            Fail("failed to register hotkey. It may already be in use.");
        }
        form.FormClosed += (_, __) => Native.UnregisterHotKey(form.Handle, 1);
        Emit(new Dictionary<string, object>
        {
            { "event", "ready" },
            { "mode", "win-hotkey" },
            { "vk", (int)vk },
            { "modifiers", (int)modifiers },
        });
        Application.Run(form);
    }

    static Dictionary<string, object> Ok(Dictionary<string, object> extra)
    {
        extra["ok"] = true;
        return extra;
    }

    static Dictionary<string, object> Err(string message)
    {
        return new Dictionary<string, object> { { "ok", false }, { "error", message } };
    }

    static void Emit(Dictionary<string, object> payload)
    {
        Console.WriteLine(ToJson(payload));
        Console.Out.Flush();
    }

    static string ToJson(object value)
    {
        if (value == null) return "null";
        if (value is bool) return ((bool)value) ? "true" : "false";
        if (value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is float || value is double || value is decimal)
        {
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (value is string) return Quote((string)value);
        Dictionary<string, object> map = value as Dictionary<string, object>;
        if (map != null)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> pair in map)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Quote(pair.Key));
                sb.Append(':');
                sb.Append(ToJson(pair.Value));
            }
            sb.Append('}');
            return sb.ToString();
        }
        return Quote(Convert.ToString(value));
    }

    static string Quote(string value)
    {
        if (value == null) return "null";
        StringBuilder sb = new StringBuilder();
        sb.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32) sb.AppendFormat("\\u{0:x4}", (int)ch);
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    static void Fail(string message)
    {
        Emit(Err(message));
        Environment.Exit(1);
    }

    static bool HasFlag(string[] args, string name)
    {
        foreach (string arg in args) if (arg == name) return true;
        return false;
    }

    static string ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }

    static int ParseInt(string value, int fallback)
    {
        int parsed;
        return int.TryParse(value, out parsed) ? parsed : fallback;
    }
}
