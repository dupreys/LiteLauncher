using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// Downloads. Steam does the real work: "Download" asks it to install the game (steam://install/ID) and the
// progress is read from the manifest files Steam keeps up to date. A download icon appears in the top bar while
// anything is downloading; Up from the top row of the screen reaches it and A opens the list.
public partial class MainWindow
{
    private readonly DispatcherTimer _downloadsTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private IReadOnlyList<DownloadInfo> _downloads = Array.Empty<DownloadInfo>();
    private readonly Dictionary<string, (string Name, DateTime At)> _requestedDownloads = new();
    private readonly Dictionary<string, (long Bytes, DateTime At, double Speed)> _downloadSpeed = new();
    private Dictionary<string, string> _activeDownloadNames = new();
    private bool _pollingDownloads;
    private IInputElement? _downloadsReturnFocus;

    private bool DownloadsOpen => DownloadsPanel.Visibility == Visibility.Visible;

    private void StartDownloadsPolling()
    {
        _downloadsTimer.Tick += async (_, _) => await PollDownloadsAsync();
        _downloadsTimer.Start();
        _ = PollDownloadsAsync();
    }

    // ---- Starting a download ----

    private void OnDownloadButton(GameItem item)
    {
        var id = item.Game.Id;
        if (_downloads.Any(d => d.AppId == id))
        {
            CloseGameDetails();
            OpenDownloads();
            return;
        }

        if (_steamPath is null)
        {
            ShowToast("Steam is not installed on this PC.");
            return;
        }

        string uri;
        try { uri = SteamDownloads.InstallUri(id); }
        catch (ArgumentException) { return; }

        if (!OpenSteamUrl(uri)) return;
        _requestedDownloads[id] = (item.Name, DateTime.UtcNow);
        App.Sounds.Play(SoundKind.Select);
        ShowToast("Confirm the installation in Steam to start the download.");
        UpdateDetailsPlayButton(item);
        _ = PollDownloadsAsync();
        StepAsideForClient(); // Steam asks where to install: its window has to be in front
    }

    private void UpdateDetailsPlayButton(GameItem item)
    {
        if (IsApp(item))
        {
            DetailsPlayLabel.Text = Loc.T("▶  Open");
            return;
        }

        if (!item.Game.NotInstalled)
        {
            DetailsPlayLabel.Text = Loc.T("▶  Play");
            return;
        }

        var current = _downloads.FirstOrDefault(d => d.AppId == item.Game.Id);
        DetailsPlayLabel.Text = current is null ? Loc.T("⬇  Download")
            : current.Status == DownloadStatus.Waiting ? Loc.T("Waiting for Steam…")
            : Loc.F("Downloading… {0}%", (int)(current.Fraction * 100));
    }

    // ---- Following the progress ----

    private async Task PollDownloadsAsync()
    {
        if (_pollingDownloads || _steamPath is null) return;
        _pollingDownloads = true;
        try
        {
            var steamPath = _steamPath;
            var read = await Task.Run(() => SteamDownloads.Read(steamPath));
            var now = DateTime.UtcNow;
            var list = new List<DownloadInfo>(read);

            // Only what Steam is really downloading is listed. A request the user never confirmed (or cancelled in
            // Steam's dialog) is forgotten here and never shown.
            foreach (var (id, request) in _requestedDownloads.ToList())
            {
                if (read.Any(d => d.AppId == id) || now - request.At > TimeSpan.FromMinutes(10))
                    _requestedDownloads.Remove(id);
            }

            // Steam rewrites the manifest only every so often, so the number would jump. Between rewrites the progress is
            // estimated from the speed measured at the last two real readings (never past the total).
            for (var k = 0; k < list.Count; k++)
            {
                var d = list[k];
                if (d.Status != DownloadStatus.Downloading || d.BytesTotal <= 0) continue;
                var rate = 0.0;
                var last = d.BytesDone;
                var changedAt = now;
                if (_downloadSpeed.TryGetValue(d.AppId, out var t))
                {
                    rate = t.Speed; last = t.Bytes; changedAt = t.At;
                    if (d.BytesDone > t.Bytes)
                    {
                        var seconds = Math.Max(1, (now - t.At).TotalSeconds);
                        var measured = (d.BytesDone - t.Bytes) / seconds;
                        rate = rate > 0 ? (rate * 0.4) + (measured * 0.6) : measured;
                        last = d.BytesDone;
                        changedAt = now;
                    }
                }
                _downloadSpeed[d.AppId] = (last, changedAt, rate);
                var estimate = last + (long)(rate * Math.Min((now - changedAt).TotalSeconds, 45));
                list[k] = d with { BytesDone = Math.Min(Math.Max(estimate, d.BytesDone), (long)(d.BytesTotal * 0.995)) };
            }
            foreach (var gone in _downloadSpeed.Keys.Where(k => read.All(d => d.AppId != k)).ToList()) _downloadSpeed.Remove(gone);

            // Something that was downloading and is no longer listed has finished (or was cancelled).
            var vanished = _activeDownloadNames.Where(p => read.All(d => d.AppId != p.Key)).ToList();
            _activeDownloadNames = read.ToDictionary(d => d.AppId, d => d.Name);
            if (vanished.Count > 0)
            {
                var installed = await Task.Run(() => SteamLibraryReader.ReadInstalledGames(steamPath).Select(g => g.Id).ToHashSet());
                foreach (var (id, name) in vanished)
                    if (installed.Contains(id)) ShowToast(Loc.F("{0} finished downloading", name));
                ScheduleRefresh();
            }

            _downloads = list;
            UpdateDownloadsUi();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Steam is rewriting its files; the next poll tries again
        }
        finally
        {
            _pollingDownloads = false;
        }
    }

