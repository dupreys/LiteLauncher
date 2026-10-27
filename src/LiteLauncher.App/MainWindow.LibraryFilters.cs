using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiteLauncher.Core;
using LiteLauncher.Core.Epic;
using LiteLauncher.Core.Steam;

namespace LiteLauncher.App;

// "My games & apps": filter tabs (Installed / Steam / Epic / Favourites) switched with LB and RB, and favourites (X).
public partial class MainWindow
{
    private enum LibraryFilter { Installed, Recent, Steam, Epic, Favorites }

    private enum LibrarySort { Recent, Name, Size }

    private LibrarySort _sort = Enum.TryParse<LibrarySort>(App.Settings.LibrarySort, out var saved) ? saved : LibrarySort.Recent;

    private static readonly (LibraryFilter Filter, string Label)[] FilterTabs =
    {
        (LibraryFilter.Installed, "Installed"),
        (LibraryFilter.Recent, "Recent"),
        (LibraryFilter.Steam, "Steam"),
        (LibraryFilter.Epic, "Epic Games"),
        (LibraryFilter.Favorites, "★ Favourites"),
    };

    private LibraryFilter _filter = LibraryFilter.Installed;

    private bool MatchesFilter(GameItem item) => _filter switch
    {
        LibraryFilter.Recent => item.Game.LastPlayed is not null,
        LibraryFilter.Steam => item.Game.Source == SteamProvider.ProviderName,
        LibraryFilter.Epic => item.Game.Source == EpicProvider.ProviderName,
        LibraryFilter.Favorites => item.IsFavorite,
        _ => true,
    };

