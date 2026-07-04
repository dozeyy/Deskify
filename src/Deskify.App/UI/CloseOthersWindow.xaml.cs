using System.Windows;
using System.Windows.Media.Imaging;
using Deskify.Models;
using Deskify.Services;

namespace Deskify.UI;

/// <summary>Confirms which non-project windows to close before "Close Other Apps" or a
/// workspace switch runs. Nothing closes until the user confirms — matches the
/// read-only CaptureWindow pattern. This dialog only asks; the caller is
/// responsible for actually closing <see cref="SelectedWindows"/>.</summary>
public partial class CloseOthersWindow : Window
{
    public enum Mode { CloseOnly, WorkspaceSwitch }

    public bool DontAskAgain => DontAskCheck.IsChecked == true;
    public List<WindowInfo> SelectedWindows => _items.Where(i => i.Selected).Select(i => i.Window).ToList();

    private readonly Mode _mode;
    private List<CloseItem> _items = [];

    public CloseOthersWindow(DeskifyProject project, IEnumerable<WindowInfo> others, Mode mode = Mode.CloseOnly)
    {
        InitializeComponent();
        ThemeManager.ApplyTitleBar(this);
        _mode = mode;

        Title = mode == Mode.WorkspaceSwitch ? "Switch Workspace" : "Close Other Apps";
        TitleText.Text = mode == Mode.WorkspaceSwitch ? "Switch workspace?" : "Close other apps?";
        SubtitleText.Text = mode == Mode.WorkspaceSwitch
            ? $"These aren't part of \"{project.Name}\" — they'll be closed before it launches. Uncheck anything you want to leave open."
            : $"These aren't part of \"{project.Name}\". Uncheck anything you want to leave open.";

        _items = others
            .Select(w => new CloseItem { Window = w, Icon = IconLoader.GetSmallIcon(w.ExePath), Selected = true })
            .OrderBy(i => i.Window.ExeName)
            .ThenBy(i => i.Window.Title)
            .ToList();

        WindowList.ItemsSource = _items;
        UpdateCount();
    }

    private void Item_CheckChanged(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        int n = _items.Count(i => i.Selected);
        CountText.Text = $"{n} of {_items.Count} window{(_items.Count == 1 ? "" : "s")} selected";
        ConfirmBtn.Content = _mode == Mode.WorkspaceSwitch
            ? "Switch Workspace"
            : (n == 1 ? "Close 1 Window" : $"Close {n} Windows");
        ConfirmBtn.IsEnabled = n > 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

public sealed class CloseItem
{
    public required WindowInfo Window { get; init; }
    public BitmapSource? Icon { get; init; }
    public bool Selected { get; set; }

    public string Title => Window.Title;
    public string ExeName => Window.ExeName;
}
