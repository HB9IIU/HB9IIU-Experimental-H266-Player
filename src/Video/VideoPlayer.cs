using opentuner;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading;

namespace QocastPlayer.Video
{
    public sealed class MediaInfo
    {
        public string VideoCodec;
        public uint VideoWidth;
        public uint VideoHeight;
        public uint VideoSarNum;
        public uint VideoSarDen;
        public string AudioCodec;
        public uint AudioRate;
        public uint AudioChannels;
    }

    // H.266 TEST player: the received transport stream goes to an ffplay process (stdin),
    // because VLC 3 cannot decode H.266. ffplay's window is put inside the view (SetParent),
    // so it looks like one window. Same public surface as the VLC VideoPlayer in QOCAST-Player2.
    // Play/Stop only record what is wanted; one worker thread does the work in order.
    public sealed class VideoPlayer : IDisposable
    {
        private const byte TsSync = 0x47;
        private const int MinBytes = 1010;

        private readonly CircularBuffer _stream;
        private readonly Thread _worker;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly object _lock = new object();

        private bool _wantPlaying;
        private int _wantSession;       // +1 on every Play(): a new lock = a fresh start
        private bool _disposed;
        private int _volume;

        private readonly System.Windows.Forms.Control _view;
        private Process _ffplay;
        private IntPtr _ffplayWindow;
        private volatile int _viewWidth;
        private volatile int _viewHeight;
        private int _playingSession = -1;

        private const int GWL_STYLE = -16;
        private const long WS_CHILD = 0x40000000L;
        private const long WS_VISIBLE = 0x10000000L;
        private const long WS_CAPTION = 0x00C00000L;
        private const long WS_THICKFRAME = 0x00040000L;
        private const long WS_POPUP = 0x80000000L;

        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseHookData { public Point Pt; public uint Data, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr h, bool enable);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h, int x, int y, int w, int h2, bool repaint);

        // picture on screen (true) or gone (false); raised on the worker thread
        public event Action<bool> PictureChanged;

        // left button down / right button up on the picture, raised on the UI thread.
        // ffplay's window would swallow these, so a mouse hook watches for them.
        public event Action<System.Windows.Forms.MouseButtons> PictureMouse;

        private readonly uint _ownPid = (uint)Process.GetCurrentProcess().Id;
        // what the bottom bar shows between the lock and the first picture: nothing for the first
        // 2 seconds (the stream info stays), then "Waiting for keyframe" until the picture is there
        private volatile bool _pictureShown;
        private int _startTick;
        public string Waiting
        {
            get
            {
                int ms = Environment.TickCount - _startTick;
                if (_ffplay == null || _pictureShown || ms < 2000)
                    return null;
                // moving dots and the seconds, so it is clear that it is not frozen
                string dots = new string('.', 1 + ms / 400 % 3).PadRight(3);
                return "Waiting for keyframe" + dots + "  " + ms / 1000 + " s";
            }
        }

        // the decoder's share of the whole PC's processor (0-100), about once a second; null while ffplay is not running
        private TimeSpan _cpuLast;
        private int _cpuTick;
        private int? _cpuShown;
        public int? DecoderCpu()
        {
            Process p = _ffplay;
            if (p == null)
            {
                _cpuTick = 0;
                _cpuShown = null;
                return null;
            }
            int now = Environment.TickCount;
            if (_cpuTick != 0 && unchecked(now - _cpuTick) < 1000)
                return _cpuShown;
            try
            {
                TimeSpan cpu = p.TotalProcessorTime;
                if (_cpuTick != 0)
                    _cpuShown = (int)Math.Round(100.0 * (cpu - _cpuLast).TotalMilliseconds
                                                / (unchecked(now - _cpuTick) * Environment.ProcessorCount));
                _cpuLast = cpu;
                _cpuTick = now == 0 ? 1 : now;
            }
            catch (InvalidOperationException) { _cpuShown = null; }      // ffplay just ended
            return _cpuShown;
        }

        // what ffplay found in the stream (null until probed): "H.266" and "854x480"
        private static readonly Regex StreamLine = new Regex(@"Stream #\d+:\d+.*?: Video: (\w+).*?, (\d{2,5})x(\d{2,5})");
        private volatile string _codec;
        private volatile string _pictureSize;
        public string Codec { get { return _codec; } }
        public string PictureSize { get { return _pictureSize; } }

        private static string CodecName(string ffmpegName)
        {
            switch (ffmpegName)
            {
                case "vvc": return "H.266";
                case "hevc": return "H.265";
                case "h264": return "H.264";
                case "mpeg2video": return "MPEG-2";
                default: return ffmpegName.ToUpperInvariant();
            }
        }

        private HookProc _hookProc;
        private IntPtr _hook;

        // ffplay does not report the picture size; the window keeps its aspect ratio
        public MediaInfo LastMediaInfo { get; private set; }

