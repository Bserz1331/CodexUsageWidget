using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using QRCoder;

namespace CodexUsageWidget
{
    internal sealed class SupportDialog : Form
    {
        private const string KoFiUrl = "https://ko-fi.com/minz_space_cat";
        private const string Bep20Address = "0x7a4E3D8D9684196E4F96a6a28c49D3a1a785A0b5";
        private const string Trc20Address = "TNm7kRfeFo2wa1TVtz5EoNNBS8LFahoe7j";
        private readonly ToolTip toolTip = new ToolTip();

        public SupportDialog()
        {
            Text = "支持開發";
            BackColor = Color.FromArgb(20, 21, 23);
            ForeColor = Color.FromArgb(238, 240, 242);
            Font = new Font("Microsoft JhengHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(670, 462);
            AutoScaleMode = AutoScaleMode.Dpi;

            Controls.Add(LabelOf("自願支持", 14, 12, 150, 20, 9F, Color.FromArgb(82, 201, 151), FontStyle.Bold));
            Controls.Add(LabelOf("支持開發", 14, 34, 300, 38, 20F, ForeColor, FontStyle.Bold));
            Controls.Add(LabelOf("如果這個小工具對你有幫助，可以支持後續維護與改善。", 14, 76, 620, 24, 10F, Color.FromArgb(166, 170, 177)));

            RoundedPanel koFi = new RoundedPanel(Color.FromArgb(19, 39, 35), Color.FromArgb(39, 111, 91), 12);
            koFi.SetBounds(14, 108, 642, 66);
            koFi.Controls.Add(LabelOf("☕", 16, 14, 38, 35, 18F, Color.FromArgb(233, 235, 239)));
            koFi.Controls.Add(LabelOf("透過 Ko-fi 支持", 60, 10, 250, 25, 12F, ForeColor, FontStyle.Bold));
            koFi.Controls.Add(LabelOf("前往宇航貓的 Ko-fi 頁面", 60, 36, 310, 20, 9F, Color.FromArgb(155, 161, 168)));
            Button open = ButtonOf("開啟 ↗", 537, 17, 88, 32, true);
            open.Click += delegate { OpenUrl(KoFiUrl); };
            koFi.Controls.Add(open);
            koFi.Cursor = Cursors.Hand;
            koFi.Click += delegate { OpenUrl(KoFiUrl); };
            Controls.Add(koFi);

            Controls.Add(CreateWalletCard("USDT | BEP20", "BNB Smart Chain", Bep20Address, 14, 190));
            Controls.Add(CreateWalletCard("USDT | TRC20", "TRON", Trc20Address, 341, 190));

            RoundedPanel warning = new RoundedPanel(Color.FromArgb(42, 36, 20), Color.FromArgb(144, 103, 26), 10);
            warning.SetBounds(14, 374, 642, 58);
            warning.Controls.Add(LabelOf("轉帳前請再次確認", 14, 8, 280, 22, 10F, Color.FromArgb(238, 181, 43), FontStyle.Bold));
            warning.Controls.Add(LabelOf("僅接受上方標示網路的 USDT。使用錯誤幣種或網路可能無法找回，建議先小額測試。", 14, 31, 610, 18, 8.5F, Color.FromArgb(174, 175, 177)));
            Controls.Add(warning);

            Controls.Add(LabelOf("支持不影響額度讀取、更新通知或任何程式功能。", 14, 438, 642, 18, 8F, Color.FromArgb(115, 119, 126), FontStyle.Regular, ContentAlignment.MiddleCenter));
        }

        private Control CreateWalletCard(string title, string network, string address, int x, int y)
        {
            RoundedPanel card = new RoundedPanel(Color.FromArgb(15, 16, 18), Color.FromArgb(49, 53, 59), 10);
            card.SetBounds(x, y, 315, 168);

            PictureBox qr = new PictureBox
            {
                BackColor = Color.White,
                Image = MakeQr(address),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            qr.SetBounds(14, 14, 122, 122);
            card.Controls.Add(qr);

            card.Controls.Add(LabelOf(title, 150, 12, 150, 25, 11F, Color.FromArgb(235, 237, 240), FontStyle.Bold));
            card.Controls.Add(LabelOf(network, 150, 41, 150, 20, 8.5F, Color.FromArgb(146, 151, 159)));
            Label addressLabel = LabelOf(address, 150, 67, 150, 42, 7.2F, Color.FromArgb(202, 205, 210), FontStyle.Bold);
            toolTip.SetToolTip(addressLabel, address);
            card.Controls.Add(addressLabel);
            Button copy = ButtonOf("複製地址", 150, 119, 96, 34, false);
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(address);
                    string old = copy.Text;
                    copy.Text = "已複製 ✓";
                    Timer reset = new Timer { Interval = 1400 };
                    reset.Tick += delegate { reset.Stop(); reset.Dispose(); if (!copy.IsDisposed) copy.Text = old; };
                    reset.Start();
                }
                catch (Exception ex)
                {
                    AppLog.Error("CopySupportAddress", ex);
                    MessageBox.Show(this, "無法複製地址，請稍後再試。", "支持開發", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            card.Controls.Add(copy);
            return card;
        }

        private static Bitmap MakeQr(string value)
        {
            using QRCodeGenerator generator = new QRCodeGenerator();
            using QRCodeData data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
            using QRCode code = new QRCode(data);
            return code.GetGraphic(6, Color.FromArgb(19, 21, 24), Color.White, true);
        }

        private static Label LabelOf(string text, int x, int y, int width, int height, float size,
            Color color, FontStyle style = FontStyle.Regular, ContentAlignment alignment = ContentAlignment.MiddleLeft)
        {
            Label label = new Label
            {
                Text = text,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft JhengHei UI", size, style),
                AutoSize = false,
                TextAlign = alignment
            };
            label.SetBounds(x, y, width, height);
            return label;
        }

        private static Button ButtonOf(string text, int x, int y, int width, int height, bool accent)
        {
            Button button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = accent ? Color.FromArgb(19, 39, 35) : Color.FromArgb(20, 22, 25),
                ForeColor = accent ? Color.FromArgb(79, 214, 163) : Color.FromArgb(231, 233, 236),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = accent ? Color.FromArgb(39, 111, 91) : Color.FromArgb(59, 64, 71);
            button.SetBounds(x, y, width, height);
            return button;
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                AppLog.Error("OpenSupportUrl", ex);
                MessageBox.Show("無法開啟瀏覽器，網址為：" + Environment.NewLine + url,
                    "支持開發", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) toolTip.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        private readonly Color borderColor;
        private readonly int radius;

        public RoundedPanel(Color fillColor, Color borderColor, int radius)
        {
            BackColor = fillColor;
            this.borderColor = borderColor;
            this.radius = radius;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using GraphicsPath path = Rounded(bounds, radius);
            using SolidBrush fill = new SolidBrush(BackColor);
            using Pen border = new Pen(borderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, diameter, diameter, 180, 90);
            path.AddArc(r.Right - diameter, r.Top, diameter, diameter, 270, 90);
            path.AddArc(r.Right - diameter, r.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(r.Left, r.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
