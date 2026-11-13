using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace ZapisEkrana
{
    internal sealed class Recorder
    {
        public string OutputFile;
        public string Warning;
        public string ErrorLog;

        Process _process;
        NamedPipeServerStream _pipe;
        AudioMixer _audio;
        readonly StringBuilder _log = new StringBuilder();
        readonly object _logGate = new object();
        volatile bool _sawFrame;

        public string Start(AppSettings settings, out string warning)
        {
            warning = null;
            Warning = null;
            ErrorLog = null;
            _log.Length = 0;

            if (!File.Exists(AppPaths.FfmpegFile))
                return "Не найден ffmpeg.exe рядом с программой.";

            string folderError = EnsureFolder(settings);
            if (folderError != null)
                return folderError;

            var monitors = Monitors.All();
            bool fallback;
            MonitorInfo monitor = Monitors.Pick(monitors, settings.MonitorDevice, out fallback);
            if (monitor == null)
                return "Не удалось начать запись.";

            string folder = AppPaths.EffectiveFolder(settings);
            OutputFile = NextFile(folder);

            bool noMic = settings.MicChosen && string.IsNullOrEmpty(settings.MicrophoneId);
            bool useDefault = !settings.MicChosen;
            _audio = new AudioMixer();
            string audioError = _audio.Prepare(noMic, useDefault, settings.MicrophoneId);
            if (audioError != null)
            {
                ErrorLog = _audio.Detail;
                WriteErrorFile();
                _audio.Stop();
                _audio = null;
                return audioError;
            }

            string pipeName = "ZapisEkrana" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            _pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1 << 20, 1 << 20);

            var psi = new ProcessStartInfo();
            psi.FileName = AppPaths.FfmpegFile;
            psi.Arguments = BuildArgs(monitor, pipeName, OutputFile, settings);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardInput = true;
            psi.WorkingDirectory = AppPaths.Dir;

            _process = new Process();
            _process.StartInfo = psi;
            _process.EnableRaisingEvents = true;
            _process.ErrorDataReceived += OnLog;
            _process.OutputDataReceived += OnLog;

            try
            {
                if (!_process.Start())
                    return Fail("Не удалось начать запись.");
            }
            catch (Exception ex)
            {
                Append("start " + ex.Message);
                return Fail("Не удалось начать запись.");
            }

            try { ChildJob.Add(_process.Handle); }
            catch { }

            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();

            IAsyncResult wait = _pipe.BeginWaitForConnection(null, null);
            int waited = 0;
            while (!wait.IsCompleted && !_process.HasExited && waited < 8000)
            {
                Thread.Sleep(50);
                waited += 50;
            }
            if (!wait.IsCompleted)
                return Fail("Не удалось начать запись.");

            try { _pipe.EndWaitForConnection(wait); }
            catch (Exception ex)
            {
                Append(ex.Message);
                return Fail("Не удалось начать запись.");
            }

            _audio.StartOutput(_pipe);

            waited = 0;
            while (waited < 8000 && !_process.HasExited && !_sawFrame)
            {
                Thread.Sleep(100);
                waited += 100;
            }
            if (_process.HasExited || !_sawFrame)
                return Fail("Не удалось начать запись.");

            if (fallback)
                warning = "Выбранный экран недоступен. Записываю основной.";
            if (!string.IsNullOrEmpty(_audio.Warning))
                warning = warning == null ? _audio.Warning : warning + " " + _audio.Warning;
            Warning = warning;
            return null;
        }

        public bool Stop()
        {
            try
            {
                if (_process != null && !_process.HasExited && _process.StartInfo.RedirectStandardInput)
                {
                    byte[] q = new byte[] { (byte)'q', 10 };
                    _process.StandardInput.BaseStream.Write(q, 0, q.Length);
                    _process.StandardInput.BaseStream.Flush();
                }
            }
            catch { }

            bool exited = false;
            try
            {
                if (_process != null)
                    exited = _process.WaitForExit(8000);
            }
            catch { }

            if (_audio != null)
            {
                try { _audio.Stop(); }
                catch { }
                _audio = null;
            }

            try
            {
                if (_pipe != null)
                {
                    _pipe.Dispose();
                    _pipe = null;
                }
            }
            catch { }

            try
            {
                if (!exited && _process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(3000);
                }
            }
            catch { }

            bool ok = exited && OutputFile != null && File.Exists(OutputFile) && new FileInfo(OutputFile).Length > 1024;
            if (!ok)
                DeleteTiny();
            CleanupProcess();
            return ok;
        }

        string Fail(string message)
        {
            try
            {
                if (_audio != null)
                    _audio.Stop();
            }
            catch { }
            _audio = null;
            try
            {
                if (_pipe != null)
                    _pipe.Dispose();
            }
            catch { }
            _pipe = null;
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(3000);
                }
            }
            catch { }
            ErrorLog = SnapshotLog();
            WriteErrorFile();
            DeleteTiny();
            CleanupProcess();
            return message;
        }

        void CleanupProcess()
        {
            if (_process == null)
                return;
            try { _process.CancelErrorRead(); }
            catch { }
            try { _process.CancelOutputRead(); }
            catch { }
            _process.Dispose();
            _process = null;
        }

        void DeleteTiny()
        {
            try
            {
                if (OutputFile != null && File.Exists(OutputFile) && new FileInfo(OutputFile).Length < 1024)
                    File.Delete(OutputFile);
            }
            catch { }
        }

        void WriteErrorFile()
        {
            try
            {
                string text = SnapshotLog();
                if (!string.IsNullOrEmpty(ErrorLog))
                    text = ErrorLog + Environment.NewLine + text;
                File.WriteAllText(Path.Combine(AppPaths.Dir, "last-error.txt"), text, Encoding.UTF8);
            }
            catch { }
        }

        string EnsureFolder(AppSettings settings)
        {
            string folder = AppPaths.EffectiveFolder(settings);
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, "~write_test.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return null;
            }
            catch
            {
                return "Нет доступа к папке. Выберите другую папку в настройках.";
            }
        }

        static string NextFile(string folder)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            string path = Path.Combine(folder, stamp + ".mp4");
            int n = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, stamp + "_" + n.ToString(CultureInfo.InvariantCulture) + ".mp4");
                n++;
            }
            return path;
        }

        static string BuildArgs(MonitorInfo monitor, string pipeName, string output, AppSettings settings)
        {
            int fps = settings.NormalizedFps();
            PictureSize frame = settings.NormalizedSize();
            int ow = monitor.Width;
            int oh = monitor.Height;
            bool shrink = ow > frame.Width || oh > frame.Height;
            if (shrink)
            {
                ow = frame.Width;
                oh = frame.Height;
            }
            else
            {
                if ((ow % 2) != 0) ow--;
                if ((oh % 2) != 0) oh--;
            }

            bool hevc = settings.UseHevc();
            double pixels = (ow * (double)oh) / (1920.0 * 1080.0);
            int video = (int)Math.Round(2000.0 * pixels * (fps / 30.0));
            if (hevc)
                video = (int)Math.Round(video * 0.65);
            if (video < 150)
                video = 150;
            int maxRate = video + (video / 4);
            int bufSize = video * 2;
            string codec = hevc
                ? "libx265 -preset superfast -tag:v hvc1 -x265-params log-level=error"
                : "libx264 -preset faster";

            string scale = "";
            if (shrink)
                scale = string.Format(CultureInfo.InvariantCulture, "-vf scale={0}:{1}:force_original_aspect_ratio=decrease,pad={0}:{1}:(ow-iw)/2:(oh-ih)/2 ", ow, oh);

            string pipe = @"\\.\pipe\" + pipeName;
            return string.Format(
                CultureInfo.InvariantCulture,
                "-hide_banner -y -thread_queue_size 1024 -f s16le -ar 48000 -ac 2 -i {0} -thread_queue_size 1024 -f gdigrab -framerate {1} -draw_mouse 1 -offset_x {2} -offset_y {3} -video_size {4}x{5} -i desktop {6}-map 1:v -map 0:a -pix_fmt yuv420p -c:v {7} -b:v {8}k -maxrate {9}k -bufsize {10}k -g {11} -c:a aac -b:a 128k -ar 48000 -ac 2 -movflags +faststart {12}",
                Quote(pipe),
                fps,
                monitor.X,
                monitor.Y,
                monitor.Width,
                monitor.Height,
                scale,
                codec,
                video,
                maxRate,
                bufSize,
                fps * 2,
                Quote(output));
        }

        static string Quote(string value)
        {
            if (value == null)
                value = "";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        void OnLog(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            if (e.Data.IndexOf("frame=", StringComparison.Ordinal) >= 0)
            {
                _sawFrame = true;
                return;
            }
            Append(e.Data);
        }

        void Append(string line)
        {
            lock (_logGate)
            {
                if (_log.Length > 60000)
                    _log.Remove(0, 20000);
                _log.AppendLine(line);
            }
        }

        string SnapshotLog()
        {
            lock (_logGate)
            {
                return _log.ToString().Replace("\u0001", "");
            }
        }
    }

    static class ChildJob
    {
        static IntPtr _job;
        const int ExtendedLimit = 9;
        const uint KillOnClose = 0x2000;

        public static void Add(IntPtr process)
        {
            if (_job == IntPtr.Zero)
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero)
                    return;
                var info = new JobExtended();
                info.Basic.LimitFlags = KillOnClose;
                int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(JobExtended));
                IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                try
                {
                    System.Runtime.InteropServices.Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(_job, ExtendedLimit, ptr, (uint)size))
                    {
                        _job = IntPtr.Zero;
                        return;
                    }
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
                }
            }
            AssignProcessToJobObject(_job, process);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct JobBasic
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public System.UIntPtr MinimumWorkingSetSize;
            public System.UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public System.UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct JobExtended
        {
            public JobBasic Basic;
            public IoCounters Io;
            public System.UIntPtr ProcessMemoryLimit;
            public System.UIntPtr JobMemoryLimit;
            public System.UIntPtr PeakProcessMemoryUsed;
            public System.UIntPtr PeakJobMemoryUsed;
        }
    }
}
