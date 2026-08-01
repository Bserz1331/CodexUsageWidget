using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Codex Usage Widget")]
[assembly: AssemblyProduct("Codex Usage Widget")]
[assembly: AssemblyVersion("2.3.2.0")]
[assembly: AssemblyFileVersion("2.3.2.0")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CodexUsageWidget.Tests")]
[assembly: SupportedOSPlatform("windows")]

namespace CodexUsageWidget
{
    internal static class AppLog
    {
        private static readonly object Sync = new object();
        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        public static string LogPath { get { return Path.Combine(DataDir, "widget.log"); } }
        public static void Error(string area, Exception ex)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(LogPath, DateTime.Now.ToString("o") + " [" + area + "] " + ex + Environment.NewLine, Encoding.UTF8);
                    FileInfo f = new FileInfo(LogPath);
                    if (f.Length > 1024 * 1024)
                    {
                        string old = LogPath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(LogPath, old);
                    }
                }
            }
            catch { }
        }
    }

    internal sealed class UsageInfo
    {
        public double UsedPercent;
        public int WindowMinutes;
        public DateTime ResetAt;
        public DateTime CapturedAt;
        public string Plan;
        public double? ShortUsedPercent;
        public int? ShortWindowMinutes;
        public DateTime SourceFileModifiedAt;
    }

    internal static class UsageReader
    {
        private static readonly object CacheLock = new object();
        private static UsageInfo cachedLatest;
        private static bool cacheInitialized;

        public static UsageInfo ReadLatest(string preferredPath, out string failureReason)
        {
            failureReason = "";
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex", "sessions");
            if (!Directory.Exists(root))
            {
                failureReason = "Codex sessions 資料夾不存在：" + root;
                return null;
            }

            if (!string.IsNullOrEmpty(preferredPath) && File.Exists(preferredPath))
            {
                UsageInfo preferred = ReadFile(preferredPath);
                lock (CacheLock)
                {
                    if (preferred != null && cacheInitialized)
                    {
                        if (cachedLatest == null || preferred.CapturedAt >= cachedLatest.CapturedAt)
                            cachedLatest = preferred;
                        return cachedLatest;
                    }
                }
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(12)
                    .ToArray();
            }
            catch (Exception ex)
            {
                failureReason = "無法列舉 Codex session 檔案：" + ex.Message;
                AppLog.Error("UsageReader.EnumerateFiles", ex);
                return null;
            }

            UsageInfo newest = null;
            foreach (string file in files)
            {
                UsageInfo candidate = ReadFile(file);
                if (candidate != null && (newest == null || candidate.CapturedAt > newest.CapturedAt))
                    newest = candidate;
            }
            lock (CacheLock)
            {
                cacheInitialized = true;
                if (newest != null && (cachedLatest == null || newest.CapturedAt >= cachedLatest.CapturedAt))
                    cachedLatest = newest;
                newest = cachedLatest;
            }
            if (newest == null)
                failureReason = files.Any()
                    ? "候選 session 檔案中找不到有效的週額度資料。可能是格式改變或檔案尚未寫入完成。"
                    : "Codex sessions 資料夾內沒有 JSONL 檔案。";
            return newest;
        }

        private static UsageInfo ReadFile(string file)
        {
            try
            {
                string[] lines = ReadTail(file, 1024 * 1024)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (lines[i].IndexOf("\"rate_limits\"", StringComparison.Ordinal) < 0) continue;
                    UsageInfo info;
                    try { info = ParseLine(lines[i]); }
                    catch { continue; }
                    if (info == null) continue;
                    info.SourceFileModifiedAt = File.GetLastWriteTime(file);
                    return info;
                }
            }
            catch (Exception ex) { AppLog.Error("ReadFile:" + Path.GetFileName(file), ex); }
            return null;
        }

        private static string ReadTail(string path, int maxBytes)
        {
            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, stream.Length - maxBytes);
                stream.Seek(start, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                {
                    if (start > 0) reader.ReadLine();
                    return reader.ReadToEnd();
                }
            }
        }

        internal static UsageInfo ParseLine(string line)
        {
            using (JsonDocument document = JsonDocument.Parse(line))
            {
                JsonElement root = document.RootElement;
                JsonElement payload;
                JsonElement type;
                JsonElement limits;
                if (!root.TryGetProperty("payload", out payload) ||
                    !payload.TryGetProperty("type", out type) ||
                    type.GetString() != "token_count" ||
                    !payload.TryGetProperty("rate_limits", out limits) ||
                    limits.ValueKind != JsonValueKind.Object)
                    return null;

                List<JsonElement> windows = new List<JsonElement>();
                JsonElement candidate;
                if (limits.TryGetProperty("primary", out candidate) && candidate.ValueKind == JsonValueKind.Object)
                    windows.Add(candidate);
                if (limits.TryGetProperty("secondary", out candidate) && candidate.ValueKind == JsonValueKind.Object)
                    windows.Add(candidate);
                if (windows.Count == 0) return null;

                JsonElement? weekly = windows
                    .Where(w => GetInt(w, "window_minutes") >= 10000 && GetInt(w, "window_minutes") <= 10200)
                    .Select(w => (JsonElement?)w)
                    .FirstOrDefault();
                if (!weekly.HasValue) return null;

                JsonElement? shortWindow = windows
                    .Where(w => GetInt(w, "window_minutes") < 10000)
                    .OrderBy(w => GetInt(w, "window_minutes"))
                    .Select(w => (JsonElement?)w)
                    .FirstOrDefault();

                long resetUnix = GetLong(weekly.Value, "resets_at");
                DateTime reset = resetUnix > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(resetUnix).LocalDateTime
                    : DateTime.MinValue;
                JsonElement timestamp;
                DateTime captured;
                string timestampText = root.TryGetProperty("timestamp", out timestamp) ? timestamp.GetString() : "";
                if (!DateTime.TryParse(timestampText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out captured))
                    return null;

                UsageInfo result = new UsageInfo();
                if (!TryGetDouble(weekly.Value, "used_percent", out result.UsedPercent))
                    return null;
                if (double.IsNaN(result.UsedPercent) ||
                    result.UsedPercent < 0 || result.UsedPercent > 100)
                    return null;
                result.WindowMinutes = GetInt(weekly.Value, "window_minutes");
                result.ResetAt = reset;
                result.CapturedAt = captured.ToLocalTime();
                result.Plan = GetString(limits, "plan_type");
                if (shortWindow.HasValue)
                {
                    result.ShortUsedPercent = GetDouble(shortWindow.Value, "used_percent");
                    result.ShortWindowMinutes = GetInt(shortWindow.Value, "window_minutes");
                }
                return result;
            }
        }

        private static string GetString(JsonElement source, string key)
        {
            JsonElement value;
            return source.TryGetProperty(key, out value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : "";
        }
        private static int GetInt(JsonElement source, string key)
        {
            JsonElement value;
            return source.TryGetProperty(key, out value) && value.TryGetInt32(out int result) ? result : 0;
        }
        private static long GetLong(JsonElement source, string key)
        {
            JsonElement value;
            return source.TryGetProperty(key, out value) && value.TryGetInt64(out long result) ? result : 0;
        }
        private static double GetDouble(JsonElement source, string key)
        {
            JsonElement value;
            return source.TryGetProperty(key, out value) && value.TryGetDouble(out double result) ? result : 0;
        }
        private static bool TryGetDouble(JsonElement source, string key, out double result)
        {
            JsonElement value;
            result = 0;
            return source.TryGetProperty(key, out value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out result);
        }
    }

    internal sealed class WidgetSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false
        };
        public int NormalMinutes = 1;
        public int IdleAfterMinutes = 10;
        public int IdleMinutes = 10;
        public int OpacityPercent = 95;
        public bool Locked;
        public bool ClickThrough;
        public bool ShowWidget = true;
        public int X = int.MinValue;
        public int Y = int.MinValue;
        public int HistoryRetentionDays = 90;
        public string UpdateManifestUrl = "";
        public DateTime LastUpdateCheckUtc = DateTime.MinValue;

        internal bool Normalize()
        {
            bool changed = false;
            changed |= NormalizeInt(ref NormalMinutes, 1, 1440, 1);
            changed |= NormalizeInt(ref IdleAfterMinutes, 1, 1440, 10);
            changed |= NormalizeInt(ref IdleMinutes, 1, 1440, 10);
            changed |= NormalizeInt(ref OpacityPercent, 20, 100, 95);
            changed |= NormalizeInt(ref HistoryRetentionDays, 1, 3650, 90);
            if (UpdateManifestUrl == null) { UpdateManifestUrl = ""; changed = true; }
            return changed;
        }

        private static bool NormalizeInt(ref int value, int minimum, int maximum, int fallback)
        {
            if (value >= minimum && value <= maximum) return false;
            value = fallback;
            return true;
        }

        internal static WidgetSettings ParseWithBackup(string primaryJson, string backupJson)
        {
            WidgetSettings value = TryParse(primaryJson) ?? TryParse(backupJson) ?? new WidgetSettings();
            value.Normalize();
            return value;
        }

        private static WidgetSettings TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<WidgetSettings>(json, JsonOptions); }
            catch { return null; }
        }

        private static string PathName
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        public static WidgetSettings Load()
        {
            try
            {
                if (!File.Exists(PathName)) return new WidgetSettings();
                WidgetSettings value = JsonSerializer.Deserialize<WidgetSettings>(
                    File.ReadAllText(PathName, Encoding.UTF8), JsonOptions) ?? new WidgetSettings();
                value.Normalize();
                return value;
            }
            catch (Exception ex)
            {
                AppLog.Error("Settings.Load", ex);
                try
                {
                    string backup = PathName + ".bak";
                    if (File.Exists(backup))
                    {
                        WidgetSettings value = JsonSerializer.Deserialize<WidgetSettings>(
                            File.ReadAllText(backup, Encoding.UTF8), JsonOptions);
                        if (value != null) { value.Normalize(); return value; }
                    }
                }
                catch (Exception backupEx) { AppLog.Error("Settings.LoadBackup", backupEx); }
                return new WidgetSettings();
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, JsonOptions);
                string temporary = PathName + ".tmp";
                string backup = PathName + ".bak";
                File.WriteAllText(temporary, json, Encoding.UTF8);
                if (File.Exists(PathName))
                    File.Replace(temporary, PathName, backup, true);
                else
                    File.Move(temporary, PathName);
            }
            catch (Exception ex) { AppLog.Error("Settings.Save", ex); }
        }
    }

    internal static class WidgetUiPolicy
    {
        internal static Version ParseReleaseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string value = tag.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            return Version.TryParse(value, out Version version) ? version : null;
        }

        internal static bool ShouldRecoverUpdate(DateTime now, bool checkRunning,
            DateTime checkStartedAt, DateTime lastCompletedAt, int fallbackMinutes)
        {
            TimeSpan timeout = TimeSpan.FromMinutes(Math.Max(2, fallbackMinutes * 2));
            if (checkRunning && checkStartedAt != DateTime.MinValue && now - checkStartedAt > timeout)
                return true;
            return !checkRunning && lastCompletedAt != DateTime.MinValue && now - lastCompletedAt > timeout;
        }

        internal static Color QuotaColor(double remaining)
        {
            if (remaining < 10) return Color.FromArgb(239, 68, 68);
            if (remaining < 30) return Color.FromArgb(245, 158, 11);
            return Color.FromArgb(34, 197, 94);
        }

        internal static Point SafeLocation(int savedX, int savedY, Size windowSize,
            IReadOnlyList<Rectangle> workingAreas, Rectangle primaryArea)
        {
            Point fallback = new Point(primaryArea.Right - windowSize.Width - 20,
                primaryArea.Bottom - windowSize.Height - 20);
            if (savedX == int.MinValue || savedY == int.MinValue) return fallback;
            Rectangle proposed = new Rectangle(savedX, savedY, windowSize.Width, windowSize.Height);
            foreach (Rectangle area in workingAreas)
            {
                Rectangle intersection = Rectangle.Intersect(area, proposed);
                if (intersection.Width >= Math.Min(40, windowSize.Width) &&
                    intersection.Height >= Math.Min(20, windowSize.Height))
                    return new Point(savedX, savedY);
            }
            return fallback;
        }
    }

    internal sealed class UsageBar : Control
    {
        public double Value;
        public Color FillColor = Color.FromArgb(34, 197, 94);
        public UsageBar() { DoubleBuffered = true; Height = 10; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 1, Width - 1, Height - 2);
            using (GraphicsPath path = Rounded(r, 5))
            using (SolidBrush back = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                e.Graphics.FillPath(back, path);
            int usedWidth = Math.Max(0, Math.Min(Width, (int)Math.Round(Width * Value / 100.0)));
            if (usedWidth > 0)
            {
                Rectangle fillRect = new Rectangle(0, 1, usedWidth, Height - 2);
                using (GraphicsPath path = Rounded(fillRect, 5))
                using (SolidBrush fill = new SolidBrush(FillColor))
                    e.Graphics.FillPath(fill, path);
            }
        }
        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal sealed class SettingsDialog : Form
    {
        private readonly NumericUpDown normal;
        private readonly NumericUpDown idleAfter;
        private readonly NumericUpDown idle;
        private readonly TrackBar opacity;
        private readonly Label opacityValue;
        public bool Saved;

        public SettingsDialog(WidgetSettings settings)
        {
            Text = "浮窗設定";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(380, 265);
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            Font = new Font("Microsoft JhengHei UI", 10F);

            normal = AddNumber("平常更新頻率（分鐘）", 24, settings.NormalMinutes, 1, 1440);
            idleAfter = AddNumber("連續無變化多久後降頻（分鐘）", 76, settings.IdleAfterMinutes, 1, 1440);
            idle = AddNumber("無變化時更新頻率（分鐘）", 128, settings.IdleMinutes, 1, 1440);

            Label opacityLabel = NewLabel("透明度", 24, 180, 90, 24);
            Controls.Add(opacityLabel);
            opacity = new TrackBar { Minimum = 20, Maximum = 100, TickFrequency = 10, Value = Math.Max(20, Math.Min(100, settings.OpacityPercent)) };
            opacity.SetBounds(105, 174, 205, 38);
            Controls.Add(opacity);
            opacityValue = NewLabel(opacity.Value + "%", 315, 180, 48, 24);
            Controls.Add(opacityValue);
            opacity.ValueChanged += delegate { opacityValue.Text = opacity.Value + "%"; };

            Button cancel = NewButton("取消", 190, 222, 78, 30);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            Button save = NewButton("儲存", 278, 222, 78, 30);
            save.Click += delegate
            {
                settings.NormalMinutes = (int)normal.Value;
                settings.IdleAfterMinutes = (int)idleAfter.Value;
                settings.IdleMinutes = (int)idle.Value;
                settings.OpacityPercent = opacity.Value;
                settings.Save();
                Saved = true;
                Close();
            };
            Controls.Add(save);
        }

        private NumericUpDown AddNumber(string text, int y, int value, int min, int max)
        {
            Controls.Add(NewLabel(text, 24, y + 5, 265, 24));
            NumericUpDown n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)) };
            n.SetBounds(295, y, 62, 28);
            Controls.Add(n);
            return n;
        }
        private Label NewLabel(string text, int x, int y, int w, int h)
        {
            Label l = new Label { Text = text, ForeColor = Color.FromArgb(220, 220, 220), AutoSize = false };
            l.SetBounds(x, y, w, h); return l;
        }
        private Button NewButton(string text, int x, int y, int w, int h)
        {
            Button b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(53, 53, 53), ForeColor = Color.White };
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80); b.SetBounds(x, y, w, h); return b;
        }
    }

    internal sealed class UsageForm : Form
    {
        private readonly Label remainingLabel;
        private readonly Label resetLabel;
        private readonly Label updatedLabel;
        private readonly UsageBar usageBar;
        private readonly Timer uiTimer;
        private readonly Timer eventDebounceTimer;
        private readonly System.Threading.Timer healthTimer;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem lockItem;
        private readonly ToolStripMenuItem clickThroughItem;
        private readonly ToolStripMenuItem showWidgetItem;
        private readonly WidgetSettings settings;
        private readonly Font titlePaintFont;
        private readonly Font bodyPaintFont;
        private FileSystemWatcher watcher;
        private bool reallyExit;
        private bool dragging;
        private Point dragCursorOrigin;
        private Point dragWindowOrigin;
        private UsageInfo currentInfo;
        private double? lastPercent;
        private DateTime lastChangedAt = DateTime.Now;
        private DateTime nextCheckAt = DateTime.MinValue;
        private string lastActivitySignature = "";
        private string lastSavedSnapshotSignature = "";
        private string pendingChangedPath;
        private bool checkRunning;
        private bool checkPending;
        private int checkGeneration;
        private DateTime checkStartedAt = DateTime.MinValue;
        private DateTime lastCheckCompletedAt = DateTime.MinValue;
        private DateTime lastSessionEventAt = DateTime.MinValue;
        private bool recoveringUpdatePipeline;
        private int lastScheduledCheckMinutes = 1;
        private bool updateCheckRunning;
        private string pendingReleaseUrl;
        private int consecutiveReadFailures;
        private int lastTrayDisplay = int.MinValue;
        private DateTime lastHistoryCleanup = DateTime.MinValue;
        private DateTime watcherRetryAt = DateTime.MinValue;
        private DateTime quotaStallStartedAt = DateTime.MinValue;
        private DateTime lastObservedQuotaAt = DateTime.MinValue;
        private DateTime lastObservedFileAt = DateTime.MinValue;
        private string lastReadFailureReason = "";
        private bool quotaStallLogged;
        private bool historyCleanupRunning;
        private readonly object historyLock = new object();
        private readonly int showMessage;
        private const string LatestReleaseApi = "https://api.github.com/repos/Bserz1331/CodexUsageWidget/releases/latest";
        private const string ReleasesBaseUrl = "https://github.com/Bserz1331/CodexUsageWidget/releases/";
        private static readonly HttpClient UpdateClient = CreateUpdateClient();

        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x20;
        private const int WsExLayered = 0x80000;
        private const int WsExToolWindow = 0x80;
        private const int WsExAppWindow = 0x40000;
        private const int HwndBroadcast = 0xffff;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string value);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int valueSize);
        private const int DwmWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;

        public UsageForm()
        {
            settings = WidgetSettings.Load();
            settings.Save();
            RepairAutoStartPath();
            Text = "Codex Usage Widget";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(33, 33, 33);
            ForeColor = Color.White;
            ClientSize = new Size(212, 42);
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            AutoScaleMode = AutoScaleMode.None;
            if (settings.OpacityPercent < 100)
                Opacity = Math.Max(.2, settings.OpacityPercent / 100.0);
            SetSavedLocation();
            titlePaintFont = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
            bodyPaintFont = new Font("Microsoft JhengHei UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel);

            Label title = MakeLabel("每週使用上限", 12F, Color.FromArgb(240, 240, 240), FontStyle.Bold);
            title.SetBounds(11, 3, 86, 17); Controls.Add(title);
            resetLabel = MakeLabel("（讀取中）", 11F, Color.FromArgb(160, 160, 160), FontStyle.Regular);
            resetLabel.SetBounds(97, 3, 105, 17); Controls.Add(resetLabel);

            usageBar = new UsageBar();
            usageBar.SetBounds(11, 24, 116, 9); Controls.Add(usageBar);
            remainingLabel = MakeLabel("剩餘 --%", 11F, Color.FromArgb(220, 220, 220), FontStyle.Regular);
            remainingLabel.TextAlign = ContentAlignment.MiddleLeft;
            remainingLabel.SetBounds(138, 18, 65, 19); Controls.Add(remainingLabel);
            title.Visible = false;
            resetLabel.Visible = false;
            remainingLabel.Visible = false;
            updatedLabel = MakeLabel("", 7.5F, Color.FromArgb(135, 135, 135), FontStyle.Regular);
            updatedLabel.TextAlign = ContentAlignment.MiddleRight;
            updatedLabel.SetBounds(244, 23, 188, 22); Controls.Add(updatedLabel);
            updatedLabel.Visible = false;

            MouseDown += BeginDrag;
            foreach (Control c in Controls)
            {
                c.MouseDown += BeginDrag;
                c.MouseEnter += delegate
                {
                    if (!settings.ClickThrough && settings.OpacityPercent < 100) Opacity = 1;
                };
                c.MouseLeave += delegate { if (!dragging && !ClientRectangle.Contains(PointToClient(Cursor.Position))) ApplyOpacity(); };
            }
            Resize += delegate { ApplyRegion(); };
            MouseEnter += delegate
            {
                if (!settings.ClickThrough && settings.OpacityPercent < 100) Opacity = 1;
            };
            MouseLeave += delegate { if (!dragging && !ClientRectangle.Contains(PointToClient(Cursor.Position))) ApplyOpacity(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            lockItem = new ToolStripMenuItem();
            lockItem.Checked = settings.Locked;
            UpdateLockText();
            lockItem.Click += delegate { ToggleLock(); };
            menu.Items.Add(lockItem);
            clickThroughItem = new ToolStripMenuItem("滑鼠穿透");
            clickThroughItem.Checked = settings.ClickThrough;
            clickThroughItem.Click += delegate { ToggleClickThrough(); };
            menu.Items.Add(clickThroughItem);
            menu.Items.Add("設定更新頻率與透明度…", null, delegate { ShowSettings(); });
            ToolStripMenuItem opacityMenu = new ToolStripMenuItem("透明度");
            foreach (int value in new[] { 50, 75, 90, 100 })
            {
                int selected = value;
                opacityMenu.DropDownItems.Add(value + "%", null, delegate
                {
                    settings.OpacityPercent = selected; settings.Save(); ApplyOpacity();
                });
            }
            menu.Items.Add(opacityMenu);
            menu.Items.Add("立即更新", null, delegate { nextCheckAt = DateTime.MinValue; CheckUsage(); });
            menu.Items.Add("官方使用量頁面", null, delegate { OpenDashboard(); });
            menu.Items.Add("支持開發…", null, delegate
            {
                using SupportDialog dialog = new SupportDialog();
                dialog.ShowDialog(this);
            });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem startItem = new ToolStripMenuItem("開機啟動");
            startItem.Checked = IsAutoStartEnabled();
            startItem.Click += delegate { startItem.Checked = !startItem.Checked; SetAutoStart(startItem.Checked); };
            menu.Items.Add(startItem);
            menu.Items.Add("複製診斷資訊", null, delegate { CopyDiagnostics(); });
            menu.Items.Add("檢查更新", null, delegate { _ = CheckForUpdatesAsync(true); });
            showWidgetItem = new ToolStripMenuItem("顯示浮窗");
            showWidgetItem.Checked = settings.ShowWidget;
            showWidgetItem.Click += delegate { SetWidgetVisible(!settings.ShowWidget); };
            menu.Items.Add(showWidgetItem);
            menu.Items.Add("結束", null, delegate { reallyExit = true; Close(); });
            ContextMenuStrip = menu;

            tray = new NotifyIcon { Text = "Codex Usage Widget", Icon = CreateTrayIcon(), ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowFromTray(); };
            tray.BalloonTipClicked += delegate
            {
                if (!string.IsNullOrEmpty(pendingReleaseUrl)) OpenUrl(pendingReleaseUrl);
            };

            uiTimer = new Timer { Interval = 1000 };
            uiTimer.Tick += delegate
            {
                if (watcher == null && DateTime.Now >= watcherRetryAt) StartWatcher();
                CheckUsage();
                UpdateAgeText();
                if (DateTime.Now - lastHistoryCleanup >= TimeSpan.FromDays(1)) CleanHistory();
            };
            eventDebounceTimer = new Timer { Interval = 600 };
            eventDebounceTimer.Tick += delegate { eventDebounceTimer.Stop(); nextCheckAt = DateTime.MinValue; CheckUsage(); };
            uiTimer.Start();
            healthTimer = new System.Threading.Timer(delegate
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((Action)EnsureUpdateHealth);
                }
                catch { }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            showMessage = RegisterWindowMessage("CodexUsageWidget.ShowExisting.v2");
            Shown += delegate
            {
                ApplyRegion();
                ApplyClickThrough();
                StartWatcher();
                CleanHistory();
                CheckUsage();
                _ = CheckForUpdatesAsync(false);
                if (!settings.ShowWidget) BeginInvoke((Action)delegate { Hide(); });
            };
            FormClosing += OnFormClosing;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExToolWindow;
                cp.ExStyle &= ~WsExAppWindow;
                return cp;
            }
        }
        private void SetSavedLocation()
        {
            Rectangle primary = Screen.PrimaryScreen.WorkingArea;
            Rectangle[] areas = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            Point safe = WidgetUiPolicy.SafeLocation(settings.X, settings.Y, Size, areas, primary);
            if (safe.X != settings.X || safe.Y != settings.Y)
            {
                settings.X = safe.X;
                settings.Y = safe.Y;
            }
            Location = safe;
        }
        private void ApplyRegion()
        {
            Region = null;
            int preference = DwmWindowCornerRound;
            try
            {
                DwmSetWindowAttribute(Handle, DwmWindowCornerPreference,
                    ref preference, sizeof(int));
            }
            catch (Exception ex) { AppLog.Error("DwmRoundedCorners", ex); }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using (Pen p = new Pen(Color.FromArgb(48, 48, 48)))
            using (GraphicsPath path = Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 13))
                e.Graphics.DrawPath(p, path);
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, "每週使用上限", titlePaintFont,
                new Rectangle(11, 3, 86, 17), Color.FromArgb(240, 240, 240), flags);
            TextRenderer.DrawText(e.Graphics, resetLabel.Text, bodyPaintFont,
                new Rectangle(97, 3, 105, 17), resetLabel.ForeColor, flags);
            TextFormatFlags remainingFlags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, remainingLabel.Text, bodyPaintFont,
                new Rectangle(131, 18, 72, 19), remainingLabel.ForeColor, remainingFlags);
        }
        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        private static Label MakeLabel(string text, float size, Color color, FontStyle style)
        {
            return new Label {
                Text = text,
                Font = new Font("Microsoft JhengHei UI", size, style, GraphicsUnit.Pixel),
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = false,
                UseCompatibleTextRendering = false
            };
        }
        private static Icon CreateTrayIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush background = new SolidBrush(Color.FromArgb(32, 32, 32)))
                    g.FillEllipse(background, 1, 1, 30, 30);
                using (Pen pen = new Pen(Color.White, 3))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new[] { new Point(10, 10), new Point(16, 16), new Point(10, 22) });
                    g.DrawLine(pen, 18, 22, 24, 22);
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        private static Icon CreateQuotaTrayIcon(string text, Color color)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);
                Rectangle tile = new Rectangle(1, 1, 30, 30);
                using (GraphicsPath tilePath = Rounded(tile, 6))
                using (SolidBrush background = new SolidBrush(color))
                    g.FillPath(background, tilePath);
                using (Pen border = new Pen(Color.FromArgb(210, 255, 255, 255), 1))
                using (GraphicsPath borderPath = Rounded(new Rectangle(1, 1, 29, 29), 6))
                    g.DrawPath(border, borderPath);
                float fontSize = text.Length >= 3 ? 14F : 18F;
                using (Font font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush,
                        (32 - size.Width) / 2F,
                        (32 - size.Height) / 2F - 1);
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        private void UpdateTrayStatus(double remaining)
        {
            int display = Math.Max(0, Math.Min(100, (int)Math.Round(remaining)));
            tray.Text = "Codex 本週剩餘 " + display + "%";
            if (display == lastTrayDisplay) return;
            Color color = display < 10
                ? Color.FromArgb(220, 38, 38)
                : display < 30 ? Color.FromArgb(234, 88, 12) : Color.FromArgb(22, 163, 74);
            Icon replacement = CreateQuotaTrayIcon(display.ToString(CultureInfo.InvariantCulture), color);
            Icon previous = tray.Icon;
            tray.Icon = replacement;
            if (previous != null) previous.Dispose();
            lastTrayDisplay = display;
        }
        private void UpdateTrayError()
        {
            tray.Text = "Codex 使用量：資料接收錯誤";
            if (lastTrayDisplay == -1) return;
            Icon replacement = CreateQuotaTrayIcon("!", Color.FromArgb(239, 68, 68));
            Icon previous = tray.Icon;
            tray.Icon = replacement;
            if (previous != null) previous.Dispose();
            lastTrayDisplay = -1;
        }
        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (settings.Locked || e.Button != MouseButtons.Left) return;
            dragging = true;
            dragCursorOrigin = Cursor.Position;
            dragWindowOrigin = Location;
            Capture = true;
            Opacity = 1;
            MouseMove += DragMove; MouseUp += EndDrag;
        }
        private void DragMove(object sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            Point cursor = Cursor.Position;
            Location = new Point(
                dragWindowOrigin.X + cursor.X - dragCursorOrigin.X,
                dragWindowOrigin.Y + cursor.Y - dragCursorOrigin.Y);
        }
        private void EndDrag(object sender, MouseEventArgs e)
        {
            MouseMove -= DragMove;
            MouseUp -= EndDrag;
            dragging = false;
            Capture = false;
            KeepWindowVisible();
            SavePosition();
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) ApplyOpacity();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!dragging || Capture) return;
            MouseMove -= DragMove;
            MouseUp -= EndDrag;
            dragging = false;
            KeepWindowVisible();
            SavePosition();
            ApplyOpacity();
        }
        private void KeepWindowVisible()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(area.Right - Width, Left));
            int y = Math.Max(area.Top, Math.Min(area.Bottom - Height, Top));
            Location = new Point(x, y);
        }
        private void SavePosition()
        {
            settings.X = Left; settings.Y = Top; settings.Save();
        }
        private void ToggleLock()
        {
            settings.Locked = !settings.Locked; settings.Save(); lockItem.Checked = settings.Locked; UpdateLockText();
        }
        private void UpdateLockText() { lockItem.Text = settings.Locked ? "解除鎖定位置" : "鎖定位置"; }
        private void ToggleClickThrough()
        {
            settings.ClickThrough = !settings.ClickThrough;
            settings.Save();
            clickThroughItem.Checked = settings.ClickThrough;
            ApplyClickThrough();
        }
        private void ApplyClickThrough()
        {
            int style = GetWindowLong(Handle, GwlExStyle);
            if (settings.ClickThrough) style |= WsExTransparent;
            else style &= ~WsExTransparent;
            SetWindowLong(Handle, GwlExStyle, style);
            ApplyOpacity();
        }
        private void ApplyOpacity()
        {
            double target = Math.Max(.2, Math.Min(1, settings.OpacityPercent / 100.0));
            if (settings.OpacityPercent < 100)
                Opacity = target;
            else if (Opacity != 1)
                Opacity = 1;
            Invalidate();
        }
        private void ShowSettings()
        {
            using (SettingsDialog dialog = new SettingsDialog(settings))
            {
                dialog.ShowDialog(this);
                if (dialog.Saved)
                {
                    ApplyOpacity();
                    nextCheckAt = DateTime.MinValue;
                }
            }
        }
        private void StartWatcher()
        {
            try
            {
                if (watcher != null) watcher.Dispose();
                string sessions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
                if (!Directory.Exists(sessions))
                {
                    watcher = null;
                    watcherRetryAt = DateTime.Now.AddSeconds(10);
                    return;
                }
                watcher = new FileSystemWatcher(sessions, "*.jsonl");
                watcher.IncludeSubdirectories = true;
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
                watcher.Changed += OnSessionChanged;
                watcher.Created += OnSessionChanged;
                watcher.Renamed += delegate(object sender, RenamedEventArgs e)
                {
                    pendingChangedPath = e.FullPath;
                    lastSessionEventAt = DateTime.Now;
                    try { BeginInvoke((Action)delegate { eventDebounceTimer.Stop(); eventDebounceTimer.Start(); }); }
                    catch { }
                };
                watcher.Error += delegate(object sender, ErrorEventArgs e)
                {
                    AppLog.Error("FileSystemWatcher", e.GetException());
                    BeginInvoke((Action)delegate
                    {
                        if (watcher != null) watcher.Dispose();
                        watcher = null;
                        watcherRetryAt = DateTime.Now.AddSeconds(10);
                    });
                };
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("StartWatcher", ex);
                if (watcher != null) watcher.Dispose();
                watcher = null;
                watcherRetryAt = DateTime.Now.AddSeconds(10);
            }
        }
        private void OnSessionChanged(object sender, FileSystemEventArgs e)
        {
            pendingChangedPath = e.FullPath;
            lastSessionEventAt = DateTime.Now;
            try { BeginInvoke((Action)delegate { eventDebounceTimer.Stop(); eventDebounceTimer.Start(); }); }
            catch { }
        }
        private void CheckUsage()
        {
            if (DateTime.Now < nextCheckAt) return;
            if (checkRunning)
            {
                checkPending = true;
                return;
            }

            checkRunning = true;
            checkStartedAt = DateTime.Now;
            int generation = ++checkGeneration;
            string preferredPath = pendingChangedPath;
            pendingChangedPath = null;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UsageInfo info = null;
                Exception failure = null;
                string failureReason = "";
                DateTime minimumAccepted = currentInfo == null ? DateTime.MinValue : currentInfo.CapturedAt;
                try
                {
                    info = UsageReader.ReadLatest(preferredPath, out failureReason);
                    if (info != null && info.CapturedAt < minimumAccepted)
                        info = UsageReader.ReadLatest(null, out failureReason);
                }
                catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (generation != checkGeneration) return;
                        ApplyUsageResult(info, failure, failureReason);
                    });
                }
                catch { }
            });
        }

        private void EnsureUpdateHealth()
        {
            int fallbackMinutes = Math.Max(1, lastScheduledCheckMinutes);
            if (!WidgetUiPolicy.ShouldRecoverUpdate(DateTime.Now, checkRunning,
                checkStartedAt, lastCheckCompletedAt, fallbackMinutes)) return;

            recoveringUpdatePipeline = true;
            AppLog.Error("UpdatePipeline.Watchdog",
                new TimeoutException("更新管線逾時，正在自動重建。running=" + checkRunning +
                    ", started=" + checkStartedAt.ToString("o") +
                    ", completed=" + lastCheckCompletedAt.ToString("o") +
                    ", lastSessionEvent=" + lastSessionEventAt.ToString("o")));
            checkGeneration++;
            checkRunning = false;
            checkPending = false;
            checkStartedAt = DateTime.MinValue;
            eventDebounceTimer.Stop();
            StartWatcher();
            resetLabel.Text = "（資料更新中斷，重試中）";
            resetLabel.ForeColor = Color.FromArgb(245, 158, 11);
            nextCheckAt = DateTime.MinValue;
            CheckUsage();
        }

        private void ApplyUsageResult(UsageInfo info, Exception failure, string failureReason)
        {
            checkRunning = false;
            checkStartedAt = DateTime.MinValue;
            lastCheckCompletedAt = DateTime.Now;
            if (failure != null) AppLog.Error("CheckUsage", failure);

            if (info != null)
            {
                if (currentInfo != null && info.CapturedAt < currentInfo.CapturedAt)
                {
                    AppLog.Error("UsageReader.StaleDataRejected",
                        new InvalidDataException("拒絕較舊額度資料。current=" +
                            currentInfo.CapturedAt.ToString("o") + ", incoming=" + info.CapturedAt.ToString("o")));
                    info = currentInfo;
                }
                consecutiveReadFailures = 0;
                lastReadFailureReason = "";
                UpdateQuotaStallState(info);
                string signature = info.UsedPercent.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
                    info.CapturedAt.Ticks + "|" + info.SourceFileModifiedAt.Ticks;
                if (signature != lastActivitySignature)
                {
                    lastPercent = info.UsedPercent;
                    lastActivitySignature = signature;
                    lastChangedAt = DateTime.Now;
                }
                currentInfo = info;
                recoveringUpdatePipeline = false;
                double remaining = Math.Max(0, 100 - info.UsedPercent);
                remainingLabel.Text = "剩餘 " + remaining.ToString("0.#") + "%";
                bool stalled = quotaStallStartedAt != DateTime.MinValue &&
                    DateTime.Now - quotaStallStartedAt >= TimeSpan.FromMinutes(10);
                resetLabel.Text = stalled
                    ? "（額度資料停滯）"
                    : info.ResetAt == DateTime.MinValue ? "（重設時間未提供）" : "（重設 " + info.ResetAt.ToString("M月d日") + "）";
                resetLabel.ForeColor = stalled ? Color.FromArgb(245, 158, 11) : Color.FromArgb(160, 160, 160);
                remainingLabel.ForeColor = remaining < 10
                    ? Color.FromArgb(239, 68, 68)
                    : remaining < 30 ? Color.FromArgb(245, 158, 11) : Color.FromArgb(235, 235, 235);
                usageBar.FillColor = WidgetUiPolicy.QuotaColor(remaining);
                usageBar.Value = remaining;
                usageBar.Invalidate();
                Invalidate();
                UpdateTrayStatus(remaining);
                SaveSnapshot(info);

                bool idle = DateTime.Now - lastChangedAt >= TimeSpan.FromMinutes(settings.IdleAfterMinutes);
                int minutes = idle ? settings.IdleMinutes : settings.NormalMinutes;
                lastScheduledCheckMinutes = Math.Max(1, minutes);
                nextCheckAt = DateTime.Now.AddMinutes(Math.Max(1, minutes));
            }
            else
            {
                consecutiveReadFailures++;
                lastReadFailureReason = !string.IsNullOrEmpty(failureReason)
                    ? failureReason
                    : failure != null ? failure.Message : "未知的資料讀取失敗。";
                if (consecutiveReadFailures >= 3)
                {
                    if (consecutiveReadFailures == 3)
                        AppLog.Error("UsageReader.ConsecutiveFailure",
                            new InvalidDataException(lastReadFailureReason));
                    ShowDataError();
                    nextCheckAt = DateTime.Now.AddMinutes(Math.Max(1, settings.NormalMinutes));
                }
                else
                {
                    nextCheckAt = DateTime.Now.AddSeconds(2);
                }
            }

            UpdateAgeText();
            if (checkPending)
            {
                checkPending = false;
                nextCheckAt = DateTime.MinValue;
                CheckUsage();
            }
        }
        private void UpdateQuotaStallState(UsageInfo info)
        {
            if (lastObservedQuotaAt == DateTime.MinValue || info.CapturedAt > lastObservedQuotaAt)
            {
                lastObservedQuotaAt = info.CapturedAt;
                lastObservedFileAt = info.SourceFileModifiedAt;
                quotaStallStartedAt = DateTime.MinValue;
                quotaStallLogged = false;
                return;
            }
            if (info.SourceFileModifiedAt > lastObservedFileAt)
            {
                lastObservedFileAt = info.SourceFileModifiedAt;
                if (quotaStallStartedAt == DateTime.MinValue) quotaStallStartedAt = DateTime.Now;
            }
            if (!quotaStallLogged && quotaStallStartedAt != DateTime.MinValue &&
                DateTime.Now - quotaStallStartedAt >= TimeSpan.FromMinutes(10))
            {
                quotaStallLogged = true;
                AppLog.Error("UsageReader.QuotaStalled",
                    new InvalidDataException("session 檔案持續更新，但額度資料時間已超過10分鐘沒有前進。lastQuota=" +
                        lastObservedQuotaAt.ToString("o") + ", lastFile=" + lastObservedFileAt.ToString("o")));
            }
        }
        private void ShowDataError()
        {
            remainingLabel.Text = "剩餘 --%";
            resetLabel.Text = "（資料接收錯誤）";
            resetLabel.ForeColor = Color.FromArgb(239, 68, 68);
            usageBar.FillColor = Color.FromArgb(239, 68, 68);
            usageBar.Value = 0;
            usageBar.Invalidate();
            Invalidate();
            UpdateTrayError();
        }
        private void UpdateAgeText()
        {
            if (currentInfo == null) { updatedLabel.Text = ""; return; }
            TimeSpan age = DateTime.Now - currentInfo.CapturedAt;
            string dataAge;
            if (age.TotalMinutes < 1) dataAge = "資料：剛剛";
            else if (age.TotalHours < 24) dataAge = "資料：" + ((int)age.TotalMinutes) + "分前";
            else dataAge = "資料：" + ((int)age.TotalDays) + "天前";
            if (recoveringUpdatePipeline) dataAge = "資料：更新中斷";
            int seconds = Math.Max(0, (int)Math.Ceiling((nextCheckAt - DateTime.Now).TotalSeconds));
            updatedLabel.Text = dataAge + "　保底：" + seconds + "秒";
        }
        private void SaveSnapshot(UsageInfo info)
        {
            try
            {
                string signature = info.CapturedAt.Ticks + "|" +
                    info.UsedPercent.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
                    info.ResetAt.Ticks;
                if (signature == lastSavedSnapshotSignature) return;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget");
                Directory.CreateDirectory(dir);
                string line = string.Format(CultureInfo.InvariantCulture,
                    "{{\"recorded_at\":\"{0:o}\",\"source_at\":\"{1:o}\",\"plan\":\"{2}\",\"weekly_used_percent\":{3},\"weekly_resets_at\":\"{4:o}\"}}",
                    DateTimeOffset.Now, info.CapturedAt, info.Plan, info.UsedPercent, info.ResetAt);
                lock (historyLock)
                    File.AppendAllText(Path.Combine(dir, "history.jsonl"), line + Environment.NewLine, Encoding.UTF8);
                lastSavedSnapshotSignature = signature;
            }
            catch (Exception ex) { AppLog.Error("SaveSnapshot", ex); }
        }
        private void CleanHistory()
        {
            if (historyCleanupRunning) return;
            historyCleanupRunning = true;
            int retentionDays = Math.Max(1, settings.HistoryRetentionDays);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string latestSignature = "";
                Exception failure = null;
                try
                {
                    lock (historyLock)
                    {
                        string path = Path.Combine(AppLog.DataDir, "history.jsonl");
                        if (File.Exists(path))
                        {
                            DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
                            string temporary = path + ".tmp";
                            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
                            using (StreamWriter writer = new StreamWriter(temporary, false, Encoding.UTF8))
                            {
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    try
                                    {
                                        using JsonDocument document = JsonDocument.Parse(line);
                                        JsonElement row = document.RootElement;
                                        JsonElement recordedValue;
                                        DateTime recorded;
                                        if (row.ValueKind == JsonValueKind.Object &&
                                            row.TryGetProperty("recorded_at", out recordedValue) &&
                                            DateTime.TryParse(recordedValue.GetString(), out recorded) && recorded >= cutoff)
                                        {
                                            writer.WriteLine(line);
                                            JsonElement sourceValue;
                                            JsonElement usedValue;
                                            JsonElement resetValue;
                                            DateTime sourceAt;
                                            DateTime resetAt;
                                            double used;
                                            if (row.TryGetProperty("source_at", out sourceValue) &&
                                                row.TryGetProperty("weekly_used_percent", out usedValue) &&
                                                row.TryGetProperty("weekly_resets_at", out resetValue) &&
                                                DateTime.TryParse(sourceValue.GetString(), out sourceAt) &&
                                                DateTime.TryParse(resetValue.GetString(), out resetAt) &&
                                                usedValue.TryGetDouble(out used))
                                                latestSignature = sourceAt.Ticks + "|" +
                                                    used.ToString("0.###", CultureInfo.InvariantCulture) + "|" + resetAt.Ticks;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            File.Replace(temporary, path, path + ".bak", true);
                        }
                    }
                }
                catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        historyCleanupRunning = false;
                        lastHistoryCleanup = DateTime.Now;
                        if (!string.IsNullOrEmpty(latestSignature)) lastSavedSnapshotSignature = latestSignature;
                        if (failure != null) AppLog.Error("CleanHistory", failure);
                    });
                }
                catch { }
            });
        }
        private void CopyDiagnostics()
        {
            try
            {
                string info = "Codex Usage Widget " + Assembly.GetExecutingAssembly().GetName().Version + Environment.NewLine +
                    "Windows: " + Environment.OSVersion + Environment.NewLine +
                    "EXE: " + Application.ExecutablePath + Environment.NewLine +
                    "Visible: " + settings.ShowWidget + ", Locked: " + settings.Locked +
                    ", ClickThrough: " + settings.ClickThrough + Environment.NewLine +
                    "Fallback: " + settings.NormalMinutes + "/" + settings.IdleAfterMinutes + "/" + settings.IdleMinutes + " min" + Environment.NewLine +
                    "Opacity: " + settings.OpacityPercent + "%" + Environment.NewLine +
                    "Last data: " + (currentInfo == null ? "none" : currentInfo.CapturedAt.ToString("o")) + Environment.NewLine +
                    "Read failures: " + consecutiveReadFailures + Environment.NewLine +
                    "Last failure: " + (string.IsNullOrEmpty(lastReadFailureReason) ? "none" : lastReadFailureReason) + Environment.NewLine +
                    "Quota stalled since: " + (quotaStallStartedAt == DateTime.MinValue ? "none" : quotaStallStartedAt.ToString("o")) + Environment.NewLine +
                    "Check started: " + (checkStartedAt == DateTime.MinValue ? "none" : checkStartedAt.ToString("o")) + Environment.NewLine +
                    "Check completed: " + (lastCheckCompletedAt == DateTime.MinValue ? "none" : lastCheckCompletedAt.ToString("o")) + Environment.NewLine +
                    "Last session event: " + (lastSessionEventAt == DateTime.MinValue ? "none" : lastSessionEventAt.ToString("o")) + Environment.NewLine +
                    "Log: " + AppLog.LogPath;
                Clipboard.SetText(info);
                tray.ShowBalloonTip(1800, "Codex Usage Widget", "診斷資訊已複製。", ToolTipIcon.Info);
            }
            catch (Exception ex) { AppLog.Error("CopyDiagnostics", ex); }
        }
        private static HttpClient CreateUpdateClient()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexUsageWidget/2.3.2");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (updateCheckRunning) return;
            if (!manual && settings.LastUpdateCheckUtc != DateTime.MinValue &&
                DateTime.UtcNow - settings.LastUpdateCheckUtc < TimeSpan.FromHours(24)) return;
            updateCheckRunning = true;
            try
            {
                using HttpResponseMessage response = await UpdateClient.GetAsync(LatestReleaseApi);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                string tag = root.TryGetProperty("tag_name", out JsonElement tagValue) ? tagValue.GetString() : "";
                string url = root.TryGetProperty("html_url", out JsonElement urlValue) ? urlValue.GetString() : "";
                Version latest = WidgetUiPolicy.ParseReleaseVersion(tag);
                Version current = Assembly.GetExecutingAssembly().GetName().Version;
                if (latest == null || string.IsNullOrWhiteSpace(url) ||
                    !url.StartsWith(ReleasesBaseUrl, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("GitHub Release 回應缺少有效版本或網址。");

                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                settings.Save();
                if (latest > current)
                {
                    pendingReleaseUrl = url;
                    if (manual)
                    {
                        DialogResult result = MessageBox.Show(this,
                            "已有新版本 " + tag + "。" + Environment.NewLine +
                            "目前版本：v" + current.ToString(3) + Environment.NewLine + Environment.NewLine +
                            "是否開啟 GitHub Release 下載頁？",
                            "發現新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (result == DialogResult.Yes) OpenUrl(url);
                    }
                    else
                        tray.ShowBalloonTip(8000, "Codex Usage Widget 有新版本",
                            tag + " 已發布，點擊通知前往下載。", ToolTipIcon.Info);
                }
                else if (manual)
                    MessageBox.Show(this, "目前已是最新版本：v" + current.ToString(3),
                        "檢查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppLog.Error("CheckForUpdates", ex);
                if (manual)
                    MessageBox.Show(this, "暫時無法連線至 GitHub 檢查更新。" + Environment.NewLine + ex.Message,
                        "檢查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { updateCheckRunning = false; }
        }
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                BeginInvoke((Action)delegate { StartWatcher(); nextCheckAt = DateTime.MinValue; CheckUsage(); });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == showMessage)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }
        private static void OpenDashboard()
        {
            Process.Start(new ProcessStartInfo("https://chatgpt.com/codex/settings/usage") { UseShellExecute = true });
        }
        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Error("OpenUrl", ex); }
        }
        private void SetWidgetVisible(bool visible)
        {
            settings.ShowWidget = visible;
            settings.Save();
            showWidgetItem.Checked = visible;
            if (visible)
            {
                Show();
                WindowState = FormWindowState.Normal;
                TopMost = true;
                BringToFront();
                Activate();
                Invalidate();
            }
            else Hide();
        }
        private void ShowFromTray() { SetWidgetVisible(true); }
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SetWidgetVisible(false);
                return;
            }
            SavePosition();
            uiTimer.Stop();
            healthTimer.Dispose();
            eventDebounceTimer.Stop();
            if (watcher != null) watcher.Dispose();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            tray.Visible = false; tray.Dispose();
            titlePaintFont.Dispose();
            bodyPaintFont.Dispose();
        }
        private static bool IsAutoStartEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("CodexUsageWidget") != null;
        }
        private static void RepairAutoStartPath()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null || key.GetValue("CodexUsageWidget") == null) return;
                string expected = "\"" + Application.ExecutablePath + "\"";
                if (!string.Equals(Convert.ToString(key.GetValue("CodexUsageWidget")), expected,
                    StringComparison.OrdinalIgnoreCase))
                    key.SetValue("CodexUsageWidget", expected);
            }
            catch (Exception ex) { AppLog.Error("RepairAutoStartPath", ex); }
        }
        private static void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enabled) key.SetValue("CodexUsageWidget", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("CodexUsageWidget", false);
                }
            }
            catch (Exception ex) { MessageBox.Show("無法更新開機啟動設定：" + ex.Message, "Codex Usage Widget"); }
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string value);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int HwndBroadcast = 0xffff;

        [STAThread]
        private static void Main()
        {
            bool created;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "CodexUsageWidget.SingleInstance", out created))
            {
                if (!created)
                {
                    int message = RegisterWindowMessage("CodexUsageWidget.ShowExisting.v2");
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        PostMessage((IntPtr)HwndBroadcast, message, IntPtr.Zero, IntPtr.Zero);
                        if (attempt < 4) System.Threading.Thread.Sleep(200);
                    }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    AppLog.Error("Application.ThreadException", e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    AppLog.Error("AppDomain.UnhandledException", ex ?? new Exception(Convert.ToString(e.ExceptionObject)));
                };
                Application.Run(new UsageForm());
            }
        }
    }
}
