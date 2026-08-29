// IconAnchor - デスクトップのアイコン配置を記憶／復元するだけのツール
// インストール不要・レジストリ不使用。配置は exe と同じ場所のテキストに保存します。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Native
{
    public const int LVM_FIRST             = 0x1000;
    public const int LVM_GETITEMCOUNT      = LVM_FIRST + 4;
    public const int LVM_GETITEMPOSITION   = LVM_FIRST + 16;
    public const int LVM_SETITEMPOSITION32 = LVM_FIRST + 49;
    public const int LVM_GETITEMSPACING    = LVM_FIRST + 51;
    public const int LVM_GETITEMTEXTW      = LVM_FIRST + 115;
    public const int LVIF_TEXT             = 0x0001;
    public const int LVS_AUTOARRANGE       = 0x0100;
    public const int GWL_STYLE             = -16;

    public const uint PROCESS_VM_OPERATION      = 0x0008;
    public const uint PROCESS_VM_READ           = 0x0010;
    public const uint PROCESS_VM_WRITE          = 0x0020;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public const uint MEM_COMMIT     = 0x1000;
    public const uint MEM_RESERVE    = 0x2000;
    public const uint MEM_RELEASE    = 0x8000;
    public const uint PAGE_READWRITE = 0x04;

    public const uint RDW_INVALIDATE  = 0x0001;
    public const uint RDW_ERASE       = 0x0004;
    public const uint RDW_ALLCHILDREN = 0x0080;
    public const uint RDW_UPDATENOW   = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct LVITEM
    {
        public uint   mask;
        public int    iItem;
        public int    iSubItem;
        public uint   state;
        public uint   stateMask;
        public IntPtr pszText;
        public int    cchTextMax;
        public int    iImage;
        public IntPtr lParam;
        public int    iIndent;
        public int    iGroupId;
        public uint   cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int    iGroup;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string cls, string win);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string win);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr hWnd, IntPtr rc, IntPtr rgn, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, IntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr h, IntPtr addr, IntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, IntPtr buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, IntPtr buf, IntPtr size, out IntPtr written);
}

// アイコン一覧は explorer.exe が持っているので、
// 問い合わせ用の作業スペースを explorer.exe 側に間借りする
sealed class Remote : IDisposable
{
    const int BLOCK = 4096;
    public const int ITEM_OFF  = 0;
    public const int TEXT_OFF  = 512;
    public const int POINT_OFF = 2048;
    public const int MAX_CHARS = 260;

    IntPtr proc;
    IntPtr mem;

    public Remote(IntPtr hwnd)
    {
        uint pid;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        proc = Native.OpenProcess(
            Native.PROCESS_VM_OPERATION | Native.PROCESS_VM_READ |
            Native.PROCESS_VM_WRITE | Native.PROCESS_QUERY_INFORMATION, false, pid);
        if (proc == IntPtr.Zero)
            throw new Exception("エクスプローラーに接続できませんでした。");

        mem = Native.VirtualAllocEx(proc, IntPtr.Zero, (IntPtr)BLOCK,
            Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
        if (mem == IntPtr.Zero)
        {
            Native.CloseHandle(proc);
            throw new Exception("作業用メモリを確保できませんでした。");
        }
    }

    public IntPtr At(int offset) { return (IntPtr)(mem.ToInt64() + offset); }

    public void Write(int offset, object value)
    {
        int size = Marshal.SizeOf(value);
        IntPtr local = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, local, false);
            IntPtr done;
            if (!Native.WriteProcessMemory(proc, At(offset), local, (IntPtr)size, out done))
                throw new Exception("メモリ書き込みに失敗しました。");
        }
        finally { Marshal.FreeHGlobal(local); }
    }

    public T Read<T>(int offset)
    {
        int size = Marshal.SizeOf(typeof(T));
        IntPtr local = Marshal.AllocHGlobal(size);
        try
        {
            IntPtr done;
            if (!Native.ReadProcessMemory(proc, At(offset), local, (IntPtr)size, out done))
                throw new Exception("メモリ読み込みに失敗しました。");
            return (T)Marshal.PtrToStructure(local, typeof(T));
        }
        finally { Marshal.FreeHGlobal(local); }
    }

    public string ReadText(int offset)
    {
        int bytes = MAX_CHARS * 2;
        IntPtr local = Marshal.AllocHGlobal(bytes);
        try
        {
            IntPtr done;
            if (!Native.ReadProcessMemory(proc, At(offset), local, (IntPtr)bytes, out done))
                return "";
            string s = Marshal.PtrToStringUni(local, MAX_CHARS);
            int nul = s.IndexOf('\0');
            return nul >= 0 ? s.Substring(0, nul) : s;
        }
        finally { Marshal.FreeHGlobal(local); }
    }

    public void Dispose()
    {
        if (mem != IntPtr.Zero) Native.VirtualFreeEx(proc, mem, IntPtr.Zero, Native.MEM_RELEASE);
        if (proc != IntPtr.Zero) Native.CloseHandle(proc);
        mem = IntPtr.Zero;
        proc = IntPtr.Zero;
    }
}

