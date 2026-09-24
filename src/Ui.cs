using System;
using System.Drawing;
using System.Windows.Forms;

namespace SchoolBell
{
    static class Ui
    {
        public static DialogResult Ask(string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (Form owner = MakeOwner())
            {
                owner.Show();
                owner.Activate();
                return MessageBox.Show(owner, text, Program.AppName, buttons, icon);
            }
        }

        public static void Info(string text)
        {
            Ask(text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Error(string text)
        {
            Ask(text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static Form MakeOwner()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            return new Form
            {
                TopMost = true,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2),
                Size = new Size(1, 1),
                Opacity = 0,
            };
        }
    }
}
