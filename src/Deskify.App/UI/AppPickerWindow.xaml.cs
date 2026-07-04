using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Deskify.Models;
using Deskify.Services;

namespace Deskify.UI;

/// <summary>Lists every real, user-recognizable application on the PC — desktop
/// programs and Microsoft Store apps, with Windows' own utilities, drivers, and
/// helper exes filtered out (see <see cref="InstalledAppFilter"/>) — searchable,
/// multi-select, so adding an app is picking from a list instead of hunting
/// through Program Files. Browse still covers anything the scan misses (portable
/// apps, unusual install locations, etc).</summary>
public partial class AppPickerWindow : Window
{
    public List<AppEntry> Result { get; } = [];

    private List<AppPickerItem> _all = [];

    public AppPickerWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        CountText.Text = "Scanning installed apps…";
        _all = await Task.Run(() => InstalledAppsScanner.Scan()
            .Select(a => new AppPickerItem
            {
                Name = a.Name,
                Path = a.Path,
                Args = a.Args,
                Icon = IconLoader.GetSmallIcon(a.Path),
            })
            .ToList());
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = query.Length == 0
            ? _all
            : _all.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || a.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                  .ToList();

        // Resetting ItemsSource loses scroll position, so this only runs when the
        // search text actually changes — not on every checkbox toggle.
        AppList.ItemsSource = filtered;
        EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount(filtered.Count);
    }

    private void UpdateCount(int shown)
    {
        int selected = _all.Count(a => a.Selected);
        CountText.Text = selected > 0
            ? $"{shown} shown · {selected} selected"
            : $"{shown} app{(shown == 1 ? "" : "s")} found";
        AddBtn.IsEnabled = selected > 0;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppPickerItem item) return;
        item.Selected = !item.Selected;
        UpdateCount(((IEnumerable<AppPickerItem>)AppList.ItemsSource).Count());
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add application",
            Filter = "Programs (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) != true) return;

        var (resolvedPath, args) = ShortcutResolver.ResolveWithArgs(dialog.FileName);
        var name = MainWindow.FriendlyName(dialog.FileName);
        if (WindowScanner.IsProtected(dialog.FileName, name) || WindowScanner.IsProtected(resolvedPath, name))
        {
            MessageBox.Show(this, "Riot Games/Valorant apps can't be added to Deskify.", "Blocked",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.Add(new AppEntry
        {
            Name = name,
            Path = resolvedPath,
            Args = string.IsNullOrWhiteSpace(args) ? null : args,
        });
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _all.Where(a => a.Selected))
        {
            Result.Add(new AppEntry
            {
                Name = item.Name,
                Path = item.Path,
                Args = item.Args,
            });
        }
        if (Result.Count > 0) DialogResult = true;
    }
}

public sealed class AppPickerItem : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Args { get; init; }
    public BitmapSource? Icon { get; init; }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