struct IconPos
{
    public string Name;
    public int X;
    public int Y;
    public IconPos(string name, int x, int y) { Name = name; X = x; Y = y; }
}

struct RestoreResult
{
    public int Restored;   // 記憶どおりに戻したもの
    public int Arranged;   // 記憶になくて先頭から並べたもの
}

static class Desktop
{
    public static IntPtr FindListView()
    {
        IntPtr progman = Native.FindWindow("Progman", null);
        IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defView == IntPtr.Zero)
        {
            // 壁紙スライドショー等では WorkerW の下にぶら下がっていることがある
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                IntPtr dv = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero) { found = dv; return false; }
                return true;
            }, IntPtr.Zero);
            defView = found;
        }

        if (defView == IntPtr.Zero) return IntPtr.Zero;
        return Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    static IntPtr RequireListView()
    {
        IntPtr lv = FindListView();
        if (lv == IntPtr.Zero)
            throw new Exception("デスクトップのアイコン一覧が見つかりませんでした。\n"
                              + "デスクトップアイコンが非表示になっていないか確認してください。");
        return lv;
    }

    public static bool IsAutoArrange()
    {
        IntPtr lv = FindListView();
        if (lv == IntPtr.Zero) return false;
        return (Native.GetWindowLong(lv, Native.GWL_STYLE) & Native.LVS_AUTOARRANGE) != 0;
    }

    static int ItemCount(IntPtr lv)
    {
        return Native.SendMessage(lv, Native.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
    }

    static string GetName(IntPtr lv, Remote r, int index)
    {
        Native.LVITEM item = new Native.LVITEM();
        item.mask       = Native.LVIF_TEXT;
        item.iItem      = index;
        item.iSubItem   = 0;
        item.pszText    = r.At(Remote.TEXT_OFF);
        item.cchTextMax = Remote.MAX_CHARS;
        r.Write(Remote.ITEM_OFF, item);
        Native.SendMessage(lv, Native.LVM_GETITEMTEXTW, (IntPtr)index, r.At(Remote.ITEM_OFF));
        return r.ReadText(Remote.TEXT_OFF);
    }

    static void SetPos(IntPtr lv, Remote r, int index, int x, int y)
    {
        Native.POINT p = new Native.POINT();
        p.x = x;
        p.y = y;
        r.Write(Remote.POINT_OFF, p);
        Native.SendMessage(lv, Native.LVM_SETITEMPOSITION32, (IntPtr)index, r.At(Remote.POINT_OFF));
    }

    public static List<IconPos> ReadAll()
    {
        IntPtr lv = RequireListView();
        int count = ItemCount(lv);
        List<IconPos> list = new List<IconPos>();
        if (count <= 0) return list;

        using (Remote r = new Remote(lv))
        {
            for (int i = 0; i < count; i++)
            {
                string name = GetName(lv, r, i);
                if (name.Length == 0) continue;
                Native.SendMessage(lv, Native.LVM_GETITEMPOSITION, (IntPtr)i, r.At(Remote.POINT_OFF));
                Native.POINT p = r.Read<Native.POINT>(Remote.POINT_OFF);
                list.Add(new IconPos(name, p.x, p.y));
            }
        }
        return list;
    }

    // 記憶にあるアイコンは元の位置へ。
    // 記憶にないアイコン（あとから増えたもの）は、空いているマスに先頭から詰める。
    public static RestoreResult Restore(Dictionary<string, Point> saved)
    {
        IntPtr lv = RequireListView();
        int count = ItemCount(lv);
        RestoreResult result = new RestoreResult();
        if (count <= 0) return result;

        // 1マスの大きさ（アイコン間隔）を取得
        int spacing = Native.SendMessage(lv, Native.LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero).ToInt32();
        int cellW = spacing & 0xFFFF;
        int cellH = (spacing >> 16) & 0xFFFF;
        if (cellW <= 0) cellW = 76;
        if (cellH <= 0) cellH = 100;

        Native.RECT rc;
        Native.GetClientRect(lv, out rc);
        int areaW = Math.Max(rc.right - rc.left, cellW);
        int areaH = Math.Max(rc.bottom - rc.top, cellH);
        int rows  = Math.Max(1, areaH / cellH);

        List<int> known = new List<int>();
        List<Point> knownPos = new List<Point>();
        List<int> unknown = new List<int>();

        using (Remote r = new Remote(lv))
        {
            for (int i = 0; i < count; i++)
            {
                string name = GetName(lv, r, i);
                Point p;
                if (name.Length > 0 && saved.TryGetValue(name, out p))
                {
                    // 画面外にはみ出さないよう内側に寄せる
                    p.X = Math.Max(0, Math.Min(p.X, areaW - cellW));
                    p.Y = Math.Max(0, Math.Min(p.Y, areaH - cellH));
                    known.Add(i);
                    knownPos.Add(p);
                }
                else unknown.Add(i);
            }

            // 記憶組が使うマスを埋まり済みとして控えておく
            Dictionary<int, bool> occupied = new Dictionary<int, bool>();
            foreach (Point p in knownPos)
            {
                int cell = (p.X / cellW) * rows + Math.Min(p.Y / cellH, rows - 1);
                occupied[cell] = true;
            }

            for (int k = 0; k < known.Count; k++)
            {
                SetPos(lv, r, known[k], knownPos[k].X, knownPos[k].Y);
                result.Restored++;
            }

            // 残りを左上から縦に詰めていく（デスクトップの並び順と同じ向き）
            int next = 0;
            foreach (int index in unknown)
            {
                while (occupied.ContainsKey(next)) next++;
                int col = next / rows;
                int row = next % rows;
                SetPos(lv, r, index, col * cellW, row * cellH);
                occupied[next] = true;
                next++;
                result.Arranged++;
            }
        }

        Native.RedrawWindow(lv, IntPtr.Zero, IntPtr.Zero,
            Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_ALLCHILDREN | Native.RDW_UPDATENOW);
        return result;
    }
}

