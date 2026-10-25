using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("Zapret Updater")]
[assembly: AssemblyProduct("Zapret Updater by jaketea")]
[assembly: AssemblyCompany("jaketea")]
[assembly: AssemblyCopyright("by jaketea")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace ZapretUpdater
{
    static class Log
    {
        public static string FilePath;
        public static bool Verbose;
        static readonly object Sync = new object();
        static readonly object ConLock = new object();

        static readonly Regex RedRx = new Regex(@"(?i)ОШИБКА|НЕ запущен|Не удалось|Не смог|Отказываюсь|Нельзя|не завершились|Не нашёл|не найдена в новой|не установлена");
        static readonly Regex YellowRx = new Regex(@"(?i)автоответ|Открываю|не найден|\(пункт|Режим|с нуля|прерываю|не появилось|Блокнот|^\s*->");
        static readonly Regex GreenRx = new Regex(@"(?i)ГОТОВО|обновлены|обновлён|Установлена стратегия|Лучшая конфигурация|Вернул|Сохранил|Game Filter:|актуальна|актуален|записан");
        static readonly Regex CyanRx = new Regex(@"(?i)Папка|Проверяю|Установлена:|Скачиваю|Останавливаю|Очищаю|Текущая стратегия|меню service|Устанавливаю|запускаю тесты|Результаты тестов|Запускаю|ИТОГИ");

        public static void Init(string appDir)
        {
            string p = Path.Combine(appDir, "zapret-updater.log");
            try { File.WriteAllText(p, "=== Zapret Updater by jaketea | " + DateTime.Now.ToString() + " ===\r\n"); }
            catch
            {
                p = Path.Combine(Path.GetTempPath(), "zapret-updater.log");
                try { File.WriteAllText(p, "=== Zapret Updater by jaketea | " + DateTime.Now.ToString() + " ===\r\n"); } catch { }
            }
            FilePath = p;
        }

        static ConsoleColor Classify(string s)
        {
            if (RedRx.IsMatch(s)) return ConsoleColor.Red;
            if (YellowRx.IsMatch(s)) return ConsoleColor.Yellow;
            if (GreenRx.IsMatch(s)) return ConsoleColor.Green;
            if (CyanRx.IsMatch(s)) return ConsoleColor.Cyan;
            return ConsoleColor.White;
        }

        class Msg
        {
            public string Ts;
            public string Text;
            public ConsoleColor Color;
            public bool Blank;
        }

        static readonly Queue<Msg> Q = new Queue<Msg>();
        static readonly object QLock = new object();
        static bool busy;
        static Thread writer;

        static void StartWriter()
        {
            if (writer != null) return;
            writer = new Thread(WriterLoop);
            writer.IsBackground = true;
            writer.Start();
        }

        static void WriterLoop()
        {
            while (true)
            {
                Msg m;
                lock (QLock)
                {
                    while (Q.Count == 0) Monitor.Wait(QLock);
                    m = Q.Dequeue();
                    busy = true;
                }
                PrintMsg(m);
                lock (QLock)
                {
                    busy = false;
                    Monitor.PulseAll(QLock);
                }
            }
        }

        static void PrintMsg(Msg m)
        {
            lock (ConLock)
            {
                try
                {
                    if (m.Blank) { Console.WriteLine(); return; }
                    if (m.Ts != null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("[" + m.Ts + "] ");
                    }
                    Console.ForegroundColor = m.Color;
                    Console.WriteLine(m.Text);
                }
                catch { }
                finally { try { Console.ResetColor(); } catch { } }
            }
        }

        static void Enqueue(Msg m)
        {
            StartWriter();
            lock (QLock)
            {
                Q.Enqueue(m);
                Monitor.PulseAll(QLock);
            }
        }

        // Waits (at most ~3 s) until everything queued has been printed.
        public static void Flush()
        {
            DateTime end = DateTime.UtcNow.AddSeconds(3);
            lock (QLock)
            {
                while ((Q.Count > 0 || busy) && DateTime.UtcNow < end) Monitor.Wait(QLock, 100);
            }
        }

        public static void Write(string s)
        {
            Write(s, Classify(s));
        }

        // Queues "[HH:mm:ss] text" - timestamp in dark gray, text in the given color.
        // The console is written by a separate thread, so a blocked console never stalls the real work.
        public static void Write(string s, ConsoleColor color)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            Msg m = new Msg();
            m.Ts = ts;
            m.Text = s;
            m.Color = color;
            Enqueue(m);
            RawFile("[ui " + ts + "] " + s + "\r\n");
        }

        // Line without timestamp (banner / watermark).
        public static void Plain(string s, ConsoleColor color)
        {
            Msg m = new Msg();
            m.Text = s;
            m.Color = color;
            Enqueue(m);
            RawFile(s + "\r\n");
        }

        public static void Banner()
        {
            Plain("  +----------------------------------+", ConsoleColor.Cyan);
            Plain("  |          ZAPRET  UPDATER         |", ConsoleColor.Green);
            Plain("  |           by  jaketea            |", ConsoleColor.Yellow);
            Plain("  +----------------------------------+", ConsoleColor.Cyan);
        }

        public static void Blank()
        {
            Msg m = new Msg();
            m.Blank = true;
            Enqueue(m);
            RawFile("\r\n");
        }

        // Empty line + highlighted header, so stages are visually separated.
        public static void Section(string title)
        {
            Blank();
            Write(">>> " + title, ConsoleColor.Magenta);
        }

        public static void RawFile(string s)
        {
            lock (Sync) { try { File.AppendAllText(FilePath, s); } catch { } }
        }

        public static void Raw(string s)
        {
            RawFile(s);
            if (Verbose) { lock (ConLock) { try { Console.Write(s); } catch { } } }
        }
    }

    static class Native
    {
        [DllImport("kernel32.dll")]
        public static extern uint GetOEMCP();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr h, out uint m);
        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr h, uint m);

        // QuickEdit makes a mouse click/selection freeze all console output (and the program with it).
        public static void DisableQuickEdit()
        {
            try
            {
                IntPtr h = GetStdHandle(-10); // STD_INPUT_HANDLE
                uint m;
                if (GetConsoleMode(h, out m))
                {
                    m &= ~0x0040u; // ENABLE_QUICK_EDIT_MODE
                    m |= 0x0080u;  // ENABLE_EXTENDED_FLAGS
                    SetConsoleMode(h, m);
                }
            }
            catch { }
        }
    }

    class WinInfo
    {
        public IntPtr H;
        public string Title;
        public string Cls;
        public uint Pid;
    }

    // Top-level window helpers (find / close Notepad and Explorer windows).
    static class Win
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        public static List<WinInfo> All()
        {
            List<WinInfo> r = new List<WinInfo>();
            EnumProc cb = delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                StringBuilder t = new StringBuilder(512);
                GetWindowText(h, t, 512);
                StringBuilder c = new StringBuilder(256);
                GetClassName(h, c, 256);
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                WinInfo w = new WinInfo();
                w.H = h;
                w.Title = t.ToString();
                w.Cls = c.ToString();
                w.Pid = pid;
                r.Add(w);
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return r;
        }

        public static void Close(IntPtr h)
        {
            PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
        }

        public static bool Alive(IntPtr h)
        {
            return IsWindow(h);
        }
    }

    static class Txt
    {
        static readonly Regex AnsiRx = new Regex("\u001B\\[[0-9;?]*[A-Za-z]");

        public static string Strip(string s)
        {
            return AnsiRx.Replace(s, "");
        }

        public static string LastLine(string s)
        {
            s = Strip(s);
            int i = s.LastIndexOf('\n');
            string t = i >= 0 ? s.Substring(i + 1) : s;
            return t.Replace("\r", "").TrimEnd();
        }

        // Parses numbered menu lines like "1. Install Service" -> ("1", "Install Service")
        public static List<KeyValuePair<string, string>> ParseMenu(string text)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            text = Strip(text);
            foreach (Match m in Regex.Matches(text, @"(?m)^[ \t]*\[?(\d+)\]?[\.\):\-]?[ \t]+(\S.*?)[ \t\r]*$"))
                r.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
            return r;
        }

        public static string Norm(string s)
        {
            s = s.Trim();
            if (s.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Trim().ToLowerInvariant();
        }
    }

    // Splits a character stream into lines (\r and \n both end a line) and hands them to a callback.
    class LineAsm
    {
        readonly StringBuilder sb = new StringBuilder();
        readonly Action<string> cb;

        public LineAsm(Action<string> cb)
        {
            this.cb = cb;
        }

        public void Feed(string t)
        {
            List<string> done = null;
            lock (sb)
            {
                foreach (char c in t)
                {
                    if (c == '\n' || c == '\r')
                    {
                        if (sb.Length > 0)
                        {
                            if (done == null) done = new List<string>();
                            done.Add(sb.ToString());
                            sb.Length = 0;
                        }
                    }
                    else sb.Append(c);
                }
            }
            if (done != null) foreach (string l in done) Emit(l);
        }

        // Emits an unfinished line (e.g. a prompt without a trailing newline).
        public void Flush()
        {
            string l = null;
            lock (sb)
            {
                if (sb.Length > 0) { l = sb.ToString(); sb.Length = 0; }
            }
            if (l != null) Emit(l);
        }

        void Emit(string l)
        {
            try { cb(l); } catch { }
        }
    }

    // Runs a console process with redirected IO and answers its prompts ("expect"-style).
    class Term : IDisposable
    {
        public Process P;
        readonly StringBuilder buf = new StringBuilder();
        readonly StringBuilder all = new StringBuilder();
        readonly object lk = new object();
        DateTime last = DateTime.UtcNow;
        StreamWriter inp;
        LineAsm asmOut;
        LineAsm asmErr;
        static readonly Regex PauseRx = new Regex(@"(?i)press any key|press enter|нажмите");
        static readonly Regex YnRx = new Regex(@"(?i)[\(\[]\s*y\s*/\s*n\s*[\)\]]");

        public Term(string file, string args, string workDir) : this(file, args, workDir, null) { }

        public Term(string file, string args, string workDir, Action<string> onLine)
        {
            if (onLine != null) { asmOut = new LineAsm(onLine); asmErr = new LineAsm(onLine); }
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.WorkingDirectory = workDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Encoding enc = Encoding.GetEncoding((int)Native.GetOEMCP());
            psi.StandardOutputEncoding = enc;
            psi.StandardErrorEncoding = enc;
            Log.RawFile("\r\n[start] " + file + " " + args + "\r\n");
            Log.Blank();
            Log.Write("> Запускаю программу: " + file + " " + args, ConsoleColor.Magenta);
            P = Process.Start(psi);
            inp = new StreamWriter(P.StandardInput.BaseStream, new UTF8Encoding(false));
            inp.AutoFlush = true;
            StartPump(P.StandardOutput, asmOut);
            StartPump(P.StandardError, asmErr);
        }

        void StartPump(StreamReader r, LineAsm a)
        {
            Thread th = new Thread(delegate() { Pump(r, a); });
            th.IsBackground = true;
            th.Start();
        }

        void Pump(StreamReader r, LineAsm a)
        {
            char[] b = new char[1024];
            int n;
            try
            {
                while ((n = r.Read(b, 0, b.Length)) > 0)
                {
                    string t = new string(b, 0, n);
                    lock (lk) { buf.Append(t); all.Append(t); last = DateTime.UtcNow; }
                    Log.Raw(t);
                    if (a != null) a.Feed(t);
                }
                if (a != null) a.Flush();
            }
            catch { }
        }

        public bool Exited { get { return P.HasExited; } }

        public string AllText()
        {
            lock (lk) { return all.ToString(); }
        }

        public void Send(string s)
        {
            if (asmOut != null) asmOut.Flush();
            if (asmErr != null) asmErr.Flush();
            lock (lk) { buf.Length = 0; last = DateTime.UtcNow; }
            Log.RawFile("\r\n[-> " + s + "]\r\n");
            try { inp.Write(s + "\r\n"); } catch { }
        }

        // Waits until output stops and the last line looks like a prompt.
        // Auto-answers "press any key" and Y/N questions. Returns all text received since the last Send.
        public string WaitPrompt(int timeoutMs, int forceQuietMs, string need)
        {
            StringBuilder acc = new StringBuilder();
            DateTime start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                Thread.Sleep(150);
                string snap;
                double quiet;
                lock (lk)
                {
                    snap = buf.ToString();
                    quiet = (DateTime.UtcNow - last).TotalMilliseconds;
                }
                if (P.HasExited)
                {
                    Thread.Sleep(400);
                    lock (lk) { snap = buf.ToString(); }
                    return acc.ToString() + snap;
                }
                if (quiet < 800) continue;

                string tail = Txt.LastLine(snap);
                if (PauseRx.IsMatch(tail))
                {
                    acc.Append(snap);
                    Send("");
                    continue;
                }
                if (YnRx.IsMatch(tail))
                {
                    acc.Append(snap);
                    Log.Write("   (автоответ Y на: " + tail + ")");
                    Send("Y");
                    continue;
                }
                string full = acc.ToString() + snap;
                bool looksPrompt = tail.Length > 0 && (tail.EndsWith(":") || tail.EndsWith("?") || tail.EndsWith("]"));
                bool forced = forceQuietMs > 0 && quiet >= forceQuietMs;
                if ((looksPrompt || forced) && (need == null || Regex.IsMatch(Txt.Strip(full), need, RegexOptions.IgnoreCase)))
                    return full;
            }
            string lastLine;
            lock (lk) { lastLine = Txt.LastLine(buf.ToString()); }
            throw new System.TimeoutException("Нет ответа от процесса. Последняя строка: " + lastLine);
        }

        public bool WaitExit(int timeoutMs)
        {
            DateTime start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                if (P.HasExited) return true;
                Thread.Sleep(300);
                string snap;
                double quiet;
                lock (lk)
                {
                    snap = buf.ToString();
                    quiet = (DateTime.UtcNow - last).TotalMilliseconds;
                }
                if (quiet >= 1500 && PauseRx.IsMatch(Txt.LastLine(snap))) Send("");
            }
            return P.HasExited;
        }

        public void Dispose()
        {
            try
            {
                if (!P.HasExited)
                {
                    ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/PID " + P.Id + " /T /F");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    using (Process k = Process.Start(psi)) { k.WaitForExit(10000); }
                }
            }
            catch { }
            try { P.Dispose(); } catch { }
        }
    }

    // service.bat menu session
    class Session : IDisposable
    {
        public const string MenuNeed = @"Enter choice|\(0-\d+\)";
        public Term T;
        public List<KeyValuePair<string, string>> Menu;

        public Session(string dir)
        {
            T = new Term("cmd.exe", "/c service.bat admin", dir);
            string first = T.WaitPrompt(90000, 0, MenuNeed);
            Menu = Txt.ParseMenu(first);
            if (Menu.Count == 0) throw new Exception("Не смог разобрать меню service.bat (см. zapret-updater.log).");
            Log.Write("   меню service.bat: пунктов " + Menu.Count);
        }

        public string Key(string[] words, string fallback)
        {
            foreach (KeyValuePair<string, string> kv in Menu)
                foreach (string w in words)
                    if (kv.Value.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Key;
            Log.Write("   (пункт '" + words[0] + "' не найден по названию, беру номер " + fallback + ")");
            return fallback;
        }

        public string Do(string input, int timeoutMs, string need)
        {
            T.Send(input);
            return T.WaitPrompt(timeoutMs, 0, need);
        }

        public void Close()
        {
            if (T.Exited) return;
            T.Send("0");
            T.WaitExit(10000);
        }

        public void Dispose()
        {
            T.Dispose();
        }
    }

    // Turns the raw output of the test script into colored, timestamped live lines
    // and collects "config = N/M" results for the final table.
    class TestView
    {
        public string Current;
        public long LastTick = DateTime.UtcNow.Ticks;
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, int[]> res = new Dictionary<string, int[]>();
        string lastLine = "";
        readonly object lk = new object();

        static readonly Regex CfgRx = new Regex(@"(?i)general[^\s\[\]=:|,;]*(?:\s*\([^)]*\))?(?:\.bat)?");
        static readonly Regex FracRx = new Regex(@"(?<![\d/])(\d{1,3})\s*/\s*(\d{1,3})(?![\d/])");
        static readonly Regex ProgRx = new Regex(@"^\s*\[\s*\d+\s*/\s*\d+\s*\]");
        static readonly Regex FailRx = new Regex(@"(?i)\b(fail\w*|error|timeout|timed out|unsup\w*|blocked|ошиб\w*)\b|\[X\]|\bSSL\b");
        static readonly Regex OkRx = new Regex(@"(?i)\b(ok|success\w*|passed|best|успеш\w*)\b|\[\+\]");
        static readonly Regex WarnRx = new Regex(@"(?i)\b(warn\w*|skip\w*|partial|retry)\b");

        static ConsoleColor RatioColor(int n, int m)
        {
            double r = (double)n / m;
            if (r >= 0.8) return ConsoleColor.Green;
            if (r >= 0.4) return ConsoleColor.Yellow;
            return ConsoleColor.Red;
        }

        static ConsoleColor ColorFor(string line)
        {
            bool bad = FailRx.IsMatch(line);
            bool good = OkRx.IsMatch(line);
            if (bad && good) return ConsoleColor.Yellow;
            if (bad) return ConsoleColor.Red;
            if (WarnRx.IsMatch(line)) return ConsoleColor.Yellow;
            if (good) return ConsoleColor.Green;
            if (CfgRx.IsMatch(line)) return ConsoleColor.Cyan;
            return ConsoleColor.Gray;
        }

        public void Handle(string raw)
        {
            string line = Txt.Strip(raw).Replace("\r", "").Trim();
            if (line.Length == 0) return;
            lock (lk)
            {
                if (line == lastLine) return;
                lastLine = line;
                Interlocked.Exchange(ref LastTick, DateTime.UtcNow.Ticks);

                Match cm = CfgRx.Match(line);
                if (cm.Success) Current = Regex.Replace(cm.Value.Trim(), @"(?i)\.bat$", "");

                if (ProgRx.IsMatch(line)) { Log.Write(line, ConsoleColor.Cyan); return; }

                MatchCollection fr = FracRx.Matches(line);
                if (fr.Count == 1 && Current != null)
                {
                    int n = int.Parse(fr[0].Groups[1].Value);
                    int m = int.Parse(fr[0].Groups[2].Value);
                    if (m > 0 && n <= m)
                    {
                        if (!res.ContainsKey(Current)) order.Add(Current);
                        res[Current] = new int[] { n, m };
                        Log.Write(Current + " = " + n + "/" + m + " сервисов", RatioColor(n, m));
                        return;
                    }
                }
                Log.Write(line, ColorFor(line));
            }
        }

        public void PrintSummary()
        {
            lock (lk)
            {
                if (order.Count == 0) return;
                List<string> names = new List<string>(order);
                names.Sort(delegate(string a, string b)
                {
                    double ra = (double)res[a][0] / res[a][1];
                    double rb = (double)res[b][0] / res[b][1];
                    return rb.CompareTo(ra);
                });
                Log.Section("ИТОГИ ТЕСТОВ (конфигураций: " + names.Count + ")");
                for (int i = 0; i < names.Count; i++)
                {
                    int n = res[names[i]][0];
                    int m = res[names[i]][1];
                    Log.Write("  " + (i + 1) + ". " + names[i] + " = " + n + "/" + m + " сервисов", RatioColor(n, m));
                }
            }
        }
    }

    // service.bat "Update Hosts" opens Notepad with the new hosts text and asks to copy it by hand.
    // This does it automatically: merges the text into the system hosts file and closes the windows.
    static class HostsFix
    {
        static string HostsPath
        {
            get { return Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts"); }
        }

        static bool IsEtcExplorer(WinInfo w)
        {
            if (w.Cls != "CabinetWClass") return false;
            string t = w.Title.Trim();
            return t.Equals("etc", StringComparison.OrdinalIgnoreCase) || t.EndsWith("\\etc", StringComparison.OrdinalIgnoreCase);
        }

        static WinInfo FindNotepad()
        {
            foreach (WinInfo w in Win.All())
            {
                if (w.Title.IndexOf("zapret_hosts", StringComparison.OrdinalIgnoreCase) >= 0) return w;
                if (w.Cls == "Notepad" && w.Title.IndexOf("hosts", StringComparison.OrdinalIgnoreCase) >= 0) return w;
            }
            return null;
        }

        static string FindSource(WinInfo np)
        {
            string tmp = Path.GetTempPath();
            List<string> cand = new List<string>();
            Match m = Regex.Match(np.Title, @"^\*?\s*(.+?)\s+[-\u2013\u2014]\s+[^\-\u2013\u2014]+$");
            if (m.Success) cand.Add(m.Groups[1].Value.Trim());
            cand.Add("zapret_hosts.txt");
            foreach (string name in cand)
            {
                try
                {
                    string p = Path.Combine(tmp, name);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }

        static string ReadShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true))
                return sr.ReadToEnd();
        }

        static string NormLine(string l)
        {
            return Regex.Replace(l.Trim(), @"\s+", " ").ToLowerInvariant();
        }

        // Appends the lines of newText that are missing in the hosts file. Returns how many lines were added.
        static int Merge(string newText, out string backup)
        {
            backup = null;
            string hp = HostsPath;
            string cur = File.Exists(hp) ? ReadShared(hp) : "";
            HashSet<string> have = new HashSet<string>();
            foreach (string l in cur.Split('\n')) have.Add(NormLine(l));

            List<string> add = new List<string>();
            foreach (string l0 in newText.Split('\n'))
            {
                string l = l0.Trim();
                if (l.Length == 0) continue;
                string n = NormLine(l);
                if (have.Contains(n)) continue;
                have.Add(n);
                add.Add(l);
            }
            if (add.Count == 0) return 0;

            if (File.Exists(hp))
            {
                try
                {
                    backup = Path.Combine(Program.DataDir, "hosts_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
                    File.Copy(hp, backup, true);
                }
                catch { backup = null; }
            }

            StringBuilder sb = new StringBuilder(cur);
            if (sb.Length > 0 && !cur.EndsWith("\n")) sb.Append("\r\n");
            sb.Append("\r\n");
            foreach (string l in add) sb.Append(l).Append("\r\n");
            string final = sb.ToString();
            Program.Retry(delegate
            {
                try { File.SetAttributes(hp, FileAttributes.Normal); } catch { }
                File.WriteAllText(hp, final, new UTF8Encoding(false));
            });
            return add.Count;
        }

        static void CloseNotepad(WinInfo np)
        {
            Win.Close(np.H);
            for (int i = 0; i < 10 && Win.Alive(np.H); i++) Thread.Sleep(300);
            if (!Win.Alive(np.H)) return;
            try
            {
                using (Process p = Process.GetProcessById((int)np.Pid))
                {
                    if (p.ProcessName.StartsWith("notepad", StringComparison.OrdinalIgnoreCase)) p.Kill();
                }
            }
            catch { }
        }

        // Closes Explorer windows showing ...\drivers\etc that were not open before. Returns how many were closed.
        static int CloseExplorer(HashSet<IntPtr> before, int timeoutMs)
        {
            int closed = 0;
            DateTime t0 = DateTime.UtcNow;
            while ((DateTime.UtcNow - t0).TotalMilliseconds < timeoutMs)
            {
                foreach (WinInfo w in Win.All())
                {
                    if (IsEtcExplorer(w) && !before.Contains(w.H))
                    {
                        Win.Close(w.H);
                        before.Add(w.H);
                        closed++;
                    }
                }
                if (closed > 0) { Thread.Sleep(500); break; }
                Thread.Sleep(400);
            }
            return closed;
        }

        public static void Run(Session s)
        {
            Log.Section("Обновление hosts");
            HashSet<IntPtr> before = new HashSet<IntPtr>();
            foreach (WinInfo w in Win.All())
                if (IsEtcExplorer(w)) before.Add(w.H);

            string key = s.Key(new string[] { "Update Hosts" }, "9");
            string text = Txt.Strip(s.Do(key, 240000, Session.MenuNeed));
            bool upToDate = Regex.IsMatch(text, "up to date|up-to-date|актуальн", RegexOptions.IgnoreCase);

            WinInfo np = null;
            DateTime t0 = DateTime.UtcNow;
            while ((DateTime.UtcNow - t0).TotalSeconds < 12)
            {
                np = FindNotepad();
                if (np != null || upToDate) break;
                Thread.Sleep(500);
            }

            if (np == null)
            {
                CloseExplorer(before, 1500);
                if (upToDate) Log.Write("Hosts уже установлен и актуален.", ConsoleColor.Green);
                else Log.Write("Блокнот с hosts не появился - считаю, что hosts уже в порядке.", ConsoleColor.Yellow);
                return;
            }

            Log.Write("Hosts не установлен - открылся Блокнот, переношу его текст в системный hosts...", ConsoleColor.Yellow);
            string src = FindSource(np);
            if (src == null)
            {
                Log.Write("Не смог найти файл, открытый в Блокноте. Окна оставил открытыми - вставь текст в hosts вручную.", ConsoleColor.Red);
                return;
            }

            int added;
            string backup;
            try
            {
                added = Merge(ReadShared(src), out backup);
            }
            catch (Exception ex)
            {
                Log.Write("Не смог записать hosts: " + ex.Message + " (окна оставил открытыми - вставь текст вручную).", ConsoleColor.Red);
                return;
            }

            if (added > 0)
            {
                Log.Write("Hosts записан: добавлено строк " + added + ".", ConsoleColor.Green);
                if (backup != null) Log.Write("   резервная копия старого hosts: " + backup, ConsoleColor.Gray);
                Program.RunQuiet("ipconfig.exe", "/flushdns");
            }
            else
            {
                Log.Write("Все строки уже были в hosts, менять нечего.", ConsoleColor.Green);
            }

            CloseNotepad(np);
            int ex2 = CloseExplorer(before, 5000);
            Log.Write("Окно Блокнота" + (ex2 > 0 ? " и окно Проводника" : "") + " закрыто.", ConsoleColor.Green);
        }
    }

    static class Program
    {
        static string AppDir;
        internal static string DataDir;

        static int Main(string[] args)
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            AppDir = Path.GetDirectoryName(exe);
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZapretUpdater");
            try { Directory.CreateDirectory(DataDir); } catch { DataDir = Path.GetTempPath(); }
            Log.Init(DataDir);
            Native.DisableQuickEdit();
            Log.Verbose = Array.IndexOf(args, "--verbose") >= 0;
            bool wait = Array.IndexOf(args, "--nowait") < 0;
            try { Console.Title = "Zapret Updater by jaketea"; } catch { }
            Log.Banner();
            int code = 0;
            Stopwatch total = Stopwatch.StartNew();
            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                Log.Write("ОШИБКА: " + ex.Message);
                Log.RawFile(ex.ToString() + "\r\n");
                Log.Write("Подробности в файле: " + Log.FilePath);
                code = 1;
            }
            Log.Blank();
            Log.Write("Всего прошло: " + total.Elapsed.ToString(@"mm\:ss"), ConsoleColor.Gray);
            Log.Plain("Zapret Updater by jaketea", ConsoleColor.DarkYellow);
            Log.Flush();
            if (wait)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("Нажми Enter для выхода...");
                Console.ResetColor();
                Console.ReadLine();
            }
            return code;
        }

        static void Run(string[] args)
        {
            if (!IsAdmin()) throw new Exception("Нужны права администратора (ПКМ по exe -> Запуск от имени администратора).");
            bool force = Array.IndexOf(args, "--force") >= 0;
            bool forceTests = Array.IndexOf(args, "--test") >= 0;

            string dir = ResolveDir(args);
            Log.Write("Папка zapret: " + dir);

            // 1. Versions
            string local = ReadLocalVersion(dir);
            Log.Section("Проверка версии");
            Log.Write("Проверяю последнюю версию на GitHub...");
            string json = HttpGet("https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest");
            Match mt = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!mt.Success) throw new Exception("Не смог прочитать версию из ответа GitHub.");
            string latest = mt.Groups[1].Value;
            string zipUrl = null;
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                if (m.Groups[1].Value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { zipUrl = m.Groups[1].Value; break; }
            }
            if (zipUrl == null) throw new Exception("В последнем релизе нет .zip архива.");
            Log.Write("Установлена: " + (local == null ? "не найдена" : local) + " | Последняя: " + latest);
            if (local != null && SameVer(local, latest) && !force && !forceTests)
            {
                Log.Write("Версия актуальна, обновление не нужно.");
                return;
            }

            // 2. Remember current strategy, back up user lists
            string strategy = ReadStrategy();
            if (strategy != null) Log.Write("Текущая стратегия: " + strategy);
            Dictionary<string, byte[]> backup = BackupUserFiles(dir);

            // 3. Stop service, empty the folder
            Log.Section("Остановка zapret и очистка папки");
            StopZapret();
            PrepareDir(dir);

            // 4. Download, unpack without nested folder
            Log.Section("Загрузка " + latest);
            string zip = Path.Combine(Path.GetTempPath(), "zapret_" + Guid.NewGuid().ToString("N") + ".zip");
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "zapret-updater";
                wc.DownloadFile(zipUrl, zip);
            }
            string tmp = Path.Combine(Path.GetTempPath(), "zapret_x_" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(zip, tmp);
            string root = tmp;
            string[] entries = Directory.GetFileSystemEntries(tmp);
            if (entries.Length == 1 && Directory.Exists(entries[0])) root = entries[0];
            CopyDir(root, dir);
            try { File.Delete(zip); Directory.Delete(tmp, true); } catch { }
            if (!File.Exists(Path.Combine(dir, "service.bat"))) throw new Exception("После распаковки в папке нет service.bat.");
            RestoreUserFiles(dir, backup);
            Log.Write("Файлы обновлены.");

            // 5. service.bat: game filter, ipset, hosts, install, status
            Session s = null;
            bool running = false;
            try
            {
                s = new Session(dir);
                SetGameFilter(s);
                Log.Section("Обновление IPSet");
                s.Do(s.Key(new string[] { "Update IPSet" }, "8"), 240000, Session.MenuNeed);
                Log.Write("IPSet обновлён.");
                HostsFix.Run(s);

                if (strategy != null && !forceTests)
                {
                    Log.Write("Устанавливаю прежнюю стратегию: " + strategy);
                    InstallStrategy(s, strategy);
                }
                CheckStatus(s);
                running = ServiceState("zapret") == "Running";

                if (!running)
                {
                    Log.Write("zapret не установлен/не запущен - запускаю тесты (5-15 минут)...");
                    s.Close();
                    s.Dispose();
                    s = null;
                    string best = RunTests(dir);
                    if (best == null)
                    {
                        Log.Write("Не удалось определить лучшую конфигурацию. Открываю service.bat - выбери и установи вручную (пункт 1).");
                        OpenManual(dir);
                        return;
                    }
                    Log.Write("Лучшая конфигурация: " + best);
                    s = new Session(dir);
                    if (!InstallStrategy(s, best))
                    {
                        s.Close();
                        OpenManual(dir);
                        return;
                    }
                    CheckStatus(s);
                    running = ServiceState("zapret") == "Running";
                }
                s.Close();
            }
            finally
            {
                if (s != null) s.Dispose();
            }

            FinalReport();
            if (running)
            {
                Log.Write("ГОТОВО: zapret установлен и работает (версия " + latest + ").");
                ReadStrategy(); // saves the installed strategy for the next run
            }
            else
            {
                Log.Write("zapret НЕ запущен. Открой service.bat и выбери стратегию вручную.");
            }
        }

        // ---------- steps ----------

        static void SetGameFilter(Session s)
        {
            Log.Section("Game Filter");
            string key = s.Key(new string[] { "Game Filter" }, "4");
            string text = s.Do(key, 60000, null);
            if (Regex.IsMatch(Txt.Strip(text), "Install Service", RegexOptions.IgnoreCase))
            {
                Log.Write("Game Filter переключён (подменю не появилось - проверь режим вручную).");
                return;
            }
            string pick = null;
            foreach (KeyValuePair<string, string> kv in Txt.ParseMenu(text))
            {
                string t = kv.Value.ToLowerInvariant();
                if (t.Contains("tcp") && t.Contains("udp") && !t.Contains("disable")) { pick = kv.Key; break; }
            }
            if (pick == null) throw new Exception("Не нашёл вариант 'TCP and UDP' в подменю Game Filter (см. лог).");
            s.Do(pick, 60000, Session.MenuNeed);
            Log.Write("Game Filter: TCP and UDP.");
        }

        static bool InstallStrategy(Session s, string strategy)
        {
            Log.Section("Установка стратегии");
            string key = s.Key(new string[] { "Install Service" }, "1");
            string text = s.Do(key, 60000, null);
            List<KeyValuePair<string, string>> list = Txt.ParseMenu(text);
            string want = Txt.Norm(strategy);
            string idx = null;
            foreach (KeyValuePair<string, string> kv in list)
                if (Txt.Norm(kv.Value) == want) { idx = kv.Key; break; }
            if (idx == null)
                foreach (KeyValuePair<string, string> kv in list)
                    if (Txt.Norm(kv.Value).Contains(want)) { idx = kv.Key; break; }
            if (idx == null)
            {
                Log.Write("Стратегия '" + strategy + "' не найдена в новой версии. Доступные:");
                foreach (KeyValuePair<string, string> kv in list) Log.Write("   " + kv.Key + ". " + kv.Value);
                return false;
            }
            s.Do(idx, 240000, Session.MenuNeed);
            Log.Write("Установлена стратегия: " + strategy);
            return true;
        }

        static void CheckStatus(Session s)
        {
            string text = s.Do(s.Key(new string[] { "Service Status", "Check Service" }, "3"), 60000, Session.MenuNeed);
            Log.Section("Check Service Status (service.bat)");
            foreach (string raw in Txt.Strip(text).Split('\n'))
            {
                string l = raw.Replace("\r", "").Trim();
                if (l.Length == 0) continue;
                if (Regex.IsMatch(l, @"^\[?\d+\]?[\.\):\-]?\s+\S")) break; // menu re-print starts here
                ConsoleColor c = ConsoleColor.Gray;
                if (Regex.IsMatch(l, @"(?i)not (installed|running|found)|stopped|fail|error|не установлен|не запущен")) c = ConsoleColor.Red;
                else if (Regex.IsMatch(l, @"(?i)running|installed|active|запущен|установлен")) c = ConsoleColor.Green;
                Log.Write("   " + l, c);
            }
            string st = ServiceState("zapret");
            Log.Write("Статус службы zapret: " + (st == null ? "не установлена" : st), st == "Running" ? ConsoleColor.Green : ConsoleColor.Red);
        }

        static void FinalReport()
        {
            string st = ServiceState("zapret");
            string strat = ReadStrategy();
            bool winws = Process.GetProcessesByName("winws").Length > 0;
            Log.Section("ИТОГОВАЯ ПРОВЕРКА");
            Log.Write("Служба zapret: " + (st == null ? "не установлена" : st), st == "Running" ? ConsoleColor.Green : ConsoleColor.Red);
            Log.Write("Установленная стратегия: " + (strat == null ? "не определена" : strat), strat != null ? ConsoleColor.Green : ConsoleColor.Red);
            Log.Write("Процесс winws.exe: " + (winws ? "запущен" : "не запущен"), winws ? ConsoleColor.Green : ConsoleColor.Red);
        }

        static string RunTests(string dir)
        {
            string utils = Path.Combine(dir, "utils");
            string[] cand = Directory.Exists(utils) ? Directory.GetFiles(utils, "test*.ps1") : new string[0];
            if (cand.Length == 0) { Log.Write("Не нашёл utils\\test*.ps1"); return null; }
            DateTime started = DateTime.Now.AddSeconds(-5);
            string text = "";
            TestView view = new TestView();
            Stopwatch sw = Stopwatch.StartNew();
            Log.Section("Тесты конфигураций");
            Log.Write("Результаты будут появляться ниже по мере выполнения.", ConsoleColor.Cyan);
            Action<string> onLine = Log.Verbose ? null : new Action<string>(view.Handle);
            using (Term t = new Term("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + cand[0] + "\"", dir, onLine))
            {
                t.WaitPrompt(180000, 1500, null);
                Log.Write("-> выбираю тип тестов: 1 (Standard tests)", ConsoleColor.Yellow);
                t.Send("1"); // Standard tests
                t.WaitPrompt(180000, 1500, null);
                Log.Write("-> выбираю режим: 1 (All configs)", ConsoleColor.Yellow);
                t.Send("1"); // All configs
                DateTime lastBeat = DateTime.UtcNow;
                bool done = false;
                while (!done)
                {
                    done = t.WaitExit(5000);
                    if (done) break;
                    string allTxt = Txt.Strip(t.AllText());
                    if (allTxt.Length > 400) allTxt = allTxt.Substring(allTxt.Length - 400);
                    if (Regex.IsMatch(allTxt, @"(?i)press any key|нажмите любую"))
                    {
                        Thread.Sleep(1500);
                        Log.Write("Тесты завершены, закрываю окно скрипта.", ConsoleColor.Green);
                        done = true;
                        break;
                    }
                    if (sw.Elapsed.TotalMinutes >= 60) break;
                    long idleMs = (DateTime.UtcNow.Ticks - Interlocked.Read(ref view.LastTick)) / 10000;
                    if (idleMs > 30000 && (DateTime.UtcNow - lastBeat).TotalSeconds >= 30)
                    {
                        lastBeat = DateTime.UtcNow;
                        string cur = view.Current;
                        Log.Write("... тесты идут, прошло " + sw.Elapsed.ToString(@"mm\:ss") + (cur != null ? ", сейчас: " + cur : ""), ConsoleColor.Gray);
                    }
                }
                if (!done) Log.Write("Тесты не завершились за час, прерываю.", ConsoleColor.Red);
                text = t.AllText();
            }
            view.PrintSummary();
            Log.Write("Тесты заняли " + sw.Elapsed.ToString(@"mm\:ss"), ConsoleColor.Gray);
            string fileText = "";
            string resDir = Path.Combine(utils, "test results");
            if (Directory.Exists(resDir))
            {
                string newest = null;
                DateTime nt = DateTime.MinValue;
                foreach (string f in Directory.GetFiles(resDir))
                {
                    DateTime w = File.GetLastWriteTime(f);
                    if (w >= started && w > nt) { nt = w; newest = f; }
                }
                if (newest != null)
                {
                    try { fileText = File.ReadAllText(newest); } catch { }
                    Log.Write("Результаты тестов: " + newest);
                }
            }
            return FindBest(text + "\n" + fileText);
        }

        static string FindBest(string text)
        {
            text = Txt.Strip(text);
            string res = null;
            foreach (Match m in Regex.Matches(text, @"(?im)best[^\r\n]*?(general[^\r\n]*?\.bat)"))
                res = m.Groups[1].Value.Trim();
            if (res != null) return res;
            foreach (Match m in Regex.Matches(text, @"(?im)best[^\r\n:]*:\s*([^\r\n]+)"))
            {
                string v = m.Groups[1].Value.Trim();
                if (v.IndexOf("general", StringComparison.OrdinalIgnoreCase) >= 0) res = v;
            }
            return res;
        }

        static void OpenManual(string dir)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/k service.bat admin");
                psi.WorkingDirectory = dir;
                Process.Start(psi);
            }
            catch { }
        }

        // ---------- helpers ----------

        static bool IsAdmin()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static string ResolveDir(string[] args)
        {
            string cfg = Path.Combine(DataDir, "zapret-updater.cfg");
            string desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "zapret");
            bool fresh = Array.IndexOf(args, "--fresh") >= 0;
            string dir = null;
            if (fresh)
            {
                dir = desktopDir;
                Log.Write("Режим --fresh: установка с нуля на рабочий стол.");
            }
            else
            {
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "--dir") dir = args[i + 1];
                if (dir == null && File.Exists(cfg))
                {
                    try { dir = File.ReadAllText(cfg).Trim(); } catch { }
                    if (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "service.bat"))) dir = null;
                }
                if (string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(AppDir, "service.bat"))) dir = AppDir;
                    else if (File.Exists(@"C:\zapret\service.bat")) dir = @"C:\zapret";
                    else
                    {
                        dir = desktopDir;
                        Log.Write("zapret не найден на компьютере - устанавливаю с нуля в папку zapret на рабочем столе.");
                    }
                }
            }
            dir = Path.GetFullPath(dir).TrimEnd('\\');
            try { File.WriteAllText(cfg, dir); } catch { }
            return dir;
        }

        static string ReadLocalVersion(string dir)
        {
            string f = Path.Combine(dir, "service.bat");
            if (!File.Exists(f)) return null;
            foreach (string line in File.ReadAllLines(f))
            {
                Match m = Regex.Match(line, "LOCAL_VERSION\\s*=\\s*([^\\s\"]+)", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }

        static bool SameVer(string a, string b)
        {
            return string.Equals(a.Trim().TrimStart('v', 'V'), b.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
        }

        static string ReadStrategy()
        {
            string reg = null;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret"))
                {
                    if (k != null) reg = k.GetValue("zapret-discord-youtube") as string;
                }
            }
            catch { }
            string f = Path.Combine(DataDir, "zapret-updater.strategy");
            if (!string.IsNullOrEmpty(reg))
            {
                try { File.WriteAllText(f, reg); } catch { }
                return reg;
            }
            try { if (File.Exists(f)) return File.ReadAllText(f).Trim(); } catch { }
            return null;
        }

        static string ServiceState(string name)
        {
            try
            {
                using (ServiceController sc = new ServiceController(name))
                    return sc.Status.ToString();
            }
            catch { return null; }
        }

        static string HttpGet(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "zapret-updater";
            r.Accept = "application/vnd.github+json";
            r.Timeout = 30000;
            using (WebResponse resp = r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static void StopZapret()
        {
            Log.Write("Останавливаю службу zapret...");
            string[] names = new string[] { "zapret", "WinDivert", "WinDivert14" };
            foreach (string n in names) RunQuiet("sc.exe", "stop " + n);
            foreach (Process p in Process.GetProcessesByName("winws"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }
            Thread.Sleep(1500);
            foreach (string n in names) RunQuiet("sc.exe", "delete " + n);
            Thread.Sleep(1000);
        }

        internal static void RunQuiet(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(20000);
                }
            }
            catch { }
        }

        static bool IsProtected(string path)
        {
            return Path.GetFileName(path).StartsWith("zapret-updater", StringComparison.OrdinalIgnoreCase);
        }

        static void PrepareDir(string dir)
        {
            string full = Path.GetFullPath(dir).TrimEnd('\\');
            if (full.Length <= 3) throw new Exception("Нельзя очищать корень диска: " + full);
            string[] bad = new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            foreach (string b in bad)
                if (!string.IsNullOrEmpty(b) && string.Equals(b.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Отказываюсь очищать системную/пользовательскую папку: " + full);

            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); return; }

            bool hasService = File.Exists(Path.Combine(dir, "service.bat"));
            int foreign = 0;
            foreach (string e in Directory.GetFileSystemEntries(dir))
                if (!IsProtected(e)) foreign++;
            if (!hasService && foreign > 0)
                Log.Write("В папке " + full + " нет service.bat - удаляю всё содержимое и ставлю zapret заново.", ConsoleColor.Yellow);

            Log.Write("Очищаю папку...");
            foreach (string f in Directory.GetFiles(dir))
            {
                if (IsProtected(f)) continue;
                string ff = f;
                Retry(delegate { File.SetAttributes(ff, FileAttributes.Normal); File.Delete(ff); });
            }
            foreach (string d in Directory.GetDirectories(dir))
            {
                string dd = d;
                Retry(delegate { ClearAttrs(dd); Directory.Delete(dd, true); });
            }
        }

        internal static void Retry(Action a)
        {
            for (int i = 0; i < 6; i++)
            {
                try { a(); return; }
                catch (Exception)
                {
                    if (i == 5) throw;
                    Thread.Sleep(1000);
                }
            }
        }

        static void ClearAttrs(string d)
        {
            foreach (string f in Directory.GetFiles(d)) File.SetAttributes(f, FileAttributes.Normal);
            foreach (string s in Directory.GetDirectories(d)) ClearAttrs(s);
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        static Dictionary<string, byte[]> BackupUserFiles(string dir)
        {
            Dictionary<string, byte[]> d = new Dictionary<string, byte[]>();
            string lists = Path.Combine(dir, "lists");
            if (Directory.Exists(lists))
                foreach (string f in Directory.GetFiles(lists, "*user*.txt"))
                    d[Path.GetFileName(f)] = File.ReadAllBytes(f);
            if (d.Count > 0) Log.Write("Сохранил пользовательские списки: " + d.Count);
            return d;
        }

        static void RestoreUserFiles(string dir, Dictionary<string, byte[]> d)
        {
            if (d.Count == 0) return;
            string lists = Path.Combine(dir, "lists");
            Directory.CreateDirectory(lists);
            foreach (KeyValuePair<string, byte[]> kv in d)
                File.WriteAllBytes(Path.Combine(lists, kv.Key), kv.Value);
            Log.Write("Вернул пользовательские списки.");
        }
    }
}
