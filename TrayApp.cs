using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows.Forms;

namespace MicroSIPRemote
{
    internal sealed class TrayApp : IDisposable
    {
        private readonly NotifyIcon _tray;
        private readonly HttpServer _server;
        private readonly CallStateMonitor _monitor;
        private readonly string _url;
        private bool _disposed;

        public TrayApp()
        {
            _monitor = new CallStateMonitor();

            int port = Settings.Port;
            _url = $"http://{GetLocalIp()}:{port}/";

            _server = new HttpServer(port, _monitor);
            _server.Start();

            var menu = new ContextMenuStrip();
            var title = new ToolStripMenuItem("MicroSIP Companion") { Enabled = false };
            title.Font = new Font(title.Font, FontStyle.Bold);
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show URL / QR Code…", null, (_, __) => QrCodeForm.ShowUrl(_url));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, (_, __) => Application.Exit());

            _tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "MicroSIP Companion",
                Visible = true,
                ContextMenuStrip = menu
            };

            _tray.ShowBalloonTip(4000, "MicroSIP Companion",
                $"Running – {_url}", ToolTipIcon.Info);
        }

        private static Icon LoadIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("MicroSIPRemote.Resources.icon.ico");
                if (stream != null) return new Icon(stream);
            }
            catch { }

            // Fallback: 16×16 solid coloured icon
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.FromArgb(34, 197, 94));
            return Icon.FromHandle(bmp.GetHicon());
        }

        private static string GetLocalIp()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 80);
                return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
            }
            catch { return "localhost"; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tray.Visible = false;
            _tray.Dispose();
            _server.Dispose();
            _monitor.Dispose();
        }
    }
}