    private void BuildFilterBar()
    {
        FilterBar.Children.Clear();
        FilterBar.Children.Add(FilterHint("LB"));

        foreach (var (filter, label) in FilterTabs)
        {
            var text = new TextBlock { Text = Loc.T(label), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border
            {
                Tag = filter,
                Child = text,
                Padding = new Thickness(16, 6, 16, 7),
                Margin = new Thickness(6, 0, 0, 0),
                CornerRadius = new CornerRadius(0),
                Cursor = Cursors.Hand,
            };
            chip.MouseLeftButtonUp += (_, _) => SetFilter(filter);
            FilterBar.Children.Add(chip);
        }

        var rb = FilterHint("RB");
        rb.Margin = new Thickness(12, 0, 0, 0);
        FilterBar.Children.Add(rb);

        // Sort: a chip at the end of the row (RT on the controller, F2 on the keyboard, or a click)
        var sort = FilterHint("RT");
        sort.Margin = new Thickness(36, 0, 0, 0);
        FilterBar.Children.Add(sort);
        var sortText = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var sortChip = new Border
        {
            Tag = SortChipTag,
            Child = sortText,
            Padding = new Thickness(16, 6, 16, 7),
            Margin = new Thickness(6, 0, 0, 0),
            CornerRadius = new CornerRadius(0),
            Cursor = Cursors.Hand,
        };
        sortChip.MouseLeftButtonUp += (_, _) => CycleSort();
        FilterBar.Children.Add(sortChip);

        StyleFilterChips();
        ApplySort();
    }

    private const string SortChipTag = "sort";

    private string SortLabel() => _sort switch
    {
        LibrarySort.Name => Loc.T("Sort: A–Z"),
        LibrarySort.Size => Loc.T("Sort: Size"),
        _ => Loc.T("Sort: Recent"),
    };

    private void CycleSort()
    {
        _sort = (LibrarySort)(((int)_sort + 1) % Enum.GetValues<LibrarySort>().Length);
        App.Settings.LibrarySort = _sort.ToString();
        QueueSave();
        App.Sounds.Play(SoundKind.Move);
        ApplySort();
        ShowToast(SortLabel());
        RefreshLibraryView();
    }

    private void ApplySort()
    {
        _view.SortDescriptions.Clear();
        switch (_sort)
        {
            case LibrarySort.Name:
                _view.SortDescriptions.Add(new SortDescription(nameof(GameItem.Name), ListSortDirection.Ascending));
                break;
            case LibrarySort.Size:
                _view.SortDescriptions.Add(new SortDescription(nameof(GameItem.SizeOnDisk), ListSortDirection.Descending));
                break;
            // Recent: the collection itself is kept most-recent-first
        }

        if (FilterBar.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, SortChipTag))?.Child is TextBlock label)
            label.Text = SortLabel();
    }

    private static Border FilterHint(string label)
    {
        var border = new Border
        {
            Padding = new Thickness(7, 2, 7, 2),
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.Bold },
        };
        border.SetResourceReference(Border.BorderBrushProperty, "TextMuted");
        ((TextBlock)border.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return border;
    }

    private void StyleFilterChips()
    {
        foreach (var chip in FilterBar.Children.OfType<Border>().Where(b => b.Tag is LibraryFilter or SortChipTag))
        {
            var active = chip.Tag is LibraryFilter f && f == _filter;
            var text = (TextBlock)chip.Child;
            if (active)
            {
                chip.SetResourceReference(Border.BackgroundProperty, "Accent");
                text.Foreground = Brushes.White;
            }
            else
            {
                chip.SetResourceReference(Border.BackgroundProperty, "Surface");
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            }
        }
    }

    private void SetFilter(LibraryFilter filter)
    {
        if (filter == _filter) return;
        _filter = filter;
        StyleFilterChips();
        App.Sounds.Play(SoundKind.Move);
        RefreshLibraryView();
    }

    private void CycleFilter(int step)
    {
        var next = ((int)_filter + step + FilterTabs.Length) % FilterTabs.Length;
        SetFilter((LibraryFilter)next);
    }

    /// <summary>Re-applies filter and search; if the focused card went away, focus the first one that is left.</summary>
    private void RefreshLibraryView()
    {
        var hadCard = GameFromElement(Keyboard.FocusedElement) is not null;
        _view.Refresh();
        UpdateCount();
        if (hadCard || Keyboard.FocusedElement is null or Window) FocusFirstResult();
    }

    private bool LibraryInputAllowed() => _current == AppView.Library && ActiveModal() is null;

    private bool HandleLibraryGamepad(GamepadButton button)
    {
        switch (button)
        {
            case GamepadButton.LeftShoulder when LibraryInputAllowed():
                CycleFilter(-1);
                return true;
            case GamepadButton.RightShoulder when LibraryInputAllowed():
                CycleFilter(+1);
                return true;
            case GamepadButton.RightTrigger when LibraryInputAllowed():
                CycleSort();
                return true;
            case GamepadButton.X when ActiveModal() is null && GameFromElement(Keyboard.FocusedElement) is { } item:
                ToggleFavorite(item);
                return true;
            default:
                return false;
        }
    }

    private bool HandleLibraryKey(Key key)
    {
        if (Keyboard.FocusedElement is TextBox) return false;

        switch (key)
        {
            case Key.PageUp when LibraryInputAllowed():
                CycleFilter(-1);
                return true;
            case Key.PageDown when LibraryInputAllowed():
                CycleFilter(+1);
                return true;
            case Key.F2 when LibraryInputAllowed():
                CycleSort();
                return true;
            case Key.F when Keyboard.Modifiers == ModifierKeys.None && ActiveModal() is null
                            && GameFromElement(Keyboard.FocusedElement) is { } item:
                ToggleFavorite(item);
                return true;
            default:
                return false;
        }
    }

    private void ToggleFavorite(GameItem item)
    {
        var key = GameSorter.Key(item.Game);
        var favourites = App.Settings.Favorites;

        if (favourites.Remove(key))
        {
            item.IsFavorite = false;
            ShowToast(Loc.F("{0} removed from favourites", item.Name));
        }
        else
        {
            favourites.Add(key);
            item.IsFavorite = true;
            ShowToast(Loc.F("{0} added to favourites", item.Name));
        }

        QueueSave();
        App.Sounds.Play(SoundKind.Select);
        if (_filter == LibraryFilter.Favorites) RefreshLibraryView();
    }
}
