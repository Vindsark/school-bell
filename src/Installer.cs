using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SchoolBell
{
    enum InstallChoice { Installed, RunHere, Cancel }

    static class Installer
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SchoolBell";
        const string RunValue = "SchoolBell";
        const string ExeName = "SchoolBell.exe";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string path);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr memory);

        public static string CurrentExe
        {
            get { return Application.ExecutablePath; }
        }

        public static string UserDir
        {
            get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "SchoolBell"); }
        }

        public static string MachineDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SchoolBell"); }
        }

        public static bool IsElevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static bool SameDir(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsMachineCopy()
        {
            return SameDir(Path.GetDirectoryName(CurrentExe), MachineDir);
        }

        public static bool IsInstalledCopy()
        {
            string dir = Path.GetDirectoryName(CurrentExe);
            return SameDir(dir, UserDir) || SameDir(dir, MachineDir);
        }

        static RegistryKey OpenMachine()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        }

        static RegistryKey OpenUser()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        }

        static bool HasRunValue(RegistryKey root)
        {
            using (RegistryKey k = root.OpenSubKey(RunKeyPath))
                return k != null && k.GetValue(RunValue) != null;
        }

        static void SetRun(RegistryKey root, string exe)
        {
            using (RegistryKey k = root.CreateSubKey(RunKeyPath))
            {
                if (exe == null) k.DeleteValue(RunValue, false);
                else k.SetValue(RunValue, "\"" + exe + "\" /autostart");
            }
        }

        public static bool MachineAutostart()
        {
            try
            {
                using (RegistryKey r = OpenMachine())
                    return HasRunValue(r);
            }
            catch
            {
                return false;
            }
        }

        public static bool UserAutostart()
        {
            try
            {
                using (RegistryKey r = OpenUser())
                    return HasRunValue(r);
            }
            catch
            {
                return false;
            }
        }

        public static void SetUserAutostart(bool on)
        {
            if (on) Unblock(CurrentExe);
            using (RegistryKey r = OpenUser())
                SetRun(r, on ? CurrentExe : null);
        }

        static void Unblock(string path)
        {
            try { DeleteFile(path + ":Zone.Identifier"); } catch { }
        }

        public static InstallChoice OfferInstall()
        {
            bool machine = IsElevated();
            string dir = machine ? MachineDir : UserDir;
            bool update = File.Exists(Path.Combine(dir, ExeName));
            string text;
            if (machine)
            {
                text = (update ? "Обновить" : "Установить") + " «" + Program.AppName + "» для всех пользователей этого компьютера?\n\n" +
                       "Папка: " + dir + "\n\n" +
                       "Программа будет сама запускаться при входе любого пользователя в Windows " +
                       "и работать с обычными правами — администратор нужен только сейчас, для установки.";
                return Ui.Ask(text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK
                    ? (Install(true) ? InstallChoice.Installed : InstallChoice.Cancel)
                    : InstallChoice.Cancel;
            }
            text = (update ? "Обновить установленную программу «" : "Установить «") + Program.AppName + "» на этот компьютер?\n\n" +
                   "Программа будет скопирована в папку\n" + dir + "\n" +
                   "и будет сама запускаться при входе в Windows (значок колокольчика — в трее, рядом с часами). " +
                   "Права администратора не нужны.\n\n" +
                   "Да — установить\n" +
                   "Нет — просто запустить отсюда, без установки\n" +
                   "Отмена — ничего не делать";
            DialogResult r = Ui.Ask(text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.No) return InstallChoice.RunHere;
            if (r != DialogResult.Yes) return InstallChoice.Cancel;
            return Install(false) ? InstallChoice.Installed : InstallChoice.Cancel;
        }

        public static bool Install(bool machine)
        {
            string dir = machine ? MachineDir : UserDir;
            string target = Path.Combine(dir, ExeName);
            try
            {
                StopRunningCopies(true);
                Directory.CreateDirectory(dir);
                if (!string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    CopyWithRetry(CurrentExe, target);
                Unblock(target);
                using (RegistryKey root = machine ? OpenMachine() : OpenUser())
                {
                    SetRun(root, target);
                    WriteUninstallEntry(root, dir, target);
                }
                if (machine)
                {
                    try
                    {
                        using (RegistryKey u = OpenUser())
                            SetRun(u, null);
                    }
                    catch { }
                }
                CreateShortcut(machine, target);
                Log.Write("Установлено: " + target + (machine ? " (для всех пользователей)" : ""));
            }
            catch (Exception ex)
            {
                Ui.Error("Не удалось установить программу:\n\n" + ex.Message);
                return false;
            }

            if (!machine)
            {
                Ui.Info("Готово! «" + Program.AppName + "» установлен и теперь будет запускаться при входе в Windows.\n\n" +
                        "Значок колокольчика — в трее рядом с часами (если его не видно, нажмите стрелку ^). " +
                        "Щёлкните по нему, чтобы открыть меню.");
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = dir });
                return true;
            }

            if (IsSessionUser())
            {
                Ui.Info("Готово! «" + Program.AppName + "» установлен для всех пользователей и будет запускаться при входе в Windows " +
                        "с обычными правами.\n\nЗначок колокольчика — в трее рядом с часами (если его не видно, нажмите стрелку ^).");
                try { Process.Start("explorer.exe", "\"" + target + "\""); }
                catch { }
            }
            else
            {
                Ui.Info("Готово! «" + Program.AppName + "» установлен для всех пользователей.\n\n" +
                        "Программа запустится сама при следующем входе в Windows. Чтобы запустить её сейчас, " +
                        "откройте меню Пуск → «" + Program.AppName + "» (без прав администратора).");
            }
            return true;
        }

        static void CopyWithRetry(string source, string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Copy(source, target, true);
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 10) throw;
                    Thread.Sleep(500);
                }
            }
        }

        static void StopRunningCopies(bool politely)
        {
            if (politely)
            {
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting(Program.ExitEventName))
                        ev.Set();
                }
                catch { }
            }
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    if (p.Id == self) continue;
                    if (!p.WaitForExit(politely ? 4000 : 0))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }

        static void WriteUninstallEntry(RegistryKey root, string dir, string target)
        {
            using (RegistryKey k = root.CreateSubKey(UninstallKeyPath))
            {
                k.SetValue("DisplayName", Program.AppName);
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("DisplayIcon", target);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + target + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024) + 1, RegistryValueKind.DWord);
            }
        }

        static string ShortcutPath(bool machine)
        {
            string programs = Environment.GetFolderPath(machine ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs);
            return Path.Combine(programs, Program.AppName + ".lnk");
        }

        static void CreateShortcut(bool machine, string target)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath(machine) });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Звонки на урок и с урока по расписанию" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception ex)
            {
                Log.Write("Не удалось создать ярлык в меню Пуск: " + ex.Message);
            }
        }

        static bool IsSessionUser()
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 5, out buffer, out bytes))
                return true;
            try
            {
                string sessionUser = Marshal.PtrToStringUni(buffer);
                return string.IsNullOrEmpty(sessionUser) || string.Equals(sessionUser, Environment.UserName, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        public static bool Uninstall(bool confirmed, bool fromTray)
        {
            bool machine = IsMachineCopy();
            if (!confirmed && Ui.Ask("Удалить программу «" + Program.AppName + "» с этого компьютера?\n\nЗвонки перестанут подаваться.",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;

            if (machine && !IsElevated())
            {
                try
                {
                    Process.Start(new ProcessStartInfo(CurrentExe, "/uninstall /confirmed") { UseShellExecute = true, Verb = "runas" });
                    return true;
                }
                catch (Win32Exception)
                {
                    Ui.Error("Программа установлена для всех пользователей, поэтому удалить её можно только с правами администратора.");
                    return false;
                }
            }

            try
            {
                StopRunningCopies(!fromTray);
                using (RegistryKey root = machine ? OpenMachine() : OpenUser())
                {
                    SetRun(root, null);
                    root.DeleteSubKeyTree(UninstallKeyPath, false);
                }
                try
                {
                    using (RegistryKey u = OpenUser())
                        SetRun(u, null);
                }
                catch { }
                try { File.Delete(ShortcutPath(machine)); } catch { }
            }
            catch (Exception ex)
            {
                Ui.Error("Не удалось удалить программу:\n\n" + ex.Message);
                return false;
            }

            if (IsInstalledCopy())
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"" + Path.GetDirectoryName(CurrentExe) + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath(),
                };
                try { Process.Start(psi); } catch { }
            }
            Log.Write("Программа удалена");
            Ui.Info("Программа удалена.\n\nНастройки и журнал звонков остались в папке\n" + Settings.DataDir + "\n— её можно удалить вручную.");
            return true;
        }
    }
}
