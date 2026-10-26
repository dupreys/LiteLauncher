using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiteLauncher.Core;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

public partial class MainWindow : Window
{
    private enum AppView { Home, Library, Settings }

    private const int HomeTileCount = 6; // small tiles next to the hero tile

    private readonly string? _steamPath;

    // Register more stores here later (Epic, Xbox, GOG...).
    private readonly IReadOnlyList<IGameProvider> _providers;

    private readonly ObservableCollection<GameItem> _items = new();
    private readonly ICollectionView _view;
    private readonly Gamepad _gamepad = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    private AppView _current = AppView.Home;
    private IInputElement? _homeFocus;
    private WindowState _restoreState = WindowState.Normal;
    private bool _libraryCoversRequested;
    private AppView _searchReturn = AppView.Library; // where B goes back to when the search closes

    // ---- Live library updates ----
    private SteamLibraryWatcher? _watcher;
    private readonly DispatcherTimer _refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private readonly DispatcherTimer _runningPoll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, DateTimeOffset> _playedNow = new(); // games started this session, ahead of Steam's own file
    private List<GameItem> _shownRecent = new();
    private bool _refreshing;
    private bool _refreshAgain;
    private DateTime _lastRefresh = DateTime.MinValue;
    private int _runningAppId;
    private string? _nowPlaying;

    public MainWindow()
    {
        InitializeComponent();

        _steamPath = SteamLocator.FindSteamPath();
        _providers = new IGameProvider[] { new SteamProvider(_steamPath), new LiteLauncher.Core.Epic.EpicProvider() };

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => o is GameItem g && MatchesSearch(g);
        GameGrid.ItemsSource = _view;

        _gamepad.ButtonPressed += OnGamepad;
        _gamepad.ComboHeld += OnGamepadCombo;
        _gamepad.Start();

        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        InitStatusIcons();

        _refreshDebounce.Tick += async (_, _) =>
        {
            _refreshDebounce.Stop();
            await RefreshAsync();
        };
        _runningPoll.Tick += (_, _) => PollRunningGame();

        // Coming back from a game is the most common moment the order has changed.
        Activated += (_, _) =>
        {
            if ((DateTime.UtcNow - _lastRefresh).TotalSeconds > 5) ScheduleRefresh();
        };

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        };

