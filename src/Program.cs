using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Школьный звонок")]
[assembly: AssemblyProduct("Школьный звонок")]
[assembly: AssemblyDescription("Звонки на урок и с урока по расписанию")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace SchoolBell
{
    static class Program
    {
        public const string AppName = "Школьный звонок";
        public const string Version = "1.0";

        public const string MutexName = @"Local\SchoolBell.Instance";
        public const string ShowEventName = @"Local\SchoolBell.Show";
        public const string ExitEventName = @"Local\SchoolBell.Exit";

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Write("Ошибка: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Критическая ошибка: " + e.ExceptionObject);

            if (HasArg(args, "/makeicon"))
            {
                IconArt.SaveIco(args.Length > 1 ? args[1] : "bell.ico");
                return 0;
            }
            if (HasArg(args, "/diag"))
                return Diag.Run(args.Length > 1 ? args[1] : "diag");
            if (HasArg(args, "/uninstall"))
                return Installer.Uninstall(HasArg(args, "/confirmed"), false) ? 0 : 1;
            if (HasArg(args, "/install"))
                return Installer.Install(Installer.IsElevated()) ? 0 : 1;

            bool autostart = HasArg(args, "/autostart");

            if (!autostart && !Installer.IsInstalledCopy())
            {
                if (Installer.OfferInstall() != InstallChoice.RunHere)
                    return 0;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (!autostart)
                    {
                        try
                        {
                            using (var ev = EventWaitHandle.OpenExisting(ShowEventName))
                                ev.Set();
                        }
                        catch
                        {
                            Ui.Info("Программа уже запущена — значок колокольчика находится в трее, рядом с часами.");
                        }
                    }
                    return 0;
                }
                Application.Run(new TrayApp(autostart, false));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-" + name.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
