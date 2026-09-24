using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SchoolBell
{
    class TrayApp : ApplicationContext
    {
        static readonly TimeSpan MaxLate = TimeSpan.FromSeconds(90);

        readonly bool preview;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly Control invoker;
        readonly BellPlayer player = new BellPlayer();
        readonly Icon iconOn, iconOff;
        readonly EventWaitHandle showEvent, exitEvent;
        readonly RegisteredWaitHandle showWait, exitWait;
        Settings settings;
        DateTime lastTick;
        DateTime lastRung = DateTime.MinValue;
        ScheduleForm editor;
        Font boldFont;
        bool exiting;

        public TrayApp(bool autostart, bool preview)
        {
            this.preview = preview;
            bool existed = true;
            settings = preview ? Settings.CreateDefault() : Settings.Load(out existed);
            if (!preview)
            {
                if (!existed) TrySave();
                Log.Trim();
                Log.Write("Программа запущена" + (autostart ? " при входе в Windows" : "") + ", версия " + Program.Version);
            }

            invoker = new Control();
            invoker.CreateControl();
            IntPtr forceHandle = invoker.Handle;

            int iconSize = SystemInformation.SmallIconSize.Width;
            iconOn = IconArt.MakeIcon(iconSize, true);
            iconOff = IconArt.MakeIcon(iconSize, false);

            menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            menu.Items.Add("…");
            menu.Opening += (s, e) =>
            {
                BuildMenu();
                e.Cancel = false;
            };

            tray = new NotifyIcon { Icon = iconOn, Text = Program.AppName, ContextMenuStrip = menu };
            if (preview) return;

            tray.MouseUp += OnTrayMouseUp;
            tray.Visible = true;

            Power.KeepAwake(settings.KeepAwake);
            try { player.Prepare(settings); }
            catch (Exception ex) { Log.Write("Не удалось подготовить звук: " + ex.Message); }

            lastTick = DateTime.Now;
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (s, e) => OnTick();
            timer.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnTimeChanged;

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (st, to) => OnUi(ShowStatusBalloon), null, -1, false);
            exitWait = ThreadPool.RegisterWaitForSingleObject(exitEvent, (st, to) => OnUi(() => ExitApp("по запросу установщика")), null, -1, false);

            UpdateIconAndTooltip();
            if (!existed)
                tray.ShowBalloonTip(10000, Program.AppName,
                    "Программа работает и будет давать звонки по расписанию. Щёлкните по колокольчику, чтобы открыть меню. " +
                    "Если значка не видно — он под стрелкой ^ рядом с часами.", ToolTipIcon.Info);
            else if (!autostart)
                ShowStatusBalloon();
        }

        void OnUi(Action action)
        {
            try { invoker.BeginInvoke(action); }
            catch { }
        }

        void OnTick()
        {
            DateTime now = DateTime.Now;
            DateTime from = lastTick;
            lastTick = now;
            if (now - from > MaxLate)
                Log.Write("Программа не работала с " + from.ToString("dd.MM HH:mm:ss") + " до " + now.ToString("dd.MM HH:mm:ss") +
                          " (сон, выключение или перевод часов) — звонки за это время пропущены");
            foreach (BellEvent ev in Schedule.Due(settings, from, now, lastRung, MaxLate))
            {
                lastRung = ev.Time;
                Ring("Звонок: " + ev.Text);
            }
            UpdateIconAndTooltip();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                OnUi(() => Log.Write("Компьютер вышел из спящего режима"));
        }

        void OnTimeChanged(object sender, EventArgs e)
        {
            OnUi(() =>
            {
                Log.Write("Изменено системное время: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
                UpdateIconAndTooltip();
            });
        }

        void Ring(string logText)
        {
            string error;
            try { error = player.Play(settings); }
            catch (Exception ex) { error = ex.Message; }
            Log.Write(logText + (error != null ? " — " + error : ""));
            if (error != null)
                tray.ShowBalloonTip(10000, Program.AppName, error, ToolTipIcon.Warning);
        }

        bool SkipToday
        {
            get { return settings.SkipDate.Date == DateTime.Today; }
        }

        void UpdateIconAndTooltip()
        {
            Icon want = settings.Enabled && !SkipToday ? iconOn : iconOff;
            if (tray.Icon != want) tray.Icon = want;
            string tip = Program.AppName + "\n" + ShortStatus(DateTime.Now);
            if (tip.Length > 63) tip = tip.Substring(0, 62) + "…";
            if (tray.Text != tip) tray.Text = tip;
        }

        string ShortStatus(DateTime now)
        {
            if (!settings.Enabled) return "Звонки выключены";
            BellEvent next = Schedule.Next(settings, now);
            if (next == null) return "Нет звонков в ближайшие дни";
            return "Следующий звонок: " + When(next.Time, now);
        }

        IEnumerable<string> StatusLines(DateTime now)
        {
            if (!settings.Enabled)
            {
                yield return "Звонки выключены";
                yield break;
            }
            if (SkipToday) yield return "Сегодня звонков не будет";
            string state = Schedule.CurrentState(settings, now);
            if (state != null) yield return "Сейчас " + state;
            BellEvent next = Schedule.Next(settings, now);
            if (next == null)
            {
                yield return "Нет звонков в ближайшие дни";
                yield break;
            }
            string line = "Следующий звонок: " + When(next.Time, now);
            if (next.Time.Date == now.Date) line += " (" + In(next.Time - now) + ")";
            yield return line;
            yield return "    " + next.Text;
        }

        static string When(DateTime t, DateTime now)
        {
            string time = t.ToString("HH:mm");
            if (t.Date == now.Date) return time;
            if (t.Date == now.Date.AddDays(1)) return "завтра в " + time;
            return Settings.DayShort[Schedule.DayIndex(t)] + " " + t.ToString("dd.MM") + " в " + time;
        }

        static string In(TimeSpan d)
        {
            int min = (int)Math.Ceiling(d.TotalMinutes);
            if (min <= 1) return "меньше минуты";
            if (min < 60) return "через " + min + " мин";
            return "через " + min / 60 + " ч " + min % 60 + " мин";
        }

        void ShowStatusBalloon()
        {
            var lines = new List<string>(StatusLines(DateTime.Now));
            tray.ShowBalloonTip(6000, Program.AppName + " работает", string.Join("\n", lines.ToArray()), ToolTipIcon.Info);
        }

        void OnTrayMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            MethodInfo show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (show != null) show.Invoke(tray, null);
        }

        void BuildMenu()
        {
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            DateTime now = DateTime.Now;
            ToolStripItemCollection items = menu.Items;

            var title = new ToolStripMenuItem(Program.AppName) { Enabled = false };
            if (boldFont == null) boldFont = new Font(menu.Font, FontStyle.Bold);
            title.Font = boldFont;
            items.Add(title);
            foreach (string line in StatusLines(now))
                items.Add(new ToolStripMenuItem(line) { Enabled = false });
            items.Add(new ToolStripSeparator());

            items.Add(Check("Звонки включены", settings.Enabled, () =>
            {
                settings.Enabled = !settings.Enabled;
                Changed(settings.Enabled ? "Звонки включены" : "Звонки выключены");
            }));
            items.Add(Check("Не звонить сегодня", SkipToday, () =>
            {
                settings.SkipDate = SkipToday ? DateTime.MinValue : DateTime.Today;
                Changed(SkipToday ? "Сегодня звонков не будет" : "Звонки сегодня снова включены");
            }));
            items.Add(new ToolStripSeparator());

            foreach (Shift sh in settings.Shifts)
            {
                Shift shift = sh;
                items.Add(Check(shift.Name + "   " + shift.RangeText(), shift.Enabled, () =>
                {
                    shift.Enabled = !shift.Enabled;
                    Changed(shift.Name + ": звонки " + (shift.Enabled ? "включены" : "выключены"));
                }));
            }
            var days = new ToolStripMenuItem("Дни недели: " + Settings.DaysSummary(settings.Days));
            for (int d = 1; d <= 7; d++)
            {
                int day = d;
                days.DropDownItems.Add(Check(Settings.DayNames[day], settings.Days[day], () =>
                {
                    settings.Days[day] = !settings.Days[day];
                    Changed("Дни недели: " + Settings.DaysSummary(settings.Days));
                }));
            }
            items.Add(days);
            items.Add(Item("Изменить расписание…", OpenEditor));
            items.Add(new ToolStripSeparator());

            var sound = new ToolStripMenuItem("Звук звонка");
            sound.DropDownItems.Add(Check("Электрический звонок (классический)", settings.Sound == SoundKind.Electric, () => SetSound(SoundKind.Electric)));
            sound.DropDownItems.Add(Check("Мелодичный звонок", settings.Sound == SoundKind.Chime, () => SetSound(SoundKind.Chime)));
            if (!string.IsNullOrEmpty(settings.SoundFile) && File.Exists(settings.SoundFile))
                sound.DropDownItems.Add(Check("Свой файл: " + settings.SoundFileTitle, settings.Sound == SoundKind.File, () => SetSound(SoundKind.File)));
            sound.DropDownItems.Add(Item("Выбрать свой файл (WAV, MP3)…", ChooseSoundFile));
            sound.DropDownItems.Add(new ToolStripSeparator());
            var volume = new ToolStripMenuItem("Громкость звонка: " + settings.Volume + "%");
            foreach (int v in new[] { 100, 75, 50, 25 })
            {
                int value = v;
                volume.DropDownItems.Add(Check(value + "%", settings.Volume == value, () =>
                {
                    settings.Volume = value;
                    Changed("Громкость звонка: " + value + "%");
                }));
            }
            sound.DropDownItems.Add(volume);
            var duration = new ToolStripMenuItem("Длительность электрического звонка: " + settings.Duration + " с");
            foreach (int v in new[] { 3, 5, 7, 10, 15 })
            {
                int value = v;
                duration.DropDownItems.Add(Check(value + " секунд", settings.Duration == value, () =>
                {
                    settings.Duration = value;
                    Changed("Длительность звонка: " + value + " с");
                }));
            }
            sound.DropDownItems.Add(duration);
            items.Add(sound);
            items.Add(Item("Проверить звонок", () => Ring("Проверка звонка")));
            if (player.IsPlaying) items.Add(Item("Остановить звук", player.Stop));
            items.Add(new ToolStripSeparator());

            var options = new ToolStripMenuItem("Настройки");
            bool machineAutostart = Installer.MachineAutostart();
            ToolStripMenuItem auto = Check("Запускать при входе в Windows" + (machineAutostart ? " (для всех пользователей)" : ""),
                machineAutostart || Installer.UserAutostart(), ToggleAutostart);
            auto.Enabled = !machineAutostart;
            options.DropDownItems.Add(auto);
            options.DropDownItems.Add(Check("Не давать компьютеру засыпать", settings.KeepAwake, () =>
            {
                settings.KeepAwake = !settings.KeepAwake;
                Power.KeepAwake(settings.KeepAwake);
                Changed(settings.KeepAwake ? "Запрет спящего режима включён" : "Запрет спящего режима выключен");
            }));
            options.DropDownItems.Add(Check("Включать звук Windows перед звонком, если он выключен", settings.Unmute, () =>
            {
                settings.Unmute = !settings.Unmute;
                Changed(null);
            }));
            options.DropDownItems.Add(new ToolStripSeparator());
            options.DropDownItems.Add(Item("Журнал звонков", () => OpenPath(Log.FilePath)));
            options.DropDownItems.Add(Item("Папка с настройками", () => OpenPath(Settings.DataDir)));
            if (Installer.IsInstalledCopy())
            {
                options.DropDownItems.Add(new ToolStripSeparator());
                options.DropDownItems.Add(Item("Удалить программу…", () =>
                {
                    if (Installer.Uninstall(false, true)) ExitApp("программа удалена");
                }));
            }
            items.Add(options);
            items.Add(Item("О программе", About));
            items.Add(Item("Выход", ConfirmExit));
        }

        static ToolStripMenuItem Item(string text, Action action)
        {
            return new ToolStripMenuItem(text, null, (s, e) => action());
        }

        static ToolStripMenuItem Check(string text, bool isChecked, Action action)
        {
            return new ToolStripMenuItem(text, null, (s, e) => action()) { Checked = isChecked };
        }

        void Changed(string logText)
        {
            TrySave();
            try { player.Prepare(settings); }
            catch (Exception ex) { Log.Write("Не удалось подготовить звук: " + ex.Message); }
            UpdateIconAndTooltip();
            if (logText != null) Log.Write(logText);
        }

        void TrySave()
        {
            if (preview) return;
            try { settings.Save(); }
            catch (Exception ex)
            {
                Log.Write("Не удалось сохранить настройки: " + ex.Message);
                tray.ShowBalloonTip(8000, Program.AppName, "Не удалось сохранить настройки: " + ex.Message, ToolTipIcon.Error);
            }
        }

        void SetSound(string kind)
        {
            settings.Sound = kind;
            Changed("Выбран звук: " + (kind == SoundKind.Electric ? "электрический звонок" : kind == SoundKind.Chime ? "мелодичный звонок" : settings.SoundFileTitle));
            Ring("Проверка звонка");
        }

        void ChooseSoundFile()
        {
            using (Form owner = Ui.MakeOwner())
            using (var dlg = new OpenFileDialog
            {
                Title = "Выберите звук звонка",
                Filter = "Звуковые файлы (*.wav; *.mp3; *.wma)|*.wav;*.mp3;*.wma|Все файлы (*.*)|*.*",
            })
            {
                owner.Show();
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    player.Stop();
                    Directory.CreateDirectory(Settings.DataDir);
                    string dest = Path.Combine(Settings.DataDir, "custom_sound" + Path.GetExtension(dlg.FileName).ToLowerInvariant());
                    File.Copy(dlg.FileName, dest, true);
                    settings.SoundFile = dest;
                    settings.SoundFileTitle = Path.GetFileName(dlg.FileName);
                }
                catch (Exception ex)
                {
                    Ui.Error("Не удалось скопировать файл:\n\n" + ex.Message);
                    return;
                }
            }
            SetSound(SoundKind.File);
        }

        void OpenEditor()
        {
            if (editor != null && !editor.IsDisposed)
            {
                editor.WindowState = FormWindowState.Normal;
                editor.Activate();
                return;
            }
            editor = new ScheduleForm(settings.Shifts, shifts =>
            {
                settings.Shifts = shifts;
                Changed("Расписание изменено: " + string.Join("; ", Array.ConvertAll(shifts.ToArray(), s => s.Name + " " + s.RangeText())));
            });
            editor.Show();
            editor.Activate();
        }

        void ToggleAutostart()
        {
            bool on = !Installer.UserAutostart();
            try
            {
                Installer.SetUserAutostart(on);
                Log.Write(on ? "Автозапуск включён" : "Автозапуск выключен");
            }
            catch (Exception ex)
            {
                Ui.Error("Не удалось изменить автозапуск:\n\n" + ex.Message);
            }
        }

        static void OpenPath(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    Directory.CreateDirectory(Settings.DataDir);
                    if (Path.HasExtension(path)) File.WriteAllText(path, "");
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Ui.Error("Не удалось открыть " + path + ":\n\n" + ex.Message);
            }
        }

        void About()
        {
            Ui.Info(Program.AppName + ", версия " + Program.Version + "\n\n" +
                    "Даёт звонки на урок и с урока по расписанию. Работает без прав администратора.\n\n" +
                    "Звуки звонков синтезирует сама программа — они свободны от авторских прав. " +
                    "Можно выбрать и свой файл: меню «Звук звонка».\n\n" +
                    "Настройки и журнал звонков:\n" + Settings.DataDir + "\n\n" +
                    "Программа:\n" + Installer.CurrentExe);
        }

        void ConfirmExit()
        {
            if (Ui.Ask("Если закрыть программу, звонки звучать не будут, пока она не запустится снова " +
                       "(например, при следующем входе в Windows).\n\nЗакрыть программу?",
                       MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                ExitApp("выход из меню");
        }

        void ExitApp(string reason)
        {
            if (exiting) return;
            exiting = true;
            Log.Write("Программа закрыта (" + reason + ")");
            timer.Stop();
            player.Stop();
            Power.KeepAwake(false);
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            showWait.Unregister(null);
            exitWait.Unregister(null);
            if (editor != null && !editor.IsDisposed) editor.Dispose();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }

        public Bitmap RenderMenu()
        {
            BuildMenu();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            menu.Show(new Point(wa.Left + 40, wa.Top + 40));
            Application.DoEvents();
            var bmp = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bmp, new Rectangle(Point.Empty, menu.Size));
            menu.Close();
            return bmp;
        }

        public Settings CurrentSettings
        {
            get { return settings; }
        }
    }
}
