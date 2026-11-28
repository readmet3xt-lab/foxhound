// The launcher: one window for everything a player sets before the title starts.
//
//  - asks once for the folder of the player's own copy of the title (a folder is enough: the
//    executable in it is found);
//  - settings: resolution and console profile, DLSS, frame generation, display mode, the card's
//    memory limit, texture loading, frame-rate target, priority, log, further switches;
//  - before the title starts, behind a loading screen: the title's shaders are translated (once
//    per build of the layer and set of switches) and the pipelines earlier runs asked for are
//    compiled, with the very switches of the run;
//  - starts the title, makes its window what "Display" says, and stays in the background:
//    F1 while playing opens the settings (each marked "at once" or "after restart"), with
//    "Restart the game" and "Quit the game";
//  - makes a desktop shortcut on the first start.
//
// It only starts victory.exe (beside it) with environment switches; nothing of the title is in
// it. Settings: %LOCALAPPDATA%\foxhound\launcher.ini. Logs: %LOCALAPPDATA%\foxhound\logs.
//
// Built by scripts\build-launcher.ps1 with the C# compiler of the .NET Framework that every
// Windows 10/11 has (C# 5: no newer syntax here).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Foxhound launcher")]
[assembly: AssemblyProduct("Foxhound")]
[assembly: AssemblyDescription("Settings and start for the foxhound layer")]
[assembly: AssemblyCompany("readmet3xt-lab")]
[assembly: AssemblyCopyright("GPL-3.0-or-later")]
[assembly: AssemblyVersion("0.2.3.0")]
[assembly: AssemblyFileVersion("0.2.3.0")]