        // While a menu/panel is open, focus must never leave it (e.g. pressing Down on the last cover).
        AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnPreviewFocusChange), true);

        // Focus is the single source of truth for "what is highlighted": it drives sounds and the backdrop.
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnAnyGotFocus), true);

        // The button hints change while a text field is focused, because A and B mean something different there.
        SearchBox.GotKeyboardFocus += (_, _) => SetHints(AppView.Library, searching: true);
        SearchBox.LostKeyboardFocus += (_, _) =>
        {
            if (_current == AppView.Library) SetHints(AppView.Library);
        };
        NameBox.GotKeyboardFocus += (_, _) =>
        {
            HintBar.Children.Clear();
            AddHint("A", "Keyboard");
            AddHint("B", "Back");
        };
        NameBox.LostKeyboardFocus += (_, _) =>
        {
            if (_current == AppView.Settings) SetHints(AppView.Settings);
        };

        Closed += (_, _) =>
        {
            _gamepad.Dispose();
            _clock.Stop();
            _runningPoll.Stop();
            _refreshDebounce.Stop();
            _watcher?.Dispose();
            VirtualKeyboard.Hide();
            _signInCts?.Cancel();
            if (_saveTimer.IsEnabled) SaveSettingsNow();
        };

        if (App.Settings.StartFullscreen || SystemInfo.IsPackaged() ||
            Environment.GetCommandLineArgs().Contains("--fullscreen", StringComparer.OrdinalIgnoreCase))
        {
            SetFullscreen(true);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitSettingsUi();
        ApplyBackdropSettings();
        UpdateClock();
        ShowView(AppView.Home, sound: false);

        await RefreshAsync();
        StartLiveUpdates();
        _ = LoadSavedAvatarAsync();
    }

    // ---- Library -------------------------------------------------------------------

    private async Task RefreshAsync()
    {
        // Several triggers can fire at once (file watcher, window activation, F5): run one at a time.
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;

                var games = new List<Game>();
                foreach (var provider in _providers.Where(p => p.IsAvailable))
                    games.AddRange(await provider.GetGamesAsync());

                // The user's names and pictures (Manage in the game menu) win over the store's.
                for (var i = 0; i < games.Count; i++)
                    games[i] = PlayTracker.Apply(GameOverrides.Apply(games[i], App.Settings.GameOverrides), App.Settings.PlayRecords);

                ApplyGames(games);
            }
            while (_refreshAgain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Steam may be rewriting its files at this very moment; the watcher triggers another pass when it settles.
        }
        finally
        {
            _refreshing = false;
            _lastRefresh = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Brings the on-screen list in line with <paramref name="games"/> without rebuilding it: existing tiles are
    /// reused and only moved, added or removed, so focus, scroll position and loaded covers survive.
    /// </summary>
    private void ApplyGames(List<Game> games)
    {
        var ordered = GameSorter.MostRecentFirst(games, _playedNow);

        var existing = new Dictionary<string, GameItem>();
        foreach (var current in _items) existing[GameSorter.Key(current.Game)] = current;

        var target = new List<GameItem>(ordered.Count);
        foreach (var game in ordered)
        {
            var reusable = existing.TryGetValue(GameSorter.Key(game), out var item)
                           && item.Game.Name == game.Name
                           && item.Game.CoverPath == game.CoverPath
                           && item.Game.BackdropPath == game.BackdropPath;
            target.Add(reusable ? item! : new GameItem(game));
        }

        var keep = new HashSet<GameItem>(target);
        for (var i = _items.Count - 1; i >= 0; i--)
            if (!keep.Contains(_items[i])) _items.RemoveAt(i);

        for (var i = 0; i < target.Count; i++)
        {
            if (i < _items.Count && ReferenceEquals(_items[i], target[i])) continue;

            var from = _items.IndexOf(target[i]);
            if (from >= 0) _items.Move(from, i);
            else _items.Insert(i, target[i]);
        }

        // Home only needs the first few covers; the full grid loads the rest when it is first opened.
        foreach (var item in _items.Take(HomeTileCount + 1)) item.EnsureCoverLoaded();
        if (_libraryCoversRequested) RequestLibraryCovers();

        RebuildHome();
        UpdateCount();
        UpdateAccountUi();
    }

    private void RebuildHome()
    {
        var hero = _items.FirstOrDefault();
        var recent = _items.Skip(1).Take(HomeTileCount).ToList();

        // Remember which game the user is on, so focus can follow it if its tile moves.
        var focusedTile = Keyboard.FocusedElement as ContentControl;
        var focusedGame = _current == AppView.Home ? focusedTile?.Content as GameItem : null;
        var focusWasOnRecent = focusedGame is not null && !ReferenceEquals(focusedTile, HeroTile);

        HeroTile.Content = hero;
        HeroTile.IsEnabled = hero is not null;

        // Replacing the list recreates its buttons, so only do it when the games actually changed.
        var recentChanged = !_shownRecent.SequenceEqual(recent);
        if (recentChanged)
        {
            RecentList.ItemsSource = recent;
            _shownRecent = recent;
        }

        EmptyText.Visibility = hero is null ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _providers.Any(p => p.IsAvailable)
            ? "No installed games found."
            : "Neither Steam nor the Epic Games Launcher was found on this PC.";

        if (_current != AppView.Home) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (focusWasOnRecent && recentChanged && focusedGame is not null)
            {
                RestoreFocusToGame(focusedGame);
            }
            else if (!HomeHasFocus())
            {
                _homeFocus = null;
                FocusView(AppView.Home); // first load, or the focused tile disappeared
            }

            // If the user is on the big tile, the backdrop must follow when a new game takes its place.
            if (Keyboard.FocusedElement == HeroTile) _ = SetBackdropAsync(hero);
        }));
    }

    private bool HomeHasFocus() =>
        Keyboard.FocusedElement is FrameworkElement { IsLoaded: true } element && HomeView.IsAncestorOf(element);

    private void RestoreFocusToGame(GameItem game)
    {
        var key = GameSorter.Key(game.Game);

        if (_items.FirstOrDefault() is { } hero && GameSorter.Key(hero.Game) == key && HeroTile.IsEnabled)
        {
            Keyboard.Focus(HeroTile);
            return;
        }

        var index = _shownRecent.FindIndex(i => GameSorter.Key(i.Game) == key);
        if (index >= 0 &&
            RecentList.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container &&
            FindDescendant<Button>(container) is { } button)
        {
            Keyboard.Focus(button);
            return;
        }

        FocusView(AppView.Home);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }

        return null;
    }

    // ---- Live updates ------------------------------------------------------------------

    private void StartLiveUpdates()
    {
        if (_steamPath is not null)
        {
            // Steam rewrites a game's manifest when it starts, stops, updates, installs or uninstalls.
            _watcher = new SteamLibraryWatcher(_steamPath);
            _watcher.Changed += () => Dispatcher.BeginInvoke(new Action(ScheduleRefresh));
        }

        StartEpicTracking();
        _runningPoll.Start();
        PollRunningGame();
    }

    /// <summary>Several file events arrive in a burst; wait for it to settle, then refresh once.</summary>
    private void ScheduleRefresh()
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    /// <summary>
    /// Steam publishes the running game in the registry, so the highlighted game follows whatever you start,
    /// even from Steam itself, without waiting for the manifest to be rewritten.
    /// </summary>
    private void PollRunningGame()
    {
        var id = SteamRunningApp.GetRunningAppId();
        UpdateLaunchOverlay(id);
        PollEpicGames();
        if (id == _runningAppId) return;

        var previous = _runningAppId;
        _runningAppId = id;

        if (id > 0)
        {
            var key = $"{SteamProvider.ProviderName}:{id}";
            _playedNow[key] = DateTimeOffset.Now;

            var item = _items.FirstOrDefault(i => GameSorter.Key(i.Game) == key);
            _nowPlaying = item?.Name;
            if (item is not null) ShowToast($"Now playing {item.Name}");
            ScheduleRefresh();
        }
        else if (previous > 0)
        {
            _nowPlaying = null;
            ScheduleRefresh(); // Steam writes the final play time when the game closes
        }

        UpdateAccountUi();
    }

    private void RequestLibraryCovers()
    {
        _libraryCoversRequested = true;
        foreach (var item in _items) item.EnsureCoverLoaded();
    }

    private bool MatchesSearch(GameItem item)
    {
        var query = SearchBox?.Text;
        return string.IsNullOrWhiteSpace(query) ||
               item.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_view is null) return; // can fire during InitializeComponent
        _view.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var shown = _view.Cast<object>().Count();
        var query = SearchBox.Text.Trim();
        var filtering = query.Length > 0;

        CountText.Text = filtering ? $"{shown} of {_items.Count} games" : $"{_items.Count} games";

        NoResultsText.Visibility = filtering && shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResultsText.Text = $"No installed game matches \"{query}\".";
    }

    /// <summary>Moves focus from the search box to the first result.</summary>
    private void FocusFirstResult()
    {
        GameGrid.UpdateLayout(); // make sure the filtered cards exist before asking for them
        if (FirstCard() is not { } card) return;

        GameGrid.SelectedIndex = 0;
        Keyboard.Focus(card);
    }

    /// <summary>On the library screen, typing anywhere starts a search.</summary>
    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_current != AppView.Library || DetailsOpen || FriendsOpen || LaunchersOpen || Keyboard.FocusedElement is TextBox) return;
        if (e.Text.Length == 0 || char.IsControl(e.Text[0])) return;
        if (SearchBox.Text.Length == 0 && char.IsWhiteSpace(e.Text[0])) return;

        SearchBox.Focus();
        SearchBox.AppendText(e.Text);
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    // ---- Launching -------------------------------------------------------------------

    private void OnGameTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is ContentControl { Content: GameItem item }) LaunchGame(item);
    }

    private void OnCardDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();

    private void OnCardGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Keyboard/gamepad focus drives selection, so the highlighted card is always the one that launches.
        if (sender is ListBoxItem item) item.IsSelected = true;
    }

    private void LaunchSelected()
    {
        if (GameGrid.SelectedItem is GameItem item) LaunchGame(item);
    }

    private void LaunchGame(GameItem item) => ShowLaunchOverlay(item);

    /// <summary>Hands the game to its provider. Returns false when it could not be started.</summary>
    private bool StartGameNow(GameItem item)
    {
        var provider = _providers.FirstOrDefault(p => p.Name == item.Game.Source);
        if (provider is null) return false;

        App.Sounds.Play(SoundKind.Launch);
        try
        {
            provider.Launch(item.Game);
            _playedNow[GameSorter.Key(item.Game)] = DateTimeOffset.Now; // becomes the featured game immediately
            ScheduleRefresh();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException)
        {
            ShowToast($"Could not launch {item.Name}");
            return false;
        }
    }

    private bool OpenSteamUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            ShowToast("Steam is not installed on this PC.");
            return false;
        }
    }

    private void OnOpenSteam(object sender, RoutedEventArgs e) => OpenSteamUrl("steam://open/main");

    private void OnOpenStore(object sender, RoutedEventArgs e) => OpenSteamUrl("steam://store");

    private void OnOpenCommunity(object sender, RoutedEventArgs e) => OpenSteamUrl("steam://url/CommunityHome");

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    // ---- Screens -------------------------------------------------------------------

    private void OnOpenLibrary(object sender, RoutedEventArgs e) => ShowView(AppView.Library);

    private void OnOpenSettings(object sender, RoutedEventArgs e) => ShowView(AppView.Settings);

    private void OnFriendsTileClick(object sender, RoutedEventArgs e) => OpenFriends();

    private void ShowView(AppView view, bool sound = true, UIElement? focusOverride = null)
    {
        VirtualKeyboard.Hide();
        var previous = _current;
        if (previous == AppView.Home && view != AppView.Home) _homeFocus = Keyboard.FocusedElement;
        if (previous == AppView.Settings && view != AppView.Settings && _saveTimer.IsEnabled) SaveSettingsNow();

        _current = view;

        HomeView.Visibility = view == AppView.Home ? Visibility.Visible : Visibility.Collapsed;
        LibraryView.Visibility = view == AppView.Library ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = view == AppView.Settings ? Visibility.Visible : Visibility.Collapsed;

        UIElement shown = view switch
        {
            AppView.Home => HomeView,
            AppView.Library => LibraryView,
            _ => SettingsView,
        };
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        if (view == AppView.Library) RequestLibraryCovers();
        SetHints(view);

        if (sound) App.Sounds.Play(view == AppView.Home ? SoundKind.Back : SoundKind.Select);
        App.Sounds.Silence(TimeSpan.FromMilliseconds(250)); // the focus move that follows shouldn't also blip

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FocusView(view, focusOverride)));
    }

    private void FocusView(AppView view, UIElement? focusOverride = null)
    {
        UIElement target;

        if (focusOverride is not null)
        {
            target = focusOverride;
        }
        else
        {
            switch (view)
            {
                case AppView.Home:
                    if (_homeFocus is FrameworkElement { IsLoaded: true, IsVisible: true, Focusable: true } remembered)
                        target = remembered;
                    else
                        target = HeroTile.IsEnabled ? HeroTile : LibraryTile;
                    break;
                case AppView.Library:
                    target = (UIElement?)SelectedCard() ?? (UIElement?)FirstCard() ?? SearchBox;
                    break;
                default:
                    target = MenuSound;
                    break;
            }
        }

        Keyboard.Focus(target);
    }

    private ListBoxItem? FirstCard() =>
        GameGrid.Items.Count > 0 ? GameGrid.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem : null;

    private ListBoxItem? SelectedCard() =>
        GameGrid.SelectedIndex >= 0
            ? GameGrid.ItemContainerGenerator.ContainerFromIndex(GameGrid.SelectedIndex) as ListBoxItem
            : null;

    /// <summary>Returns true when it navigated or cleared something.</summary>
    private bool GoBack()
    {
        // B while searching: close the search (and its keyboard) and return to where you were.
        if (ReferenceEquals(Keyboard.FocusedElement, SearchBox))
        {
            CloseSearch();
            return true;
        }

        // B with the keyboard open somewhere else (e.g. while typing a profile name): just close the keyboard.
        if (VirtualKeyboard.IsVisible)
        {
            VirtualKeyboard.Hide();
            return true;
        }

        if (_current == AppView.Home) return false;

        if (_current == AppView.Library && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            App.Sounds.Play(SoundKind.Back);
            FocusView(AppView.Library);
            return true;
        }

        ShowView(AppView.Home);
        return true;
    }

    /// <summary>Opens the search field. With a controller (Y) the Windows on-screen keyboard opens too.</summary>
    private void OpenSearch(bool virtualKeyboard)
    {
        if (_current != AppView.Library)
        {
            _searchReturn = _current;
            ShowView(AppView.Library, focusOverride: SearchBox);
        }
        else
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        if (virtualKeyboard) _ = VirtualKeyboard.ShowAsync();
    }

    /// <summary>A (or Enter) in the search field: accept what was typed and jump to the results.</summary>
    private void ConfirmSearch()
    {
        VirtualKeyboard.Hide();

        if (GameGrid.Items.Count == 0)
        {
            ShowToast("No installed game matches your search.");
            return;
        }

        App.Sounds.Play(SoundKind.Select);
        FocusFirstResult();
    }

    /// <summary>B (or Esc) in the search field: drop the search, close the keyboard and go back.</summary>
    private void CloseSearch()
    {
        VirtualKeyboard.Hide();
        SearchBox.Clear();
        App.Sounds.Play(SoundKind.Back);

        if (_searchReturn != AppView.Library)
        {
            var target = _searchReturn;
            _searchReturn = AppView.Library;
            ShowView(target, sound: false);
            return;
        }

        GameGrid.UpdateLayout(); // the full list was just restored; make sure its cards exist
        FocusView(AppView.Library);
    }

    /// <summary>A on the profile name field: first press opens the keyboard, the next one closes it and moves on.</summary>
    private void ToggleNameKeyboard()
    {
        if (VirtualKeyboard.IsVisible)
        {
            VirtualKeyboard.Hide();
            Move(FocusNavigationDirection.Down);
        }
        else
        {
            _ = VirtualKeyboard.ShowAsync();
        }
    }

    // ---- Button hints, clock, toast ------------------------------------------------------

    private void SetHints(AppView view, bool searching = false)
    {
        HintBar.Children.Clear();

        if (searching)
        {
            AddHint("A", "Confirm");
            AddHint("B", "Close");
            return;
        }

        switch (view)
        {
            case AppView.Home:
                AddHint("A", "Select");
                AddHint("Y", "Search");
                break;
            case AppView.Library:
                AddHint("A", "Play");
                AddHint("Y", "Search");
                AddHint("B", "Back");
                break;
            default:
                AddHint("A", "Select");
                AddHint("B", "Back");
                break;
        }
    }

    private void AddHint(string key, string label)
    {
        var colour = key switch { "A" => "#6DC04B", "B" => "#E5484D", "X" => "#4C9AFF", _ => "#F5C518" };

        HintBar.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(ThemeManager.Parse(colour, Colors.Gray)),
            Child = new TextBlock
            {
                Text = key,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = key == "Y" ? Brushes.Black : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        HintBar.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 16,
            Margin = new Thickness(0, 0, 22, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("t");

        if (SystemInfo.GetBattery() is { } battery)
        {
            BatteryPanel.Visibility = Visibility.Visible;
            BatteryFill.Width = Math.Max(1, 20 * battery.Percent / 100.0);
        }
        else
        {
            BatteryPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---- Backdrop (game art behind the UI) ----------------------------------------------

    private readonly Dictionary<string, ImageSource> _backdropCache = new();
    private readonly Queue<string> _backdropOrder = new();
    private GameItem? _lastBackdropItem;
    private string? _currentBackdropKey;
    private int _backdropToken;
    private bool _backdropFlip;

    private void ApplyBackdropSettings()
    {
        var settings = App.Settings;
        DimOverlay.Opacity = settings.ShowBackdrop ? settings.BackdropDim : 0;
        ConfigureBackdrop(BackdropA, BackdropA.Source); // the fit/fill choice may have changed
        ConfigureBackdrop(BackdropB, BackdropB.Source);
        ConfigureBackdrop(OverlayBackdrop, OverlayBackdrop.Source);

        if (!settings.ShowBackdrop)
        {
            _currentBackdropKey = null;
            FadeBackdrop(null);
        }
        else if (_currentBackdropKey is null && _lastBackdropItem is not null)
        {
            _ = SetBackdropAsync(_lastBackdropItem);
        }
    }

    private async Task SetBackdropAsync(GameItem? item)
    {
        _lastBackdropItem = item;
        if (!App.Settings.ShowBackdrop) return;

        var path = item?.Game.BackdropPath ?? item?.Game.CoverPath;
        if (path == _currentBackdropKey) return;
        _currentBackdropKey = path;

        var token = ++_backdropToken;
        ImageSource? image = null;

        if (path is not null)
        {
            if (!_backdropCache.TryGetValue(path, out image))
            {
                await Task.Delay(120); // skip the load while the user is still scrolling past
                if (token != _backdropToken) return;
                image = await LoadBackdropAsync(path);
            }
        }

        if (token != _backdropToken) return; // a newer focus change superseded this one
        FadeBackdrop(image);
    }

    // Wide art (hero banners) is shown whole: full width, pinned to the top, fading into the background colour
    // at the bottom. Tall art (plain covers) would end up a tiny strip that way, so it still fills the screen.
    private const double FitMinAspect = 1.4;

    private static readonly Brush BackdropFadeMask = CreateBackdropFadeMask();

    private static Brush CreateBackdropFadeMask()
    {
        var mask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, 0.7),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        mask.Freeze();
        return mask;
    }

    private static void ConfigureBackdrop(System.Windows.Controls.Image target, ImageSource? source)
    {
        var fit = App.Settings.FitBackdrop
                  && source is System.Windows.Media.Imaging.BitmapSource { PixelHeight: > 0 } bitmap
                  && (double)bitmap.PixelWidth / bitmap.PixelHeight >= FitMinAspect;

        target.Stretch = fit ? Stretch.Uniform : Stretch.UniformToFill;
        target.VerticalAlignment = fit ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        target.OpacityMask = fit ? BackdropFadeMask : null;
    }

    private async Task<ImageSource?> LoadBackdropAsync(string path)
    {
        var image = await ImageLoader.LoadAsync(path, decodeWidth: 1280);
        if (image is null) return null;

        _backdropCache[path] = image;
        _backdropOrder.Enqueue(path);
        while (_backdropOrder.Count > 6) _backdropCache.Remove(_backdropOrder.Dequeue()); // keep RAM bounded
        return image;
    }

    private void FadeBackdrop(ImageSource? image)
    {
        // Two stacked images alternate: the hidden one fades in while the visible one fades out.
        var incoming = _backdropFlip ? BackdropA : BackdropB;
        var outgoing = _backdropFlip ? BackdropB : BackdropA;
        _backdropFlip = !_backdropFlip;

        ConfigureBackdrop(incoming, image);
        incoming.Source = image;
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(image is null ? 0 : 1, TimeSpan.FromMilliseconds(350)));
        outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)));
    }

    private void OnAnyGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is ContentControl { Content: GameItem item }) _ = SetBackdropAsync(item);
        if (e.NewFocus is not TextBox) App.Sounds.Play(SoundKind.Move);
    }

    // ---- Input -------------------------------------------------------------------------

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandlePowerKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (HandleQuickKey(e.Key) || HandleTopBarKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (HandleLaunchersKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (HandleFriendsKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (HandleDetailsKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Apps && GameFromElement(Keyboard.FocusedElement) is { } menuItem)
        {
            OpenGameDetails(menuItem);
            e.Handled = true;
            return;
        }

        if (HandleOverlayKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        var focused = Keyboard.FocusedElement;

        switch (e.Key)
        {
            case Key.Enter when focused is ListBoxItem:
                LaunchSelected();
                e.Handled = true;
                break;
            case Key.Enter when ReferenceEquals(focused, SearchBox):
                ConfirmSearch();
                e.Handled = true;
                break;
            case Key.Enter when ReferenceEquals(focused, NameBox):
                Move(FocusNavigationDirection.Down);
                e.Handled = true;
                break;
            case Key.F5:
                await RefreshAsync();
                e.Handled = true;
                break;
            case Key.F11:
                SetFullscreen(WindowStyle != WindowStyle.None);
                e.Handled = true;
                break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                OpenSearch(virtualKeyboard: false);
                e.Handled = true;
                break;
            case Key.Escape:
                if (!GoBack() && WindowStyle == WindowStyle.None) SetFullscreen(false);
                e.Handled = true;
                break;
            case Key.Up when TryFocusTopBar():
                e.Handled = true; // nothing above: go up to the top bar
                break;
            case Key.Up or Key.Down when focused is Slider:
                // A slider would swallow Up/Down as value changes; keep them for navigation.
                Move(e.Key == Key.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down);
                e.Handled = true;
                break;
        }
    }

    private void OnGamepad(GamepadButton button)
    {
        if (!IsActive) return; // ignore the pad while a game is in front
        if (HandleOverlayGamepad(button)) return;
        if (HandlePowerGamepad(button)) return;
        if (HandleQuickGamepad(button)) return;
        if (HandleTopBarGamepad(button)) return;
        if (HandleDetailsGamepad(button)) return;
        if (HandleFriendsGamepad(button)) return;
        if (HandleLaunchersGamepad(button)) return;
        if (button == GamepadButton.A && Keyboard.FocusedElement is TextBox box && (box == SteamApiKeyBox || box == GridDbKeyBox))
        {
            ToggleNameKeyboard();
            return;
        }
        if (button == GamepadButton.Start && GameFromElement(Keyboard.FocusedElement) is { } menuItem)
        {
            OpenGameDetails(menuItem);
            return;
        }

        switch (button)
        {
            case GamepadButton.Up:
                if (!TryFocusTopBar()) Move(FocusNavigationDirection.Up);
                break;
            case GamepadButton.Down: Move(FocusNavigationDirection.Down); break;
            case GamepadButton.Left:
                if (Keyboard.FocusedElement is Slider left) AdjustSlider(left, -1);
                else Move(FocusNavigationDirection.Left);
                break;
            case GamepadButton.Right:
                if (Keyboard.FocusedElement is Slider right) AdjustSlider(right, +1);
                else Move(FocusNavigationDirection.Right);
                break;
            case GamepadButton.A:
                if (ReferenceEquals(Keyboard.FocusedElement, SearchBox)) ConfirmSearch();
                else if (ReferenceEquals(Keyboard.FocusedElement, NameBox)) ToggleNameKeyboard();
                else ActivateFocused();
                break;
            case GamepadButton.B: GoBack(); break;
            case GamepadButton.Y: OpenSearch(virtualKeyboard: true); break;
            case GamepadButton.Start: _ = RefreshAsync(); break;
        }
    }

    private static void AdjustSlider(Slider slider, int direction) =>
        slider.Value = Math.Clamp(slider.Value + direction * slider.SmallChange, slider.Minimum, slider.Maximum);

    /// <summary>What pressing A does: launch a card, or click/toggle whatever control has focus.</summary>
    private void ActivateFocused()
    {
        switch (Keyboard.FocusedElement)
        {
            case ListBoxItem:
                LaunchSelected();
                break;
            case UIElement element:
                var peer = UIElementAutomationPeer.CreatePeerForElement(element);
                if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
                else if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
                break;
        }
    }

    /// <summary>The panel that currently owns focus, innermost first; null when only the main screens are shown.</summary>
    private UIElement? ActiveModal() =>
        PowerOpen ? PowerPanel :
        QuickOpen ? QuickPanel :
        GridDbOpen ? GridDbPanel :
        DetailsOpen ? GameDetails :
        FriendsOpen ? FriendsPanel :
        LaunchersOpen ? LaunchersPanel :
        _overlayPhase != OverlayPhase.None ? LaunchOverlay : // not Visibility: the fade-out must not trap focus
        null;

    private void OnPreviewFocusChange(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ActiveModal() is not { } modal) return;
        if (e.NewFocus is DependencyObject target && (ReferenceEquals(target, modal) || modal.IsAncestorOf(target))) return;
        if (e.NewFocus is null || e.NewFocus is Window) return; // window losing focus to a game or a dialog
        e.Handled = true; // cancel: stay on the current item at the edge
    }

    private void Move(FocusNavigationDirection direction)
    {
        if (ReferenceEquals(Keyboard.FocusedElement, SearchBox) || ReferenceEquals(Keyboard.FocusedElement, NameBox))
            VirtualKeyboard.Hide(); // leaving a text field closes the on-screen keyboard

        if (Keyboard.FocusedElement is not UIElement focused)
        {
            FocusView(_current);
            return;
        }

        focused.MoveFocus(new TraversalRequest(direction));
    }

    private void SetFullscreen(bool on)
    {
        if (on)
        {
            _restoreState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _restoreState;
        }
    }
}
