using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MicroSIPRemote
{
    internal static class MicroSipController
    {
        public static bool HangupIncoming() => Run("/hangupincoming");
        public static bool HangupAll()      => Run("/hangupall");
        public static bool Answer()         => Run("/answer");

        private static bool Run(string args)
        {
            var exe = FindExe();
            if (exe == null) return false;
            try
            {
                Process.Start(new ProcessStartInfo(exe, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                return true;
            }
            catch { return false; }
        }

        internal static string FindExe()
        {
            var configured = Settings.MicroSipExePath;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            try
            {
                var proc = Process.GetProcessesByName("MicroSIP").FirstOrDefault();
                if (proc != null) return proc.MainModule.FileName;
            }
            catch { }

            string[] candidates = {
                @"C:\Program Files\MicroSIP\MicroSIP.exe",
                @"C:\Program Files (x86)\MicroSIP\MicroSIP.exe",
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    @"MicroSIP\MicroSIP.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MicroSIP.exe"),
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
