using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiteLauncher.Core;
using LiteLauncher.Core.Epic;
using LiteLauncher.Core.Manual;
using LiteLauncher.Core.Settings;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// Settings > General tools: settings backup, diagnostics, release notes ("What's new") and the play time screen.
public partial class MainWindow
{
    private Action? _infoAction1;
    private Action? _infoAction2;
    private IInputElement? _infoReturnFocus;
    private IInputElement? _statsReturnFocus;

    private bool InfoOpen => InfoPanel.Visibility == Visibility.Visible;

    private bool StatsOpen => StatsPanel.Visibility == Visibility.Visible;

    // ---- Backup and restore of the settings ----

    private void OnExportSettingsClick(object sender, RoutedEventArgs e)
    {
        SaveSettingsNow();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("Save a backup of the settings"),
            Filter = Loc.T("LiteLauncher backup") + "|*.json",
            FileName = $"LiteLauncher-settings-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, SettingsBackup.Export(App.Settings));
            ShowToast("Backup saved. API keys are not included.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Could not write the file.");
        }
    }

    private async void OnImportSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose a settings backup"),
            Filter = Loc.T("LiteLauncher backup") + "|*.json",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        AppSettings? imported;
        try
        {
            if (new FileInfo(dialog.FileName).Length > SettingsBackup.MaxBytes)
            {
                ShowToast("That file is not a LiteLauncher backup.");
                return;
            }
            imported = SettingsBackup.Import(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Could not read the file.");
            return;
        }

        if (imported is null)
        {
            ShowToast("That file is not a LiteLauncher backup.");
            return;
        }

        SettingsBackup.ApplyTo(App.Settings, imported);
        SaveSettingsNow();

        // What can change on the spot does; the rest (language, the settings screen's controls) needs a restart.
        ThemeManager.Apply(App.Settings);
        _currentBackdropKey = null;
        ApplyBackdropSettings();
        ApplyHomeStyle();
        await RefreshAsync();
        ShowToast("Settings imported. Restart LiteLauncher to apply everything.");
    }

    // ---- Info panel (diagnostics, release notes) ----

    private void ShowInfo(string caption, string title, string subtitle, string body, bool monospace,
                          (string Label, Action Do)? action1 = null, (string Label, Action Do)? action2 = null)
    {
        _infoReturnFocus = Keyboard.FocusedElement;
        InfoCaption.Text = caption;
        InfoTitle.Text = title;
        InfoSubtitle.Text = subtitle;
        InfoSubtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        InfoBody.Text = body;
        InfoBody.FontFamily = monospace ? new FontFamily("Consolas") : SystemFonts.MessageFontFamily;
        InfoBody.FontSize = monospace ? 13 : 16;

        _infoAction1 = action1?.Do;
        _infoAction2 = action2?.Do;
        InfoAction1Button.Visibility = action1 is null ? Visibility.Collapsed : Visibility.Visible;
        InfoAction2Button.Visibility = action2 is null ? Visibility.Collapsed : Visibility.Visible;
        if (action1 is { } a1) InfoAction1Label.Text = a1.Label;
        if (action2 is { } a2) InfoAction2Label.Text = a2.Label;

        InfoPanel.Visibility = Visibility.Visible;
        InfoScroll.ScrollToTop();
        App.Sounds.Play(SoundKind.Select);
        Keyboard.Focus(InfoCloseButton);
    }

    private void CloseInfo()
    {
        if (!InfoOpen) return;
        InfoPanel.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Back);
        if (_infoReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private void OnInfoCloseClick(object sender, RoutedEventArgs e) => CloseInfo();

    private void OnInfoDimClick(object sender, MouseButtonEventArgs e) => CloseInfo();

    private void OnInfoAction1Click(object sender, RoutedEventArgs e) => _infoAction1?.Invoke();

    private void OnInfoAction2Click(object sender, RoutedEventArgs e) => _infoAction2?.Invoke();

    private void ScrollInfo(int direction) => InfoScroll.ScrollToVerticalOffset(Math.Max(0, InfoScroll.VerticalOffset + direction * 90));

    private bool HandleInfoKey(Key key)
    {
        if (!InfoOpen) return false;
        switch (key)
        {
            case Key.Escape or Key.Back:
                CloseInfo();
                return true;
            case Key.Up:
                ScrollInfo(-1);
                return true;
            case Key.Down:
                ScrollInfo(+1);
                return true;
            case Key.PageUp:
                ScrollInfo(-4);
                return true;
            case Key.PageDown:
                ScrollInfo(+4);
                return true;
            default:
                return false;
        }
    }

    private bool HandleInfoGamepad(GamepadButton button)
    {
        if (!InfoOpen) return false;
        switch (button)
        {
            case GamepadButton.B or GamepadButton.Start:
                CloseInfo();
                return true;
            case GamepadButton.Up:
                ScrollInfo(-1);
                return true;
            case GamepadButton.Down:
                ScrollInfo(+1);
                return true;
            case GamepadButton.Y:
                return true;
            default:
                return false;
        }
    }

    // ---- Diagnostics ----

    private void OnDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var text = BuildDiagnostics();
        ShowInfo(Loc.T("DIAGNOSTICS"), Loc.T("Diagnostics"),
                 Loc.T("Facts about this installation. Keys and passwords are never included. Use the buttons to copy it or save it as a file."),
                 text, monospace: true,
                 (Loc.T("Copy"), () => CopyDiagnostics(text)),
                 (Loc.T("Save as file…"), () => SaveDiagnostics(text)));
    }

    private string BuildDiagnostics()
    {
        static string YesNo(bool value) => value ? "yes" : "no"; // the report stays in English so it can be shared
        var settings = App.Settings;
        var steamGames = _items.Count(i => i.Game.Source == SteamProvider.ProviderName);
        var epicGames = _items.Count(i => i.Game.Source == EpicProvider.ProviderName);
        var manual = settings.ManualGames;

        string FolderSize(string folder)
        {
            try { return Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Count().ToString(CultureInfo.InvariantCulture) : "0"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "?"; }
        }

        long SettingsSize()
        {
            try { return File.Exists(SettingsStore.DefaultFilePath) ? new FileInfo(SettingsStore.DefaultFilePath).Length : 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }

        var sections = new List<(string, IReadOnlyList<(string, string)>)>
        {
            ("LiteLauncher", new (string, string)[]
            {
                ("Version", AppInfo.Display),
                ("Repository", string.IsNullOrEmpty(AppInfo.Repository) ? "—" : AppInfo.Repository!),
                ("Windows", RuntimeInformation.OSDescription),
                (".NET", Environment.Version.ToString()),
                ("Language", settings.Language ?? "auto"),
                ("Home style", settings.HomeStyle),
                ("Background", (settings.FullBackdrop ? "full screen" : "banner") + (settings.UseBackgroundColor ? "" : ", colour off")
                               + (settings.Wallpaper.Length > 0 ? ", wallpaper" : "")),
            }),
            ("Steam", new (string, string)[]
            {
                ("Folder", _steamPath ?? "not found"),
                ("Libraries", _steamPath is null ? "—" : string.Join("; ", SteamLibraryReader.GetLibraryFolders(_steamPath))),
                ("Installed games", steamGames.ToString(CultureInfo.InvariantCulture)),
                ("Not installed (known)", _owned.Count.ToString(CultureInfo.InvariantCulture)),
                ("Signed in", YesNo(SteamLoginUsers.IsSteamId64(settings.SteamId))),
                ("Web API key", YesNo(!string.IsNullOrEmpty(settings.SteamWebApiKey))),
                ("SteamGridDB key", YesNo(!string.IsNullOrEmpty(settings.SteamGridDbApiKey))),
                ("Downloads in progress", _downloads.Count.ToString(CultureInfo.InvariantCulture)),
            }),
            ("Epic Games", new (string, string)[]
            {
                ("Manifests", EpicProvider.DefaultManifestDirectory() ?? "not found"),
                ("Installed games", epicGames.ToString(CultureInfo.InvariantCulture)),
            }),
            ("Manual games", new (string, string)[]
            {
                ("Added", manual.Count.ToString(CultureInfo.InvariantCulture)),
                ("With a missing .exe", manual.Count(g => !File.Exists(g.ExePath)).ToString(CultureInfo.InvariantCulture)),
            }),
            ("Data", new (string, string)[]
            {
                ("Settings file", $"{SettingsStore.DefaultFilePath} ({SettingsSize()} bytes)"),
                ("Art cache files", FolderSize(ArtCacheDir)),
                ("Play sessions kept", settings.PlaySessions.Count.ToString(CultureInfo.InvariantCulture)),
                ("Error log", CrashLog.FilePath),
            }),
        };

        string? crash = null;
        try { if (File.Exists(CrashLog.FilePath)) crash = DiagnosticsReport.Tail(File.ReadAllText(CrashLog.FilePath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { crash = null; }

        return DiagnosticsReport.Redact(DiagnosticsReport.Build(sections, crash));
    }

    private void CopyDiagnostics(string text)
    {
        try
        {
            Clipboard.SetText(text);
            ShowToast("Copied to the clipboard.");
        }
        catch (COMException)
        {
            ShowToast("Could not use the clipboard right now.");
        }
    }

    private void SaveDiagnostics(string text)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("Save the diagnostics"),
            Filter = Loc.T("Text file") + "|*.txt",
            FileName = $"LiteLauncher-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, text);
            ShowToast("File saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Could not write the file.");
        }
    }

    // ---- Release notes ----

    private static string VersionNumber(Version v) => $"{Math.Max(v.Major, 0)}.{Math.Max(v.Minor, 0)}.{Math.Max(v.Build, 0)}";

    private async void OnWhatsNewClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(AppInfo.Repository) || AppInfo.Current <= new Version(0, 0, 0))
        {
            ShowToast("Release notes only exist for installed releases, not development builds.");
            return;
        }

        ShowToast("Loading the release notes…");
        var notes = await ReleaseNotes.FetchAsync(Http, AppInfo.Repository, AppInfo.Current);
        if (notes is null)
        {
            ShowToast("The release notes are not available right now (offline, or this version has none).");
            return;
        }
        ShowReleaseNotes(notes);
    }

    private void ShowReleaseNotes(ReleaseNotesInfo notes) =>
        ShowInfo(Loc.T("WHAT'S NEW"), notes.Title, $"LiteLauncher {notes.Tag}", notes.Body, monospace: false,
                 (Loc.T("Open on GitHub"), () => OpenReleasePage(notes.PageUrl)));

    private void OpenReleasePage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { ShowToast("Could not open the browser."); }
    }

    /// <summary>Once after an update: shows what changed. A fresh install only records the version.</summary>
    private async Task MaybeShowReleaseNotesAsync()
    {
        var current = AppInfo.Current;
        if (current <= new Version(0, 0, 0) || string.IsNullOrEmpty(AppInfo.Repository)) return;

        var number = VersionNumber(current);
        if (App.Settings.LastSeenVersion == number) return;
        if (App.FirstRun)
        {
            App.Settings.LastSeenVersion = number;
            QueueSave();
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(6)); // let the home screen settle first
        var notes = await ReleaseNotes.FetchAsync(Http, AppInfo.Repository, current);
        if (notes is null) return; // offline: try again next start

        if (ActiveModal() is not null || _current != AppView.Home) return; // busy right now: next start
        App.Settings.LastSeenVersion = number;
        SaveSettingsNow();
        ShowReleaseNotes(notes);
    }

    // ---- Play time ----

    private void OnStatsClick(object sender, RoutedEventArgs e) => _ = OpenStatsAsync();

    private async Task OpenStatsAsync()
    {
        if (StatsOpen || DownloadsOpen || PowerOpen || OverlayVisible) return;
        VirtualKeyboard.Hide();
        _statsReturnFocus = Keyboard.FocusedElement;

        var steamPath = _steamPath;
        var steamId = App.Settings.SteamId;
        var steamMinutes = steamPath is null
            ? new Dictionary<string, int>() as IReadOnlyDictionary<string, int>
            : await Task.Run(() => SteamPlaytime.ReadAll(steamPath, steamId));

        BuildStats(steamMinutes);
        StatsPanel.Visibility = Visibility.Visible;
        App.Sounds.Play(SoundKind.Select);
        Keyboard.Focus(StatsCloseButton);
    }

    private void CloseStats()
    {
        if (!StatsOpen) return;
        StatsPanel.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Back);
        if (_statsReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private void OnStatsCloseClick(object sender, RoutedEventArgs e) => CloseStats();

    private void OnStatsDimClick(object sender, MouseButtonEventArgs e) => CloseStats();

    private bool HandleStatsKey(Key key)
    {
        if (!StatsOpen) return false;
        if (key is Key.Escape or Key.Back)
        {
            CloseStats();
            return true;
        }
        return false;
    }

    private bool HandleStatsGamepad(GamepadButton button)
    {
        if (!StatsOpen) return false;
        if (button is GamepadButton.B or GamepadButton.Start)
        {
            CloseStats();
            return true;
        }
        return button == GamepadButton.Y;
    }

    private static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        return block;
    }

    private static string Duration(int minutes) => minutes <= 0 ? "—" : GameInfo.FormatPlaytime(minutes);

    private void BuildStats(IReadOnlyDictionary<string, int> steamMinutes)
    {
        StatsContent.Children.Clear();
        var sessions = App.Settings.PlaySessions;
        var today = DateTime.Now;
        var perDay = PlayHistory.MinutesPerDay(sessions, today, 7);

        // Summary cards
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 3 };
        void Card(string caption, int minutes, bool last = false)
        {
            var stack = new StackPanel();
            stack.Children.Add(Caption(caption));
            stack.Children.Add(new TextBlock { Text = Duration(minutes), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            cards.Children.Add(new Border { Style = (Style)FindResource("XboxCard"), Child = stack, Margin = new Thickness(0, 0, last ? 0 : 12, 12) });
        }
        Card(Loc.T("TODAY"), perDay[6]);
        Card(Loc.T("LAST 7 DAYS"), perDay.Sum());
        Card(Loc.T("LAST 30 DAYS"), PlayHistory.TotalMinutes(sessions, today, 30), last: true);
        StatsContent.Children.Add(cards);

        // Last 7 days
        var chart = new Grid { Height = 130 };
        var max = Math.Max(1, perDay.Max());
        for (var i = 0; i < 7; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, 0) };
            var value = new TextBlock { Text = perDay[i] > 0 ? Duration(perDay[i]).Replace(" min", "m").Replace(" hrs ", "h ") : "", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            var bar = new Border { Height = Math.Max(3, 70.0 * perDay[i] / max), Opacity = perDay[i] > 0 ? 1 : 0.25, VerticalAlignment = VerticalAlignment.Bottom };
            bar.SetResourceReference(Border.BackgroundProperty, "Accent");
            var day = new TextBlock { Text = today.Date.AddDays(i - 6).ToString("ddd", CultureInfo.CurrentCulture), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            day.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            column.Children.Add(value);
            column.Children.Add(bar);
            column.Children.Add(day);
            Grid.SetColumn(column, i);
            chart.Children.Add(column);
        }
        var chartBox = new StackPanel();
        chartBox.Children.Add(Caption(Loc.T("LAST 7 DAYS")));
        chartBox.Children.Add(new Border { Height = 8 });
        chartBox.Children.Add(chart);
        StatsContent.Children.Add(new Border { Style = (Style)FindResource("XboxCard"), Child = chartBox });

        // Most played and recent sessions side by side
        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var top = new StackPanel();
        top.Children.Add(Caption(Loc.T("MOST PLAYED")));
        foreach (var (name, minutes) in MostPlayed(steamMinutes).Take(7)) top.Children.Add(StatsRow(name, Duration(minutes)));
        if (top.Children.Count == 1) top.Children.Add(StatsRow(Loc.T("Nothing yet"), ""));
        var topCard = new Border { Style = (Style)FindResource("XboxCard"), Child = top, Margin = new Thickness(0, 0, 12, 12) };

        var recent = new StackPanel();
        recent.Children.Add(Caption(Loc.T("RECENT SESSIONS")));
        foreach (var s in sessions.AsEnumerable().Reverse().Take(7))
            recent.Children.Add(StatsRow($"{s.Name}  ·  {s.Start.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture)}", Duration(s.Minutes)));
        if (recent.Children.Count == 1) recent.Children.Add(StatsRow(Loc.T("Nothing yet"), ""));
        var recentCard = new Border { Style = (Style)FindResource("XboxCard"), Child = recent, Margin = new Thickness(0, 0, 0, 12) };

        Grid.SetColumn(recentCard, 1);
        lists.Children.Add(topCard);
        lists.Children.Add(recentCard);
        StatsContent.Children.Add(lists);

        var note = new TextBlock { Text = Loc.T("Sessions are counted while LiteLauncher is open. Total time comes from Steam and from what the launcher recorded."), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        StatsContent.Children.Add(note);
    }

    private static Grid StatsRow(string left, string right)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = left, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        var value = new TextBlock { Text = right, FontSize = 14, Margin = new Thickness(16, 0, 0, 0) };
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    /// <summary>Total time per game: Steam's own figure for Steam games, what the launcher recorded for the others.</summary>
    private IEnumerable<(string Name, int Minutes)> MostPlayed(IReadOnlyDictionary<string, int> steamMinutes)
    {
        var totals = new Dictionary<string, (string Name, int Minutes)>();

        foreach (var item in _items.Concat(_owned))
        {
            var game = item.Game;
            var key = GameSorter.Key(game);
            var minutes = 0;
            if (game.Source == SteamProvider.ProviderName) steamMinutes.TryGetValue(game.Id, out minutes);
            else if (App.Settings.PlayRecords.TryGetValue(key, out var record)) minutes = record.Minutes;
            if (minutes > 0) totals[key] = (item.Name, minutes);
        }

        // Games that are gone or unknown but were played from here
        foreach (var (key, name, minutes) in PlayHistory.TotalsByGame(App.Settings.PlaySessions))
            if (!totals.ContainsKey(key)) totals[key] = (name, minutes);

        return totals.Values.OrderByDescending(t => t.Minutes);
    }
}
