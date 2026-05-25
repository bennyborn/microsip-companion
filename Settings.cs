using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MicroSIPRemote
{
    internal static class Settings
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(string section, string key, string value, string filePath);

        private static readonly string IniPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "MicroSIPCompanion.ini");

        public static int Port
        {
            get
            {
                var val = Get("Server", "Port", "8765");
                return int.TryParse(val, out int p) ? p : 8765;
            }
        }

        public static string MicroSipExePath => Get("MicroSIP", "ExePath", "");

        private static string Get(string section, string key, string def)
        {
            EnsureDefaults();
            var sb = new StringBuilder(512);
            GetPrivateProfileString(section, key, def, sb, 512, IniPath);
            return sb.ToString();
        }

        private static bool _defaultsWritten;
        private static void EnsureDefaults()
        {
            if (_defaultsWritten || File.Exists(IniPath)) return;
            WritePrivateProfileString("Server", "Port", "8765", IniPath);
            WritePrivateProfileString("MicroSIP", "ExePath", "", IniPath);
            _defaultsWritten = true;
        }
    }
}
