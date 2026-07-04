using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Deskify.Interop;
using Deskify.Models;
using Deskify.Services;

namespace Deskify.UI;

/// <summary>Lists running application windows; user picks which become project apps.
/// Nothing is saved automatically — the caller decides what to do with Result.</summary>
public partial class CaptureWindow : Window
{
    public List<AppEntry> Result { get; } = [];

    private List<CaptureItem> _items = [];

    public CaptureWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyTitleBar(this);
        Rescan();
    }

    private void Rescan()
    {
        var monitors = Monitors.All();
        _items = WindowScanner.Scan()
            .Select(w => new CaptureItem
            {
                Window = w,
                Icon = IconLoader.GetSmallIcon(w.ExePath),
                MonitorText = Monitors.ForWindow(w.Hwnd, monitors).ToString(),
            })
            .OrderBy(i => i.Window.ExeName)
            .ThenBy(i => i.Window.Title)
            .ToList();

        WindowList.ItemsSource = _items;
        UpdateCount();
    }

    private void Item_CheckChanged(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        int selected = _items.Count(i => i.Selected);
        CountText.Text = selected > 0
            ? $"{_items.Count} window{(_items.Count == 1 ? "" : "s")} · {selected} selected"
            : $"{_items.Count} window{(_items.Count == 1 ? "" : "s")} found";
        AddBtn.IsEnabled = selected > 0;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Rescan();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // Capture placements at confirm time so last-second window moves count.
        var monitors = Monitors.All();
        foreach (var item in _items.Where(i => i.Selected))
        {
            var entry = new AppEntry
            {
                Name = MainWindow.FriendlyName(item.Window.ExePath),
                Path = item.Window.ExePath,
                Window = LayoutService.Capture(item.Window.Hwnd, monitors),
            };

            // explorer.exe hosts every File Explorer window in one shared shell
            // process — relaunching it with no path just opens a default window,
            // so capture the specific folder this window is showing as launch args.
            if (item.Window.ExeName == "explorer.exe")
            {
                var folderPath = ExplorerWindows.PathFor(item.Window.Hwnd);
                if (folderPath != null)
                {
                    entry.Args = $"\"{folderPath}\"";
                    entry.Name = Path.GetFileName(folderPath.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : folderPath;
                }
                else
                {
                    entry.Name = "File Explorer";
                }
            }

            Result.Add(entry);
        }
        DialogResult = true;
    }
}

public sealed class CaptureItem
{
    public required WindowInfo Window { get; init; }
    public BitmapSource? Icon { get; init; }
    public string MonitorText { get; init; } = "";
    public bool Selected { get; set; }

    public string Title => Window.Title;
    public string ExePath => Window.ExePath;
}
