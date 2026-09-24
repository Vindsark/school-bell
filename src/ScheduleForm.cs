using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SchoolBell
{
    class ScheduleForm : Form
    {
        class ShiftEditor
        {
            public TabPage Page;
            public TextBox NameBox;
            public CheckBox EnabledBox;
            public DataGridView Grid;
        }

        readonly TabControl tabs = new TabControl();
        readonly List<ShiftEditor> editors = new List<ShiftEditor>();
        readonly Action<List<Shift>> onSave;
        readonly int u;
        bool dirty;

        public ScheduleForm(List<Shift> shifts, Action<List<Shift>> onSave)
        {
            this.onSave = onSave;
            Font = SystemFonts.MessageBoxFont;
            u = Font.Height;
            Text = "Расписание звонков — " + Program.AppName;
            Icon = IconArt.MakeIcon(32, true);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ClientSize = new Size(u * 46, u * 31);
            MinimumSize = new Size(u * 40, u * 24);

            var hint = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = u * 3,
                Padding = new Padding(u / 2, u / 2, u / 2, 0),
                Text = "Звонок звенит в начале и в конце каждого урока. Время вводите так: 08:00. " +
                       "Чтобы изменить время, щёлкните по ячейке. Изменения начнут действовать после «Сохранить».",
            };

            tabs.Dock = DockStyle.Fill;
            foreach (Shift s in shifts) AddShiftTab(s.Clone());

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(u / 3),
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
            left.Controls.Add(MakeButton("Добавить смену", AddShift));
            left.Controls.Add(MakeButton("Удалить смену", RemoveShift));
            left.Controls.Add(MakeButton("Как по умолчанию", ResetDefaults));
            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            Button save = MakeButton("Сохранить", () =>
            {
                if (Commit()) Close();
            });
            save.Font = new Font(Font, FontStyle.Bold);
            right.Controls.Add(save);
            right.Controls.Add(MakeButton("Отмена", () =>
            {
                dirty = false;
                Close();
            }));
            bottom.Controls.Add(left, 0, 0);
            bottom.Controls.Add(right, 1, 0);

            Controls.Add(tabs);
            Controls.Add(hint);
            Controls.Add(bottom);

            FormClosing += OnFormClosing;
            Shown += (s, e) => Activate();
        }

        Button MakeButton(string text, Action action)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(u / 2, u / 6, u / 2, u / 6),
                MinimumSize = new Size(u * 6, 0),
                UseVisualStyleBackColor = true,
            };
            b.Click += (s, e) => action();
            return b;
        }

        void AddShiftTab(Shift s)
        {
            var ed = new ShiftEditor();
            ed.Page = new TabPage(s.Name) { Padding = new Padding(u / 2), UseVisualStyleBackColor = true };

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 0, 0, u / 3) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var label = new Label { Text = "Название:", AutoSize = true, Anchor = AnchorStyles.Left };
            ed.NameBox = new TextBox { Text = s.Name, Width = u * 11, Anchor = AnchorStyles.Left };
            ed.NameBox.TextChanged += (o, e) =>
            {
                ed.Page.Text = ed.NameBox.Text;
                dirty = true;
            };
            ed.EnabledBox = new CheckBox { Text = "звонки этой смены включены", Checked = s.Enabled, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(u, 3, 3, 3) };
            ed.EnabledBox.CheckedChanged += (o, e) => dirty = true;
            top.Controls.Add(label, 0, 0);
            top.Controls.Add(ed.NameBox, 1, 0);
            top.Controls.Add(ed.EnabledBox, 2, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, u / 3, 0, 0) };
            buttons.Controls.Add(MakeButton("+ Добавить урок", () => AddLesson(ed)));
            buttons.Controls.Add(MakeButton("− Удалить урок", () => RemoveLesson(ed)));

            ed.Grid = MakeGrid();
            foreach (Lesson l in s.Lessons)
                ed.Grid.Rows.Add("", TimeUtil.Format(l.Start), TimeUtil.Format(l.End), "", "");
            ed.Grid.CellEndEdit += (o, e) =>
            {
                NormalizeCell(ed.Grid.Rows[e.RowIndex].Cells[e.ColumnIndex]);
                Recalc(ed.Grid);
                dirty = true;
            };
            ed.Grid.RowsRemoved += (o, e) => Recalc(ed.Grid);
            Recalc(ed.Grid);

            ed.Page.Controls.Add(ed.Grid);
            ed.Page.Controls.Add(buttons);
            ed.Page.Controls.Add(top);
            tabs.TabPages.Add(ed.Page);
            editors.Add(ed);
        }

        DataGridView MakeGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
            };
            g.RowTemplate.Height = (int)(u * 1.7);
            g.Columns.Add(Column("num", "Урок", 12, true));
            g.Columns.Add(Column("start", "Начало", 20, false));
            g.Columns.Add(Column("end", "Конец", 20, false));
            g.Columns.Add(Column("len", "Длительность", 22, true));
            g.Columns.Add(Column("brk", "Перемена перед уроком", 30, true));
            return g;
        }

        static DataGridViewTextBoxColumn Column(string name, string header, float weight, bool readOnly)
        {
            var c = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            if (readOnly)
            {
                c.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                c.DefaultCellStyle.BackColor = SystemColors.Control;
                c.DefaultCellStyle.SelectionForeColor = SystemColors.GrayText;
                c.DefaultCellStyle.SelectionBackColor = SystemColors.Control;
            }
            else
            {
                c.DefaultCellStyle.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
            }
            return c;
        }

        static void NormalizeCell(DataGridViewCell cell)
        {
            if (cell.OwningColumn.Name != "start" && cell.OwningColumn.Name != "end") return;
            TimeSpan t;
            if (TimeUtil.TryParse(Convert.ToString(cell.Value), out t))
                cell.Value = TimeUtil.Format(t);
        }

        static void Recalc(DataGridView g)
        {
            TimeSpan? prevEnd = null;
            for (int i = 0; i < g.Rows.Count; i++)
            {
                DataGridViewRow r = g.Rows[i];
                r.Cells["num"].Value = (i + 1).ToString();
                TimeSpan st, en;
                bool okStart = TimeUtil.TryParse(Convert.ToString(r.Cells["start"].Value), out st);
                bool okEnd = TimeUtil.TryParse(Convert.ToString(r.Cells["end"].Value), out en);
                r.Cells["start"].ErrorText = okStart ? "" : "Неверное время. Пример: 08:00";
                r.Cells["end"].ErrorText = !okEnd ? "Неверное время. Пример: 08:45" : okStart && en <= st ? "Конец урока раньше начала" : "";
                r.Cells["len"].Value = okStart && okEnd && en > st ? Minutes(en - st) : "";
                if (i == 0) r.Cells["brk"].Value = "—";
                else if (okStart && prevEnd.HasValue) r.Cells["brk"].Value = st >= prevEnd.Value ? Minutes(st - prevEnd.Value) : "накладка!";
                else r.Cells["brk"].Value = "";
                prevEnd = okEnd ? (TimeSpan?)en : null;
            }
        }

        static string Minutes(TimeSpan t)
        {
            return (int)t.TotalMinutes + " мин";
        }

        void AddLesson(ShiftEditor ed)
        {
            DataGridView g = ed.Grid;
            g.EndEdit();
            TimeSpan start = new TimeSpan(8, 0, 0), length = TimeSpan.FromMinutes(45);
            if (g.Rows.Count > 0)
            {
                DataGridViewRow last = g.Rows[g.Rows.Count - 1];
                TimeSpan ls, le;
                if (TimeUtil.TryParse(Convert.ToString(last.Cells["start"].Value), out ls) &&
                    TimeUtil.TryParse(Convert.ToString(last.Cells["end"].Value), out le) && le > ls)
                {
                    start = le + TimeSpan.FromMinutes(10);
                    length = le - ls;
                }
            }
            TimeSpan end = start + length;
            if (end.TotalHours >= 24)
            {
                start = new TimeSpan(23, 0, 0);
                end = new TimeSpan(23, 45, 0);
            }
            int index = g.Rows.Add("", TimeUtil.Format(start), TimeUtil.Format(end), "", "");
            Recalc(g);
            g.CurrentCell = g.Rows[index].Cells["start"];
            dirty = true;
        }

        void RemoveLesson(ShiftEditor ed)
        {
            DataGridView g = ed.Grid;
            if (g.CurrentCell == null || g.Rows.Count == 0) return;
            g.EndEdit();
            int index = g.CurrentCell.RowIndex;
            g.Rows.RemoveAt(index);
            dirty = true;
        }

        void AddShift()
        {
            Shift template = Settings.DefaultShifts()[0];
            template.Name = (editors.Count + 1) + " смена";
            AddShiftTab(template);
            tabs.SelectedIndex = tabs.TabPages.Count - 1;
            dirty = true;
        }

        void RemoveShift()
        {
            ShiftEditor ed = SelectedEditor();
            if (ed == null) return;
            if (editors.Count <= 1)
            {
                MessageBox.Show(this, "Должна остаться хотя бы одна смена. Если её звонки не нужны — снимите галочку «звонки этой смены включены».",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Удалить «" + ed.NameBox.Text + "» вместе с её уроками?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            editors.Remove(ed);
            tabs.TabPages.Remove(ed.Page);
            ed.Page.Dispose();
            dirty = true;
        }

        void ResetDefaults()
        {
            if (MessageBox.Show(this, "Вернуть стандартное расписание (1 смена с 08:00, 2 смена с 14:00)? Ваши изменения в этом окне пропадут.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (ShiftEditor ed in editors) ed.Page.Dispose();
            editors.Clear();
            tabs.TabPages.Clear();
            foreach (Shift s in Settings.DefaultShifts()) AddShiftTab(s);
            dirty = true;
        }

        ShiftEditor SelectedEditor()
        {
            foreach (ShiftEditor ed in editors)
                if (ed.Page == tabs.SelectedTab) return ed;
            return null;
        }

        bool Commit()
        {
            var result = new List<Shift>();
            for (int k = 0; k < editors.Count; k++)
            {
                ShiftEditor ed = editors[k];
                ed.Grid.EndEdit();
                var s = new Shift { Name = ed.NameBox.Text.Trim(), Enabled = ed.EnabledBox.Checked };
                if (s.Name.Length == 0) s.Name = (k + 1) + " смена";
                TimeSpan prevEnd = TimeSpan.Zero;
                for (int i = 0; i < ed.Grid.Rows.Count; i++)
                {
                    DataGridViewRow r = ed.Grid.Rows[i];
                    TimeSpan st, en;
                    string where = "«" + s.Name + "», " + (i + 1) + " урок: ";
                    if (!TimeUtil.TryParse(Convert.ToString(r.Cells["start"].Value), out st))
                        return Fail(ed, i, "start", where + "неверное время начала. Введите, например, 08:00.");
                    if (!TimeUtil.TryParse(Convert.ToString(r.Cells["end"].Value), out en))
                        return Fail(ed, i, "end", where + "неверное время конца. Введите, например, 08:45.");
                    if (en <= st)
                        return Fail(ed, i, "end", where + "конец урока должен быть позже начала.");
                    if (i > 0 && st < prevEnd)
                        return Fail(ed, i, "start", where + "урок начинается раньше, чем закончился предыдущий.");
                    s.Lessons.Add(new Lesson(st, en));
                    prevEnd = en;
                }
                result.Add(s);
            }
            onSave(result);
            dirty = false;
            return true;
        }

        bool Fail(ShiftEditor ed, int row, string column, string message)
        {
            tabs.SelectedTab = ed.Page;
            ed.Grid.CurrentCell = ed.Grid.Rows[row].Cells[column];
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!dirty || e.CloseReason != CloseReason.UserClosing) return;
            DialogResult r = MessageBox.Show(this, "Сохранить изменения в расписании?", Text,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel || (r == DialogResult.Yes && !Commit()))
                e.Cancel = true;
        }
    }
}