static class Store
{
    public static string Dir()
    {
        string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
        try
        {
            string probe = Path.Combine(exeDir, "_ia_write_test.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return exeDir;
        }
        catch
        {
            // Program Files 等、書き込めない場所に置かれた場合の逃げ道
            string d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IconAnchor");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    // 画面サイズごとに別ファイル。解像度が変わっても混ざらない
    public static string FilePath()
    {
        Rectangle vs = SystemInformation.VirtualScreen;
        return Path.Combine(Dir(), string.Format("layout_{0}x{1}.txt", vs.Width, vs.Height));
    }

    public static void Save(List<IconPos> icons)
    {
        Rectangle vs = SystemInformation.VirtualScreen;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# IconAnchor : デスクトップのアイコン配置");
        sb.AppendLine("# 保存日時   : " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));
        sb.AppendLine("# 画面サイズ : " + vs.Width + "x" + vs.Height);
        sb.AppendLine("# 形式       : X <TAB> Y <TAB> 名前");
        foreach (IconPos ic in icons)
            sb.AppendLine(ic.X + "\t" + ic.Y + "\t" + ic.Name);
        File.WriteAllText(FilePath(), sb.ToString(), new UTF8Encoding(true));
    }

    public static Dictionary<string, Point> Load()
    {
        Dictionary<string, Point> map = new Dictionary<string, Point>();
        string path = FilePath();
        if (!File.Exists(path)) return map;

        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] parts = line.Split('\t');
            if (parts.Length < 3) continue;
            int x, y;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) continue;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) continue;
            map[string.Join("\t", parts, 2, parts.Length - 2)] = new Point(x, y);
        }
        return map;
    }
}

