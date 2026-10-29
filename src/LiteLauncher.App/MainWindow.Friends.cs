using LiteLauncher.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

/// <summary>One row of the friends list.</summary>
public sealed class FriendRow : INotifyPropertyChanged
{
    private static readonly Brush InGame = Frozen("#90BA3C");
    private static readonly Brush Online = Frozen("#57CBDE");
    private static readonly Brush Away = Frozen("#8A93A3");

    private ImageSource? _avatar;

    public FriendRow(SteamFriend friend)
    {
        SteamId = friend.SteamId;
        Name = friend.Name;
        Status = friend.StatusText;
        StatusBrush = friend.Game is not null ? InGame
            : friend.State is FriendState.Away or FriendState.Snooze or FriendState.Busy ? Away : Online;
    }

    public string SteamId { get; }
    public string Name { get; }
    public string Status { get; }
    public Brush StatusBrush { get; }

    public ImageSource? Avatar
    {
        get => _avatar;
        set { _avatar = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Avatar))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

internal static class RemoteImage
{
    private static readonly Dictionary<string, ImageSource> Cache = new();

    /// <summary>Downloads and decodes a small image off the UI thread. Null on any failure.</summary>
    public static async Task<ImageSource?> LoadAsync(HttpClient http, string? url, int decodeWidth)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var cacheKey = $"{decodeWidth}|{url}";
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            var image = await Task.Run<ImageSource?>(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            });
            if (image is not null && Cache.Count < 300) Cache[cacheKey] = image;
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException
                                       or IOException or InvalidOperationException or FileFormatException)
        {
            return null;
        }
    }
}

// Friends: the home tile shows how many Steam friends are online; the panel lists them and opens Steam chat.
public partial class MainWindow
{
    private readonly ObservableCollection<FriendRow> _friends = new();
    private readonly ObservableCollection<FriendRow> _tileFriends = new();
    private readonly DispatcherTimer _friendsTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private bool _friendsWired;
    private bool _friendsLoading;
    private string? _friendsError;
    private IInputElement? _friendsReturnFocus;

    private bool FriendsOpen => FriendsPanel.Visibility == Visibility.Visible;

    private bool FriendsConfigured =>
        App.Settings.SteamWebApiKey is not null && SteamLoginUsers.IsSteamId64(App.Settings.SteamId);

    /// <summary>Called whenever account/profile info changes (and at start-up).</summary>
    private void UpdateFriendsTile()
    {
        if (!_friendsWired)
        {
            _friendsWired = true;
            FriendsList.ItemsSource = _friends;
            FriendsTileAvatars.ItemsSource = _tileFriends;
            _friendsTimer.Tick += async (_, _) => await RefreshFriendsAsync();
            _friendsTimer.Start();
            _ = RefreshFriendsAsync();
        }

        FriendsTileSub.Text =
            !SteamLoginUsers.IsSteamId64(App.Settings.SteamId) ? Loc.T("Sign in with Steam to see friends") :
            App.Settings.SteamWebApiKey is null ? Loc.T("Add a Steam Web API key in Settings") :
            _friendsError is not null ? Loc.T("Friends list unavailable") :
            _friendsLoading && _friends.Count == 0 ? Loc.T("Loading…") :
            _friends.Count == 0 ? Loc.T("No friends online") :
            _friends.Count == 1 ? Loc.T("1 friend online") : Loc.F("{0} friends online", _friends.Count);
    }

