using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

    static readonly Dictionary<ControlType, string> RoleLabels = new Dictionary<ControlType, string>
    {
        { ControlType.Window, "标准窗口" },
        { ControlType.Pane, "container" },
        { ControlType.Group, "container" },
        { ControlType.Document, "HTML 内容" },
        { ControlType.Button, "按钮" },
        { ControlType.Hyperlink, "link" },
        { ControlType.Text, "文本" },
        { ControlType.List, "内容列表" },
        { ControlType.Header, "标题" },
        { ControlType.ToolBar, "工具栏" },
        { ControlType.Menu, "菜单" },
        { ControlType.ComboBox, "组合框" },
        { ControlType.Edit, "文本栏" },
        { ControlType.CheckBox, "复选框" },
        { ControlType.RadioButton, "标签" },
        { ControlType.Tab, "标签" },
        { ControlType.TabItem, "标签" },
        { ControlType.Separator, "分离器" },
        { ControlType.Image, "图像" },
        { ControlType.SplitButton, "弹出式按钮" },
        { ControlType.DataItem, "row" },
        { ControlType.Tree, "外框" },
        { ControlType.TreeItem, "row" },
        { ControlType.Slider, "滑块" },
        { ControlType.DataGrid, "表格" },
        { ControlType.Table, "表格" },
        { ControlType.HeaderItem, "列标题" },
        { ControlType.Custom, "container" },
        { ControlType.MenuItem, "" },
    };

    static readonly HashSet<ControlType> SkipTypes = new HashSet<ControlType>
    {
        ControlType.MenuBar, ControlType.TitleBar,
    };

    static readonly HashSet<ControlType> ChromeTypes = new HashSet<ControlType>
    {
        ControlType.ToolBar, ControlType.Tab,
    };

    static void WindowText(IntPtr hwnd, int maxChars, out string text, out bool truncated)
    {
        List<string> lines = new List<string>();
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(3500);
        int budget = 8000;
        string owner, title;
        uint pid;
        Describe(hwnd, out owner, out title, out pid);
        _ = owner;
        string focused = "";
        try
        {
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            if (root != null)
            {
                HashSet<string> seenChrome = new HashSet<string>();
                DumpTree(root, 0, deadline, ref budget, lines, seenChrome);
                try
                {
                    AutomationElement focusedEl = root.Current.HasKeyboardFocus
                        ? root
                        : root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));
                    if (focusedEl != null)
                    {
                        ControlType type = null;
                        try { type = focusedEl.Current.ControlType; } catch { }
                        if (type != ControlType.Window)
                        {
                            focused = NodeLine(focusedEl);
                        }
                    }
                }
                catch { }
            }
        }
        catch
        {
        }
        string cleaned = string.Join("\n", lines.ToArray());
        if (string.IsNullOrWhiteSpace(cleaned) && !string.IsNullOrWhiteSpace(title)) cleaned = title.Trim();
        if (!string.IsNullOrWhiteSpace(focused))
        {
            cleaned += "\n\nThe focused UI element is " + focused;
        }
        truncated = cleaned.Length > maxChars;
        text = truncated ? cleaned.Substring(0, maxChars) : cleaned;
    }

    static string CompactURL(string raw)
    {
        string value = (raw ?? "").Trim();
        string folded = value.ToLowerInvariant();
        string[] prefixes = { "https://www.", "http://www.", "https://", "http://" };
        foreach (string prefix in prefixes)
        {
            if (folded.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value.Substring(prefix.Length);
            }
        }
        return value;
    }

    static bool LinkHasURL(AutomationElement element)
    {
        try
        {
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                string value = ((ValuePattern)pattern).Current.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string lower = value.ToLowerInvariant();
                    if (lower.StartsWith("http://") || lower.StartsWith("https://") || lower.StartsWith("chrome://"))
                    {
                        return true;
                    }
                    if (value.Contains("/") || value.Contains(".")) return true;
                }
            }
        }
        catch { }
        return false;
    }

    static string RoleDescription(AutomationElement element, ControlType type)
    {
        if (type == ControlType.Hyperlink)
        {
            // Codex: URL-bearing web links → "link"; named in-page links → "链接".
            return LinkHasURL(element) ? "link" : "链接";
        }
        if (type == ControlType.Separator)
        {
            string name = null;
            try { name = element.Current.Name; } catch { }
            if (!string.IsNullOrWhiteSpace(name)) return "分离器";
            return "container";
        }
        if (type != null && RoleLabels.ContainsKey(type)) return RoleLabels[type];
        try
        {
            string localized = element.Current.LocalizedControlType;
            if (!string.IsNullOrWhiteSpace(localized)) return localized;
        }
        catch { }
        return type == null ? "container" : type.LocalizedControlType;
    }

    static string ChromeFingerprint(AutomationElement element, ControlType type)
    {
        string name = null;
        try { name = element.Current.Name; } catch { }
        if (name != null) name = name.Trim();
        else name = "";
        if (type != null && ChromeTypes.Contains(type))
        {
            return type.ProgrammaticName + "|" + name;
        }
        if (name == "标签页搜索" || name == "新标签页" || name == "打开 Chrome 中的 Gemini")
        {
            return (type == null ? "control" : type.ProgrammaticName) + "|" + name;
        }
        return null;
    }

    static string NodeLine(AutomationElement element)
    {
        ControlType type = null;
        try { type = element.Current.ControlType; } catch { }
        string roleDesc = RoleDescription(element, type);
        string name = null;
        try { name = element.Current.Name; } catch { }
        if (!string.IsNullOrWhiteSpace(name)) name = name.Trim();
        else name = null;
        string help = null;
        try { help = element.Current.HelpText; } catch { }
        if (!string.IsNullOrWhiteSpace(help)) help = help.Trim();
        else help = null;
        string value = null;
        try
        {
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                value = ((ValuePattern)pattern).Current.Value;
            }
        }
        catch { }
        if (!string.IsNullOrWhiteSpace(value)) value = value.Trim();
        else value = null;

        List<string> states = new List<string>();
        try
        {
            object selection;
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out selection)
                && ((SelectionItemPattern)selection).Current.IsSelected)
            {
                states.Add("selected");
            }
        }
        catch { }
        bool settable = false;
        try
        {
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                settable = !((ValuePattern)pattern).Current.IsReadOnly;
            }
        }
        catch { }
        if (settable) states.Add("settable");
        bool booleanLike = type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.TabItem
            || value == "on" || value == "off" || value == "0" || value == "1" || value == "true" || value == "false";
        if (settable && booleanLike) states.Add("boolean");

        string head = roleDesc ?? "";
        if (states.Count > 0) head += " (" + string.Join(", ", states.ToArray()) + ")";
        if (name != null) head += string.IsNullOrEmpty(head) ? name : " " + name;

        List<string> extras = new List<string>();
        if (value != null && value != name && value.Length <= 500)
        {
            extras.Add("Value: " + CompactURL(value));
        }
        if (help != null && help != name)
        {
            extras.Add("Help: " + help);
        }
        if (extras.Count == 0) return head;
        if (name == null) return (head + " " + string.Join(", ", extras.ToArray())).Trim();
        return head + ", " + string.Join(", ", extras.ToArray());
    }

    static List<AutomationElement> ChildrenOf(AutomationElement element)
    {
        List<AutomationElement> children = new List<AutomationElement>();
        try
        {
            AutomationElement child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            int count = 0;
            int cap = 200;
            while (child != null && count < cap)
            {
                children.Add(child);
                child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                count++;
            }
        }
        catch { }
        return children;
    }

    static bool IsUnlabeled(AutomationElement element)
    {
        string name = null;
        try { name = element.Current.Name; } catch { }
        string help = null;
        try { help = element.Current.HelpText; } catch { }
        return string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(help);
    }

    static bool IsUnlabeledWrapper(AutomationElement element, ControlType type)
    {
        _ = element;
        _ = type;
        return false;
    }

    static bool MenuItemHasSubmenu(AutomationElement item)
    {
        foreach (AutomationElement child in ChildrenOf(item))
        {
            ControlType childType = null;
            try { childType = child.Current.ControlType; } catch { }
            if (childType == ControlType.Menu) return true;
        }
        return false;
    }

    static bool ItemHasDirectLink(AutomationElement item)
    {
        foreach (AutomationElement child in ChildrenOf(item))
        {
            ControlType childType = null;
            try { childType = child.Current.ControlType; } catch { }
            if (childType == ControlType.Menu) continue;
            if (childType == ControlType.Hyperlink) return true;
            if ((childType == ControlType.Pane || childType == ControlType.Group || childType == ControlType.Custom)
                && IsUnlabeled(child) && ItemHasDirectLink(child))
            {
                return true;
            }
        }
        return false;
    }

    static bool IsLeafyMenu(AutomationElement menu)
    {
        int items = 0;
        int linked = 0;
        foreach (AutomationElement child in ChildrenOf(menu))
        {
            ControlType childType = null;
            try { childType = child.Current.ControlType; } catch { }
            if (childType != ControlType.MenuItem) continue;
            items++;
            if (ItemHasDirectLink(child)) linked++;
        }
        if (items == 0) return false;
        return linked >= 1 && linked * 2 >= items;
    }

    static bool ShouldHoistMenu(AutomationElement element, ControlType type, ControlType parentType, bool inLeafMenu)
    {
        if (type != ControlType.Menu || !IsUnlabeled(element)) return false;
        if (!IsLeafyMenu(element))
        {
            return parentType == ControlType.Menu || parentType == ControlType.MenuItem;
        }
        return parentType == ControlType.MenuItem && inLeafMenu;
    }

    static void DumpTree(AutomationElement element, int depth, DateTime deadline, ref int budget, List<string> lines, HashSet<string> seenChrome)
    {
        DumpTree(element, depth, deadline, ref budget, lines, seenChrome, null, false);
    }

    static void DumpTree(AutomationElement element, int depth, DateTime deadline, ref int budget, List<string> lines, HashSet<string> seenChrome, ControlType parentType, bool inLeafMenu)
    {
        if (DateTime.UtcNow > deadline || budget <= 0 || element == null) return;
        ControlType type = null;
        try { type = element.Current.ControlType; } catch { }
        if (type != null && SkipTypes.Contains(type)) return;
        List<AutomationElement> kids = ChildrenOf(element);
        string chromeKey = ChromeFingerprint(element, type);
        if (chromeKey != null && !seenChrome.Add(chromeKey)) return;
        if ((type == ControlType.Pane || type == ControlType.Group || type == ControlType.Custom)
            && IsUnlabeled(element) && kids.Count == 0)
        {
            return;
        }
        if (IsUnlabeledWrapper(element, type) || ShouldHoistMenu(element, type, parentType, inLeafMenu))
        {
            foreach (AutomationElement child in kids)
            {
                DumpTree(child, depth, deadline, ref budget, lines, seenChrome, parentType, inLeafMenu);
            }
            return;
        }
        budget -= 1;
        lines.Add(new string('\t', depth) + NodeLine(element));
        // Collapse URL-bearing web links; expand named 「链接」 (e.g. 复制code).
        if (type == ControlType.Hyperlink && LinkHasURL(element)) return;
        bool nextInLeafMenu = inLeafMenu || (type == ControlType.Menu && IsLeafyMenu(element));
        foreach (AutomationElement child in kids)
        {
            ControlType childType = null;
            try { childType = child.Current.ControlType; } catch { }
            if ((type == ControlType.DataGrid || type == ControlType.Table)
                && (childType == ControlType.HeaderItem
                    || ((childType == ControlType.Pane || childType == ControlType.Group || childType == ControlType.Custom)
                        && IsUnlabeled(child) && ChildrenOf(child).Count == 0)))
            {
                continue;
            }
            bool siblingMenu = type == ControlType.MenuItem
                && childType == ControlType.Menu
                && IsUnlabeled(child);
            DumpTree(child, siblingMenu ? depth : depth + 1, deadline, ref budget, lines, seenChrome, type, nextInLeafMenu);
        }
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