class MainForm : Form
{
    Label status;
    float scale = 1F;

    // 画面の拡大率（125% など）に合わせて寸法を換算する。
    // 文字サイズはポイント指定なので自動で追従するが、座標や幅は自前で調整が必要。
    int S(int value) { return (int)Math.Round(value * scale); }

    public MainForm()
    {
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96F;
        AutoScaleMode = AutoScaleMode.None;

        Text = "IconAnchor";
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(400), S(286));

        Button save = new Button();
        save.Text = "今の配置を記憶する";
        save.SetBounds(S(20), S(18), S(360), S(64));
        save.Font = new Font(Font.FontFamily, 13F);
        save.Click += delegate { DoSave(); };

        Button restore = new Button();
        restore.Text = "記憶した配置に戻す";
        restore.SetBounds(S(20), S(94), S(360), S(64));
        restore.Font = new Font(Font.FontFamily, 13F);
        restore.Click += delegate { DoRestore(); };

        status = new Label();
        status.SetBounds(S(20), S(172), S(360), S(76));
        status.Font = new Font(Font.FontFamily, 9F);

        Label pathInfo = new Label();
        pathInfo.SetBounds(S(20), S(252), S(360), S(26));
        pathInfo.ForeColor = SystemColors.GrayText;
        pathInfo.Font = new Font(Font.FontFamily, 8F);
        pathInfo.AutoEllipsis = true;
        pathInfo.Text = "保存先: " + Store.Dir();

        Controls.Add(save);
        Controls.Add(restore);
        Controls.Add(status);
        Controls.Add(pathInfo);

        ShowStatus(null);
    }

    void ShowStatus(string message)
    {
        StringBuilder sb = new StringBuilder();
        if (message != null) sb.AppendLine(message);

        string path = Store.FilePath();
        if (File.Exists(path))
            sb.AppendLine("記憶済み: " + File.GetLastWriteTime(path).ToString("yyyy/MM/dd HH:mm")
                        + "  (" + Store.Load().Count + " 個)");
        else
            sb.AppendLine("この画面サイズではまだ記憶していません。");

        if (Desktop.IsAutoArrange())
            sb.AppendLine("※ 自動整列がONです。デスクトップ右クリック →「表示」→"
                        + "「アイコンの自動整列」のチェックを外してください。");

        status.Text = sb.ToString().TrimEnd();
    }

    void DoSave()
    {
        try
        {
            List<IconPos> icons = Desktop.ReadAll();
            if (icons.Count == 0) { ShowStatus("アイコンが見つかりませんでした。"); return; }
            Store.Save(icons);
            ShowStatus("記憶しました。(" + icons.Count + " 個)");
        }
        catch (Exception ex) { Fail(ex); }
    }

    void DoRestore()
    {
        try
        {
            Dictionary<string, Point> saved = Store.Load();
            if (saved.Count == 0) { ShowStatus("記憶データがありません。先に記憶してください。"); return; }

            RestoreResult r = Desktop.Restore(saved);
            string msg = "戻しました。記憶どおり " + r.Restored + " 個";
            if (r.Arranged > 0) msg += " / 新しいアイコン " + r.Arranged + " 個を先頭から整列";
            ShowStatus(msg);
        }
        catch (Exception ex) { Fail(ex); }
    }

    void Fail(Exception ex)
    {
        MessageBox.Show(this, ex.Message, "IconAnchor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // /save や /restore を付けると画面を出さずに実行（ショートカット用）
        string mode = "";
        foreach (string a in args)
        {
            string k = a.TrimStart('/', '-').ToLowerInvariant();
            if (k == "save" || k == "restore") mode = k;
        }

        if (mode.Length == 0)
        {
            Application.Run(new MainForm());
            return 0;
        }

        try
        {
            if (mode == "save")
            {
                List<IconPos> icons = Desktop.ReadAll();
                if (icons.Count == 0) throw new Exception("アイコンが見つかりませんでした。");
                Store.Save(icons);
            }
            else
            {
                Dictionary<string, Point> saved = Store.Load();
                if (saved.Count == 0) throw new Exception("記憶データがありません。先に記憶してください。");
                Desktop.Restore(saved);
            }
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "IconAnchor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