        // view: ffplay's window is put inside this control
        public VideoPlayer(System.Windows.Forms.Control view, CircularBuffer stream, int volume)
        {
            _view = view;
            _viewWidth = Math.Max(320, _view.ClientSize.Width);
            _viewHeight = Math.Max(180, _view.ClientSize.Height);
            _view.Resize += (s, e) =>
            {
                _viewWidth = Math.Max(1, _view.ClientSize.Width);
                _viewHeight = Math.Max(1, _view.ClientSize.Height);
                FitFfplayWindow();
            };
            _stream = stream;
            _volume = volume;
            _worker = new Thread(WorkerLoop) { Name = "Video", IsBackground = true };
            _worker.Start();
        }

        public void Play()
        {
            lock (_lock)
            {
                _wantPlaying = true;
                _wantSession++;
            }
            _wake.Set();
        }

        public void Stop()
        {
            lock (_lock)
                _wantPlaying = false;
            _wake.Set();
        }

        // ffplay's process, whose line in the Windows mixer the bar's volume and mute work on (0 = none)
        public int SoundProcessId
        {
            get
            {
                try { return _ffplay == null ? 0 : _ffplay.Id; }
                catch (InvalidOperationException) { return 0; }      // just ended
            }
        }

        // volume and mute are set in Windows (AudioSession), ffplay itself always plays at full volume
        // 0..100, 0 = silent; ffplay only takes the volume at start, so it counts from the next lock
        public void SetVolume(int volume)
        {
            _volume = volume;
        }

        private static string FindFfplay()
        {
            string env = Environment.GetEnvironmentVariable("QOCAST_FFPLAY");
            string[] candidates =
            {
                env,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffplay.exe"),
                @"C:\mabs\local64\bin-video\ffplay.exe",
            };
            foreach (string c in candidates)
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                    return c;
            return null;
        }

        private void WorkerLoop()
        {
            while (true)
            {
                _wake.WaitOne();

                bool wantPlaying;
                int wantSession;
                lock (_lock)
                {
                    if (_disposed)
                        break;
                    wantPlaying = _wantPlaying;
                    wantSession = _wantSession;
                }

                try
                {
                    if (!wantPlaying)
                        StopFfplay();
                    else if (_playingSession != wantSession)
                    {
                        StopFfplay();
                        StartFfplay(wantSession);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Video: ffplay command failed");
                }

                // feed the stream while this session is wanted
                if (_ffplay != null)
                    Pump(wantSession);
            }

            StopFfplay();
        }

        private void StartFfplay(int session)
        {
            string exe = FindFfplay();
            if (exe == null)
            {
                Log.Error("Video: ffplay.exe not found (next to the player, QOCAST_FFPLAY, or C:\\mabs\\local64\\bin-video)");
                return;
            }

            Log.Information("Video: play (ffplay {Exe})", exe);
            var psi = new ProcessStartInfo(exe,
                "-hide_banner -loglevel info -fflags nobuffer -flags low_delay " +
                "-x " + _viewWidth + " -y " + _viewHeight + " -volume 100 " +
                "-window_title \"HB9IIU Experimental H.266 Player\" -i pipe:0")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var p = new Process { StartInfo = psi };
            _pictureShown = false;
            _codec = null;
            _pictureSize = null;
            _startTick = Environment.TickCount;
            p.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                // the progress line (about 30 a second) is left out of the log; ffplay prints the stream
                // line below only while it is on
                if (!(e.Data.Contains("M-V:") || e.Data.Contains("A-V:") || e.Data.Contains("M-A:")))
                    Log.Information("ffplay: " + e.Data);
                // ffplay sets up its picture filter when the first frame is decoded
                if (e.Data.Contains("ffplay_buffer"))
                    _pictureShown = true;
                // the stream line ffplay prints when it has probed the input, e.g.
                // "Stream #0:0[0x100]: Video: vvc (Main 10), yuv420p10le(tv), 854x480, 25 fps"
                Match video = StreamLine.Match(e.Data);
                if (video.Success)
                {
                    _codec = CodecName(video.Groups[1].Value);
                    _pictureSize = video.Groups[2].Value + "x" + video.Groups[3].Value;
                }
            };
            p.Start();
            p.BeginErrorReadLine();
            _ffplay = p;
            _playingSession = session;
            PictureChanged?.Invoke(true);
            new Thread(() => EmbedWhenReady(p)) { IsBackground = true, Name = "VideoEmbed" }.Start();
        }

