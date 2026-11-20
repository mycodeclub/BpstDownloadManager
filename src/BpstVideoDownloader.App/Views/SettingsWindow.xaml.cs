using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using BpstVideoDownloader.Core;
using BpstVideoDownloader.Core.Media;

namespace BpstVideoDownloader.App.Views;

public partial class SettingsWindow : Window
{
    private readonly DownloadManager _manager;

    public SettingsWindow(DownloadManager manager)
    {
        InitializeComponent();
        _manager = manager;
        var s = manager.Settings;
        FolderBox.Text = s.DownloadFolder;
        ConnSlider.Value = Math.Clamp(s.Connections, 1, 32);
        ConcSlider.Value = Math.Clamp(s.MaxConcurrentDownloads, 1, 10);
        QualityBox.ItemsSource = MediaQuality.Options;
        QualityBox.SelectedValue = s.DefaultMediaQuality;
        ClipboardCheck.IsChecked = s.MonitorClipboard;
        TrayCheck.IsChecked = s.MinimizeToTray;
        NotifyCheck.IsChecked = s.ShowCompletionNotification;
        ConnText.Text = ((int)ConnSlider.Value).ToString();
        ConcText.Text = ((int)ConcSlider.Value).ToString();
        BrowserBox.ItemsSource = Browsers;
        LoadSignIn();
    }

    private static readonly string[] Browsers = ["(none)", "firefox", "chrome", "edge", "brave", "opera", "vivaldi", "chromium"];

    private void LoadSignIn()
    {
        var s = _manager.Settings;
        bool velox = _manager.IsYouTubeSignedIn;
        SignInStatus.Text = velox ? "Signed in to YouTube in BpstVideoDownloader." : "Not signed in.";
        SignOutButton.IsEnabled = velox;
        CookiesBox.Text = velox ? "" : s.CookiesFile ?? "";
        BrowserBox.SelectedItem = string.IsNullOrEmpty(s.CookiesFromBrowser) ? Browsers[0] : s.CookiesFromBrowser;
    }

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (new YouTubeSignInWindow(_manager) { Owner = this }.ShowDialog() == true) LoadSignIn();
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        YouTubeSignInWindow.SignOut(_manager);
        LoadSignIn();
    }

    private void BrowseCookies_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "cookies.txt|*.txt|All files|*.*" };
        if (dlg.ShowDialog(this) == true) CookiesBox.Text = dlg.FileName;
    }

    private void ConnSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ConnText is not null) ConnText.Text = ((int)e.NewValue).ToString();
    }

    private void ConcSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ConcText is not null) ConcText.Text = ((int)e.NewValue).ToString();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null };
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _manager.Settings;
        s.DownloadFolder = FolderBox.Text.Trim();
        s.Connections = (int)ConnSlider.Value;
        s.MaxConcurrentDownloads = (int)ConcSlider.Value;
        s.DefaultMediaQuality = QualityBox.SelectedValue as string ?? MediaQuality.Best;
        s.MonitorClipboard = ClipboardCheck.IsChecked == true;
        s.MinimizeToTray = TrayCheck.IsChecked == true;
        s.ShowCompletionNotification = NotifyCheck.IsChecked == true;
        if (!_manager.IsYouTubeSignedIn)
            s.CookiesFile = string.IsNullOrWhiteSpace(CookiesBox.Text) ? null : CookiesBox.Text.Trim();
        s.CookiesFromBrowser = BrowserBox.SelectedItem is string b && b != Browsers[0] ? b : null;
        _manager.SaveSettings();
        DialogResult = true;
    }
}