    private async Task RefreshFriendsAsync()
    {
        if (_friendsLoading) return;
        if (!FriendsConfigured)
        {
            _friends.Clear();
            _tileFriends.Clear();
            UpdateFriendsTile();
            UpdateFriendsPanel();
            return;
        }

        _friendsLoading = true;
        UpdateFriendsTile();
        try
        {
            var list = await SteamFriends.FetchOnlineAsync(Http, App.Settings.SteamWebApiKey!, App.Settings.SteamId!);
            _friendsError = null;

            var focusedId = (Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
            _friends.Clear();
            _tileFriends.Clear();
            foreach (var friend in list)
            {
                var row = new FriendRow(friend);
                _friends.Add(row);
                if (_tileFriends.Count < 5) _tileFriends.Add(row);
                _ = LoadFriendAvatarAsync(row, friend.AvatarUrl);
            }
            if (FriendsOpen) RestoreFriendFocus(focusedId);
        }
        catch (UnauthorizedAccessException)
        {
            _friendsError = Loc.T("Steam refused the request. Check your Web API key, and that your friends list is not private.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _friendsError = Loc.T("Could not reach Steam. Check your internet connection.");
        }
        finally
        {
            _friendsLoading = false;
            UpdateFriendsTile();
            UpdateFriendsPanel();
        }
    }

    private static async Task LoadFriendAvatarAsync(FriendRow row, string? url) =>
        row.Avatar = await RemoteImage.LoadAsync(Http, url, decodeWidth: 64);

    private void UpdateFriendsPanel()
    {
        var signedIn = SteamLoginUsers.IsSteamId64(App.Settings.SteamId);
        FriendsStatus.Text =
            !signedIn ? Loc.T("Sign in with Steam (Settings → Steam account) to see your friends.") :
            App.Settings.SteamWebApiKey is null ? Loc.T("Add your free Steam Web API key in Settings → Integrations to see who is online.") :
            _friendsError ?? (_friendsLoading && _friends.Count == 0 ? Loc.T("Loading…") :
                              _friends.Count == 0 ? Loc.T("None of your friends are online right now.") :
                              Loc.F("{0} online · press A to chat", _friends.Count));
        FriendsSetupLabel.Text = !signedIn || App.Settings.SteamWebApiKey is null ? Loc.T("Set up…") : Loc.T("Refresh");
    }

    private void OpenFriends()
    {
        _friendsReturnFocus = Keyboard.FocusedElement;
        FriendsPanel.Visibility = Visibility.Visible;
        UpdateFriendsPanel();
        App.Sounds.Play(SoundKind.Select);
        RestoreFriendFocus(null);
        _ = RefreshFriendsAsync();
    }

    private void RestoreFriendFocus(string? steamId)
    {
        FriendsList.UpdateLayout();
        Button? target = null;
        for (var i = 0; i < _friends.Count; i++)
        {
            if (FriendsList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter) continue;
            var button = FindChild<Button>(presenter);
            if (button is null) continue;
            target ??= button;
            if (steamId is not null && (string?)button.Tag == steamId) { target = button; break; }
        }
        Keyboard.Focus((IInputElement?)target ?? FriendsChatButton);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void CloseFriends()
    {
        if (!FriendsOpen) return;
        FriendsPanel.Visibility = Visibility.Collapsed;
        App.Sounds.Play(SoundKind.Back);
        if (_friendsReturnFocus is UIElement { IsVisible: true } back) Keyboard.Focus(back);
        else FocusView(_current);
    }

    private void OnFriendsDimClick(object sender, MouseButtonEventArgs e) => CloseFriends();

    private void OnFriendClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && SteamLoginUsers.IsSteamId64(id))
            OpenSteamUrl($"steam://friends/message/{id}");
    }

    private void OnOpenSteamChatClick(object sender, RoutedEventArgs e) => OpenSteamUrl("steam://open/friends");

    private async void OnFriendsSetupClick(object sender, RoutedEventArgs e)
    {
        if (!SteamLoginUsers.IsSteamId64(App.Settings.SteamId))
        {
            CloseFriends();
            ShowView(AppView.Settings, focusOverride: MenuAccount);
        }
        else if (App.Settings.SteamWebApiKey is null)
        {
            CloseFriends();
            ShowView(AppView.Settings, focusOverride: MenuIntegrations);
        }
        else
        {
            await RefreshFriendsAsync();
        }
    }

    // ---- Settings → Integrations ----

    private void OnApiKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingUi) return;
        var steamKey = SteamApiKeyBox.Text.Trim();
        var gridKey = GridDbKeyBox.Text.Trim();
        var steamChanged = (steamKey.Length == 0 ? null : steamKey) != App.Settings.SteamWebApiKey;
        App.Settings.SteamWebApiKey = steamKey.Length == 0 ? null : steamKey;
        var gridChanged = (gridKey.Length == 0 ? null : gridKey) != App.Settings.SteamGridDbApiKey;
        App.Settings.SteamGridDbApiKey = gridKey.Length == 0 ? null : gridKey;
        _gridGameIds.Clear();
        if (gridChanged && gridKey.Length > 0)
        {
            _artTried.RemoveWhere(k => k.EndsWith("#sgdb", StringComparison.Ordinal)); // new key: look for the missing art again
            ScheduleRefresh();
        }
        QueueSave();
        if (steamChanged)
        {
            _friendsError = null;
            _ = RefreshFriendsAsync();
        }
    }

    private void OnOpenLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url }) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { ShowToast("Could not open the browser."); }
    }

    // ---- Input ----

    private bool HandleFriendsKey(Key key)
    {
        if (!FriendsOpen) return false;
        if (key is Key.Escape or Key.Back)
        {
            CloseFriends();
            return true;
        }
        return false;
    }

    private bool HandleFriendsGamepad(GamepadButton button)
    {
        if (!FriendsOpen) return false;
        switch (button)
        {
            case GamepadButton.B:
                CloseFriends();
                return true;
            case GamepadButton.Start:
                _ = RefreshFriendsAsync();
                return true;
            case GamepadButton.Y:
                return true;
            default:
                return false;
        }
    }
}