        // waits for ffplay's window, then moves it into the view. ffplay shows its window only when the first
        // picture is decoded, which can take 20 s or more (waiting for a keyframe): so it waits as long as this
        // ffplay is the current one and runs
        private void EmbedWhenReady(Process p)
        {
            try
            {
                while (p == _ffplay && !p.HasExited)
                {
                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        IntPtr h = p.MainWindowHandle;
                        if (!_view.IsHandleCreated)
                            return;
                        _view.BeginInvoke(new Action(() =>
                        {
                            if (p != _ffplay)
                                return;
                            long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
                            style = (style & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME)) | WS_CHILD | WS_VISIBLE;
                            SetParent(h, _view.Handle);
                            SetWindowLongPtr(h, GWL_STYLE, new IntPtr(style));
                            // a disabled window gets no mouse input: clicks and dragging reach our view
                            EnableWindow(h, false);
                            InstallMouseHook();
                            _ffplayWindow = h;
                            FitFfplayWindow();
                        }));
                        // ffplay resizes its window to the video size at the first frame: fit again
                        foreach (int wait in new[] { 1000, 2000, 4000, 8000 })
                        {
                            Thread.Sleep(wait);
                            if (p.HasExited || !_view.IsHandleCreated)
                                return;
                            _view.BeginInvoke(new Action(FitFfplayWindow));
                        }
                        return;
                    }
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Video: could not put ffplay into the window: " + ex.Message);
            }
        }

        // UI thread
        private void InstallMouseHook()
        {
            if (_hook != IntPtr.Zero)
                return;
            _hookProc = MouseHookProc;
            _hook = SetWindowsHookEx(14, _hookProc, GetModuleHandle(null), 0);     // WH_MOUSE_LL
            if (_hook == IntPtr.Zero)
                Log.Warning("Video: mouse hook failed");
        }

        private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONUP = 0x205;
            int msg = wParam.ToInt32();
            IntPtr h = _ffplayWindow;
            if (code >= 0 && h != IntPtr.Zero && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONUP))
            {
                // the hook's own point is in screen pixels, the window rectangles are not when the
                // app is DPI-unaware: ask for the cursor the same way as for the rectangles
                Point pt;
                GetCursorPos(out pt);
                // only when the click is on ffplay's picture and nothing of another program is in front of it
                // (WindowFromPoint skips disabled windows, so it names one of our own windows there)
                Rect r;
                bool inside = GetWindowRect(h, out r) && pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
                if (inside)
                {
                    IntPtr under = WindowFromPoint(pt);
                    uint pid = 0;
                    if (under != IntPtr.Zero)
                        GetWindowThreadProcessId(under, out pid);
                    bool front = false;
                    try
                    {
                        Process ff = _ffplay;
                        front = pid != 0 && (pid == _ownPid || (ff != null && pid == (uint)ff.Id));
                    }
                    catch (InvalidOperationException) { }       // ffplay just ended
                    if (front)
                    {
                        var button = msg == WM_LBUTTONDOWN ? System.Windows.Forms.MouseButtons.Left
                                                           : System.Windows.Forms.MouseButtons.Right;
                        _view.BeginInvoke(new Action(() => PictureMouse?.Invoke(button)));
                    }
                    else
                        Log.Information("Video: click on the picture not used, window under the cursor {Under} belongs to process {Pid}", under, pid);
                }
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void FitFfplayWindow()
        {
            IntPtr h = _ffplayWindow;
            if (h != IntPtr.Zero)
                MoveWindow(h, 0, 0, _viewWidth, _viewHeight, true);
        }

        // TS bytes from the receiver to ffplay, starting at a sync byte, until stopped or ffplay ends
        private void Pump(int session)
        {
            bool synced = false;
            Stream stdin = _ffplay.StandardInput.BaseStream;
            try
            {
                while (true)
                {
                    lock (_lock)
                        if (_disposed || !_wantPlaying || _wantSession != session)
                            return;
                    if (_ffplay.HasExited)
                        break;

                    if (_stream.Count < MinBytes)
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    byte[] data = _stream.DequeueBytes(_stream.Count);
                    int start = 0;
                    if (!synced)
                    {
                        start = Array.IndexOf(data, TsSync);
                        if (start < 0)
                            continue;
                        synced = true;
                    }
                    stdin.Write(data, start, data.Length - start);
                    stdin.Flush();
                }
            }
            catch (IOException)
            {
                // ffplay window was closed
            }

            StopFfplay();
        }

        private void StopFfplay()
        {
            Process p = _ffplay;
            if (p == null)
                return;

            Log.Information("Video: stop");
            _ffplay = null;
            _ffplayWindow = IntPtr.Zero;
            _playingSession = -1;
            try { if (!p.HasExited) p.Kill(); }
            catch (Exception ex) { Log.Debug("ffplay kill: " + ex.Message); }
            p.Dispose();
            PictureChanged?.Invoke(false);
        }

        public void Dispose()
        {
            lock (_lock)
                _disposed = true;
            _wake.Set();
            if (!_worker.Join(5000))
                Log.Warning("Video: worker did not stop in time");
            StopFfplay();
            if (_hook != IntPtr.Zero)
                UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
