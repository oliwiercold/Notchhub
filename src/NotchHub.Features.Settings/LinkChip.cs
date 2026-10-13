using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using NotchHub.Core;

namespace NotchHub.Features.Settings;

/// <summary>
/// A URL / redirect URI shown in a how-to: clicking it copies the text to the clipboard and briefly shows "Skopiowano".
/// With <see cref="CanOpen"/> an extra "Otwórz" button opens the page in the browser. Template lives in SettingsView.xaml.
/// </summary>
[TemplatePart(Name = "PART_Copy", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Open", Type = typeof(ButtonBase))]
public sealed class LinkChip : Control
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
        nameof(Url), typeof(string), typeof(LinkChip), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty CanOpenProperty = DependencyProperty.Register(
        nameof(CanOpen), typeof(bool), typeof(LinkChip), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey IsCopiedKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCopied), typeof(bool), typeof(LinkChip), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty IsCopiedProperty = IsCopiedKey.DependencyProperty;

    private ButtonBase? _copy, _open;
    private DispatcherTimer? _reset;

    public string Url { get => (string)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }
    public bool CanOpen { get => (bool)GetValue(CanOpenProperty); set => SetValue(CanOpenProperty, value); }
    public bool IsCopied => (bool)GetValue(IsCopiedProperty);

    public LinkChip() => Unloaded += (_, _) => { _reset?.Stop(); SetValue(IsCopiedKey, false); };

    public override void OnApplyTemplate()
    {
        if (_copy is not null) _copy.Click -= OnCopyClick;
        if (_open is not null) _open.Click -= OnOpenClick;
        base.OnApplyTemplate();
        _copy = GetTemplateChild("PART_Copy") as ButtonBase;
        _open = GetTemplateChild("PART_Open") as ButtonBase;
        if (_copy is not null) _copy.Click += OnCopyClick;
        if (_open is not null) _open.Click += OnOpenClick;
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Url)) return;
        if (!await ClipboardText.SetAsync(Url)) return;
        SetValue(IsCopiedKey, true);
        _reset ??= CreateResetTimer();
        _reset.Stop();
        _reset.Start();
    }

    private DispatcherTimer CreateResetTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        t.Tick += (_, _) => { t.Stop(); SetValue(IsCopiedKey, false); };
        return t;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => ClipboardText.OpenUrl(Url);
}

/// <summary>Clipboard writes on the UI thread with a short retry while another app holds the clipboard open.</summary>
internal static class ClipboardText
{
    private const int ClipbrdECantOpen = unchecked((int)0x800401D0);

    public static async Task<bool> SetAsync(string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (COMException ex) when (ex.HResult == ClipbrdECantOpen && attempt < 6)
            {
                await Task.Delay(40 * attempt); // resumes on the UI thread (dispatcher sync context)
            }
            catch (Exception ex)
            {
                Log.Warn("Settings: copying a link to the clipboard failed", ex);
                return false;
            }
        }
    }

    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
        try { using (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })) { } }
        catch (Exception ex) { Log.Warn("Settings: open url failed", ex); }
    }
}
