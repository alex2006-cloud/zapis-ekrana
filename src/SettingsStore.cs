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
