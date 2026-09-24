using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SchoolBell
{
    static class Diag
    {
        static readonly StringBuilder report = new StringBuilder();
        static int failures;

        public static int Run(string outDir)
        {
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            Environment.SetEnvironmentVariable("SCHOOLBELL_DATA", Path.Combine(outDir, "data"));

            try
            {
                TestTimes();
                TestSchedule();
                TestSettingsRoundTrip();
                File.WriteAllBytes(Path.Combine(outDir, "electric.wav"), BellSynth.ToWav(BellSynth.Electric(5), 1.0));
                File.WriteAllBytes(Path.Combine(outDir, "chime.wav"), BellSynth.ToWav(BellSynth.Chime(), 1.0));
                Line("WAV сохранены");
                RenderUi(outDir);
            }
            catch (Exception ex)
            {
                failures++;
                Line("ИСКЛЮЧЕНИЕ: " + ex);
            }
            Line(failures == 0 ? "ИТОГ: всё в порядке" : "ИТОГ: ошибок — " + failures);
            File.WriteAllText(Path.Combine(outDir, "diag.txt"), report.ToString(), new UTF8Encoding(true));
            return failures == 0 ? 0 : 1;
        }

        static void Line(string s)
        {
            report.AppendLine(s);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Line((ok ? "ok    " : "FAIL  ") + what);
        }

        static void TestTimes()
        {
            string[] good = { "8:00", "08:00", "8.05", "0800", "800", "8 30", "8Ж30", " 13:40 " };
            string[] expect = { "08:00", "08:00", "08:05", "08:00", "08:00", "08:30", "08:30", "13:40" };
            for (int i = 0; i < good.Length; i++)
            {
                TimeSpan t;
                Check(TimeUtil.TryParse(good[i], out t) && TimeUtil.Format(t) == expect[i], "время «" + good[i] + "» -> " + expect[i]);
            }
            foreach (string bad in new[] { "", "25:00", "8:60", "abc", "8:5" })
            {
                TimeSpan t;
                Check(!TimeUtil.TryParse(bad, out t), "время «" + bad + "» отклонено");
            }
        }

        static void TestSchedule()
        {
            Settings s = Settings.CreateDefault();
            var thu = new DateTime(2026, 9, 24);
            List<BellEvent> events = Schedule.ForDate(s, thu);
            Check(events.Count == 24, "в учебный день 24 звонка (2 смены × 6 уроков × 2), получено " + events.Count);
            Check(events[0].Time == thu.AddHours(8) && events[0].Text.Contains("начало 1 урока (1 смена)"), "первый звонок 08:00, начало 1 урока");
            Check(events[events.Count - 1].Time == thu.Add(new TimeSpan(19, 40, 0)), "последний звонок 19:40");
            Check(Schedule.ForDate(s, new DateTime(2026, 9, 26)).Count == 0, "в субботу звонков нет (по умолчанию пн–пт)");

            BellEvent next = Schedule.Next(s, thu.Add(new TimeSpan(9, 41, 0)));
            Check(next != null && next.Time == thu.AddHours(10) && next.Text.Contains("начало 3 урока"), "после 09:41 следующий — 10:00, начало 3 урока");
            next = Schedule.Next(s, new DateTime(2026, 9, 25, 19, 45, 0));
            Check(next != null && next.Time == new DateTime(2026, 9, 28, 8, 0, 0), "в пятницу вечером следующий — понедельник 08:00");

            Check((Schedule.CurrentState(s, thu.Add(new TimeSpan(8, 30, 0))) ?? "").StartsWith("идёт 1 урок"), "08:30 — идёт 1 урок");
            Check((Schedule.CurrentState(s, thu.Add(new TimeSpan(9, 45, 0))) ?? "").Contains("до 10:00"), "09:45 — перемена до 10:00");
            Check(Schedule.CurrentState(s, thu.Add(new TimeSpan(13, 50, 0))) == null, "13:50 — между сменами уроков нет");

            TimeSpan late = TimeSpan.FromSeconds(90);
            DateTime t0 = thu.Add(new TimeSpan(7, 59, 59)).AddMilliseconds(600);
            List<BellEvent> due = Schedule.Due(s, t0, t0.AddSeconds(1), DateTime.MinValue, late);
            Check(due.Count == 1 && due[0].Time == thu.AddHours(8), "тик через 08:00:00 — звонок");
            due = Schedule.Due(s, t0, t0.AddSeconds(1), thu.AddHours(8), late);
            Check(due.Count == 0, "повторно тот же звонок не звенит");
            due = Schedule.Due(s, thu.AddHours(7), thu.Add(new TimeSpan(9, 0, 0)), DateTime.MinValue, late);
            Check(due.Count == 0, "после сна 07:00→09:00 старые звонки не звенят");
            due = Schedule.Due(s, thu.AddHours(7), thu.Add(new TimeSpan(8, 0, 30)), DateTime.MinValue, late);
            Check(due.Count == 1, "после сна, закончившегося через 30 с после звонка, — звонок");

            s.Enabled = false;
            Check(Schedule.Due(s, t0, t0.AddSeconds(1), DateTime.MinValue, late).Count == 0, "выключенные звонки не звенят");
            s.Enabled = true;
            s.SkipDate = thu;
            Check(Schedule.Due(s, t0, t0.AddSeconds(1), DateTime.MinValue, late).Count == 0, "«не звонить сегодня» работает");
            s.SkipDate = DateTime.MinValue;
            s.Shifts[0].Enabled = false;
            Check(Schedule.ForDate(s, thu).Count == 12, "выключенная смена не звенит");
            Check(Settings.DaysSummary(s.Days) == "пн–пт", "дни недели: пн–пт");
        }

        static void TestSettingsRoundTrip()
        {
            Settings s = Settings.CreateDefault();
            s.Volume = 75;
            s.Sound = SoundKind.Chime;
            s.Days[6] = true;
            s.Shifts[1].Enabled = false;
            s.Shifts[0].Name = "Первая смена";
            s.SkipDate = new DateTime(2026, 9, 24);
            s.Save();
            bool existed;
            Settings l = Settings.Load(out existed);
            Check(existed && l.Volume == 75 && l.Sound == SoundKind.Chime && l.Days[6] && !l.Days[7] &&
                  !l.Shifts[1].Enabled && l.Shifts[0].Name == "Первая смена" && l.Shifts[0].Lessons.Count == 6 &&
                  l.SkipDate == s.SkipDate && TimeUtil.Format(l.Shifts[1].Lessons[5].End) == "19:40",
                  "настройки сохраняются и читаются обратно");
        }

        static void RenderUi(string outDir)
        {
            IconArt.SaveIco(Path.Combine(outDir, "bell.ico"));
            using (var strip = new Bitmap(16 + 24 + 32 + 48 + 64 + 40, 64))
            {
                using (Graphics g = Graphics.FromImage(strip))
                {
                    g.Clear(Color.White);
                    int x = 0;
                    foreach (int size in new[] { 16, 24, 32, 48, 64 })
                    {
                        using (Bitmap b = IconArt.Draw(size, size != 24)) g.DrawImage(b, x, 0, size, size);
                        x += size + 8;
                    }
                }
                strip.Save(Path.Combine(outDir, "icons.png"), ImageFormat.Png);
            }

            var app = new TrayApp(false, true);
            using (Bitmap menuBmp = app.RenderMenu())
                menuBmp.Save(Path.Combine(outDir, "menu.png"), ImageFormat.Png);

            var form = new ScheduleForm(app.CurrentSettings.Shifts, shifts => { });
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-5000, -5000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                bmp.Save(Path.Combine(outDir, "schedule.png"), ImageFormat.Png);
            }
            form.Dispose();
            Line("картинки сохранены");
        }
    }
}
