using System;
using System.Threading;
using System.Windows.Forms;

namespace MicroSIPRemote
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.ToString(), "MicroSIP Companion – Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show(e.ExceptionObject?.ToString(), "MicroSIP Companion – Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            _mutex = new Mutex(true, "MicroSIPCompanion_SingleInstance", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("MicroSIP Companion is already running.",
                    "MicroSIP Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                using var app = new TrayApp();
                Application.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "MicroSIP Companion – Startup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