namespace Foxhound
{
    // The launcher reads no keys, changes no other program's window and starts no system tools: the
    // game's own window tells it of F1 and F2 (VICTORY_LAUNCHER_KEYS) and takes its display mode itself
    // (VICTORY_DISPLAY). Those things, done from outside the game, made antivirus programs flag it.
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("dxgi.dll")] public static extern int CreateDXGIFactory1([In] ref Guid iid, out IDxgiFactory1 factory);
    }

    // DXGI's list of graphics adapters (only the calls the card choice makes; the others hold their
    // places in the interfaces' tables).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("29038f61-3839-4626-91fd-086879011a05")]
    interface IDxgiAdapter1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport();
        [PreserveSig] int GetDesc1(out DxgiAdapterDesc1 desc);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    interface IDxgiFactory1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint index, out IDxgiAdapter1 adapter);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLinkObject { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    // launcher.ini: key=value lines; in a value a line break is written as \n, a backslash as two.
    class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "foxhound");
        public static readonly string LogDir = Path.Combine(Dir, "logs");
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        static string File_ { get { return Path.Combine(Dir, "launcher.ini"); } }

        public static Store Load()
        {
            Store s = new Store();
            try
            {
                foreach (string line in File.ReadAllLines(File_, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) s.values[line.Substring(0, eq)] = Unescape(line.Substring(eq + 1));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        // \\ is a backslash, \n a line break (a path may hold "\n" itself).
        static string Unescape(string v)
        {
            StringBuilder b = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length) { i++; b.Append(v[i] == 'n' ? '\n' : v[i]); }
                else b.Append(v[i]);
            }
            return b.ToString();
        }

        public string Get(string key, string otherwise)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : otherwise;
        }

        public bool Has(string key) { return values.ContainsKey(key); }

        public void Set(string key, string value) { values[key] = value ?? ""; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> p in values) lines.Add(p.Key + "=" + p.Value.Replace("\r", "").Replace("\\", "\\\\").Replace("\n", "\\n"));
                lines.Sort(StringComparer.Ordinal);
                File.WriteAllLines(File_, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Whether Windows can give programs the memory the game needs: RAM and page file together (the
    // commit limit). While the game runs the test PC commits about 48 GB in all (31 GB of RAM, the rest
    // in its page file); past the limit allocations fail and the title stops ("Failed to allocate ...").
    // A page file whose size Windows manages grows as needed: no warning then.
    static class MemoryCheck
    {
        [StructLayout(LayoutKind.Sequential)]
        class Status
        {
            public uint Length = 64;
            public uint Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtended;
        }

        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] Status s);

        // RAM and page file at least, in GB.
        const double Needed = 52;

        // Null when there is enough, else the warning's text.
        public static string Warning()
        {
            Status s = new Status();
            if (!GlobalMemoryStatusEx(s)) return null;
            double ram = s.TotalPhys / 1073741824.0;
            double most = MostPageFileGb();
            if (most < 0 || ram + most >= Needed) return null;
            string file = most == 0 ? "no page file" : string.Format("a page file of at most {0:0} GB", most);
            return string.Format(
                "Windows can give programs at most {0:0} GB of memory on this PC: {1:0} GB of RAM and {2}.\n\n" +
                "While the game runs, the test PC uses about 48 GB of it. When Windows runs out, the game stops, often with \"Failed to allocate\".\n\n" +
                "Fix: Start, type \"View advanced system settings\", then Performance: Settings, Advanced, Virtual memory: Change. Tick \"Automatically manage paging file size for all drives\" (or set a maximum of at least {3:0} MB), then restart Windows.\n\n" +
                "Play anyway?",
                ram + most, ram, file, Math.Ceiling(Needed - ram) * 1024);
        }

        // The most the page files may grow to, in GB: -1 when Windows manages a size (it grows as
        // needed) or the setting cannot be read.
        static double MostPageFileGb()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
                {
                    string[] files = k == null ? null : k.GetValue("PagingFiles") as string[];
                    if (files == null) return -1;
                    double sum = 0;
                    foreach (string f in files)
                    {
                        string[] p = f.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length == 0) continue;
                        if (p[0].StartsWith("?") || p.Length < 3) return -1;    // managed by Windows
                        long min, max;
                        if (!long.TryParse(p[1], out min) || !long.TryParse(p[2], out max)) return -1;
                        if (min == 0 && max == 0) return -1;                    // "System managed size"
                        sum += max / 1024.0;
                    }
                    return sum;                                                 // 0: no page file
                }
            }
            catch (Exception) { return -1; }
        }
    }

    // The graphics card a run uses, by the layer's rule: the first hardware adapter whose name holds
    // VICTORY_ADAPTER (a further switch or the environment), else the one with the most memory of its own.
    // The choice is DXGI's: the registry's figure for an integrated GPU can be larger than a graphics
    // card's (issue #14). The size is the registry's for the chosen card where it has one.
    class Card
    {
        public string Name = "";
        public double Gb;   // 0: not found
        public bool Nvidia { get { return Name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0; } }
        // DLSS Multi Frame Generation (3x, 4x): GeForce RTX 50 cards.
        public bool Rtx50 { get { return Nvidia && System.Text.RegularExpressions.Regex.IsMatch(Name, @"RTX\s*50\d\d"); } }
        public bool Amd { get { return Name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || Name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0; } }

        public static Card Detect()
        {
            string named = Environment.GetEnvironmentVariable("VICTORY_ADAPTER") ?? "";
            foreach (string raw in Store.Load().Get("extra", "").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith("#")) continue;
                string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                if (key == "VICTORY_ADAPTER" || key == "ADAPTER") named = line.Substring(eq + 1).Trim();
            }
            named = named.Trim();
            Card best = new Card();
            try
            {
                Guid iid = typeof(IDxgiFactory1).GUID;
                IDxgiFactory1 factory;
                if (Native.CreateDXGIFactory1(ref iid, out factory) == 0 && factory != null)
                {
                    long most = -1;
                    for (uint i = 0; i < 64; i++)
                    {
                        IDxgiAdapter1 a;
                        if (factory.EnumAdapters1(i, out a) != 0 || a == null) break;
                        DxgiAdapterDesc1 d;
                        int hr = a.GetDesc1(out d);
                        Marshal.ReleaseComObject(a);
                        if (hr != 0 || (d.Flags & 2) != 0) continue;   // 2: the software adapter
                        long own = (long)d.DedicatedVideoMemory.ToUInt64();
                        string name = (d.Description ?? "").Trim();
                        if (named.Length > 0 && name.IndexOf(named, StringComparison.OrdinalIgnoreCase) >= 0) { best.Name = name; best.Gb = own / 1073741824.0; break; }
                        if (own > most) { most = own; best.Name = name; best.Gb = own / 1073741824.0; }
                    }
                    Marshal.ReleaseComObject(factory);
                }
            }
            catch (Exception) { }
            if (best.Name.Length == 0) return FromRegistry(null);
            Card listed = FromRegistry(best.Name);
            if (listed.Gb > 0) best.Gb = listed.Gb;
            return best;
        }

        // The registry's cards: the one named (null: the one with the most memory).
        static Card FromRegistry(string only)
        {
            Card best = new Card();
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return best;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                object size = k.GetValue("HardwareInformation.qwMemorySize");
                                object name = k.GetValue("DriverDesc");
                                if (size == null || name == null) continue;
                                if (only != null && !string.Equals(name.ToString().Trim(), only, StringComparison.OrdinalIgnoreCase)) continue;
                                double gb = Convert.ToInt64(size) / 1073741824.0;
                                if (gb > best.Gb) { best.Gb = gb; best.Name = name.ToString(); }
                            }
                        }
                        catch (Exception) { }   // subkeys this user may not open
                    }
                }
            }
            catch (Exception) { }
            return best;
        }
    }

    class Opt
    {
        public string Key, Label, Otherwise, Hint;
        public string[] Values, Names;
        public bool Live;   // takes effect at once, in a running game
        public Opt(string key, string label, bool live, string otherwise, string[] values, string[] names, string hint)
        {
            Key = key; Label = label; Live = live; Otherwise = otherwise; Values = values; Names = names; Hint = hint;
        }
    }

    static class Settings
    {
        public static Opt[] Options(Card card)
        {
            string cardText = card.Gb > 0 ? string.Format("{0}, {1:0.#} GB", card.Name, card.Gb) : "not found";
            return new Opt[] {
                new Opt("resolution", "Resolution", false, "1440",
                    new string[] { "1080", "1440", "4k" },
                    new string[] { "1080p class: Series S profile (lightest)", "1440p: Series X profile (tested)", "4K: Series X profile" },
                    "The console profile the title is told it runs on, and its output size. Series S: the title's own low-resolution scene (1080p output), the lightest on the card's memory; for cards under 12 GB, with DLSS and Frame Generation as on Series X. 1440p on the Series X profile is the tested choice. 4K is the profile's stock output (scene 1440p unless DLSS sets it)."),
                new Opt("dlss", "DLSS", false, "quality",
                    new string[] { "off", "dlaa", "quality", "balanced", "performance" },
                    new string[] { "Off (the title's own upscaling)", "DLAA (scene at full size)", "Quality (scene at 67%)", "Balanced (scene at 58%)", "Performance (scene at 50%)" },
                    "NVIDIA RTX cards, both profiles (on Series S it takes the place of the title's own upscaling to 1080p). Needs the folder of NVIDIA's Streamline SDK (set above; NVIDIA's licence does not let it be shipped here). A smaller scene is less work for the card; where the processor is the limit it changes nothing."),
                new Opt("framegen", "Frame Generation", false, "0",
                    card.Rtx50 ? new string[] { "0", "1", "3", "4" } : new string[] { "0", "1" },
                    card.Rtx50
                        ? new string[] { "Off", "2x (needs DLSS)", "3x (Multi Frame Generation, untested)", "4x (Multi Frame Generation, untested)" }
                        : new string[] { "Off", "On (RTX 40 or later, needs DLSS)" },
                    "DLSS Frame Generation: frames between the title's are made by the card (smoother, not more responsive; best from 30 fps up). Needs DLSS on and hardware-accelerated GPU scheduling in Windows. 3x and 4x (RTX 50 only) are DLSS Multi Frame Generation, not tested here. Experimental: reports of the title stopping at its first loading with DLSS on and the 60 fps frame rate (RTX 4090, RTX 5070 Ti); with 30 fps none so far."),
                new Opt("display", "Display", false, "fullscreen",
                    new string[] { "fullscreen", "borderless", "windowed" },
                    new string[] { "Fullscreen (borderless, fills the monitor)", "Borderless window", "Window with a title bar (shows fps)" },
                    "How the game's window is shown (the game sets it when it starts). Exclusive fullscreen is not available."),
                new Opt("vram", "VRAM limit", false, "auto",
                    new string[] { "auto", "6", "8", "10", "12", "16", "20", "24" },
                    new string[] { "Auto (what Windows gives; card: " + cardText + ")", "6 GB", "8 GB", "10 GB", "12 GB", "16 GB", "20 GB", "24 GB" },
                    "The most of the card's memory the layer plans with. Auto: the budget Windows reports. A lower limit leaves memory to other programs and keeps a small card from overflowing into the PC's memory (which is slow). The tested setup uses 11.6 GB at 1440p; under 12 GB choose the Series S profile."),
                new Opt("textures", "Texture loading", false, "auto",
                    new string[] { "auto", "sharp", "ondemand" },
                    new string[] { "Auto (all detail)", "All detail loaded (sharp)", "Detail on demand (less VRAM, blurry or missing textures)" },
                    "All detail loaded: every texture level is put on the card. On demand: levels are loaded as they are needed; less memory, but on a full card textures stay blurry or missing (Series S test at 6 GB, 2026-11-28). Auto: all detail on every card."),
                new Opt("ambient", "Dark areas", false, "0.8",
                    new string[] { "0.6", "0.8", "1.0", "1.3", "1.6" },
                    new string[] { "Darker (0.6)", "As tested (0.8)", "Brighter (1.0)", "Bright (1.3)", "Brightest (1.6)" },
                    "Ray tracing is off: an estimate stands in for the light that reaches rooms and shadows indirectly. This is the share of that estimate that is used; a higher value lifts dark rooms and shadows."),
                new Opt("fps", "Frame rate", false, "title",
                    new string[] { "title", "40", "60" },
                    new string[] { "30 fps (the title's own)", "40 fps", "60 fps" },
                    "The rate the title is told to run at (its own setting is 30; its frame time, its limiter and its simulation step follow, so the game is not faster, only smoother). The layer needs about 26 ms a frame in gameplay on the tested PC, so 40 is near what it reaches and 60 is not reached yet; frames then come as fast as they are made. Frame Generation doubles what is shown. Experimental: with 60 fps and DLSS two runs stopped where the world first loads (\"Failed to allocate upload buffer\", issue #9); the log now names what was refused."),
                new Opt("profile", "Profile", false, "1",
                    new string[] { "1", "2", "3", "4" },
                    new string[] { "Profile 1 (the saves so far)", "Profile 2", "Profile 3", "Profile 4" },
                    "Separate players on one PC: each profile has its own saves and game settings (a data folder of its own under %LOCALAPPDATA%\victory), and its own name in the game (Player, Player 2, ...). Profile 1 is the folder every earlier version used."),
                new Opt("priority", "Process priority", true, "normal",
                    new string[] { "normal", "above", "high" },
                    new string[] { "Normal", "Above normal", "High" },
                    "The priority Windows gives the game's process. Changes at once. Higher can steady frame times when other programs are busy."),
                new Opt("build", "Layer build", false, "release",
                    new string[] { "release", "diag" },
                    new string[] { "Release", "Diagnostics (slower; F2 saves the frame)" },
                    "Diagnostics: for finding faults. Needs victory-diag.exe beside the launcher (built from the source with the diagnostics feature; not in the release). While playing, F2 saves the frame being drawn, with everything needed to look at it later, under %LOCALAPPDATA%\\foxhound\\captures (several GB each)."),
                new Opt("log", "Log", false, "full",
                    new string[] { "full", "warn" },
                    new string[] { "Full (for bug reports)", "Warnings only" },
                    "What a run writes to its log file (about 50 MB an hour when full; the last 8 logs are kept)."),
            };
        }

        // The switch set tested on an RTX 4070 SUPER (see scripts\play-foxhound.ps1 for what each is).
        static readonly string[] Base = {
            "VICTORY_THIN=1", "VICTORY_JOIN_PARTS=8", "VICTORY_RELAY=2", "VICTORY_RELAY_COPY=1", "VICTORY_WIDE_BACK=1",
            "VICTORY_CARD_REHASH_RUNS=1", "VICTORY_JOIN_COPIES=1", "VICTORY_CARD_STEPS=2", "VICTORY_CARD_AWAKE=1",
            "VICTORY_CARD_BUFFERS_MIN_KB=64", "VICTORY_INDIRECT_WINDOW=1", "VICTORY_INDIRECT_BATCH=2", "VICTORY_UPLOAD_AHEAD=1",
            "VICTORY_SHOW_AFTER=1", "VICTORY_MAP_THREADS=1", "VICTORY_WC_NO_BACK=1", "VICTORY_WC_NO_BACK_MIN_KB=1024",
            "VICTORY_SOFT_VIEWS=copies", "VICTORY_TEXTURE_POOL=1", "VICTORY_RIPE_BLOCKS=0", "VICTORY_TEXTURE_NO_WAIT=1",
            "VICTORY_TEXTURE_CATCHUP=0", "VICTORY_TEXTURE_STAGE_MS=16", "VICTORY_LEVELS_ON_DEMAND=0", "VICTORY_PRELUDE_UPLOADS=1",
            "VICTORY_SHOW_NEW_LATER=1", "VICTORY_POOL_SMALL=0", "VICTORY_TABLED_REREAD=1", "VICTORY_QUEUE_HOST_TEXTURES=0",
            // A frame's texture passes within 8 ms together; helpers woken at each submission's start (PR #13).
            "VICTORY_TEXSPK_PASS_MS=8", "VICTORY_PASSES_HOT=1",
            // Write faults open guarded blocks without the guards' lock (issue #8): the title's threads
            // waited 13-17 ms a frame for it; frame time 2-3 % lower (runs mem5/mem6).
            "VICTORY_FAULT_UNLOCKED=1",
            "VICTORY_HEAP_SYNC_FIRST=1", "VICTORY_SYNC_IN_PLACE=1", "VICTORY_SPANS_QUICK=1", "VICTORY_SYNC_FINE=1",
            "VICTORY_HEAP_SYNC_AT_SUBMIT=1", "VICTORY_HELPER_PRIORITY=1", "VICTORY_AMBIENT=0.8", "VICTORY_AMBIENT_AIR=1.5",
            "VICTORY_AMBIENT_SPECULAR=1", "VICTORY_EARLY_DEPTH=1", "VICTORY_LEAVE_OUT=02384a60fda6f2d0,731a39171b568298",
            "VICTORY_PIPELINE_BOOK=1",
        };

        // Switches that do not change a shader's translation: left out of the stamp that decides
        // whether the shaders are gone through again.
        public static readonly string[] NotOfShaders = {
            "VICTORY_DLSS", "VICTORY_DLSS_MODE", "VICTORY_FRAMEGEN", "VICTORY_STREAMLINE_DIR", "VICTORY_VRAM_BUDGET_MB",
            "VICTORY_FRAME_INTERVAL_MS", "VICTORY_RECORD", "VICTORY_LOG", "VICTORY_GAME_DIR", "VICTORY_LEVELS_ON_DEMAND", "VICTORY_RIPE_BLOCKS",
            "VICTORY_AMBIENT", "VICTORY_AMBIENT_AIR", "VICTORY_AMBIENT_SPECULAR", "VICTORY_DISPLAY", "VICTORY_LAUNCHER_KEYS", "VICTORY_CONTROLS_FILE",
            "VICTORY_PLACED_ORDER", "VICTORY_FRAMEGEN_FRAMES", "VICTORY_DATA_DIR", "VICTORY_GAMERTAG",
        };

        public static bool SeriesS(Store s) { return s.Get("resolution", "1440") == "1080"; }

        public static readonly string CaptureDir = Path.Combine(Store.Dir, "captures");

        public static bool DiagThere() { return File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "victory-diag.exe")); }

        public static bool Diag(Store s) { return s.Get("build", "release") == "diag" && DiagThere(); }

        public static string StreamlineDir(Store s)
        {
            string dir = s.Get("streamline_dir", "");
            return dir.Length > 0 && File.Exists(Path.Combine(dir, "sl.interposer.dll")) ? dir : "";
        }

        public static bool DlssUsable(Store s, Card card)
        {
            return StreamlineDir(s).Length > 0 && (card.Nvidia || card.Gb == 0);
        }

        public static Dictionary<string, string> Environment_(Store s, Card card)
        {
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string kv in Base) { int eq = kv.IndexOf('='); env[kv.Substring(0, eq)] = kv.Substring(eq + 1); }
            string res = s.Get("resolution", "1440");
            if (res == "1440") env["VICTORY_NATIVE_1440P"] = "1";
            string dlss = s.Get("dlss", "quality");
            if (dlss != "off" && DlssUsable(s, card))
            {
                env["VICTORY_DLSS"] = "1";
                env["VICTORY_DLSS_MODE"] = dlss;
                env["VICTORY_STREAMLINE_DIR"] = StreamlineDir(s);
                string fg = s.Get("framegen", "0");
                if (fg == "1" || fg == "3" || fg == "4")
                {
                    env["VICTORY_FRAMEGEN"] = "1";
                    // 3x and 4x: Multi Frame Generation, RTX 50 only (the layer asks no more than the card can).
                    if (fg != "1" && card.Rtx50) env["VICTORY_FRAMEGEN_FRAMES"] = fg;
                }
            }
            double limit;
            if (double.TryParse(s.Get("vram", "auto"), out limit)) env["VICTORY_VRAM_BUDGET_MB"] = ((int)(limit * 1024)).ToString();
            string textures = s.Get("textures", "auto");
            // (auto was "on demand under 11 GB" until 2026-11-28: on a 6 GB card it left the opening's textures and
            // models missing, ~19,600 waiting; off, as the layer's default, they load)
            if (textures == "ondemand") env["VICTORY_LEVELS_ON_DEMAND"] = "1";
            env["VICTORY_AMBIENT"] = s.Get("ambient", "0.8");
            env["VICTORY_DISPLAY"] = s.Get("display", "fullscreen");
            env["VICTORY_LAUNCHER_KEYS"] = "1";
            if (!File.Exists(ControlSet.FilePath)) new ControlSet().Save();
            env["VICTORY_CONTROLS_FILE"] = ControlSet.FilePath;
            string fps = s.Get("fps", "title");
            if (fps == "40") env["VICTORY_FRAME_INTERVAL_MS"] = "25";
            else if (fps == "60") env["VICTORY_FRAME_INTERVAL_MS"] = "16.667";
            // Profiles 2-4: saves and settings in a data folder of their own (issue #11).
            string profile = s.Get("profile", "1");
            if (profile == "2" || profile == "3" || profile == "4")
            {
                env["VICTORY_DATA_DIR"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "victory", "data-profile" + profile);
                env["VICTORY_GAMERTAG"] = "Player " + profile;
            }
            if (s.Get("log", "full") == "warn") env["VICTORY_LOG"] = "warn";
            if (Diag(s)) env["VICTORY_RECORD"] = CaptureDir;
            foreach (string raw in s.Get("extra", "").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith("#")) continue;
                string name = line.Substring(0, eq).Trim();
                if (!name.StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase)) name = "VICTORY_" + name;
                env[name.ToUpperInvariant()] = line.Substring(eq + 1).Trim();
            }
            return env;
        }

        // The title's executable in a folder (or one folder below it); null: none or several.
        public static string FindExecutable(string folder)
        {
            try
            {
                string found = OneExecutable(folder);
                if (found != null) return found;
                foreach (string sub in Directory.GetDirectories(folder))
                {
                    found = OneExecutable(sub);
                    if (found != null) return found;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        static string OneExecutable(string folder)
        {
            string[] exes;
            try { exes = Directory.GetFiles(folder, "*.exe"); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            if (exes.Length == 1) return exes[0];
            string game = null;
            int games = 0;
            foreach (string e in exes)
                if (Path.GetFileName(e).StartsWith("game", StringComparison.OrdinalIgnoreCase)) { game = e; games++; }
            return games == 1 ? game : null;
        }
    }

    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(24, 26, 31);
        public static readonly Color Panel = Color.FromArgb(34, 37, 44);
        public static readonly Color Text = Color.FromArgb(232, 234, 238);
        public static readonly Color Dim = Color.FromArgb(160, 166, 178);
        public static readonly Color Accent = Color.FromArgb(232, 142, 48);
        public static readonly Color Good = Color.FromArgb(120, 200, 130);
        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Head = new Font("Segoe UI Semibold", 17f);

        public static Button Button(string text, bool accent)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = accent ? Accent : Color.FromArgb(70, 75, 88);
            b.BackColor = accent ? Accent : Panel;
            b.ForeColor = accent ? Color.Black : Text;
            b.Font = accent ? new Font("Segoe UI Semibold", 10.5f) : Body;
            b.Height = 34;
            return b;
        }

        public static Label Label(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text; l.Font = font; l.ForeColor = color; l.BackColor = Color.Transparent; l.AutoSize = true;
            return l;
        }
    }

    // The rows of options: a label, a list, and (in the F1 menu) when a change takes effect.
    class OptionsPanel : Panel
    {
        readonly Store store;
        readonly Card card;
        readonly Opt[] options;
        readonly Dictionary<string, ComboBox> boxes = new Dictionary<string, ComboBox>();
        readonly Dictionary<string, string> atStart = new Dictionary<string, string>();
        bool filling;
        public Label Hint;
        public event Action<Opt> Changed;

        public OptionsPanel(Store store, Card card, bool menu, int width)
        {
            this.store = store; this.card = card;
            options = Settings.Options(card);
            BackColor = Theme.Back;
            Width = width;
            int y = 0, labelWidth = 130, tagWidth = menu ? 96 : 0;
            foreach (Opt o in options)
            {
                Label l = Theme.Label(o.Label, Theme.Body, Theme.Text);
                l.Location = new Point(0, y + 5);
                Controls.Add(l);
                ComboBox c = new ComboBox();
                c.DropDownStyle = ComboBoxStyle.DropDownList;
                c.FlatStyle = FlatStyle.Flat;
                c.BackColor = Theme.Panel; c.ForeColor = Theme.Text; c.Font = Theme.Body;
                c.Location = new Point(labelWidth, y);
                c.Width = width - labelWidth - tagWidth - (menu ? 10 : 0);
                c.Items.AddRange(o.Names);
                c.Tag = o;
                c.SelectedIndexChanged += OnPick;
                c.Enter += delegate(object sender, EventArgs e) { ShowHint((Opt)((ComboBox)sender).Tag); };
                Controls.Add(c);
                boxes[o.Key] = c;
                if (menu)
                {
                    Label tag = Theme.Label(o.Live ? "at once" : "after restart", Theme.Small, o.Live ? Theme.Good : Theme.Accent);
                    tag.Location = new Point(width - tagWidth, y + 6);
                    Controls.Add(tag);
                }
                y += 32;
            }
            Hint = new Label();
            Hint.Font = Theme.Small; Hint.ForeColor = Theme.Dim; Hint.BackColor = Theme.Back;
            Hint.Location = new Point(0, y + 4);
            Hint.Size = new Size(width, 64);
            Controls.Add(Hint);
            Height = y + 72;
            Fill();
            foreach (Opt o in options) atStart[o.Key] = store.Get(o.Key, o.Otherwise);
        }

        // Selections from the store; lists that cannot apply are greyed.
        public void Fill()
        {
            filling = true;
            foreach (Opt o in options)
            {
                ComboBox c = boxes[o.Key];
                int at = Array.IndexOf(o.Values, store.Get(o.Key, o.Otherwise));
                c.SelectedIndex = at < 0 ? Array.IndexOf(o.Values, o.Otherwise) : at;
            }
            bool dlss = Settings.DlssUsable(store, card);
            boxes["dlss"].Enabled = dlss;
            boxes["framegen"].Enabled = dlss && store.Get("dlss", "quality") != "off";
            boxes["build"].Enabled = Settings.DiagThere();
            filling = false;
        }

        // The options changed since this panel was made that need a restart.
        public int WaitingForRestart()
        {
            int n = 0;
            foreach (Opt o in options)
                if (!o.Live && atStart[o.Key] != store.Get(o.Key, o.Otherwise)) n++;
            return n;
        }

        void ShowHint(Opt o)
        {
            string more = "";
            if ((o.Key == "dlss" || o.Key == "framegen") && !Settings.DlssUsable(store, card))
                more = Settings.SeriesS(store) ? " (Not with the Series S profile.)"
                    : !card.Nvidia && card.Gb > 0 ? " (No NVIDIA card found.)" : " (Set the Streamline folder in the launcher window first.)";
            Hint.Text = o.Hint + more;
        }

        void OnPick(object sender, EventArgs e)
        {
            if (filling) return;
            ComboBox c = (ComboBox)sender;
            Opt o = (Opt)c.Tag;
            if (c.SelectedIndex < 0) return;
            store.Set(o.Key, o.Values[c.SelectedIndex]);
            store.Save();
            Fill();
            ShowHint(o);
            if (Changed != null) Changed(o);
        }
    }

    class LoadingForm : Form
    {
        readonly Label stage, detail;
        readonly ProgressBar bar;
        readonly Button skip;
        public event Action Cancelled, Skipped;

        public LoadingForm()
        {
            Text = "Foxhound";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ControlBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 236);
            Label head = Theme.Label("Getting ready", Theme.Head, Theme.Text);
            head.Location = new Point(24, 18);
            stage = Theme.Label("", Theme.Body, Theme.Text);
            stage.Location = new Point(26, 66);
            bar = new ProgressBar();
            bar.Location = new Point(26, 94); bar.Size = new Size(508, 14);
            bar.Style = ProgressBarStyle.Marquee; bar.MarqueeAnimationSpeed = 25;
            detail = new Label();
            detail.Font = Theme.Small; detail.ForeColor = Theme.Dim;
            detail.Location = new Point(26, 114); detail.Size = new Size(508, 66);
            Button cancel = Theme.Button("Cancel", false);
            cancel.Location = new Point(444, 186); cancel.Width = 90;
            cancel.Click += delegate { if (Cancelled != null) Cancelled(); };
            skip = Theme.Button("Skip: compile while playing", false);
            skip.Location = new Point(236, 186); skip.Width = 198;
            skip.Visible = false;
            skip.Click += delegate { skip.Enabled = false; if (Skipped != null) Skipped(); };
            Controls.AddRange(new Control[] { head, stage, bar, detail, skip, cancel });
        }

        // done < 0 or total <= 0: no known end (the bar runs to and fro).
        // The pipelines can be left to the run (it compiles what is missing while playing).
        public void CanSkip(bool can) { skip.Visible = can; skip.Enabled = can; }

        public void Show_(string stageText, string detailText, int done, int total)
        {
            stage.Text = stageText;
            detail.Text = detailText;
            if (total > 0 && done >= 0)
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Maximum = total; bar.Value = Math.Min(done, total);
            }
            else bar.Style = ProgressBarStyle.Marquee;
        }
    }

    // The settings while playing (F1).
    // The keyboard and mouse controls: %LOCALAPPDATA%\foxhound\controls.txt. The game reads it
    // (VICTORY_CONTROLS_FILE) when it starts and again within half a second of every change.
    // Lines "<function> <key> <key>" (virtual keys in hex, 0 for none), "mouse 0|1",
    // "sensitivity <x>", "invert_y 0|1", "deadzone <x>". A gamepad works beside them.
    class ControlSet
    {
        public static readonly string FilePath = Path.Combine(Store.Dir, "controls.txt");
        public static readonly string[] Names = {
            "menu", "view", "a", "b", "x", "y", "up", "down", "left", "right", "lb", "rb",
            "ls", "rs", "lt", "rt", "move_up", "move_down", "move_left", "move_right",
            "look_up", "look_down", "look_left", "look_right",
        };
        public static readonly string[] Labels = {
            "Menu (Start)", "View (Back)", "A", "B", "X", "Y", "D-pad up", "D-pad down", "D-pad left", "D-pad right",
            "Left bumper (LB)", "Right bumper (RB)", "Left stick click (LS)", "Right stick click (RS)",
            "Left trigger (LT)", "Right trigger (RT)", "Move forward", "Move back", "Move left", "Move right",
            "Look up", "Look down", "Look left", "Look right",
        };
        // The same as the game's own defaults (gameinput.rs, FUNCTIONS).
        static readonly int[] Defaults = {
            0x1B, 0, 0x09, 0, 0x0D, 0x20, 0x08, 0, 0x58, 0, 0x59, 0, 0x26, 0, 0x28, 0, 0x25, 0, 0x27, 0,
            0x51, 0, 0x45, 0, 0x46, 0x10, 0x48, 0x04, 0x5A, 0x02, 0x43, 0x01,
            0x57, 0, 0x53, 0, 0x41, 0, 0x44, 0, 0x49, 0, 0x4B, 0, 0x4A, 0, 0x4C, 0,
        };
        public readonly int[] Keys = new int[48];
        public bool Mouse = true;
        public double Sensitivity = 1.0;
        public bool InvertY;
        public double Deadzone = 0.2;

        public ControlSet() { Reset(); }

        public void Reset()
        {
            Array.Copy(Defaults, Keys, Keys.Length);
            Mouse = true; Sensitivity = 1.0; InvertY = false; Deadzone = 0.2;
        }

        public static ControlSet Load()
        {
            ControlSet c = new ControlSet();
            if (!File.Exists(FilePath)) return c;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] w = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (w.Length < 2) continue;
                    double v;
                    if (w[0] == "mouse") c.Mouse = w[1] != "0";
                    else if (w[0] == "invert_y") c.InvertY = w[1] == "1";
                    else if (w[0] == "sensitivity" && double.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) c.Sensitivity = v;
                    else if (w[0] == "deadzone" && double.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) c.Deadzone = v;
                    else
                    {
                        int i = Array.IndexOf(Names, w[0]);
                        if (i < 0) continue;
                        for (int k = 0; k < 2; k++)
                        {
                            int key = 0;
                            if (k + 1 < w.Length) int.TryParse(w[k + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out key);
                            c.Keys[2 * i + k] = key & 0xFF;
                        }
                    }
                }
            }
            catch (IOException) { }
            return c;
        }

        public void Save()
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < Names.Length; i++)
                b.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:X2} {2:X2}\n", Names[i], Keys[2 * i], Keys[2 * i + 1]);
            b.AppendFormat(CultureInfo.InvariantCulture, "mouse {0}\nsensitivity {1:0.##}\ninvert_y {2}\ndeadzone {3:0.##}\n",
                Mouse ? 1 : 0, Sensitivity, InvertY ? 1 : 0, Deadzone);
            try { Directory.CreateDirectory(Store.Dir); File.WriteAllText(FilePath, b.ToString()); }
            catch (IOException) { }
        }

        public static string KeyName(int vk)
        {
            switch (vk)
            {
                case 0: return "-";
                case 0x01: return "Mouse left";
                case 0x02: return "Mouse right";
                case 0x04: return "Mouse middle";
                case 0x05: return "Mouse side 1";
                case 0x06: return "Mouse side 2";
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x10: return "Shift";
                case 0x11: return "Ctrl";
                case 0x12: return "Alt";
                case 0x14: return "Caps Lock";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "Page Up";
                case 0x22: return "Page Down";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
            }
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            return ((Keys)vk).ToString();
        }
    }

    // Keys and mouse buttons for each pad function, and the mouse's camera settings. Each change
    // is saved at once (a running game takes it within half a second).
    class ControlsForm : Form
    {
        readonly ControlSet set = ControlSet.Load();
        readonly Button[] cells = new Button[48];
        readonly Label hint;
        readonly CheckBox mouse, invert;
        readonly NumericUpDown sensitivity;
        Button listening;
        bool justBound;
        bool filling;   // (setting the boxes saves nothing)

        public ControlsForm(bool overGame)
        {
            Text = "Foxhound controls";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false; TopMost = overGame; KeyPreview = true;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            Label head = Theme.Label("Controls: keyboard and mouse", Theme.Head, Theme.Text);
            head.Location = new Point(24, 16);
            hint = Theme.Label("Click a key, then press a key or a mouse button for it. Delete clears it. A gamepad works beside these.", Theme.Small, Theme.Dim);
            hint.Location = new Point(26, 54);
            Controls.AddRange(new Control[] { head, hint });
            for (int i = 0; i < 24; i++)
            {
                int col = i / 12, row = i % 12;
                int x = 24 + col * 440, y = 84 + row * 32;
                Label l = Theme.Label(ControlSet.Labels[i], Theme.Body, Theme.Text);
                l.Location = new Point(x, y + 6);
                Controls.Add(l);
                for (int k = 0; k < 2; k++)
                {
                    Button b = Theme.Button("", false);
                    b.Height = 28; b.Width = 118;
                    b.Location = new Point(x + 170 + k * 126, y);
                    b.Tag = 2 * i + k;
                    b.Click += delegate(object sender, EventArgs e) { Clicked((Button)sender); };
                    b.MouseDown += delegate(object sender, MouseEventArgs e) { Pressed((Button)sender, e); };
                    cells[2 * i + k] = b;
                    Controls.Add(b);
                }
            }
            int my = 84 + 12 * 32 + 14;
            mouse = new CheckBox(); mouse.Text = "The mouse turns the camera (the cursor is hidden over the game)";
            mouse.AutoSize = true; mouse.ForeColor = Theme.Text; mouse.Location = new Point(26, my);
            mouse.CheckedChanged += delegate { if (filling) return; set.Mouse = mouse.Checked; set.Save(); };
            Label sl = Theme.Label("Sensitivity", Theme.Body, Theme.Text);
            sl.Location = new Point(26, my + 34);
            sensitivity = new NumericUpDown();
            sensitivity.DecimalPlaces = 1; sensitivity.Increment = 0.1m; sensitivity.Minimum = 0.1m; sensitivity.Maximum = 5m;
            sensitivity.Width = 70; sensitivity.Location = new Point(110, my + 31);
            sensitivity.ValueChanged += delegate { if (filling) return; set.Sensitivity = (double)sensitivity.Value; set.Save(); };
            invert = new CheckBox(); invert.Text = "Invert vertical"; invert.AutoSize = true; invert.ForeColor = Theme.Text;
            invert.Location = new Point(210, my + 33);
            invert.CheckedChanged += delegate { if (filling) return; set.InvertY = invert.Checked; set.Save(); };
            Button defaults = Theme.Button("Defaults", false);
            defaults.Width = 120; defaults.Location = new Point(24 + 880 - 254, my + 26);
            defaults.Click += delegate { set.Reset(); set.Save(); Fill(); };
            Button done = Theme.Button("Done", true);
            done.Width = 120; done.Location = new Point(24 + 880 - 120, my + 26);
            done.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { mouse, sl, sensitivity, invert, defaults, done });
            ClientSize = new Size(24 + 880 + 24, my + 26 + 34 + 20);
            Fill();
        }

        void Fill()
        {
            filling = true;
            for (int n = 0; n < 48; n++) cells[n].Text = ControlSet.KeyName(set.Keys[n]);
            mouse.Checked = set.Mouse;
            sensitivity.Value = Math.Max(sensitivity.Minimum, Math.Min(sensitivity.Maximum, (decimal)set.Sensitivity));
            invert.Checked = set.InvertY;
            filling = false;
        }

        void Clicked(Button b)
        {
            if (justBound) { justBound = false; return; }
            if (listening != null) listening.Text = ControlSet.KeyName(set.Keys[(int)listening.Tag]);
            listening = b;
            b.Text = "press a key...";
        }

        // While a key is being chosen, a mouse button pressed over it is the choice.
        void Pressed(Button b, MouseEventArgs e)
        {
            if (listening == null) return;
            int vk = e.Button == MouseButtons.Left ? 0x01 : e.Button == MouseButtons.Right ? 0x02 : e.Button == MouseButtons.Middle ? 0x04
                : e.Button == MouseButtons.XButton1 ? 0x05 : e.Button == MouseButtons.XButton2 ? 0x06 : 0;
            if (vk == 0) return;
            justBound = e.Button == MouseButtons.Left;
            Bind(vk);
        }

        void Bind(int vk)
        {
            int n = (int)listening.Tag;
            set.Keys[n] = vk;
            set.Save();
            listening.Text = ControlSet.KeyName(vk);
            listening = null;
            string also = "";
            for (int m = 0; m < 48; m++)
                if (vk != 0 && m != n && set.Keys[m] == vk) also += (also.Length > 0 ? ", " : "") + ControlSet.Labels[m / 2];
            hint.Text = also.Length > 0 ? ControlSet.KeyName(vk) + " is also: " + also + "." : "Saved.";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (listening != null)
            {
                Keys k = keyData & System.Windows.Forms.Keys.KeyCode;
                if (k == System.Windows.Forms.Keys.F1 || k == System.Windows.Forms.Keys.F2)
                {
                    hint.Text = "F1 and F2 belong to the launcher (settings, frame capture).";
                    return true;
                }
                Bind(k == System.Windows.Forms.Keys.Delete ? 0 : (int)k & 0xFF);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    class MenuForm : Form
    {
        readonly OptionsPanel panel;
        readonly Label waiting;
        public event Action Back, Restart, Quit;
        public event Action<Opt> Changed;

        public MenuForm(Store store, Card card)
        {
            Text = "Foxhound settings";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 520);
            Padding = new Padding(1);
            Label head = Theme.Label("Settings", Theme.Head, Theme.Text);
            head.Location = new Point(28, 20);
            Label note = Theme.Label("F1 or Esc: back to the game. The game keeps running behind this menu.", Theme.Small, Theme.Dim);
            note.Location = new Point(30, 58);
            panel = new OptionsPanel(store, card, true, 624);
            panel.Location = new Point(28, 88);
            panel.Changed += delegate(Opt o) { Count(); if (Changed != null) Changed(o); };
            waiting = Theme.Label("", Theme.Small, Theme.Accent);
            waiting.Location = new Point(30, 88 + panel.Height + 2);
            Label other = Theme.Label("Game folder, Streamline folder and further switches: in the launcher window, after quitting.", Theme.Small, Theme.Dim);
            other.Location = new Point(30, 88 + panel.Height + 22);
            int by = 88 + panel.Height + 52;
            Button back = Theme.Button("Back to the game", true);
            back.Location = new Point(28, by); back.Width = 160;
            Button keys = Theme.Button("Controls", false);
            keys.Location = new Point(198, by); keys.Width = 130;
            keys.Click += delegate { using (ControlsForm f = new ControlsForm(true)) f.ShowDialog(this); };
            back.Click += delegate { if (Back != null) Back(); };
            Button restart = Theme.Button("Restart the game", false);
            restart.Location = new Point(338, by); restart.Width = 150;
            restart.Click += delegate
            {
                if (Ask("Restart the game now?\n\nSettings marked \"after restart\" take effect. Progress since the title's last save is lost.") && Restart != null) Restart();
            };
            Button quit = Theme.Button("Quit the game", false);
            quit.Location = new Point(498, by); quit.Width = 154;
            quit.Click += delegate
            {
                if (Ask("Quit the game?\n\nProgress since the title's last save is lost.") && Quit != null) Quit();
            };
            Controls.AddRange(new Control[] { head, note, panel, waiting, other, back, keys, restart, quit });
            ClientSize = new Size(680, by + 34 + 26);
            KeyDown += delegate(object sender, KeyEventArgs e) { if ((e.KeyCode == Keys.Escape || e.KeyCode == Keys.F1) && Back != null) Back(); };
            Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };
            Count();
        }

        bool Ask(string text)
        {
            return MessageBox.Show(this, text, "Foxhound", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void Count()
        {
            int n = panel.WaitingForRestart();
            waiting.Text = n == 0 ? "" : n == 1 ? "1 changed setting takes effect after a restart of the game." : n + " changed settings take effect after a restart of the game.";
        }

        public void Refill() { panel.Fill(); Count(); }
    }

    class MainForm : Form
    {
        readonly Store store = Store.Load();
        readonly Card card = Card.Detect();
        readonly string here = Path.GetDirectoryName(Application.ExecutablePath);
        readonly TextBox gameBox, streamlineBox, extraBox;
        readonly Label status;
        readonly OptionsPanel panel;
        readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer();

        LoadingForm loading;
        MenuForm menu;
        Process game;          // the running title
        Process tool;          // a preparing tool
        volatile bool cancelled;
        StreamWriter log;
        string logPath = "";
        IntPtr window = IntPtr.Zero;
        bool quitting;

        string Victory { get { return Path.Combine(here, Settings.Diag(store) ? "victory-diag.exe" : "victory.exe"); } }

        readonly bool playAtOnce;
        readonly bool prepareOnly;

        // Arguments: --game <folder or executable> sets the game folder; --play starts at once
        // with the saved settings (for a shortcut that skips this window); --prepare translates the
        // shaders and compiles the pipelines with the saved settings, then closes without starting
        // the game.
        public MainForm(string[] args)
        {
            for (int n = 0; n < args.Length; n++)
            {
                if (args[n] == "--play") playAtOnce = true;
                else if (args[n] == "--prepare") { playAtOnce = true; prepareOnly = true; }
                else if (args[n] == "--game" && n + 1 < args.Length)
                {
                    string given = args[++n];
                    string exe = File.Exists(given) ? given : Directory.Exists(given) ? Settings.FindExecutable(given) : null;
                    if (exe != null) { store.Set("game_exe", Path.GetFullPath(exe)); store.Save(); }
                }
            }
            Text = "Foxhound";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            const int w = 600, x = 28;
            Label head = Theme.Label("Foxhound", Theme.Head, Theme.Text);
            head.Location = new Point(x, 18);
            Label sub = Theme.Label("Runs your own copy of the title. No game files are included.", Theme.Small, Theme.Dim);
            sub.Location = new Point(x + 2, 56);
            int y = 88;
            gameBox = PathRow("Game folder", x, y, w, PickGame);
            y += 34;
            streamlineBox = PathRow("Streamline folder", x, y, w, PickStreamline);
            y += 42;
            if (!store.Has("resolution") && card.Gb > 0 && card.Gb < 11) store.Set("resolution", "1080");
            if (!store.Has("dlss") && Settings.StreamlineDir(store).Length == 0) FindStreamline();
            panel = new OptionsPanel(store, card, false, w);
            panel.Location = new Point(x, y);
            y += panel.Height + 4;
            Label extraLabel = Theme.Label("Further switches", Theme.Body, Theme.Text);
            extraLabel.Location = new Point(x, y + 4);
            extraBox = new TextBox();
            extraBox.Multiline = true; extraBox.ScrollBars = ScrollBars.Vertical;
            extraBox.BackColor = Theme.Panel; extraBox.ForeColor = Theme.Text; extraBox.BorderStyle = BorderStyle.FixedSingle;
            extraBox.Font = new Font("Consolas", 9f);
            extraBox.Location = new Point(x + 130, y); extraBox.Size = new Size(w - 130, 50);
            extraBox.Text = store.Get("extra", "").Replace("\n", "\r\n");
            extraBox.Enter += delegate { panel.Hint.Text = "One NAME=value a line, for example AMBIENT=0.6 (VICTORY_ is put in front). The switches are described in the README and in the code where each is read. They override the lists above."; };
            extraBox.Leave += delegate { store.Set("extra", extraBox.Text.Replace("\r", "")); store.Save(); };
            y += 60;
            status = new Label();
            status.Font = Theme.Small; status.ForeColor = Theme.Accent;
            status.Location = new Point(x, y); status.Size = new Size(w, 34);
            y += 38;
            Button play = Theme.Button("Play", true);
            play.Location = new Point(x, y); play.Size = new Size(200, 40);
            Button keys = Theme.Button("Controls", false);
            keys.Location = new Point(x + 212, y + 3); keys.Width = 104;
            keys.Click += delegate { using (ControlsForm f = new ControlsForm(false)) f.ShowDialog(this); };
            play.Click += delegate { Play(); };
            Button shortcut = Theme.Button("Desktop shortcut", false);
            shortcut.Location = new Point(x + 324, y + 3); shortcut.Width = 128;
            shortcut.Click += delegate { status.Text = MakeShortcut() ? "A shortcut to this launcher is on the desktop." : "The shortcut could not be made."; };
            Button logs = Theme.Button("Logs", false);
            logs.Location = new Point(x + 460, y + 3); logs.Width = 64;
            logs.Click += delegate { Directory.CreateDirectory(Store.LogDir); Process.Start("explorer.exe", "\"" + Store.LogDir + "\""); };
            Button quit = Theme.Button("Close", false);
            quit.Location = new Point(x + 532, y + 3); quit.Width = 68;
            quit.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { head, sub, panel, extraLabel, extraBox, status, play, keys, shortcut, logs, quit });
            ClientSize = new Size(w + 2 * x, y + 62);
            AcceptButton = play;
            watch.Interval = 40;
            watch.Tick += delegate { Watch(); };
            Shown += delegate { FirstStart(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                store.Set("extra", extraBox.Text.Replace("\r", ""));
                store.Save();
                if (game != null && !quitting) { e.Cancel = true; Hide(); }
            };
            ShowPaths();
            panel.Fill();
        }

        TextBox PathRow(string label, int x, int y, int w, Action pick)
        {
            Label l = Theme.Label(label, Theme.Body, Theme.Text);
            l.Location = new Point(x, y + 5);
            TextBox t = new TextBox();
            t.ReadOnly = true; t.BackColor = Theme.Panel; t.ForeColor = Theme.Text; t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = Theme.Body;
            t.Location = new Point(x + 130, y + 1); t.Width = w - 130 - 96;
            Button b = Theme.Button("Change...", false);
            b.Location = new Point(x + w - 88, y - 1); b.Size = new Size(88, 28);
            b.Click += delegate { pick(); };
            Controls.AddRange(new Control[] { l, t, b });
            return t;
        }

        void ShowPaths()
        {
            string exe = store.Get("game_exe", "");
            gameBox.Text = exe.Length > 0 ? Path.GetDirectoryName(exe) : "(not set)";
            string sl = Settings.StreamlineDir(store);
            streamlineBox.Text = sl.Length > 0 ? sl : "(not set: no DLSS)";
        }

        void FirstStart()
        {
            if (!File.Exists(Victory))
            {
                status.Text = "victory.exe is not beside this launcher. Keep the files of the release together.";
                return;
            }
            string exe = store.Get("game_exe", "");
            if (exe.Length == 0 || !File.Exists(exe))
            {
                MessageBox.Show(this, "Welcome.\n\nChoose the folder that holds your own copy of the title (the folder with its .exe). It is asked once.", "Foxhound", MessageBoxButtons.OK, MessageBoxIcon.Information);
                PickGame();
            }
            if (store.Get("shortcut_made", "0") != "1" && File.Exists(store.Get("game_exe", "")))
            {
                if (MakeShortcut()) status.Text = "A shortcut to this launcher was put on the desktop.";
                store.Set("shortcut_made", "1");
                store.Save();
            }
            if (playAtOnce && File.Exists(store.Get("game_exe", ""))) Play();
        }

        void PickGame()
        {
            while (true)
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "The folder that holds your own copy of the title (the folder with its .exe).";
                    d.ShowNewFolderButton = false;
                    string now = store.Get("game_exe", "");
                    if (now.Length > 0) d.SelectedPath = Path.GetDirectoryName(now);
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    string exe = Settings.FindExecutable(d.SelectedPath);
                    if (exe != null)
                    {
                        store.Set("game_exe", exe);
                        store.Save();
                        ShowPaths();
                        status.Text = "";
                        return;
                    }
                    if (MessageBox.Show(this, "The title's executable was not found in\n" + d.SelectedPath + "\n(nor one folder below). Choose another folder?", "Foxhound", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return;
                }
            }
        }

        // The folder of sl.interposer.dll: the folder given, or below it (an SDK's bin\x64).
        static string InterposerFolder(string folder, int depth)
        {
            try
            {
                if (File.Exists(Path.Combine(folder, "sl.interposer.dll"))) return folder;
                if (depth == 0) return null;
                foreach (string sub in Directory.GetDirectories(folder))
                {
                    string name = Path.GetFileName(sub).ToLowerInvariant();
                    if (name == "development" || name == "arm64" || name == "x86") continue;   // unsigned or other builds
                    string found = InterposerFolder(sub, depth - 1);
                    if (found != null) return found;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        void FindStreamline()
        {
            foreach (string guess in new string[] { Environment.GetEnvironmentVariable("VICTORY_STREAMLINE_DIR"), Path.Combine(here, "streamline") })
            {
                if (string.IsNullOrEmpty(guess) || !Directory.Exists(guess)) continue;
                string found = InterposerFolder(guess, 3);
                if (found != null) { store.Set("streamline_dir", found); return; }
            }
            if (!store.Has("dlss")) store.Set("dlss", "off");
        }

        void PickStreamline()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "The folder of NVIDIA's Streamline SDK (the one you downloaded from NVIDIA and unpacked; its bin\\x64 holds sl.interposer.dll).";
                d.ShowNewFolderButton = false;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string found = InterposerFolder(d.SelectedPath, 3);
                if (found == null)
                {
                    MessageBox.Show(this, "No sl.interposer.dll in that folder or below it.", "Foxhound", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                store.Set("streamline_dir", found);
                if (store.Get("dlss", "off") == "off") store.Set("dlss", "quality");
                store.Save();
                ShowPaths();
                panel.Fill();
            }
        }

        bool MakeShortcut()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Foxhound.lnk");
                IShellLinkW link = (IShellLinkW)new ShellLinkObject();
                link.SetPath(Application.ExecutablePath);
                link.SetWorkingDirectory(here);
                link.SetDescription("Foxhound launcher");
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, false);
                return true;
            }
            catch (Exception) { return false; }
        }

        // ---- preparing and starting ----

        void Play()
        {
            if (game != null || loading != null) return;
            store.Set("extra", extraBox.Text.Replace("\r", ""));
            store.Save();
            if (!File.Exists(Victory)) { status.Text = "victory.exe is not beside this launcher."; return; }
            string exe = store.Get("game_exe", "");
            if (exe.Length == 0 || !File.Exists(exe)) { PickGame(); exe = store.Get("game_exe", ""); if (exe.Length == 0 || !File.Exists(exe)) return; }
            string memory = MemoryCheck.Warning();
            if (memory != null && MessageBox.Show(this, memory, "Foxhound", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Dictionary<string, string> env = Settings.Environment_(store, card);
            env["VICTORY_GAME_DIR"] = Path.GetDirectoryName(exe);
            string console = Settings.SeriesS(store) ? "series-s" : "series-x";
            cancelled = false;
            loading = new LoadingForm();
            loading.Skipped += delegate { EndProcess(tool); };
            loading.Cancelled += delegate { cancelled = true; EndProcess(tool); EndProcess(game); };
            loading.Show();
            Hide();
            Thread t = new Thread(delegate() { Prepare(exe, console, env); });
            t.IsBackground = true;
            t.Start();
        }

        void Ui(Action a)
        {
            try { if (IsHandleCreated) BeginInvoke(a); }
            catch (InvalidOperationException) { }
        }

        void Stage(string stage, string detail, int done, int total)
        {
            Ui(delegate { if (loading != null) loading.Show_(stage, detail, done, total); });
        }

        // (Worker thread.) Shaders, pipelines, then the title.
        void Prepare(string exe, string console, Dictionary<string, string> env)
        {
            try
            {
                Directory.CreateDirectory(Store.LogDir);
                Dictionary<string, string> toolEnv = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase);
                // The tools cannot ask the card as a run does: NVIDIA and Intel cards run waves of
                // 32 lanes, AMD cards the console's 64.
                if (!card.Amd) toolEnv["VICTORY_HOST_WAVE"] = "32";
                FileInfo layer = new FileInfo(Victory);
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, string> p in toolEnv)
                    if (Array.IndexOf(Settings.NotOfShaders, p.Key.ToUpperInvariant()) < 0) parts.Add(p.Key + "=" + p.Value);
                parts.Sort(StringComparer.Ordinal);
                string stamp = layer.Length + " " + layer.LastWriteTimeUtc.Ticks + " " + Path.GetDirectoryName(exe) + " " + string.Join(" ", parts.ToArray());
                string stampFile = Path.Combine(Store.Dir, "prepared.txt");
                string before = File.Exists(stampFile) ? File.ReadAllText(stampFile) : "";
                string prepLog = Path.Combine(Store.LogDir, "prepare.log");
                File.WriteAllText(prepLog, "");
                DateTime began = DateTime.Now;
                if (before != stamp)
                {
                    int total = 0;
                    int.TryParse(store.Get("shader_total", "0"), out total);
                    // The title 1.0.2.1 has 25,758 (before a first pass has counted them here).
                    if (total == 0) total = 25758;
                    int last = 0;
                    Stage("Translating the title's shaders", "Once per version of Foxhound and set of switches: a few minutes.", -1, 0);
                    int code = RunTool("shaders \"" + Path.GetDirectoryName(exe) + "\"", toolEnv, prepLog, delegate(string line)
                    {
                        Match m = Regex.Match(line, @"^(\d+) shaders in the cache");
                        if (!m.Success) return;
                        last = int.Parse(m.Groups[1].Value);
                        Stage("Translating the title's shaders", string.Format("{0:N0} shaders done, {1:0} s. Once per version of Foxhound and set of switches.", last, (DateTime.Now - began).TotalSeconds), last, total);
                    });
                    if (cancelled) { Ui(delegate { BackToLauncher("Cancelled."); }); return; }
                    if (code != 0) { Ui(delegate { BackToLauncher("The shaders could not be translated (see prepare.log under Logs). Is the game folder right?"); }); return; }
                    File.WriteAllText(stampFile, stamp);
                    if (last > 0) { int t = last; Ui(delegate { store.Set("shader_total", t.ToString()); store.Save(); }); }
                }
                began = DateTime.Now;
                Stage("Compiling pipelines", "Those earlier runs asked for. Seconds; much longer after an update of Foxhound or of the graphics driver.", -1, 0);
                int known = -1;
                Ui(delegate { if (loading != null) loading.CanSkip(true); });
                RunTool("warm", toolEnv, prepLog, delegate(string line)
                {
                    Match k = Regex.Match(line, @"^(\d+) pipelines known");
                    if (k.Success) known = int.Parse(k.Groups[1].Value);
                    Match m = Regex.Match(line, @"^(\d+) of (\d+), ");
                    if (!m.Success) return;
                    int done = int.Parse(m.Groups[1].Value), all = int.Parse(m.Groups[2].Value);
                    double spent = (DateTime.Now - began).TotalSeconds;
                    double left = done > 0 ? spent * (all - done) / done : 0;
                    string rest = left >= 90 ? string.Format("about {0:0} minutes left", left / 60) : string.Format("about {0:0} s left", left);
                    string slow = left >= 600 ? " The graphics driver is compiling them anew (after an update, or the first time from this folder). Skip starts the game now; they are then compiled as scenes need them, with a slow picture at first." : "";
                    Stage("Compiling pipelines", string.Format("{0:N0} of {1:N0}, {2}.", done, all, rest) + slow, done, all);
                });
                Ui(delegate { if (loading != null) loading.CanSkip(false); });
                if (cancelled) { Ui(delegate { BackToLauncher("Cancelled."); }); return; }
                if (prepareOnly)
                {
                    Ui(delegate { if (loading != null) { loading.Close(); loading = null; } Application.Exit(); });
                    return;
                }
                Stage("Starting the title", known == 0
                    ? "First run on this PC: every scene compiles its pipelines the first time it is shown. The first scene keeps the loading picture while they are made (up to two minutes); later ones can be slow and miss objects for about a minute. This is remembered; the next start compiles them here, ahead. F1 while playing: settings."
                    : "The window appears in about half a minute. F1 while playing: settings, restart, quit.", -1, 0);
                Ui(delegate { Start(exe, console, env); });
            }
            catch (Exception e)
            {
                string text = e.Message;
                Ui(delegate { BackToLauncher("Could not prepare: " + text); });
            }
        }

        static ProcessStartInfo Info(string exe, string arguments, string folder, Dictionary<string, string> env)
        {
            ProcessStartInfo i = new ProcessStartInfo(exe, arguments);
            i.UseShellExecute = false; i.CreateNoWindow = true;
            i.RedirectStandardOutput = true; i.RedirectStandardError = true;
            i.StandardOutputEncoding = Encoding.UTF8; i.StandardErrorEncoding = Encoding.UTF8;
            i.WorkingDirectory = folder;
            // Switches left over in this session do not reach the run.
            List<string> old = new List<string>();
            foreach (System.Collections.DictionaryEntry e in i.EnvironmentVariables)
                if (e.Key.ToString().StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase)) old.Add(e.Key.ToString());
            foreach (string k in old) i.EnvironmentVariables.Remove(k);
            foreach (KeyValuePair<string, string> p in env) i.EnvironmentVariables[p.Key] = p.Value;
            return i;
        }

        int RunTool(string arguments, Dictionary<string, string> env, string logFile, Action<string> line)
        {
            object gate = new object();
            using (StreamWriter w = new StreamWriter(logFile, true, new UTF8Encoding(false)))
            using (Process p = new Process())
            {
                w.AutoFlush = true;
                p.StartInfo = Info(Victory, arguments, here, env);
                DataReceivedEventHandler on = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (gate) { w.WriteLine(e.Data); }
                    line(e.Data);
                };
                p.OutputDataReceived += on;
                p.ErrorDataReceived += on;
                lock (gate) { w.WriteLine("> victory.exe " + arguments); }
                p.Start();
                tool = p;
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
                tool = null;
                return p.ExitCode;
            }
        }

        // Ends a tool or the game. The processes `victory warm` starts end by themselves at their
        // next line once the one that started them is gone.
        static void EndProcess(Process p)
        {
            if (p == null) return;
            try
            {
                if (p.HasExited) return;
                p.Kill();
                p.WaitForExit(5000);
            }
            catch (Exception) { }
        }

        void Start(string exe, string console, Dictionary<string, string> env)
        {
            if (cancelled) { BackToLauncher("Cancelled."); return; }
            try
            {
                // The last 8 logs are kept.
                string[] old = Directory.GetFiles(Store.LogDir, "foxhound-*.log");
                Array.Sort(old, StringComparer.Ordinal);
                for (int n = 0; n < old.Length - 7; n++) { try { File.Delete(old[n]); } catch (IOException) { } }
                logPath = Path.Combine(Store.LogDir, "foxhound-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                log = new StreamWriter(logPath, false, new UTF8Encoding(false));
                log.AutoFlush = true;
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, string> p in env) if (p.Key != "VICTORY_GAME_DIR") parts.Add(p.Key + "=" + p.Value);
                parts.Sort(StringComparer.Ordinal);
                log.WriteLine("# launcher " + Application.ProductVersion + "; card: " + card.Name + string.Format(" {0:0.#} GB", card.Gb) + "; console=" + console + "; display=" + store.Get("display", "fullscreen") + "; switches: " + string.Join(" ", parts.ToArray()));
                Process p2 = new Process();
                p2.StartInfo = Info(Victory, "run \"" + exe + "\" --console " + console + " --execute", here, env);
                StreamWriter w = log;
                DataReceivedEventHandler on = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    if (e.Data.StartsWith("victory-key ", StringComparison.Ordinal))
                    {
                        string key = e.Data.Substring(12).Trim();
                        Ui(delegate { GameKey(key); });
                    }
                    lock (w) { try { w.WriteLine(e.Data); } catch (ObjectDisposedException) { } }
                };
                p2.OutputDataReceived += on;
                p2.ErrorDataReceived += on;
                p2.Start();
                p2.BeginOutputReadLine(); p2.BeginErrorReadLine();
                game = p2;
                SetPriority();
                window = IntPtr.Zero;
                watch.Start();
            }
            catch (Exception e)
            {
                BackToLauncher("The title could not be started: " + e.Message);
            }
        }

        void BackToLauncher(string text)
        {
            watch.Stop();
            if (loading != null) { loading.Close(); loading = null; }
            if (menu != null) { menu.Close(); menu = null; }
            if (game != null) { try { game.Dispose(); } catch (Exception) { } game = null; }
            if (log != null) { lock (log) { log.Dispose(); } log = null; }
            window = IntPtr.Zero;
            status.Text = text;
            panel.Fill();
            Show();
            Activate();
        }

        // The last "Error: ..." line the game wrote, with a space in front; "" if none.
        string LastError()
        {
            try
            {
                // The game's output is read to its end first.
                if (game != null) game.WaitForExit();
                string found = "";
                using (FileStream f = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(f))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                        if (line.StartsWith("Error: ", StringComparison.Ordinal)) found = line.Substring(7);
                }
                return found.Length > 0 ? "\n\n" + found + "\n\n" : "";
            }
            catch (Exception) { return ""; }
        }

        // ---- while the title runs (every 40 ms) ----

        void Watch()
        {
            if (game == null) return;
            if (game.HasExited)
            {
                int code = 0;
                try { code = game.ExitCode; } catch (InvalidOperationException) { }
                if (quitting) { Application.Exit(); return; }
                BackToLauncher(window == IntPtr.Zero
                    ? "The title ended before its window appeared (code " + code + ")." + LastError() + " The rest is at the end of the newest log (Logs)."
                    : "The title ended (code " + code + "). Log: " + Path.GetFileName(logPath));
                return;
            }
            if (window == IntPtr.Zero)
            {
                game.Refresh();
                IntPtr h = game.MainWindowHandle;
                if (h == IntPtr.Zero) return;
                window = h;
                if (loading != null) { loading.Close(); loading = null; }
            }
        }

        // A key the game's window was given (its line "victory-key F1" on standard output).
        void GameKey(string key)
        {
            if (game == null) return;
            if (key == "F1")
            {
                if (menu != null && menu.Visible) CloseMenu();
                else OpenMenu();
            }
            // F2 (diagnostics build): the frame being drawn is saved.
            else if (key == "F2" && Settings.Diag(store))
            {
                try
                {
                    Directory.CreateDirectory(Settings.CaptureDir);
                    File.WriteAllText(Path.Combine(Settings.CaptureDir, "go"), "go");
                    File.AppendAllText(Path.Combine(Settings.CaptureDir, "notes.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " F2, log " + Path.GetFileName(logPath) + "\r\n");
                }
                catch (IOException) { }
            }
        }

        void SetPriority()
        {
            if (game == null) return;
            try
            {
                string p = store.Get("priority", "normal");
                game.PriorityClass = p == "high" ? ProcessPriorityClass.High : p == "above" ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal;
            }
            catch (Exception) { }
        }

        void OpenMenu()
        {
            if (menu == null)
            {
                menu = new MenuForm(store, card);
                menu.Back += CloseMenu;
                menu.Changed += delegate(Opt o)
                {
                    if (o.Key == "priority") SetPriority();
                };
                menu.Restart += delegate { RestartGame(); };
                menu.Quit += delegate { quitting = true; EndGame(); };
            }
            menu.Refill();
            PlaceMenu();
            menu.Show();
            menu.Activate();
        }

        void PlaceMenu()
        {
            Rectangle m = window != IntPtr.Zero ? Screen.FromHandle(window).Bounds : Screen.PrimaryScreen.Bounds;
            menu.Location = new Point(m.Left + (m.Width - menu.Width) / 2, m.Top + (m.Height - menu.Height) / 2);
        }

        void CloseMenu()
        {
            if (menu != null) menu.Hide();
            if (window != IntPtr.Zero) Native.SetForegroundWindow(window);
        }

        // Asks the window to close, then ends the process (the title has no way out of its own).
        void EndGame()
        {
            if (menu != null) menu.Hide();
            Process p = game;
            if (p == null) return;
            try
            {
                if (!p.HasExited) { p.CloseMainWindow(); if (!p.WaitForExit(2500)) EndProcess(p); p.WaitForExit(5000); }
            }
            catch (Exception) { }
        }

        void RestartGame()
        {
            EndGame();
            BackToLauncher("");
            Play();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (Mutex one = new Mutex(true, "FoxhoundLauncher-7c1e", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Foxhound is running already (press F1 in the game for its settings).", "Foxhound", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm(args));
            }
        }
    }
}
