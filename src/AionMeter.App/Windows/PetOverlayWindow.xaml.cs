using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AionMeter.App.Services;
using PetTracker.Core;

namespace AionMeter.App.Windows;

/// <summary>
/// The pet being farmed, large (genus, name, a progress bar, souls), and one quiet line for the other pets around that
/// opens into a list grouped by genus. Clicking a pet in the list pins it as the focus; the pin button lets go again
/// (the focus then follows the last soul drop). × on a hovered row removes a pet until its mobs show up again.
/// Right-click for size, background, rows and lock; Ctrl + wheel resizes; drag to move.
/// </summary>
public partial class PetOverlayWindow : Window
{
    private const string CreditUrl = "https://metabot.gg/en/aion-2/mounts";
    // Content width 217 (panel 236 − padding 17 − border 2): the focus bar keeps 3 px from the edge, list bars 16 for the ×.
    private const double FocusBarWidth = 214;
    private const double RowBarWidth = 201;
    private const string PinGlyph = "", PinnedGlyph = "";
    private static UiText T => UiText.Current;

    private static readonly Dictionary<Genus, Color> GenusColors = new()
    {
        [Genus.Cogni] = Color.FromRgb(0x7C, 0xB8, 0xE8),
        [Genus.Fera] = Color.FromRgb(0xE0, 0x9A, 0x66),
        [Genus.Natura] = Color.FromRgb(0x82, 0xC9, 0x8E),
        [Genus.Varian] = Color.FromRgb(0xB8, 0x94, 0xE0),
        [Genus.Special] = Color.FromRgb(0xE6, 0xC2, 0x5C),
        [Genus.Unknown] = Color.FromRgb(0x9A, 0x9A, 0x9A),
    };

    private static readonly (string Label, double Opacity)[] Backgrounds = [("Off", 0), ("Low", 0.35), ("Medium", 0.55), ("High", 0.8)];

    private readonly MeterService _meter;
    private int _shownVersion = -1;
    private long _lastFullRefresh;
    private (int PetId, int Level, int Souls)? _shownFocus;
    private NearbyPet? _focus;

    /// <summary>One line of the nearby list: a genus header (<see cref="Header"/> set) or a pet.</summary>
    public sealed record Line(string Header, int PetId, string Name, string Count, string Souls, double FillWidth, Brush Accent,
        double RowOpacity, string? Tip, string? RemoveTip)
    {
        public Visibility HeaderVisibility => Header.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RowVisibility => Header.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        public Visibility BarVisibility => FillWidth >= 0 ? Visibility.Visible : Visibility.Collapsed;
        public Cursor? RowCursor => Header.Length > 0 ? null : Cursors.Hand;
    }

    public PetOverlayWindow(MeterService meter)
    {
        _meter = meter;
        InitializeComponent();
        Title = T.PetsTitle;
        var s = meter.Settings;
        Left = s.PetOverlayLeft;
        Top = s.PetOverlayTop;
        ApplySize();
        ApplyBackground();
        Controls.DragAnywhere.Attach(this, () => !_meter.Settings.PetOverlayLocked, SavePosition);
        MouseRightButtonUp += (_, e) =>
        {
            var row = (e.OriginalSource as FrameworkElement)?.DataContext as Line;
            ShowOptions(row is { Header.Length: 0 } ? row : null, FocusCard.IsMouseOver ? _focus : null);
            e.Handled = true;
        };
        PreviewMouseWheel += OnWheel;
        Loaded += (_, _) => Refresh(force: true);
    }

    /// <summary>Called by the app's UI clock; redraws when the pet data changed, and every few seconds for expiry.</summary>
    public void Refresh(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && _meter.Pets.Version == _shownVersion && now - _lastFullRefresh < 5_000) return;
        _shownVersion = _meter.Pets.Version;
        _lastFullRefresh = now;

