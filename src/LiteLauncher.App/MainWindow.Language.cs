using System.Windows;
using System.Windows.Controls;
using LiteLauncher.Core;

namespace LiteLauncher.App;

// Interface language: translates the texts written in XAML (captured once, in English) and refreshes the dynamic ones.
public partial class MainWindow
{
    private sealed class TextSlot
    {
        public required DependencyObject Owner;
        public required DependencyProperty Property;
        public required string English;
        public string? Applied;
        public bool IsToolTip;
    }

    private readonly List<TextSlot> _textSlots = new();

    /// <summary>Remembers every plain-text Text/Content/ToolTip written in XAML. Call once, right after InitializeComponent.</summary>
    private void CaptureTexts()
    {
        void Visit(DependencyObject node)
        {
            switch (node)
            {
                case TextBlock tb when !string.IsNullOrWhiteSpace(tb.Text):
                    _textSlots.Add(new TextSlot { Owner = tb, Property = TextBlock.TextProperty, English = tb.Text, Applied = tb.Text });
                    break;
                case ContentControl cc when cc.Content is string s && !string.IsNullOrWhiteSpace(s):
                    _textSlots.Add(new TextSlot { Owner = cc, Property = ContentControl.ContentProperty, English = s, Applied = s });
                    break;
            }

            if (node is FrameworkElement fe && fe.ToolTip is string tip && !string.IsNullOrWhiteSpace(tip))
                _textSlots.Add(new TextSlot { Owner = fe, Property = FrameworkElement.ToolTipProperty, English = tip, Applied = tip, IsToolTip = true });

            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject d) Visit(d);
        }

        Visit(this);
    }

    /// <summary>Applies the current language to the captured texts, leaving alone any text that code has changed meanwhile.</summary>
    private void ApplyTexts()
    {
        foreach (var slot in _textSlots)
        {
            var current = slot.Owner.GetValue(slot.Property) as string;
            if (current != slot.Applied) continue; // code wrote its own text here (and translates it itself)
            var translated = Loc.T(slot.English);
            slot.Owner.SetValue(slot.Property, translated);
            slot.Applied = translated;
        }
    }

    private void InitLanguage()
    {
        CaptureTexts();
        ApplyTexts();
        UpdateLanguageLabel();
        Loc.Changed += () => Dispatcher.Invoke(OnLanguageChanged);
    }

    private void UpdateLanguageLabel() =>
        LanguageLabel.Text = Loc.F("Language: {0}", Loc.NameOf(Loc.Language));

    private void OnLanguageChanged()
    {
        ApplyTexts();
        UpdateLanguageLabel();
        UpdateBackdropStyleLabel();
        UpdateHomeStyleLabel();
        UpdateHomeGameInfo(HeroTile.Content as GameItem);
        BuildFilterBar();
        UpdateCount();
        UpdateAccountUi();
        UpdateFriendsTile();
        UpdateStatusIcons();
        ShowUpdateState(toast: false, manual: false);
        if (FriendsOpen) UpdateFriendsPanel();
        SetHints(_current, SearchBox.IsKeyboardFocused);
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        var next = Loc.Next(Loc.Language);
        Loc.Set(next);
        App.Settings.Language = next;
        QueueSave();
        App.Sounds.Play(SoundKind.Select);
    }
}
