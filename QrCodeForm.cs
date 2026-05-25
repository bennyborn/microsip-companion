using System;
using System.Drawing;
using System.Windows.Forms;

namespace MicroSIPRemote
{
    internal sealed class QrCodeForm : Form
    {
        private readonly string _url;
        private readonly bool[,] _matrix;

        private QrCodeForm(string url)
        {
            _url = url;
            _matrix = QrCode.Encode(url);

            Text = "Scan QR Code";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(15, 15, 19);
            AutoScaleMode = AutoScaleMode.Dpi;

            int qrSize = _matrix != null ? _matrix.GetLength(0) : 0;
            const int cellSize = 6;
            const int padding = 24;
            int qrPx = qrSize * cellSize + padding * 2;

            var pic = new PictureBox
            {
                Left = padding, Top = padding,
                Width = qrSize * cellSize + padding * 2,
                Height = qrSize * cellSize + padding * 2,
                BackColor = Color.White
            };
            pic.Paint += (s, e) => DrawQr(e.Graphics, qrSize, cellSize, padding);

            var lbl = new Label
            {
                Text = _url,
                ForeColor = Color.FromArgb(180, 180, 200),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Left = 0,
                Width = qrPx + padding * 2,
                Height = 28,
                Top = pic.Bottom + 10
            };

            ClientSize = new Size(qrPx + padding * 2, lbl.Bottom + padding);
            Controls.Add(pic);
            Controls.Add(lbl);
        }

        private void DrawQr(Graphics g, int qrSize, int cell, int pad)
        {
            if (_matrix == null) return;
            g.Clear(Color.White);
            using var dark = new SolidBrush(Color.Black);
            for (int r = 0; r < qrSize; r++)
                for (int c = 0; c < qrSize; c++)
                    if (_matrix[r, c])
                        g.FillRectangle(dark, pad + c * cell, pad + r * cell, cell, cell);
        }

        public static void ShowUrl(string url)
        {
            using var f = new QrCodeForm(url);
            f.ShowDialog();
        }
    }
}