    private void UpdateDownloadsUi()
    {
        var any = _downloads.Count > 0;
        var wasFocused = ReferenceEquals(Keyboard.FocusedElement, DownloadsButton);
        DownloadsButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        if (any)
        {
            var main = _downloads.FirstOrDefault(d => d.Status == DownloadStatus.Downloading && d.BytesTotal > 0);
            DownloadsText.Text = _downloads.Count > 1 ? Loc.F("{0} downloads", _downloads.Count)
                : main is not null ? $"{(int)(main.Fraction * 100)}%" : Loc.T("Waiting for Steam…");
        }
        else if (wasFocused && !DownloadsOpen)
        {
            ReturnFromTopBar();
        }

        if (DownloadsOpen) BuildDownloadRows();
        if (DetailsOpen && DetailsItem() is { } shown) UpdateDetailsPlayButton(shown);
    }

    // ---- The downloads screen ----

    private void OnDownloadsClick(object sender, RoutedEventArgs e) => OpenDownloads();

    private void OpenDownloads()
    {
        if (DownloadsOpen || PowerOpen || OverlayVisible) return;
        VirtualKeyboard.Hide();
        _downloadsReturnFocus = Keyboard.FocusedElement is UIElement f && !ReferenceEquals(f, DownloadsButton) ? f : _topBarReturn;
        BuildDownloadRows();
        DownloadsPanel.Visibility = Visibility.Visible;
        App.Sounds.Play(SoundKind.Select);
        Keyboard.Focus(DownloadsCloseButton);
        _ = PollDownloadsAsync();
    }

    private void CloseDownloads()
    {
        if (!DownloadsOpen) return;
        DownloadsPanel.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Back);
        if (_downloadsReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private void OnDownloadsCloseClick(object sender, RoutedEventArgs e) => CloseDownloads();

    private void OnDownloadsDimClick(object sender, MouseButtonEventArgs e) => CloseDownloads();

    private void OnDownloadsOpenSteamClick(object sender, RoutedEventArgs e)
    {
        if (OpenSteamUrl("steam://nav/downloads")) StepAsideForClient();
    }

    private void BuildDownloadRows()
    {
        DownloadsList.Children.Clear();
        if (_downloads.Count == 0)
        {
            DownloadsList.Children.Add(new TextBlock
            {
                Text = Loc.T("No downloads right now."),
                FontSize = 16,
                Margin = new Thickness(0, 8, 0, 0),
            }.Muted());
            return;
        }

        foreach (var d in _downloads)
        {
            var speed = _downloadSpeed.TryGetValue(d.AppId, out var s) ? s.Speed : 0;
            var status = d.Status switch
            {
                DownloadStatus.Waiting => Loc.T("Waiting for you to confirm in Steam…"),
                DownloadStatus.Paused => Loc.F("Paused · {0} of {1}", SteamDownloads.FormatBytes(d.BytesDone), SteamDownloads.FormatBytes(d.BytesTotal)),
                _ when d.BytesTotal == 0 => Loc.T("Preparing…"),
                _ => Loc.F("{0} of {1}", SteamDownloads.FormatBytes(d.BytesDone), SteamDownloads.FormatBytes(d.BytesTotal))
                     + (speed > 1 ? "  ·  " + Loc.F("{0}/s", SteamDownloads.FormatBytes((long)speed)) : ""),
            };

            var name = new TextBlock { Text = d.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var kind = new TextBlock { Text = Loc.T(d.IsUpdate ? "Updating" : "Installing"), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            kind.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var info = new TextBlock { Text = status, FontSize = 13, Margin = new Thickness(0, 3, 0, 0) }.Muted();
            var percent = new TextBlock
            {
                Text = d.Status == DownloadStatus.Waiting || d.BytesTotal == 0 ? "" : $"{(int)(d.Fraction * 100)}%",
                FontSize = 24,
                FontWeight = FontWeights.Light,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var bar = new Grid { Height = 6, Margin = new Thickness(0, 10, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(d.Fraction, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - d.Fraction, GridUnitType.Star) });
            var track = new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)) };
            Grid.SetColumnSpan(track, 2);
            var fill = new Border();
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            bar.Children.Add(track);
            bar.Children.Add(fill);

            var text = new StackPanel();
            text.Children.Add(kind);
            text.Children.Add(name);
            text.Children.Add(info);
            text.Children.Add(bar);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(text);
            Grid.SetColumn(percent, 1);
            percent.Margin = new Thickness(24, 0, 0, 0);
            row.Children.Add(percent);

            DownloadsList.Children.Add(new Border { Style = (Style)FindResource("XboxCard"), Child = row });
        }
    }

    private bool HandleDownloadsKey(Key key)
    {
        if (!DownloadsOpen) return false;
        if (key is Key.Escape or Key.Back)
        {
            CloseDownloads();
            return true;
        }
        return false;
    }

    private bool HandleDownloadsGamepad(GamepadButton button)
    {
        if (!DownloadsOpen) return false;
        if (button is GamepadButton.B or GamepadButton.Start)
        {
            CloseDownloads();
            return true;
        }
        return button == GamepadButton.Y;
    }
}

internal static class DownloadsUiExtensions
{
    /// <summary>Muted text colour that follows the theme.</summary>
    public static TextBlock Muted(this TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return text;
    }
}
