using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolBell
{
    class Lesson
    {
        public TimeSpan Start;
        public TimeSpan End;

        public Lesson(TimeSpan start, TimeSpan end)
        {
            Start = start;
            End = end;
        }
    }

    class Shift
    {
        public string Name = "";
        public bool Enabled = true;
        public List<Lesson> Lessons = new List<Lesson>();

        public Shift Clone()
        {
            var c = new Shift { Name = Name, Enabled = Enabled };
            foreach (Lesson l in Lessons)
                c.Lessons.Add(new Lesson(l.Start, l.End));
            return c;
        }

        public string RangeText()
        {
            if (Lessons.Count == 0) return "нет уроков";
            return TimeUtil.Format(Lessons[0].Start) + "–" + TimeUtil.Format(Lessons[Lessons.Count - 1].End);
        }
    }

    static class SoundKind
    {
        public const string Electric = "electric";
        public const string Chime = "chime";
        public const string File = "file";
    }

    static class TimeUtil
    {
        static readonly Regex TimeRx = new Regex(@"^\s*(\d{1,2})\s*[:.,\-жЖ ]?\s*(\d{2})\s*$");

        public static bool TryParse(string text, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (text == null) return false;
            Match m = TimeRx.Match(text);
            if (!m.Success) return false;
            int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (h > 23 || min > 59) return false;
            t = new TimeSpan(h, min, 0);
            return true;
        }

        public static string Format(TimeSpan t)
        {
            return t.Hours.ToString("00") + ":" + t.Minutes.ToString("00");
        }
    }

    class Settings
    {
        public bool Enabled = true;
        public int Volume = 100;
        public string Sound = SoundKind.Electric;
        public string SoundFile = "";
        public string SoundFileTitle = "";
        public int Duration = 5;
        public bool[] Days = new bool[8];
        public DateTime SkipDate = DateTime.MinValue;
        public bool KeepAwake = true;
        public bool Unmute = true;
        public List<Shift> Shifts = new List<Shift>();

        public static readonly string[] DayNames = { "", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье" };
        public static readonly string[] DayShort = { "", "пн", "вт", "ср", "чт", "пт", "сб", "вс" };

        public static string DataDir
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("SCHOOLBELL_DATA");
                if (!string.IsNullOrEmpty(over)) return over;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchoolBell");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(DataDir, "settings.ini"); }
        }

        public static Settings CreateDefault()
        {
            var s = new Settings();
            for (int d = 1; d <= 5; d++) s.Days[d] = true;
            s.Shifts = DefaultShifts();
            return s;
        }

        public static List<Shift> DefaultShifts()
        {
            return new List<Shift>
            {
                MakeShift("1 смена", "08:00-08:45", "08:55-09:40", "10:00-10:45", "11:05-11:50", "12:00-12:45", "12:55-13:40"),
                MakeShift("2 смена", "14:00-14:45", "14:55-15:40", "16:00-16:45", "17:05-17:50", "18:00-18:45", "18:55-19:40"),
            };
        }

        static Shift MakeShift(string name, params string[] lessons)
        {
            var s = new Shift { Name = name };
            foreach (string text in lessons)
            {
                Lesson l;
                if (TryParseLesson(text, out l)) s.Lessons.Add(l);
            }
            return s;
        }

        static readonly Regex LessonRx = new Regex(@"^\s*(\d{1,2}[:.]\d{2})\s*[-–—]\s*(\d{1,2}[:.]\d{2})\s*$");

        public static bool TryParseLesson(string text, out Lesson lesson)
        {
            lesson = null;
            Match m = LessonRx.Match(text ?? "");
            TimeSpan a, b;
            if (!m.Success || !TimeUtil.TryParse(m.Groups[1].Value, out a) || !TimeUtil.TryParse(m.Groups[2].Value, out b) || b <= a)
                return false;
            lesson = new Lesson(a, b);
            return true;
        }

        public static Settings Load(out bool existed)
        {
            existed = File.Exists(FilePath);
            Settings s = CreateDefault();
            if (!existed) return s;
            try
            {
                var shifts = new List<Shift>();
                Shift cur = null;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("["))
                    {
                        cur = new Shift();
                        shifts.Add(cur);
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    if (cur != null)
                    {
                        if (key == "name") cur.Name = val;
                        else if (key == "enabled") cur.Enabled = val != "0";
                        else if (key == "lesson")
                        {
                            Lesson l;
                            if (TryParseLesson(val, out l)) cur.Lessons.Add(l);
                        }
                        continue;
                    }
                    switch (key)
                    {
                        case "enabled": s.Enabled = val != "0"; break;
                        case "volume": s.Volume = Clamp(ParseInt(val, 100), 5, 100); break;
                        case "sound": s.Sound = val; break;
                        case "soundfile": s.SoundFile = val; break;
                        case "soundfiletitle": s.SoundFileTitle = val; break;
                        case "duration": s.Duration = Clamp(ParseInt(val, 5), 1, 60); break;
                        case "days": s.Days = ParseDays(val); break;
                        case "keepawake": s.KeepAwake = val != "0"; break;
                        case "unmute": s.Unmute = val != "0"; break;
                        case "skipdate":
                            DateTime d;
                            s.SkipDate = DateTime.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                                ? d : DateTime.MinValue;
                            break;
                    }
                }
                if (shifts.Count > 0)
                {
                    foreach (Shift sh in shifts)
                    {
                        sh.Lessons.Sort((x, y) => x.Start.CompareTo(y.Start));
                        if (sh.Name.Length == 0) sh.Name = (shifts.IndexOf(sh) + 1) + " смена";
                    }
                    s.Shifts = shifts;
                }
                if (s.Sound != SoundKind.Electric && s.Sound != SoundKind.Chime && s.Sound != SoundKind.File)
                    s.Sound = SoundKind.Electric;
            }
            catch (Exception ex)
            {
                Log.Write("Не удалось прочитать настройки, взяты стандартные: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                return CreateDefault();
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDir);
            var sb = new StringBuilder();
            sb.AppendLine("; Школьный звонок — настройки.");
            sb.AppendLine("; Удобнее менять через меню программы (значок колокольчика в трее).");
            sb.AppendLine("; Если правите файл вручную, сначала закройте программу (меню → Выход).");
            sb.AppendLine();
            sb.AppendLine("Enabled=" + (Enabled ? 1 : 0));
            sb.AppendLine("Sound=" + Sound);
            sb.AppendLine("SoundFile=" + SoundFile);
            sb.AppendLine("SoundFileTitle=" + SoundFileTitle);
            sb.AppendLine("Volume=" + Volume);
            sb.AppendLine("Duration=" + Duration);
            sb.AppendLine("; дни недели: 1 = понедельник ... 7 = воскресенье");
            sb.AppendLine("Days=" + FormatDays(Days));
            sb.AppendLine("SkipDate=" + (SkipDate == DateTime.MinValue ? "" : SkipDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            sb.AppendLine("KeepAwake=" + (KeepAwake ? 1 : 0));
            sb.AppendLine("Unmute=" + (Unmute ? 1 : 0));
            foreach (Shift sh in Shifts)
            {
                sb.AppendLine();
                sb.AppendLine("[Shift]");
                sb.AppendLine("Name=" + sh.Name);
                sb.AppendLine("Enabled=" + (sh.Enabled ? 1 : 0));
                foreach (Lesson l in sh.Lessons)
                    sb.AppendLine("Lesson=" + TimeUtil.Format(l.Start) + "-" + TimeUtil.Format(l.End));
            }
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
            File.Copy(tmp, FilePath, true);
            File.Delete(tmp);
        }

        public static string DaysSummary(bool[] days)
        {
            var list = new List<int>();
            for (int d = 1; d <= 7; d++) if (days[d]) list.Add(d);
            if (list.Count == 0) return "нет";
            if (list.Count >= 3 && list[list.Count - 1] - list[0] == list.Count - 1)
                return DayShort[list[0]] + "–" + DayShort[list[list.Count - 1]];
            var names = new List<string>();
            foreach (int d in list) names.Add(DayShort[d]);
            return string.Join(", ", names.ToArray());
        }

        static string FormatDays(bool[] days)
        {
            var list = new List<string>();
            for (int d = 1; d <= 7; d++) if (days[d]) list.Add(d.ToString());
            return string.Join(",", list.ToArray());
        }

        static bool[] ParseDays(string val)
        {
            var days = new bool[8];
            foreach (string part in val.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int d = ParseInt(part, 0);
                if (d >= 1 && d <= 7) days[d] = true;
            }
            return days;
        }

        static int ParseInt(string s, int def)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }

    class BellEvent
    {
        public DateTime Time;
        public string Text;
    }

    static class Schedule
    {
        public static int DayIndex(DateTime d)
        {
            return ((int)d.DayOfWeek + 6) % 7 + 1;
        }

        public static bool IsDayActive(Settings s, DateTime date)
        {
            return s.Days[DayIndex(date)] && s.SkipDate.Date != date.Date;
        }

        public static List<BellEvent> ForDate(Settings s, DateTime date)
        {
            var result = new List<BellEvent>();
            if (!IsDayActive(s, date)) return result;
            var map = new SortedDictionary<DateTime, List<string>>();
            foreach (Shift sh in s.Shifts)
            {
                if (!sh.Enabled) continue;
                for (int i = 0; i < sh.Lessons.Count; i++)
                {
                    Add(map, date.Date + sh.Lessons[i].Start, "начало " + (i + 1) + " урока (" + sh.Name + ")");
                    Add(map, date.Date + sh.Lessons[i].End, "конец " + (i + 1) + " урока (" + sh.Name + ")");
                }
            }
            foreach (var kv in map)
                result.Add(new BellEvent { Time = kv.Key, Text = string.Join("; ", kv.Value.ToArray()) });
            return result;
        }

        static void Add(SortedDictionary<DateTime, List<string>> map, DateTime t, string text)
        {
            List<string> list;
            if (!map.TryGetValue(t, out list))
            {
                list = new List<string>();
                map[t] = list;
            }
            list.Add(text);
        }

        public static BellEvent Next(Settings s, DateTime now)
        {
            for (int d = 0; d <= 7; d++)
                foreach (BellEvent ev in ForDate(s, now.Date.AddDays(d)))
                    if (ev.Time > now) return ev;
            return null;
        }

        public static List<BellEvent> Due(Settings s, DateTime from, DateTime now, DateTime lastRung, TimeSpan maxLate)
        {
            var due = new List<BellEvent>();
            if (!s.Enabled || now <= from) return due;
            DateTime windowStart = from;
            if (now - windowStart > maxLate) windowStart = now - maxLate;
            if (lastRung > windowStart) windowStart = lastRung;
            for (DateTime day = windowStart.Date; day <= now.Date; day = day.AddDays(1))
                foreach (BellEvent ev in ForDate(s, day))
                    if (ev.Time > windowStart && ev.Time <= now) due.Add(ev);
            return due;
        }

        public static string CurrentState(Settings s, DateTime now)
        {
            if (!IsDayActive(s, now)) return null;
            TimeSpan t = now.TimeOfDay;
            foreach (Shift sh in s.Shifts)
            {
                if (!sh.Enabled) continue;
                for (int i = 0; i < sh.Lessons.Count; i++)
                {
                    Lesson l = sh.Lessons[i];
                    if (t >= l.Start && t < l.End)
                        return "идёт " + (i + 1) + " урок (" + sh.Name + "), до " + TimeUtil.Format(l.End);
                    if (i + 1 < sh.Lessons.Count && t >= l.End && t < sh.Lessons[i + 1].Start)
                        return "перемена (" + sh.Name + "), до " + TimeUtil.Format(sh.Lessons[i + 1].Start);
                }
            }
            return null;
        }
    }

    static class Log
    {
        public static string FilePath
        {
            get { return Path.Combine(Settings.DataDir, "journal.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void Trim()
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (!fi.Exists || fi.Length < 1024 * 1024) return;
                string text = File.ReadAllText(FilePath, Encoding.UTF8);
                int cut = text.IndexOf('\n', text.Length - 300 * 1024);
                File.WriteAllText(FilePath, text.Substring(cut + 1), new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