        var snap = _meter.Pets.Snapshot(_meter.Settings.PetFocusPinned);
        ShowFocus(snap);
        ShowNearby(snap);
    }

    // ------------------------------------------------------------ the pet being farmed

    private void ShowFocus(PetOverlaySnapshot snap)
    {
        _focus = snap.Focus;
        if (snap.Focus is not { } f)
        {
            FocusCard.Visibility = Visibility.Collapsed;
            FocusHint.Visibility = Visibility.Visible;
            FocusHint.Text = snap.HasCollection ? T.PetsFocusHint : T.PetsNeedLogin;
            _shownFocus = null;
            return;
        }
        FocusCard.Visibility = Visibility.Visible;
        FocusHint.Visibility = Visibility.Collapsed;

        var accent = new SolidColorBrush(GenusColors[f.Pet.Genus]);
        FocusGenus.Text = f.Pet.Genus.ToString().ToUpperInvariant();
        FocusGenus.Foreground = accent;
        FocusName.Text = f.Pet.Name;
        FocusNearby.Text = f.NearbyCount > 0 ? string.Format(T.PetsInView, f.NearbyCount) : T.PetsNoneInView;
        FocusCard.ToolTip = string.Join(", ", f.NpcCodes.Select(_meter.Data.NpcName).Distinct());
        PinIcon.Text = snap.FocusPinned ? PinnedGlyph : PinGlyph;
        PinButton.Foreground = snap.FocusPinned ? accent : (Brush)FindResource("TextMute");
        PinButton.ToolTip = snap.FocusPinned ? T.PetsUnpinTip : T.PetsPinTip;
        FocusFill.Background = accent;

        var pr = f.Progress;
        if (!snap.HasCollection)
        {
            FocusLevel.Text = T.PetsNeedLogin;
            FocusSouls.Text = "";
            FocusFill.Width = 0;
        }
        else if (pr.Needed is not { } needed)
        {
            FocusLevel.Text = $"Lv{pr.Level} · {T.PetsMax}";
            FocusSouls.Text = "";
            FocusFill.Width = FocusBarWidth;
        }
        else
        {
            FocusLevel.Text = pr.Level == 0
                ? string.Format(T.PetsToUnlock, needed - pr.Souls)
                : string.Format(T.PetsToNext, needed - pr.Souls, pr.Level + 1);
            FocusSouls.Text = $"{pr.Souls} / {needed}";
            FocusFill.Width = FocusBarWidth * Math.Clamp(pr.Souls / (double)needed, 0, 1);
        }

        // A soul for the pet on screen: a short glow on the numbers.
        var key = (f.Pet.Id, pr.Level, pr.Souls);
        if (_shownFocus is { } shown && shown.PetId == key.Id && (shown.Souls, shown.Level) != (key.Souls, key.Level)) Glow();
        _shownFocus = key;
    }

    private void Glow()
    {
        var brush = new SolidColorBrush(((SolidColorBrush)FindResource("GoldBright")).Color);
        FocusSouls.Foreground = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(((SolidColorBrush)FindResource("Text")).Color, TimeSpan.FromSeconds(1.6)) { BeginTime = TimeSpan.FromSeconds(0.4) });
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        // Pinned: let go (follow the last soul drop). Following: keep this pet.
        SetFocus(_meter.Settings.PetFocusPinned is null ? _focus?.Pet.Id : null);
        e.Handled = true;
    }

    private void SetFocus(int? petId)
    {
        _meter.Settings.PetFocusPinned = petId;
        _meter.Settings.Save();
        Refresh(force: true);
    }

    // ------------------------------------------------------------ other pets around

    private void ShowNearby(PetOverlaySnapshot snap)
    {
        var all = snap.NearbyByGenus.SelectMany(g => g.Value).ToList();
        var open = _meter.Settings.PetDiscoveryOpen && all.Count > 0;
        DiscoveryText.Text = all.Count == 0
            ? T.PetsNoOthers
            : string.Format(all.Count == 1 ? T.PetsOthersOne : T.PetsOthers, all.Count) + (open ? "  ▴" : "  ▾");
        DiscoveryToggle.IsEnabled = all.Count > 0;
        Divider.Visibility = snap.Focus is null && !snap.HasCollection ? Visibility.Collapsed : Visibility.Visible;
        DiscoveryToggle.Visibility = Divider.Visibility;

        if (!open)
        {
            List.ItemsSource = null;
            List.Visibility = Visibility.Collapsed;
            MoreText.Visibility = Visibility.Collapsed;
            return;
        }
        var lines = new List<Line>();
        var maxRows = _meter.Settings.PetOverlayMaxRows;
        var shown = 0;
        foreach (var (genus, pets) in snap.NearbyByGenus)
        {
            if (shown >= maxRows) break;
            var accent = new SolidColorBrush(GenusColors[genus]);
            lines.Add(new Line(genus.ToString().ToUpperInvariant(), 0, "", "", "", -1, accent, 1, null, null));
            foreach (var p in pets.Take(maxRows - shown))
            {
                lines.Add(Row(p, accent, snap.HasCollection));
                shown++;
            }
        }
        List.ItemsSource = lines;
        List.Visibility = Visibility.Visible;
        MoreText.Text = string.Format(T.PetsMore, all.Count - shown);
        MoreText.Visibility = all.Count > shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private Line Row(NearbyPet p, Brush accent, bool known)
    {
        var pr = p.Progress;
        var names = string.Join(", ", p.NpcCodes.Select(_meter.Data.NpcName).Distinct());
        var tip = string.Format(T.PetsNearbyTip, p.NearbyCount, names) + "\n" + T.PetsClickToFocus;
        var remove = string.Format(T.PetsRemoveTip, p.Pet.Name);
        // Kept up after its last source died: faded, until more come into view or it times out.
        var opacity = p.NearbyCount == 0 ? 0.45 : 1.0;
        var count = p.NearbyCount > 1 ? $"×{p.NearbyCount}" : "";
        if (!known) return new Line("", p.Pet.Id, p.Pet.Name, count, "?", -1, accent, opacity, tip, remove);
        if (pr.Needed is not { } needed) return new Line("", p.Pet.Id, p.Pet.Name, count, T.PetsMax, -1, accent, opacity * 0.6, tip, remove);
        var souls = pr.Level == 0 ? $"{T.PetsNew} {pr.Souls}/{needed}" : $"Lv{pr.Level}  {pr.Souls}/{needed}";
        return new Line("", p.Pet.Id, p.Pet.Name, count, souls, RowBarWidth * Math.Clamp(pr.Souls / (double)needed, 0, 1), accent,
            opacity, tip, remove);
    }

    private void Discovery_Click(object sender, RoutedEventArgs e)
    {
        _meter.Settings.PetDiscoveryOpen = !_meter.Settings.PetDiscoveryOpen;
        _meter.Settings.Save();
        Refresh(force: true);
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Line { Header.Length: 0 } line) SetFocus(line.PetId);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Line line) return;
        _meter.Pets.Dismiss(line.PetId);
        Refresh(force: true);
        e.Handled = true;
    }

    // ------------------------------------------------------------ options

    private void ApplySize()
    {
        var scale = _meter.Settings.PetOverlaySize / 100.0;
        Scale.ScaleX = scale;
        Scale.ScaleY = scale;
    }

    private void ApplyBackground()
    {
        PanelFill.Opacity = _meter.Settings.PetOverlayOpacity;
        // With no background at all the hairline frame would float alone.
        Panel.BorderBrush = _meter.Settings.PetOverlayOpacity <= 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    private void SetSize(int percent)
    {
        _meter.Settings.PetOverlaySize = percent;
        _meter.Settings.Save();
        ApplySize();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var sizes = AppSettings.PetSizes;
        var at = Array.IndexOf(sizes, _meter.Settings.PetOverlaySize);
        if (at < 0) at = Array.IndexOf(sizes, 100);
        SetSize(sizes[Math.Clamp(at + (e.Delta > 0 ? 1 : -1), 0, sizes.Length - 1)]);
        e.Handled = true;
    }

    /// <param name="row">A nearby pet right-clicked on: focus or remove it.</param>
    /// <param name="focus">The focus card right-clicked on: pin or let go.</param>
    private void ShowOptions(Line? row, NearbyPet? focus)
    {
        var s = _meter.Settings;
        var menu = new ContextMenu { PlacementTarget = this };

        if (row is not null)
        {
            menu.Items.Add(Item(string.Format(T.PetsFocusThis, row.Name), () => SetFocus(row.PetId)));
            menu.Items.Add(Item(string.Format(T.PetsRemove, row.Name), () =>
            {
                _meter.Pets.Dismiss(row.PetId);
                Refresh(force: true);
            }));
            menu.Items.Add(new Separator());
        }
        else if (focus is not null)
        {
            menu.Items.Add(s.PetFocusPinned is null
                ? Item(string.Format(T.PetsFocusThis, focus.Pet.Name), () => SetFocus(focus.Pet.Id))
                : Item(T.PetsFollowLast, () => SetFocus(null)));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Choices(T.PetsSize, new[] { ("S", 80), ("M", 100), ("L", 125), ("XL", 150) },
            pct => s.PetOverlaySize == pct, SetSize));
        menu.Items.Add(Choices(T.PetsBackground, Backgrounds, o => Math.Abs(s.PetOverlayOpacity - o) < 0.01, o =>
        {
            s.PetOverlayOpacity = o;
            s.Save();
            ApplyBackground();
        }));
        menu.Items.Add(Choices(T.PetsRows, AppSettings.PetRowChoices.Select(n => (n.ToString(), n)), n => s.PetOverlayMaxRows == n, n =>
        {
            s.PetOverlayMaxRows = n;
            s.Save();
            Refresh(force: true);
        }));

        var locked = new MenuItem { Header = T.PetsLock, IsCheckable = true, IsChecked = s.PetOverlayLocked };
        locked.Click += (_, _) =>
        {
            s.PetOverlayLocked = !s.PetOverlayLocked;
            s.Save();
        };
        menu.Items.Add(locked);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(T.PetsCredit + "  ↗", OpenCredit));
        menu.Items.Add(Item(string.Format(T.PetsHide, s.HotkeyPets), () => Hidden?.Invoke()));
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    private static MenuItem Choices<TValue>(string header, IEnumerable<(string Label, TValue Value)> choices, Func<TValue, bool> isCurrent,
        Action<TValue> pick)
    {
        var parent = new MenuItem { Header = header };
        foreach (var (label, value) in choices)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = isCurrent(value) };
            item.Click += (_, _) => pick(value);
            parent.Items.Add(item);
        }
        return parent;
    }

    /// <summary>The user hid the overlay from its own menu (the app saves that and hides it).</summary>
    public event Action? Hidden;

    private static void OpenCredit()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CreditUrl) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("Could not open the pet data page", ex);
        }
    }

    private void SavePosition()
    {
        _meter.Settings.PetOverlayLeft = Left;
        _meter.Settings.PetOverlayTop = Top;
        _meter.Settings.Save();
    }
}
