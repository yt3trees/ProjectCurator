// TrayService は System.Windows.Forms.NotifyIcon を使用します。
// .csproj に <UseWindowsForms>true</UseWindowsForms> が必要です。
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace Curia.Services;

public class TrayService : IDisposable
{
    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ToolStripMenuItem? _hotkeyMenuItem;
    private bool _disposed;

    public Action? OnActivated { get; set; }
    public Action? OnCaptureActivated { get; set; }

    public BitmapSource? DiamondBitmapSource { get; private set; }

    public void Initialize(Window window)
    {
        var icon = CreateDiamondIcon(out var bitmapSource);
        DiamondBitmapSource = bitmapSource;

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "Curia",
            Visible = true,
        };

        var contextMenu = new WinForms.ContextMenuStrip();

        var showItem = new WinForms.ToolStripMenuItem("Show");
        showItem.Click += (_, _) => OnActivated?.Invoke();
        contextMenu.Items.Add(showItem);

        var quickCaptureItem = new WinForms.ToolStripMenuItem("Quick Capture");
        quickCaptureItem.Click += (_, _) => OnCaptureActivated?.Invoke();
        contextMenu.Items.Add(quickCaptureItem);

        contextMenu.Items.Add(new WinForms.ToolStripSeparator());

        _hotkeyMenuItem = new WinForms.ToolStripMenuItem("Hotkey: (none)") { Enabled = false };
        contextMenu.Items.Add(_hotkeyMenuItem);

        contextMenu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            Dispose();
            System.Windows.Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                OnActivated?.Invoke();
        };
    }

    public void UpdateHotkeyDisplay(string hotkeyText)
    {
        if (_hotkeyMenuItem != null)
            _hotkeyMenuItem.Text = $"Hotkey: {hotkeyText}";
    }

    public void ShowBalloonTip(string title, string text, int timeoutMs = 3000)
    {
        _notifyIcon?.ShowBalloonTip(timeoutMs, title, text, WinForms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    /// <summary>
    /// GitHub Blue (#58a6ff) のダイヤモンド形アイコンを 32x32 で生成する。
    /// bitmapSource には WPF 用の BitmapSource も返す。
    /// </summary>
    private static Icon CreateDiamondIcon(out BitmapSource bitmapSource)
    {
        const int size = 32;
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        // GitHub Blue (#58a6ff)
        var ghBlue = Color.FromArgb(255, 0x58, 0xa6, 0xff);
        using var brush = new SolidBrush(ghBlue);

        // タイトルバー上で文字と重心を合わせるため、WPF表示用のみ少し上に寄せる。
        var diamondForWpf = new System.Drawing.Point[]
        {
            new(size / 2, 0),            // top
            new(size - 2, (size / 2) - 2), // right
            new(size / 2, size - 4),     // bottom
            new(2, (size / 2) - 2),      // left
        };
        g.FillPolygon(brush, diamondForWpf);

        // 外枠
        using var pen = new Pen(Color.FromArgb(200, 0x1f, 0x6f, 0xed), 1.5f);
        g.DrawPolygon(pen, diamondForWpf);

        // WPF 用 BitmapSource を生成 (HBITMAP 経由)
        var hBitmap = bmp.GetHbitmap();
        bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap, IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        bitmapSource.Freeze();
        NativeMethods.DeleteObject(hBitmap);
        bmp.Dispose();

        // トレイ用 Icon を生成
        var bmpForIcon = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g2 = Graphics.FromImage(bmpForIcon);
        g2.Clear(Color.Transparent);
        using var brush2 = new SolidBrush(ghBlue);
        var diamondForTray = new System.Drawing.Point[]
        {
            new(size / 2, 2),           // top
            new(size - 2, size / 2),    // right
            new(size / 2, size - 2),    // bottom
            new(2, size / 2),           // left
        };
        g2.FillPolygon(brush2, diamondForTray);
        using var pen2 = new Pen(Color.FromArgb(200, 0x1f, 0x6f, 0xed), 1.5f);
        g2.DrawPolygon(pen2, diamondForTray);
        IntPtr hIcon = bmpForIcon.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        return icon;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
