using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionMeter.App.Services;
using PetTracker.Core;

namespace AionMeter.App.Windows;

/// <summary>
/// Small always-on-top list of the pet soul sources around the character, grouped by genus, with level and souls.
/// Nothing else: right-click for size, rows, lock, hide; Ctrl + wheel resizes; drag to move.
/// </summary>
public partial class PetOverlayWindow : Window
{
    private const string CreditUrl = "https://metabot.gg/en/aion-2/mounts";
    private const double BarWidth = 86;
    private static UiText T => UiText.Current;

    private static readonly Dictionary<Genus, Color> GenusColors = new()
    {
        [Genus.Cogni] = Color.FromRgb(0x7C, 0xC4, 0xFF),
        [Genus.Fera] = Color.FromRgb(0xF0, 0x9A, 0x5A),
        [Genus.Natura] = Color.FromRgb(0x7D, 0xD8, 0x8A),
        [Genus.Varian] = Color.FromRgb(0xC7, 0x8C, 0xF5),
        [Genus.Special] = Color.FromRgb(0xF7, 0xC9, 0x48),
        [Genus.Unknown] = Color.FromRgb(0xA0, 0xA0, 0xA0),
    };

    private readonly MeterService _meter;
    private int _shownVersion = -1;
    private long _lastFullRefresh;

    /// <summary>One line of the list: a genus header or a pet.</summary>
    public sealed record Line(
        string Header, string Name, string Count, string Level, string Souls, double FillWidth, Brush Accent, Brush NameBrush, string? Tip)
    {
        public Visibility HeaderVisibility => Header.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RowVisibility => Header.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
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
        Controls.DragAnywhere.Attach(this, () => !_meter.Settings.PetOverlayLocked, SavePosition);
        MouseRightButtonUp += (_, e) =>
        {
            ShowOptions();
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

        var snap = _meter.Pets.Snapshot();
        var lines = new List<Line>();
        var maxRows = _meter.Settings.PetOverlayMaxRows;
        var total = snap.NearbyByGenus.Values.Sum(p => p.Count);
        var shown = 0;
        foreach (var (genus, pets) in snap.NearbyByGenus)
        {
            if (shown >= maxRows) break;
            var accent = new SolidColorBrush(GenusColors[genus]);
            lines.Add(new Line(genus.ToString().ToUpperInvariant(), "", "", "", "", 0, accent, accent, null));
            foreach (var p in pets.Take(maxRows - shown))
            {
                lines.Add(Row(p, accent, snap.HasCollection));
                shown++;
            }
        }
        if (total > shown)
            lines.Add(new Line(string.Format(T.PetsMore, total - shown), "", "", "", "", 0,
                (Brush)FindResource("TextMute"), Brushes.Transparent, null));

        List.ItemsSource = lines;
        EmptyText.Text = !snap.HasCollection && total == 0 ? T.PetsNeedLogin : T.PetsNoneNearby;
        EmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        Panel.ToolTip = snap.HasCollection ? null : T.PetsNeedLogin;
    }

    private Line Row(NearbyPet p, Brush accent, bool known)
    {
        var pr = p.Progress;
        var names = string.Join(", ", p.NpcCodes.Select(_meter.Data.NpcName).Distinct());
        var tip = string.Format(T.PetsNearbyTip, p.NearbyCount, names);
        if (!known) return new Line("", p.Pet.Name, $"×{p.NearbyCount}", "", "?", 0, accent, (Brush)FindResource("Text"), tip);
        if (pr.Needed is not { } needed)
            return new Line("", p.Pet.Name, $"×{p.NearbyCount}", T.PetsMax, "", BarWidth, accent, (Brush)FindResource("TextMute"), tip);
        var level = pr.Level == 0 ? T.PetsNew : $"Lv{pr.Level}";
        var fill = BarWidth * Math.Clamp(pr.Souls / (double)needed, 0, 1);
        return new Line("", p.Pet.Name, $"×{p.NearbyCount}", level, $"{pr.Souls}/{needed}", fill, accent,
            pr.Level == 0 ? (Brush)FindResource("GoldBright") : (Brush)FindResource("Text"), tip);
    }

    // ------------------------------------------------------------ options

    private void ApplySize()
    {
        var scale = _meter.Settings.PetOverlaySize / 100.0;
        Scale.ScaleX = scale;
        Scale.ScaleY = scale;
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

    private void ShowOptions()
    {
        var s = _meter.Settings;
        var menu = new ContextMenu { PlacementTarget = this };

        var size = new MenuItem { Header = T.PetsSize };
        foreach (var (label, pct) in new[] { ("S", 80), ("M", 100), ("L", 125), ("XL", 150) })
        {
            var item = new MenuItem { Header = $"{label}  ({pct}%)", IsCheckable = true, IsChecked = s.PetOverlaySize == pct };
            item.Click += (_, _) => SetSize(pct);
            size.Items.Add(item);
        }
        menu.Items.Add(size);

        var rows = new MenuItem { Header = T.PetsRows };
        foreach (var n in AppSettings.PetRowChoices)
        {
            var item = new MenuItem { Header = n.ToString(), IsCheckable = true, IsChecked = s.PetOverlayMaxRows == n };
            item.Click += (_, _) =>
            {
                s.PetOverlayMaxRows = n;
                s.Save();
                Refresh(force: true);
            };
            rows.Items.Add(item);
        }
        menu.Items.Add(rows);

        var locked = new MenuItem { Header = T.PetsLock, IsCheckable = true, IsChecked = s.PetOverlayLocked };
        locked.Click += (_, _) =>
        {
            s.PetOverlayLocked = !s.PetOverlayLocked;
            s.Save();
        };
        menu.Items.Add(locked);
        menu.Items.Add(new Separator());

        var credit = new MenuItem { Header = T.PetsCredit + "  ↗" };
        credit.Click += (_, _) => OpenCredit();
        menu.Items.Add(credit);

        var hide = new MenuItem { Header = string.Format(T.PetsHide, s.HotkeyPets) };
        hide.Click += (_, _) => Hidden?.Invoke();
        menu.Items.Add(hide);
        menu.IsOpen = true;
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
