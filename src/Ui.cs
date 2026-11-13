using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ZapisEkrana
{
    static class Theme
    {
        public static readonly Color Paper = Color.FromArgb(250, 249, 245);
        public static readonly Color Card = Color.White;
        public static readonly Color Ink = Color.FromArgb(31, 30, 27);
        public static readonly Color Muted = Color.FromArgb(110, 106, 98);
        public static readonly Color Line = Color.FromArgb(232, 228, 218);
        public static readonly Color Accent = Color.FromArgb(204, 120, 92);
        public static readonly Color Danger = Color.FromArgb(166, 62, 48);
        public static readonly Color Note = Color.FromArgb(140, 78, 58);

        public static void StyleForm(Form form)
        {
            form.BackColor = Paper;
            form.ForeColor = Ink;
        }

        public static void QuietButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Card;
            button.ForeColor = Ink;
            button.FlatAppearance.BorderColor = Line;
            button.FlatAppearance.BorderSize = 1;
            button.Cursor = Cursors.Hand;
        }

        public static void AccentButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
        }
    }

    internal sealed class MainForm : Form
    {
        enum Phase { Idle, Starting, Recording, Stopping }

        readonly Indicator _dot = new Indicator();
        readonly Label _state = new Label();
        readonly Label _timer = new Label();
        readonly Button _record = new Button();
        readonly Button _settingsButton = new Button();
        readonly Label _message = new Label();
        readonly ListBox _recent = new ListBox();
        readonly System.Windows.Forms.Timer _clock = new System.Windows.Forms.Timer();
        AppSettings _settings;
        Recorder _recorder;
        Phase _phase;
        bool _closeAfter;
        DateTime _started;

        public MainForm()
        {
            Text = "Запись экрана";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size(400, 318);
            Font = new Font("Segoe UI", 10f);
            Theme.StyleForm(this);

            _dot.Bounds = new Rectangle(18, 18, 16, 16);
            _state.Bounds = new Rectangle(42, 14, 340, 24);
            _state.Text = "Готово";
            _state.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _state.ForeColor = Theme.Ink;
            _timer.Bounds = new Rectangle(42, 40, 340, 32);
            _timer.Text = "00:00:00";
            _timer.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            _timer.ForeColor = Theme.Ink;

            _record.Bounds = new Rectangle(18, 84, 210, 40);
            _record.Text = "Захват экрана";
            _record.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _record.Click += OnRecordClick;
            Theme.AccentButton(_record);

            _settingsButton.Bounds = new Rectangle(240, 84, 142, 40);
            _settingsButton.Text = "Настройки";
            _settingsButton.Click += OnSettingsClick;
            Theme.QuietButton(_settingsButton);

            _message.Bounds = new Rectangle(18, 130, 364, 36);
            _message.ForeColor = Theme.Note;
            _message.Text = "";

            var recentLabel = new Label();
            recentLabel.Bounds = new Rectangle(18, 168, 364, 20);
            recentLabel.ForeColor = Theme.Muted;
            recentLabel.Text = "Последние записи";

            _recent.Bounds = new Rectangle(18, 190, 364, 112);
            _recent.IntegralHeight = false;
            _recent.BorderStyle = BorderStyle.FixedSingle;
            _recent.BackColor = Theme.Card;
            _recent.ForeColor = Theme.Ink;
            _recent.Font = new Font("Segoe UI", 9f);
            _recent.MouseClick += OnRecentClick;

            Controls.Add(_dot);
            Controls.Add(_state);
            Controls.Add(_timer);
            Controls.Add(_record);
            Controls.Add(_settingsButton);
            Controls.Add(_message);
            Controls.Add(recentLabel);
            Controls.Add(_recent);

            _clock.Interval = 200;
            _clock.Tick += OnTick;
            _settings = SettingsStore.Load();
            RefreshRecent();
            if (!File.Exists(AppPaths.FfmpegFile))
                _message.Text = "Не найден ffmpeg.exe рядом с программой.";
        }

        void RefreshRecent()
        {
            _recent.Items.Clear();
            string folder = AppPaths.EffectiveFolder(_settings);
            if (!Directory.Exists(folder))
                return;
            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.mp4");
            }
            catch
            {
                return;
            }
            Array.Sort(files, delegate(string a, string b)
            {
                return File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a));
            });
            int count = files.Length;
            if (count > 6)
                count = 6;
            for (int i = 0; i < count; i++)
                _recent.Items.Add(new RecentItem(files[i]));
        }

        void OnRecentClick(object sender, MouseEventArgs e)
        {
            if (_phase != Phase.Idle && _phase != Phase.Recording)
                return;
            int index = _recent.IndexFromPoint(e.Location);
            if (index < 0 || index >= _recent.Items.Count)
                return;
            var item = _recent.Items[index] as RecentItem;
            if (item == null)
                return;
            if (!File.Exists(item.Path))
            {
                _message.Text = "Файл уже удалён.";
                RefreshRecent();
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            }
            catch
            {
                _message.Text = "Не удалось открыть запись.";
            }
        }

        void OnTick(object sender, EventArgs e)
        {
            if (_phase != Phase.Recording)
                return;
            TimeSpan elapsed = DateTime.Now - _started;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;
            _timer.Text = string.Format("{0:00}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
        }

        void OnSettingsClick(object sender, EventArgs e)
        {
            if (_phase != Phase.Idle)
                return;
            using (var dialog = new SettingsForm(_settings))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _settings = SettingsStore.Load();
                    RefreshRecent();
                }
            }
        }

        void OnRecordClick(object sender, EventArgs e)
        {
            if (_phase == Phase.Idle)
                BeginStart();
            else if (_phase == Phase.Recording)
                BeginStop(false);
        }

        void BeginStart()
        {
            _phase = Phase.Starting;
            _message.Text = "";
            _state.Text = "Запуск...";
            _record.Enabled = false;
            _settingsButton.Enabled = false;
            var worker = new System.Threading.Thread(StartWorker);
            worker.IsBackground = true;
            worker.Start();
        }

        void StartWorker()
        {
            var recorder = new Recorder();
            string warning;
            string error;
            try
            {
                error = recorder.Start(_settings, out warning);
            }
            catch (Exception ex)
            {
                error = "Не удалось начать запись.";
                try
                {
                    File.WriteAllText(Path.Combine(AppPaths.Dir, "last-error.txt"), ex.ToString());
                }
                catch { }
                warning = null;
                try { recorder.Stop(); }
                catch { }
            }

            BeginInvoke(new Action(delegate
            {
                if (IsDisposed)
                    return;
                if (error != null)
                {
                    _recorder = null;
                    _phase = Phase.Idle;
                    _state.Text = "Готово";
                    _record.Enabled = true;
                    StyleIdle();
                    _settingsButton.Enabled = true;
                    _message.Text = error;
                    if (_closeAfter)
                        Close();
                    else if (error.IndexOf("папк", StringComparison.Ordinal) < 0)
                        MessageBox.Show(this, error, "Запись экрана", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else
                        MessageBox.Show(this, error, "Запись экрана", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _recorder = recorder;
                _phase = Phase.Recording;
                _started = DateTime.Now;
                _state.Text = "Идёт запись";
                _dot.On = true;
                _dot.Invalidate();
                _timer.Text = "00:00:00";
                _clock.Start();
                _record.Enabled = true;
                StyleStop();
                _settingsButton.Enabled = false;
                _message.Text = warning ?? "";
                if (_closeAfter)
                    BeginStop(true);
                else if (_settings.MinimizeOnCapture)
                    WindowState = FormWindowState.Minimized;
            }));
        }

        void BeginStop(bool thenClose)
        {
            if (_phase != Phase.Recording)
                return;
            _phase = Phase.Stopping;
            _closeAfter = thenClose;
            _clock.Stop();
            _record.Enabled = false;
            _state.Text = "Сохранение...";
            Recorder recorder = _recorder;
            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                bool ok = false;
                string path = recorder == null ? null : recorder.OutputFile;
                try
                {
                    if (recorder != null)
                        ok = recorder.Stop();
                }
                catch { }
                BeginInvoke(new Action(delegate { AfterStop(ok, path); }));
            }));
            worker.IsBackground = true;
            worker.Start();
        }

        void AfterStop(bool ok, string path)
        {
            if (IsDisposed)
                return;
            _recorder = null;
            _phase = Phase.Idle;
            _dot.On = false;
            _dot.Invalidate();
            _record.Enabled = true;
            StyleIdle();
            _settingsButton.Enabled = true;
            _timer.Text = "00:00:00";
            if (ok && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _state.Text = "Готово";
                _message.Text = "Сохранено: " + Path.GetFileName(path);
                RefreshRecent();
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch { }
            }
            else
            {
                _state.Text = "Готово";
                _message.Text = "Не удалось сохранить запись.";
            }
            if (_closeAfter)
                Close();
        }

        void StyleStop()
        {
            _record.Text = "Стоп";
            _record.BackColor = Theme.Danger;
            _record.ForeColor = Color.White;
            _record.FlatAppearance.BorderSize = 0;
        }

        void StyleIdle()
        {
            _record.Text = "Захват экрана";
            Theme.AccentButton(_record);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_phase == Phase.Recording)
            {
                e.Cancel = true;
                _closeAfter = true;
                BeginStop(true);
                return;
            }
            if (_phase == Phase.Starting || _phase == Phase.Stopping)
            {
                e.Cancel = true;
                _closeAfter = true;
            }
            base.OnFormClosing(e);
        }
    }

    sealed class RecentItem
    {
        public readonly string Path;

        public RecentItem(string path)
        {
            Path = path;
        }

        public override string ToString()
        {
            return System.IO.Path.GetFileName(Path);
        }
    }

    sealed class Indicator : Control
    {
        public bool On;

        public Indicator()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = On ? Theme.Danger : Theme.Line;
            using (var brush = new SolidBrush(color))
                e.Graphics.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
        }
    }

    internal sealed class SettingsForm : Form
    {
        readonly TextBox _folder = new TextBox();
        readonly ComboBox _mic = new ComboBox();
        readonly ComboBox _screen = new ComboBox();
        readonly Label _screenLabel = new Label();
        readonly ComboBox _codec = new ComboBox();
        readonly ComboBox _width = new ComboBox();
        readonly ComboBox _fps = new ComboBox();
        readonly CheckBox _minimize = new CheckBox();
        readonly AppSettings _settings;
        readonly List<MonitorInfo> _monitors;

        public SettingsForm(AppSettings settings)
        {
            _settings = settings;
            _monitors = Monitors.All();
            Text = "Настройки";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            int optionsTop = _monitors.Count > 1 ? 176 : 122;
            ClientSize = new Size(460, optionsTop + 250);
            Font = new Font("Segoe UI", 10f);
            Theme.StyleForm(this);

            var folderLabel = new Label();
            folderLabel.Text = "Папка записей";
            folderLabel.Bounds = new Rectangle(16, 12, 420, 20);
            _folder.Bounds = new Rectangle(16, 34, 330, 24);
            _folder.Text = AppPaths.EffectiveFolder(settings);
            var browse = new Button();
            browse.Text = "Обзор";
            browse.Bounds = new Rectangle(354, 32, 90, 28);
            browse.Click += OnBrowse;

            var micLabel = new Label();
            micLabel.Text = "Микрофон";
            micLabel.Bounds = new Rectangle(16, 68, 420, 20);
            _mic.Bounds = new Rectangle(16, 90, 428, 24);
            _mic.DropDownStyle = ComboBoxStyle.DropDownList;
            FillMics();

            _screenLabel.Text = "Экран";
            _screenLabel.Bounds = new Rectangle(16, 122, 420, 20);
            _screen.Bounds = new Rectangle(16, 144, 428, 24);
            _screen.DropDownStyle = ComboBoxStyle.DropDownList;
            _screenLabel.Visible = _monitors.Count > 1;
            _screen.Visible = _monitors.Count > 1;
            for (int i = 0; i < _monitors.Count; i++)
                _screen.Items.Add(_monitors[i]);
            SelectMonitor();

            var codecLabel = new Label();
            codecLabel.Text = "Кодек";
            codecLabel.Bounds = new Rectangle(16, optionsTop, 420, 20);
            _codec.Bounds = new Rectangle(16, optionsTop + 22, 428, 24);
            _codec.DropDownStyle = ComboBoxStyle.DropDownList;
            _codec.Items.Add("H.264 — открывается в Windows");
            _codec.Items.Add("H.265 — файл меньше, нужен плеер с HEVC");
            _codec.SelectedIndex = settings.UseHevc() ? 1 : 0;

            var widthLabel = new Label();
            widthLabel.Text = "Размер картинки";
            widthLabel.Bounds = new Rectangle(16, optionsTop + 52, 420, 20);
            _width.Bounds = new Rectangle(16, optionsTop + 74, 428, 24);
            _width.DropDownStyle = ComboBoxStyle.DropDownList;
            PictureSize selectedSize = settings.NormalizedSize();
            PictureSize[] sizes = PictureSize.All();
            int widthIndex = sizes.Length - 1;
            for (int i = 0; i < sizes.Length; i++)
            {
                _width.Items.Add(sizes[i]);
                if (sizes[i].Width == selectedSize.Width && sizes[i].Height == selectedSize.Height)
                    widthIndex = i;
            }
            _width.SelectedIndex = widthIndex;

            var fpsLabel = new Label();
            fpsLabel.Text = "Кадры в секунду";
            fpsLabel.Bounds = new Rectangle(16, optionsTop + 104, 420, 20);
            _fps.Bounds = new Rectangle(16, optionsTop + 126, 428, 24);
            _fps.DropDownStyle = ComboBoxStyle.DropDownList;
            int[] rates = new int[] { 15, 24, 30 };
            int fpsIndex = 2;
            for (int i = 0; i < rates.Length; i++)
            {
                _fps.Items.Add(rates[i].ToString());
                if (rates[i] == settings.NormalizedFps())
                    fpsIndex = i;
            }
            _fps.SelectedIndex = fpsIndex;

            _minimize.Text = "Сворачивать окно";
            _minimize.AutoSize = true;
            _minimize.Checked = settings.MinimizeOnCapture;
            _minimize.Location = new Point(16, optionsTop + 160);

            int buttonsTop = optionsTop + 192;
            var save = new Button();
            save.Text = "Сохранить";
            save.Bounds = new Rectangle(244, buttonsTop, 110, 32);
            save.Click += OnSave;
            var cancel = new Button();
            cancel.Text = "Отмена";
            cancel.Bounds = new Rectangle(360, buttonsTop, 84, 32);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            Theme.AccentButton(save);
            Theme.QuietButton(cancel);
            Theme.QuietButton(browse);

            Controls.Add(folderLabel);
            Controls.Add(_folder);
            Controls.Add(browse);
            Controls.Add(micLabel);
            Controls.Add(_mic);
            Controls.Add(_screenLabel);
            Controls.Add(_screen);
            Controls.Add(codecLabel);
            Controls.Add(_codec);
            Controls.Add(widthLabel);
            Controls.Add(_width);
            Controls.Add(fpsLabel);
            Controls.Add(_fps);
            Controls.Add(_minimize);
            Controls.Add(save);
            Controls.Add(cancel);
            for (int i = 0; i < Controls.Count; i++)
            {
                Label label = Controls[i] as Label;
                if (label != null)
                    label.ForeColor = Theme.Muted;
                TextBox box = Controls[i] as TextBox;
                if (box != null)
                {
                    box.BackColor = Theme.Card;
                    box.ForeColor = Theme.Ink;
                    box.BorderStyle = BorderStyle.FixedSingle;
                }
                ComboBox combo = Controls[i] as ComboBox;
                if (combo != null)
                {
                    combo.BackColor = Theme.Card;
                    combo.ForeColor = Theme.Ink;
                    combo.FlatStyle = FlatStyle.Flat;
                }
            }
            _minimize.ForeColor = Theme.Ink;
        }

        void FillMics()
        {
            _mic.Items.Clear();
            _mic.Items.Add("Без микрофона");
            List<MicInfo> mics = AudioDevices.ListCapture();
            int select = 0;
            if (!_settings.MicChosen)
            {
                string preferred = DefaultMicId();
                for (int i = 0; i < mics.Count; i++)
                {
                    _mic.Items.Add(mics[i]);
                    if (!string.IsNullOrEmpty(preferred) && string.Equals(mics[i].Id, preferred, StringComparison.OrdinalIgnoreCase))
                        select = i + 1;
                }
                if (select == 0 && mics.Count > 0)
                    select = 1;
            }
            else if (string.IsNullOrEmpty(_settings.MicrophoneId))
            {
                for (int i = 0; i < mics.Count; i++)
                    _mic.Items.Add(mics[i]);
                select = 0;
            }
            else
            {
                bool found = false;
                for (int i = 0; i < mics.Count; i++)
                {
                    _mic.Items.Add(mics[i]);
                    if (string.Equals(mics[i].Id, _settings.MicrophoneId, StringComparison.OrdinalIgnoreCase))
                    {
                        select = i + 1;
                        found = true;
                    }
                }
                if (!found)
                    select = 0;
            }
            _mic.SelectedIndex = select;
        }

        static string DefaultMicId()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(1, 0, out device) < 0 || device == null)
                    return "";
                string id = WasapiNative.ReadId(device);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(device);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(enumerator);
                return id;
            }
            catch
            {
                return "";
            }
        }

        void SelectMonitor()
        {
            if (_screen.Items.Count == 0)
                return;
            int select = 0;
            for (int i = 0; i < _monitors.Count; i++)
            {
                if (_monitors[i].Primary)
                    select = i;
                if (!string.IsNullOrEmpty(_settings.MonitorDevice) &&
                    string.Equals(_monitors[i].DeviceName, _settings.MonitorDevice, StringComparison.OrdinalIgnoreCase))
                {
                    select = i;
                    break;
                }
            }
            _screen.SelectedIndex = select;
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Папка для записей";
                try
                {
                    if (Directory.Exists(_folder.Text))
                        dialog.SelectedPath = _folder.Text;
                }
                catch { }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _folder.Text = dialog.SelectedPath;
            }
        }

        void OnSave(object sender, EventArgs e)
        {
            string folder = _folder.Text.Trim();
            try
            {
                folder = Path.GetFullPath(folder);
            }
            catch
            {
                MessageBox.Show(this, "Нет доступа к папке. Выберите другую папку в настройках.", "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.Equals(folder, AppPaths.DefaultFolder(), StringComparison.OrdinalIgnoreCase))
                _settings.OutputFolder = "";
            else
                _settings.OutputFolder = folder;

            if (_mic.SelectedIndex <= 0)
            {
                _settings.MicChosen = true;
                _settings.MicrophoneId = "";
            }
            else
            {
                var mic = _mic.SelectedItem as MicInfo;
                _settings.MicChosen = true;
                _settings.MicrophoneId = mic == null ? "" : mic.Id;
            }

            if (_monitors.Count == 1)
                _settings.MonitorDevice = _monitors[0].DeviceName;
            else if (_screen.SelectedItem is MonitorInfo)
                _settings.MonitorDevice = ((MonitorInfo)_screen.SelectedItem).DeviceName;

            _settings.MinimizeOnCapture = _minimize.Checked;
            _settings.Codec = _codec.SelectedIndex == 1 ? "h265" : "h264";
            PictureSize chosenSize = _width.SelectedItem as PictureSize;
            if (chosenSize == null)
                chosenSize = _settings.NormalizedSize();
            _settings.Width = chosenSize.Width;
            _settings.Height = chosenSize.Height;
            int chosenFps;
            _settings.Fps = int.TryParse(_fps.SelectedItem as string, out chosenFps) ? chosenFps : 30;

            try
            {
                SettingsStore.Save(_settings);
            }
            catch
            {
                MessageBox.Show(this, "Не удалось сохранить настройки.", "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
