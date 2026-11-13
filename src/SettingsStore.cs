using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ZapisEkrana
{
    internal sealed class AppSettings
    {
        public string OutputFolder = "";
        public string MicrophoneId = "";
        public string MonitorDevice = "";
        public bool MicChosen;
        public bool MinimizeOnCapture;
        public string Codec = "h264";
        public int Width = 1920;
        public int Height = 1080;
        public int Fps = 30;

        public bool UseHevc()
        {
            return Codec == "h265";
        }

        public PictureSize NormalizedSize()
        {
            PictureSize[] sizes = PictureSize.All();
            for (int i = 0; i < sizes.Length; i++)
            {
                if (Width == sizes[i].Width && Height == sizes[i].Height)
                    return sizes[i];
            }
            for (int i = 0; i < sizes.Length; i++)
            {
                if (Width == sizes[i].Width)
                    return sizes[i];
            }
            return sizes[sizes.Length - 1];
        }

        public int NormalizedFps()
        {
            if (Fps == 15 || Fps == 24 || Fps == 30)
                return Fps;
            return 30;
        }
    }

    internal sealed class PictureSize
    {
        public int Width;
        public int Height;
        public string Label;

        public override string ToString()
        {
            return Label;
        }

        public static PictureSize[] All()
        {
            return new PictureSize[]
            {
                Make(854, 480, "854×480 (480p)"),
                Make(1280, 720, "HD 1280×720"),
                Make(1920, 1080, "Full HD 1920×1080")
            };
        }

        static PictureSize Make(int width, int height, string label)
        {
            PictureSize size = new PictureSize();
            size.Width = width;
            size.Height = height;
            size.Label = label;
            return size;
        }
    }

    internal static class AppPaths
    {
        public static string Dir
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(Dir, "settings.txt"); }
        }

        public static string FfmpegFile
        {
            get { return Path.Combine(Dir, "ffmpeg.exe"); }
        }

        public static string DefaultFolder()
        {
            return Path.Combine(Dir, "Записи");
        }

        public static string EffectiveFolder(AppSettings settings)
        {
            if (settings != null && !string.IsNullOrEmpty(settings.OutputFolder))
                return settings.OutputFolder;
            return DefaultFolder();
        }
    }

    internal static class SettingsStore
    {
        public static AppSettings Load()
        {
            var settings = new AppSettings();
            string path = AppPaths.SettingsFile;
            if (!File.Exists(path))
                return settings;

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = line.Substring(0, eq);
                    string value = Unescape(line.Substring(eq + 1));
                    if (key == "output")
                        settings.OutputFolder = value;
                    else if (key == "mic")
                        settings.MicrophoneId = value;
                    else if (key == "monitor")
                        settings.MonitorDevice = value;
                    else if (key == "micChosen")
                        settings.MicChosen = value == "1";
                    else if (key == "minimize")
                        settings.MinimizeOnCapture = value == "1";
                    else if (key == "codec")
                        settings.Codec = value;
                    else if (key == "width")
                    {
                        int width;
                        if (int.TryParse(value, out width))
                            settings.Width = width;
                    }
                    else if (key == "height")
                    {
                        int height;
                        if (int.TryParse(value, out height))
                            settings.Height = height;
                    }
                    else if (key == "fps")
                    {
                        int fps;
                        if (int.TryParse(value, out fps))
                            settings.Fps = fps;
                    }
                }
            }
            catch
            {
            }

            return settings;
        }

        public static void Save(AppSettings settings)
        {
            var text = new StringBuilder();
            text.Append("output=").AppendLine(Escape(settings.OutputFolder));
            text.Append("mic=").AppendLine(Escape(settings.MicrophoneId));
            text.Append("monitor=").AppendLine(Escape(settings.MonitorDevice));
            text.Append("micChosen=").AppendLine(settings.MicChosen ? "1" : "0");
            text.Append("minimize=").AppendLine(settings.MinimizeOnCapture ? "1" : "0");
            text.Append("codec=").AppendLine(Escape(settings.Codec));
            PictureSize size = settings.NormalizedSize();
            text.Append("width=").AppendLine(size.Width.ToString());
            text.Append("height=").AppendLine(size.Height.ToString());
            text.Append("fps=").AppendLine(settings.NormalizedFps().ToString());
            File.WriteAllText(AppPaths.SettingsFile, text.ToString(), new UTF8Encoding(true));
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");
        }

        static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char n = value[i + 1];
                    if (n == '\\') { sb.Append('\\'); i++; }
                    else if (n == 'n') { sb.Append('\n'); i++; }
                    else sb.Append(c);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
