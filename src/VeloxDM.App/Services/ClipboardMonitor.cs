using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using VeloxDM.Core;

namespace VeloxDM.App.Services;

/// <summary>
/// Listens for clipboard changes (WM_CLIPBOARDUPDATE) and raises <see cref="UrlCaptured"/>
/// when a single downloadable or video URL is copied anywhere in Windows.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private readonly HwndSource _source;
    private string? _last;
    private DateTime _lastAt;

    public ClipboardMonitor(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)!;
        _source.AddHook(WndProc);
        AddClipboardFormatListener(handle);
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Set while VeloxDM itself writes to the clipboard.</summary>
    public bool Suppress { get; set; }

    public event Action<string>? UrlCaptured;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && Enabled && !Suppress)
            _ = ReadAsync();
        return IntPtr.Zero;
    }

    private async Task ReadAsync()
    {
        string? text = null;
        // The copying app may still hold the clipboard open; retry briefly.
        for (int i = 0; i < 5 && text is null; i++)
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                text = Clipboard.GetText();
            }
            catch (COMException)
            {
                await Task.Delay(60);
            }
        }

        var url = UrlClassifier.ExtractUrl(text);
        if (url is null || !UrlClassifier.IsCatchable(url)) return;
        if (url == _last && DateTime.UtcNow - _lastAt < TimeSpan.FromSeconds(10)) return;
        _last = url;
        _lastAt = DateTime.UtcNow;
        UrlCaptured?.Invoke(url);
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
