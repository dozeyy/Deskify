using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Deskify.Models;

namespace Deskify.UI;

/// <summary>Global-hotkey popup (Ctrl+Space): opens with the search box focused —
/// type to filter, Up/Down to move, Enter or click to launch, Esc or clicking
/// away to close. Nothing launches until the user explicitly confirms, so an
/// accidental hotkey press never launches a whole workspace by itself.</summary>
public partial class QuickSwitchWindow : Window
{
    public DeskifyProject? Chosen { get; private set; }

    private readonly List<ProjectRow> _all;
    private bool _resultSet;

    public QuickSwitchWindow(IEnumerable<DeskifyProject> projects)
    {
        InitializeComponent();

        _all = projects
            .OrderByDescending(p => p.Pinned)
            .ThenByDescending(p => p.LastUsed)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ProjectRow.From)
            .ToList();

        ApplyFilter("");
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyFilter(SearchBox.Text.Trim());

    private void ApplyFilter(string query)
    {
        var filtered = query.Length == 0
            ? _all
            : _all.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        ResultList.ItemsSource = filtered;
        EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (filtered.Count > 0) ResultList.SelectedIndex = 0;
    }

    // PreviewKeyDown so navigation keys are handled here even while the search
    // box has keyboard focus (a TextBox would otherwise swallow Up/Down/Enter).
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                SetResult(false);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                Confirm();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (ResultList.Items.Count == 0) return;
        int next = Math.Clamp(ResultList.SelectedIndex + delta, 0, ResultList.Items.Count - 1);
        ResultList.SelectedIndex = next;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private void Confirm()
    {
        if (ResultList.SelectedItem is ProjectRow row)
        {
            Chosen = row.Project;
            SetResult(true);
        }
    }

    private void ResultRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectRow row)
        {
            Chosen = row.Project;
            SetResult(true);
        }
    }

    private void Window_Deactivated(object sender, EventArgs e) => SetResult(false);

    /// <summary>Lets the global hotkey handler close this popup from outside
    /// (pressing Ctrl+Space again while it's already open) without duplicating
    /// the "already resolved" guard in <see cref="SetResult"/>.</summary>
    public void Dismiss() => SetResult(false);

    private void SetResult(bool value)
    {
        if (_resultSet) return;
        _resultSet = true;
        DialogResult = value;
    }
}
