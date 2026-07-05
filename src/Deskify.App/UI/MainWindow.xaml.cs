using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Deskify.Interop;
using Deskify.Models;
using Deskify.Services;
using Microsoft.Win32;

namespace Deskify.UI;

public partial class MainWindow : Window
{
    private const int QuickSwitchHotkeyId = 0xD5;

    private readonly List<DeskifyProject> _projects;
    private AppSettings _settings;

    private DeskifyProject? _selected;
    private DeskifyProject? _editing;
    private bool _editingIsNew;
    private bool _launching;
    private bool _collapsed;
    private bool _snapEnabled;
    private QuickSwitchWindow? _quickSwitchWindow;
    private int _wizardStep;
    private HwndSource? _hwndSource;

    private readonly ObservableCollection<AppRow> _editApps = [];
    private readonly ObservableCollection<string> _editFolders = [];
    private readonly ObservableCollection<string> _editUrls = [];

    private StackPanel[] _steps = [];
    private Border[] _segments = [];
    private static readonly string[] StepTitles = ["Name", "Apps", "Folders", "Websites"];

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _projects = ProjectStore.LoadAll();

        EditApps.ItemsSource = _editApps;
        EditFolders.ItemsSource = _editFolders;
        EditUrls.ItemsSource = _editUrls;

        // Same asset as the window/taskbar icon (Assets/deskify.ico) so the sidebar
        // badge and empty-state badge are the literal brand mark, not a lookalike.
        BrandLogoImage.Source = IconLoader.GetAppLogo(32);
        EmptyStateLogoImage.Source = IconLoader.GetAppLogo(48);

        _steps = [StepName, StepApps, StepFolders, StepWebsites];
        _segments = [Seg1, Seg2, Seg3, Seg4];

        ShowProjectsList();
        RestoreDraftIfAny();
    }

    // ==================== Quick switch (global hotkey) ====================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyTitleBar(this);
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(HotkeyWndProc);
        bool registered = NativeMethods.RegisterHotKey(handle, QuickSwitchHotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_SPACE);
        if (!registered)
            Log.Error("Quick-switch hotkey (Ctrl+Space) is already claimed by another app — quick switch won't respond to it.");
    }

    protected override void OnClosed(EventArgs e)
    {
        AutoSaveOnExit();
        if (_hwndSource != null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, QuickSwitchHotkeyId);
            _hwndSource.RemoveHook(HotkeyWndProc);
        }
        base.OnClosed(e);
    }

    /// <summary>Nothing typed should be lost just because the app was closed:
    /// a wizard mid-edit is saved as a draft (restored on next start), unsaved
    /// settings edits are applied if valid, and unsaved notes are written out
    /// (LostFocus never fires when the app closes with the notes box focused).</summary>
    private void AutoSaveOnExit()
    {
        try
        {
            if (EditView.Visibility == Visibility.Visible && _editing != null)
            {
                CollectWizardInto(_editing);
                // An untouched brand-new wizard has no progress worth restoring —
                // reopening it on every start would just be noise.
                bool emptyNew = _editingIsNew
                    && _editing.Apps.Count == 0 && _editing.Folders.Count == 0 && _editing.Urls.Count == 0
                    && _editing.Name is "Untitled" or "New Project";
                if (!emptyNew)
                {
                    DraftStore.Save(new DraftStore.Draft
                    {
                        Project = _editing,
                        IsNew = _editingIsNew,
                        Step = _wizardStep,
                        FilePath = _editing.FilePath,
                    });
                }
            }

            if (SettingsView.Visibility == Visibility.Visible) AutoSaveSettings();

            if (DetailView.Visibility == Visibility.Visible && _selected != null)
            {
                var text = DetailNotes.Text.Trim();
                var notes = text.Length == 0 ? null : text;
                if (_selected.Notes != notes)
                {
                    _selected.Notes = notes;
                    ProjectStore.Save(_selected);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Autosave on exit failed", ex);
        }
    }

    /// <summary>Reopens the wizard exactly where it was when the app closed
    /// mid-edit. The draft is cleared immediately so it can't resurrect later —
    /// closing again mid-edit simply writes a fresh one.</summary>
    private void RestoreDraftIfAny()
    {
        var draft = DraftStore.Load();
        if (draft == null) return;
        DraftStore.Clear();

        var project = draft.Project;
        bool isNew = draft.IsNew;
        if (!isNew && draft.FilePath != null)
        {
            var original = _projects.FirstOrDefault(p =>
                string.Equals(p.FilePath, draft.FilePath, StringComparison.OrdinalIgnoreCase));
            if (original != null)
            {
                _selected = original;
                project.FilePath = draft.FilePath;
            }
            else
            {
                isNew = true; // original project file is gone — keep the edits as a new project
            }
        }
        OpenWizard(project, isNew);
        GoToStep(draft.Step);
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == QuickSwitchHotkeyId)
        {
            ShowQuickSwitch();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShowQuickSwitch()
    {
        // Ctrl+Space is a toggle: pressing it again while the popup is already
        // open closes it, same as clicking away or Esc, instead of doing nothing.
        if (_quickSwitchWindow != null)
        {
            _quickSwitchWindow.Dismiss();
            return;
        }

        var picker = new QuickSwitchWindow(_projects);
        _quickSwitchWindow = picker;
        try
        {
            if (picker.ShowDialog() == true && picker.Chosen != null)
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
                ShowDetails(picker.Chosen);
                Launch_Click(this, new RoutedEventArgs());
            }
        }
        finally
        {
            _quickSwitchWindow = null;
        }
    }

    // ==================== Navigation ====================

    private void SetActiveNav(Button? active)
    {
        NavProjects.Tag = active == NavProjects ? "active" : null;
        NavSettings.Tag = active == NavSettings ? "active" : null;
    }

    private void HideAllViews()
    {
        // Leaving Settings by any route counts as "done editing" — valid changes
        // are applied automatically instead of being silently dropped.
        if (SettingsView.Visibility == Visibility.Visible) AutoSaveSettings();

        ProjectsView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Collapsed;
        EditView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
    }

    private void ShowRightPanel(bool show)
    {
        RightPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RightCol.Width = show ? new GridLength(316) : new GridLength(0);
    }

    private void ExitLayoutMode()
    {
        LayoutToolbar.Visibility = Visibility.Collapsed;
    }

    private void ShowProjectsList()
    {
        ExitLayoutMode();
        HideAllViews();
        ShowRightPanel(false);
        SetActiveNav(NavProjects);
        RefreshProjects();
        ProjectsView.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(ProjectsView);
    }

    private void RefreshProjects()
    {
        _projects.Sort((a, b) =>
        {
            int pinCmp = b.Pinned.CompareTo(a.Pinned);
            if (pinCmp != 0) return pinCmp;
            int cmp = Nullable.Compare(b.LastUsed, a.LastUsed);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        ProjectRows.ItemsSource = _projects.Select(ProjectRow.From).ToList();
        ProjectCount.Text = _projects.Count switch
        {
            0 => "No projects yet",
            1 => "1 project",
            _ => $"{_projects.Count} projects",
        };
        ProjectsEmpty.Visibility = _projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavProjects_Click(object sender, RoutedEventArgs e) => ShowProjectsList();

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        // Already on Settings: don't repopulate the boxes — that would wipe
        // whatever the user has typed but not saved yet.
        if (SettingsView.Visibility == Visibility.Visible) return;

        ExitLayoutMode();
        TimeoutBox.Text = _settings.DetectTimeoutSeconds.ToString();
        RetryBox.Text = _settings.RetryIntervalMs.ToString();
        SnapSizeBox.Text = _settings.SnapGridSize.ToString();
        StrictDefaultCheck.IsChecked = _settings.StrictLayoutDefault;
        ConfirmCloseOthersCheck.IsChecked = _settings.ConfirmCloseOthers;
        SoundCheck.IsChecked = _settings.InterfaceSounds;
        SettingsStatus.Text = "";
        PopulateThemeList();

        HideAllViews();
        ShowRightPanel(false);
        SetActiveNav(NavSettings);
        SettingsView.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(SettingsView);
    }

    /// <summary>Swatch fill + check-mark color for each theme's circle preview.</summary>
    private static readonly Dictionary<string, (Color Bg, Color Check)> ThemeSwatchColors = new()
    {
        ["Dark"] = (Color.FromRgb(0x12, 0x12, 0x12), Colors.White),
        ["Light"] = (Color.FromRgb(0xF4, 0xF4, 0xF4), Color.FromRgb(0x1A, 0x1A, 0x1A)),
    };

    private void PopulateThemeList()
    {
        var items = ThemeManager.Available.Select(name =>
        {
            var (bg, check) = ThemeSwatchColors[name];
            var btn = new Button
            {
                Style = (Style)FindResource("ThemeSwatchBtn"),
                Background = new SolidColorBrush(bg),
                Foreground = new SolidColorBrush(check),
                Tag = name == _settings.ThemeName ? "active" : null,
                ToolTip = name,
            };
            btn.Click += (_, _) => SelectTheme(name);

            var label = new TextBlock
            {
                Text = name,
                FontSize = 11,
                Margin = new Thickness(0, 7, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("TextDimBrush"),
            };

            var item = new StackPanel { Margin = new Thickness(0, 0, 22, 0), HorizontalAlignment = HorizontalAlignment.Center };
            item.Children.Add(btn);
            item.Children.Add(label);
            return item;
        }).ToList();
        ThemeList.ItemsSource = items;
    }

    private void SelectTheme(string name)
    {
        if (_settings.ThemeName == name) return;
        _settings.ThemeName = name;
        _settings.Save();
        ThemeManager.Apply(name);
        PopulateThemeList();
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        SidebarCol.Width = new GridLength(_collapsed ? 76 : 216);
        var vis = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        BrandText.Visibility = vis;
        LblProjects.Visibility = vis;
        LblSettings.Visibility = vis;
        LblCollapse.Visibility = vis;
        CollapseIcon.Data = (Geometry)FindResource(_collapsed ? "IcoChevronRight" : "IcoChevronLeft");
        CollapseBtn.ToolTip = _collapsed ? "Expand sidebar" : "Collapse sidebar";

        // Collapsed: only the badge is visible, so center it for even left/right
        // spacing. Expanded: badge + text sit left-aligned as a normal row.
        BrandRow.HorizontalAlignment = _collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    }

    // ==================== Project details ====================

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DeskifyProject project)
        {
            _selected = project;
            ShowDetails(project);
        }
    }

    private void ShowDetails(DeskifyProject project)
    {
        ExitLayoutMode();
        _selected = project;

        DetailName.Text = project.Name;
        DetailMeta.Text = $"{project.SummaryText}  ·  {project.LastUsedText}";
        int withLayout = project.Apps.Count(a => a.Window != null);
        LayoutModeText.Text = withLayout > 0
            ? $"Saved for {withLayout} app{(withLayout == 1 ? "" : "s")} · positions {(project.StrictLayout ? "locked" : "flexible")}"
            : "No layout saved yet — use Edit Layout.";

        DetailIcons.ItemsSource = ProjectRow.IconsFor(project);
        DetailNotes.Text = project.Notes ?? "";

        var appRows = project.Apps.Select(AppRow.From).ToList();
        DetailApps.ItemsSource = appRows;
        DetailAppsEmpty.Visibility = appRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailUrls.ItemsSource = project.Urls.ToList();
        DetailUrlsEmpty.Visibility = project.Urls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailFolders.ItemsSource = project.Folders.ToList();
        DetailFoldersEmpty.Visibility = project.Folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = "";
        ErrorList.ItemsSource = null;

        HideAllViews();
        SetActiveNav(NavProjects);
        DetailView.Visibility = Visibility.Visible;
        ShowRightPanel(true);
        Motion.FadeSlideIn(DetailView);
    }

    private void DetailNotes_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var text = DetailNotes.Text.Trim();
        _selected.Notes = text.Length == 0 ? null : text;
        if (!ProjectStore.Save(_selected))
            StatusText.Text = "Couldn't save your notes — check that the project file isn't open elsewhere or read-only.";
    }

    private void BackToList_Click(object sender, RoutedEventArgs e) => ShowProjectsList();

    private void RowLaunch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DeskifyProject project) return;
        ShowDetails(project);
        Launch_Click(sender, e);
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DeskifyProject project) return;
        project.Pinned = !project.Pinned;
        if (!ProjectStore.Save(project))
        {
            // Don't leave the UI showing a pin state that isn't actually on disk.
            project.Pinned = !project.Pinned;
            StatusText.Text = "Couldn't save — check that the project file isn't open elsewhere or read-only.";
            return;
        }
        RefreshProjects();
    }

    /// <summary>Launching a project is a full workspace switch: close everything that
    /// doesn't belong (asking first, unless disabled in Settings), then launch as usual.
    /// Shared by the row Launch button, the detail-view Launch button, and Fast
    /// Switch (via <see cref="ShowQuickSwitch"/>) — one place, so all three switch
    /// workspaces identically.</summary>
    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _launching) return;
        _launching = true;
        LaunchBtn.IsEnabled = false;
        EditLayoutBtn.IsEnabled = false;
        CloseOthersBtn.IsEnabled = false;
        ErrorList.ItemsSource = null;
        var project = _selected;

        try
        {
            LaunchEngine.SyncAutoLinkedEntries(project);

            var closeErrors = await CloseUnrelatedForSwitchAsync(project);
            if (closeErrors == null) return; // user cancelled the switch in the confirmation dialog

            var progress = new Progress<string>(msg => StatusText.Text = msg);
            var result = await LaunchEngine.LaunchAsync(project, _settings, progress);
            var errors = closeErrors.Count > 0 ? closeErrors.Concat(result.Errors).ToList() : result.Errors;
            ErrorList.ItemsSource = errors;

            project.LastUsed = DateTime.UtcNow;
            if (!ProjectStore.Save(project))
                ErrorList.ItemsSource = errors.Append("Couldn't save the project file (last-used time wasn't updated) — everything else above still launched normally.").ToList();
            DetailMeta.Text = $"{project.SummaryText}  ·  {project.LastUsedText}";

            // A satisfying resolve once the workspace is actually up — fires after
            // the async launch, well clear of the click that started it.
            Sfx.Play(errors.Count == 0 ? Sfx.Cue.Success : Sfx.Cue.Confirm);
        }
        catch (Exception ex)
        {
            Log.Error("Launch failed", ex);
            StatusText.Text = $"Launch failed: {ex.Message}";
        }
        finally
        {
            _launching = false;
            LaunchBtn.IsEnabled = true;
            EditLayoutBtn.IsEnabled = true;
            CloseOthersBtn.IsEnabled = true;
        }
    }

    /// <summary>Closes every running app that isn't part of <paramref name="project"/>.
    /// Asks first via <see cref="CloseOthersWindow"/> unless the user turned that off
    /// (Settings, or a previous "Don't ask again"). Returns null if the user cancelled
    /// the confirmation — callers should abort the switch entirely in that case — or a
    /// (possibly empty) list of anything that couldn't be closed otherwise.</summary>
    private async Task<List<string>?> CloseUnrelatedForSwitchAsync(DeskifyProject project)
    {
        var others = WindowCloser.OtherWindows(project);
        if (others.Count == 0) return [];

        var toClose = others;
        if (_settings.ConfirmCloseOthers)
        {
            var dialog = new CloseOthersWindow(project, others, CloseOthersWindow.Mode.WorkspaceSwitch) { Owner = this };
            if (dialog.ShowDialog() != true) return null;
            if (dialog.DontAskAgain)
            {
                _settings.ConfirmCloseOthers = false;
                _settings.Save();
            }
            toClose = dialog.SelectedWindows;
        }

        if (toClose.Count == 0) return [];
        StatusText.Text = "Closing other apps…";
        return await WindowCloser.CloseAndVerifyAsync(toClose);
    }

    private async void CloseOthers_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _launching) return;
        var others = WindowCloser.OtherWindows(_selected);
        if (others.Count == 0)
        {
            StatusText.Text = "Nothing else is open.";
            return;
        }

        var toClose = others;
        if (_settings.ConfirmCloseOthers)
        {
            var dialog = new CloseOthersWindow(_selected, others, CloseOthersWindow.Mode.CloseOnly) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (dialog.DontAskAgain)
            {
                _settings.ConfirmCloseOthers = false;
                _settings.Save();
            }
            toClose = dialog.SelectedWindows;
            if (toClose.Count == 0) return;
        }

        _launching = true;
        CloseOthersBtn.IsEnabled = false;
        LaunchBtn.IsEnabled = false;
        EditLayoutBtn.IsEnabled = false;
        try
        {
            StatusText.Text = "Closing other apps…";
            var errors = await WindowCloser.CloseAndVerifyAsync(toClose);
            ErrorList.ItemsSource = errors.Count > 0 ? errors : null;
            StatusText.Text = "Closed other apps.";
        }
        finally
        {
            _launching = false;
            CloseOthersBtn.IsEnabled = true;
            LaunchBtn.IsEnabled = true;
            EditLayoutBtn.IsEnabled = true;
        }
    }

    // ==================== Layout editor ====================

    private async void EditLayout_Click(object sender, RoutedEventArgs e)
    {
        // _launching also guards this: launching apps for Edit Layout while a
        // regular Launch is mid-flight (or vice versa) would double-start apps.
        if (_selected == null || _launching) return;
        LaunchEngine.SyncAutoLinkedEntries(_selected);
        if (_selected.Apps.Count == 0)
        {
            StatusText.Text = "Add apps, folders, or websites to this project first — layout applies to their windows.";
            return;
        }

        _launching = true;
        EditLayoutBtn.IsEnabled = false;
        LaunchBtn.IsEnabled = false;
        try
        {
            var result = new LaunchResult();
            StatusText.Text = "Opening anything that isn't running yet…";
            int launched = await LaunchEngine.LaunchMissingAppsAsync(_selected, _settings, result);
            ErrorList.ItemsSource = result.Errors;
            StatusText.Text = launched > 0
                ? $"Started {launched} app{(launched == 1 ? "" : "s")}. Arrange the windows, then save the layout."
                : "All apps are already running. Arrange the windows, then save the layout.";
            LayoutToolbar.Visibility = Visibility.Visible;
        }
        finally
        {
            _launching = false;
            EditLayoutBtn.IsEnabled = true;
            LaunchBtn.IsEnabled = true;
        }
    }

    private void SnapToggle_Click(object sender, RoutedEventArgs e)
    {
        _snapEnabled = !_snapEnabled;
        SnapToggle.Foreground = _snapEnabled
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("TextDimBrush");
        LayoutHint.Text = _snapEnabled
            ? $"Snap on — positions round to {_settings.SnapGridSize}px on save"
            : "Arrange your windows, then save";
    }

    private void SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var project = _selected;
        var missing = LaunchEngine.CaptureLayouts(project);
        if (_snapEnabled) SnapLayouts(project, _settings.SnapGridSize);
        bool saved = ProjectStore.Save(project);
        LayoutToolbar.Visibility = Visibility.Collapsed;
        ShowDetails(project);

        int captured = project.Apps.Count - missing.Count;
        if (!saved)
        {
            // Distinct from "some app's window wasn't found" below: this means the
            // write to disk itself failed, so nothing from this attempt persisted —
            // say so plainly rather than reporting a partial success that isn't real.
            StatusText.Text = "Couldn't save the project file — nothing from this attempt was written to disk. Check that the project file isn't open elsewhere or read-only, then try again.";
            ErrorList.ItemsSource = null;
            return;
        }

        Sfx.Play(Sfx.Cue.Confirm);
        StatusText.Text = missing.Count > 0
            ? $"Layout saved for {captured}/{project.Apps.Count} apps — the rest of the project was saved normally."
            : $"Layout saved for all {project.Apps.Count} app{(project.Apps.Count == 1 ? "" : "s")}.";
        ErrorList.ItemsSource = missing.Count > 0
            ? missing.Select(name => $"{name}: no open window found — its layout wasn't updated, but everything else was saved.").ToList()
            : null;
    }

    private void CancelLayout_Click(object sender, RoutedEventArgs e)
    {
        LayoutToolbar.Visibility = Visibility.Collapsed;
        StatusText.Text = "Layout editing cancelled — nothing saved.";
    }

    /// <summary>Round captured window positions/sizes to the nearest grid multiple.</summary>
    private static void SnapLayouts(DeskifyProject project, int grid)
    {
        if (grid < 2) return;
        static int Round(int v, int g) => (int)Math.Round(v / (double)g) * g;
        foreach (var app in project.Apps)
        {
            if (app.Window is not { } w || w.IsMaximized) continue;
            w.X = Round(w.X, grid);
            w.Y = Round(w.Y, grid);
            w.Width = Math.Max(grid, Round(w.Width, grid));
            w.Height = Math.Max(grid, Round(w.Height, grid));
        }
    }

    // ==================== Create / edit / delete ====================

    private void NewProject_Click(object sender, RoutedEventArgs e) =>
        OpenWizard(new DeskifyProject { Name = "New Project", StrictLayout = _settings.StrictLayoutDefault }, isNew: true);

    private void NewCapture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CaptureWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result.Count == 0) return;

        var project = new DeskifyProject
        {
            Name = $"Captured {DateTime.Now:MMM d HH:mm}",
            Apps = dialog.Result,
            StrictLayout = _settings.StrictLayoutDefault,
        };
        OpenWizard(project, isNew: true);
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null) OpenWizard(_selected.Clone(), isNew: false);
    }

    private void DuplicateProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var copy = _selected.Clone();
        copy.FilePath = null;
        copy.Name += " (copy)";
        copy.LastUsed = null;
        if (!ProjectStore.Save(copy))
        {
            // Don't show a duplicate that doesn't actually exist on disk — it
            // would vanish the next time projects are loaded, with no warning.
            StatusText.Text = "Couldn't save the duplicate — check that the projects folder isn't read-only, then try again.";
            return;
        }
        _projects.Add(copy);
        ShowDetails(copy);
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var answer = MessageBox.Show(this, $"Delete \"{_selected.Name}\"? This cannot be undone.",
            "Delete project", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (!ProjectStore.Delete(_selected))
        {
            // The file is still on disk — keep the project in the list rather than
            // showing it gone and having it reappear on the next restart.
            StatusText.Text = "Couldn't delete the project file — check that it isn't open elsewhere, then try again.";
            return;
        }
        _projects.Remove(_selected);
        _selected = null;
        ShowProjectsList();
    }

    // ==================== Wizard ====================

    private void OpenWizard(DeskifyProject project, bool isNew)
    {
        _editing = project;
        _editingIsNew = isNew;

        EditTitle.Text = isNew ? "New Project" : $"Edit — {project.Name}";
        EditName.Text = project.Name;
        StrictCheck.IsChecked = project.StrictLayout;
        UrlBox.Text = "";
        UrlHint.Visibility = Visibility.Collapsed;

        _editApps.Clear();
        foreach (var app in project.Apps) _editApps.Add(AppRow.From(app));
        _editFolders.Clear();
        foreach (var folder in project.Folders) _editFolders.Add(folder);
        _editUrls.Clear();
        foreach (var url in project.Urls) _editUrls.Add(url);

        ExitLayoutMode();
        HideAllViews();
        ShowRightPanel(false);
        SetActiveNav(NavProjects);
        EditView.Visibility = Visibility.Visible;
        Motion.FadeSlideIn(EditView);
        GoToStep(0);
    }

    private void GoToStep(int index)
    {
        _wizardStep = Math.Clamp(index, 0, _steps.Length - 1);
        for (int i = 0; i < _steps.Length; i++)
            _steps[i].Visibility = i == _wizardStep ? Visibility.Visible : Visibility.Collapsed;
        Motion.FadeSlideIn(_steps[_wizardStep], rise: 8, quick: true);

        var on = (Brush)FindResource("AccentBrush");
        var off = (Brush)FindResource("StrokeBrush");
        for (int i = 0; i < _segments.Length; i++)
            _segments[i].Background = i <= _wizardStep ? on : off;

        StepLabel.Text = $"Step {_wizardStep + 1} of {_steps.Length} · {StepTitles[_wizardStep]}";
        WizardBack.Visibility = _wizardStep == 0 ? Visibility.Hidden : Visibility.Visible;

        bool last = _wizardStep == _steps.Length - 1;
        WizardNext.Content = last
            ? (_editingIsNew ? "Create" : "Save")
            : "Next";

        if (_wizardStep == 0) { EditName.Focus(); EditName.SelectAll(); }
    }

    private void WizardNext_Click(object sender, RoutedEventArgs e)
    {
        if (_wizardStep == _steps.Length - 1) { SaveProject(); return; }
        GoToStep(_wizardStep + 1);
    }

    private void WizardBack_Click(object sender, RoutedEventArgs e) => GoToStep(_wizardStep - 1);

    /// <summary>Pulls the wizard's current field values into the project being
    /// edited. Shared by Save and by the close-time draft autosave, so whatever
    /// is on screen is always exactly what gets persisted.</summary>
    private void CollectWizardInto(DeskifyProject project)
    {
        PendingUrlToList();
        project.Name = string.IsNullOrWhiteSpace(EditName.Text) ? "Untitled" : EditName.Text.Trim();
        project.StrictLayout = StrictCheck.IsChecked == true;
        project.Apps = _editApps.Select(r => r.Entry).ToList();
        project.Folders = _editFolders.ToList();
        project.Urls = _editUrls.ToList();
    }

    private void SaveProject()
    {
        if (_editing == null) return;

        CollectWizardInto(_editing);
        LaunchEngine.SyncAutoLinkedEntries(_editing);

        if (!ProjectStore.Save(_editing))
        {
            // Stay on the wizard rather than closing it and adding a project to
            // the list that doesn't actually exist on disk yet.
            MessageBox.Show(this, "Couldn't save the project — check that the projects folder isn't read-only, then try again.",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_editingIsNew)
        {
            _projects.Add(_editing);
        }
        else if (_selected != null)
        {
            int idx = _projects.IndexOf(_selected);
            if (idx >= 0) _projects[idx] = _editing;
        }

        _selected = _editing;
        _editing = null;
        DraftStore.Clear();
        Sfx.Play(Sfx.Cue.Confirm);
        ShowDetails(_selected);
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        _editing = null;
        DraftStore.Clear();
        if (_selected != null) ShowDetails(_selected);
        else ShowProjectsList();
    }

    // ==================== Editor item actions ====================

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppPickerWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;

        foreach (var entry in dialog.Result)
        {
            if (_editApps.Any(r => string.Equals(r.Entry.Path, entry.Path, StringComparison.OrdinalIgnoreCase))) continue;
            _editApps.Add(AppRow.From(entry));
        }
    }

    private void AddRunning_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CaptureWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var entry in dialog.Result)
            _editApps.Add(AppRow.From(entry));
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AppRow row) _editApps.Remove(row);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add folder" };
        if (dialog.ShowDialog(this) != true || string.IsNullOrEmpty(dialog.FolderName)) return;
        if (_editFolders.Any(f => ExplorerWindows.PathsEqual(f, dialog.FolderName)))
            return; // already in the list — adding it again would open it twice on launch

        _editFolders.Add(dialog.FolderName);

        // Also add File Explorer as an app entry so this folder's window can be
        // positioned via Edit Layout — it opens through the Folders group above,
        // not launched again from here (see AppEntry.AutoLinked).
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (File.Exists(explorerPath))
        {
            _editApps.Add(AppRow.From(new AppEntry
            {
                Name = Path.GetFileName(dialog.FolderName.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : dialog.FolderName,
                Path = explorerPath,
                Args = $"\"{dialog.FolderName}\"",
                AutoLinked = true,
            }));
        }
    }

    private void AddUrl_Click(object sender, RoutedEventArgs e) => PendingUrlToList();

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) PendingUrlToList();
    }

    private void PendingUrlToList()
    {
        UrlHint.Visibility = Visibility.Collapsed;
        var url = UrlBox.Text.Trim();
        if (url.Length == 0) return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        // Feedback shows next to the URL box (UrlHint) — the detail view's
        // StatusText is hidden while the wizard is open, so a message there
        // would never be seen.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            UrlHint.Text = "That doesn't look like a web address — try something like example.com.";
            UrlHint.Visibility = Visibility.Visible;
            return;
        }
        if (WindowScanner.IsProtected(url))
        {
            UrlHint.Text = "Riot Games/Valorant links can't be added to Deskify.";
            UrlHint.Visibility = Visibility.Visible;
            UrlBox.Text = "";
            return;
        }
        if (_editUrls.Any(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)))
        {
            UrlHint.Text = "That website is already in the list.";
            UrlHint.Visibility = Visibility.Visible;
            UrlBox.Text = "";
            return;
        }
        _editUrls.Add(url);
        UrlBox.Text = "";

        // Also add the default browser as an app entry (once) so its window can
        // be positioned via Edit Layout — all sites open as tabs in the same
        // window, so one entry covers every website in the project.
        var browser = DefaultBrowser.Detect();
        if (browser != null && !_editApps.Any(r => string.Equals(r.Entry.Path, browser.Value.Path, StringComparison.OrdinalIgnoreCase)))
        {
            _editApps.Add(AppRow.From(new AppEntry
            {
                Name = browser.Value.Name,
                Path = browser.Value.Path,
                AutoLinked = true,
            }));
        }
    }

    private void RemoveString_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string value) return;
        if (!_editFolders.Remove(value)) _editUrls.Remove(value);
    }

    // ==================== Settings ====================

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimeoutBox.Text, out int timeout) || timeout < 1 || timeout > 120)
        {
            SettingsStatus.Text = "Timeout must be 1–120 seconds.";
            return;
        }
        if (!int.TryParse(RetryBox.Text, out int retry) || retry < 100 || retry > 5000)
        {
            SettingsStatus.Text = "Retry interval must be 100–5000 ms.";
            return;
        }
        if (!int.TryParse(SnapSizeBox.Text, out int snap) || snap < 2 || snap > 200)
        {
            SettingsStatus.Text = "Snap grid size must be 2–200 px.";
            return;
        }

        _settings.DetectTimeoutSeconds = timeout;
        _settings.RetryIntervalMs = retry;
        _settings.SnapGridSize = snap;
        _settings.StrictLayoutDefault = StrictDefaultCheck.IsChecked == true;
        _settings.ConfirmCloseOthers = ConfirmCloseOthersCheck.IsChecked == true;
        _settings.InterfaceSounds = SoundCheck.IsChecked == true;
        Sfx.Enabled = _settings.InterfaceSounds;
        _settings.Save();
        SettingsStatus.Text = "Saved.";
    }

    /// <summary>Silent counterpart of SaveSettings_Click for when the user leaves
    /// the page (or the app) without pressing Save: applies whatever is valid and
    /// changed, leaves invalid text alone rather than guessing. The explicit Save
    /// button still exists for immediate feedback and validation messages.</summary>
    private void AutoSaveSettings()
    {
        bool changed = false;

        if (int.TryParse(TimeoutBox.Text, out int timeout) && timeout is >= 1 and <= 120 &&
            timeout != _settings.DetectTimeoutSeconds)
        {
            _settings.DetectTimeoutSeconds = timeout;
            changed = true;
        }
        if (int.TryParse(RetryBox.Text, out int retry) && retry is >= 100 and <= 5000 &&
            retry != _settings.RetryIntervalMs)
        {
            _settings.RetryIntervalMs = retry;
            changed = true;
        }
        if (int.TryParse(SnapSizeBox.Text, out int snap) && snap is >= 2 and <= 200 &&
            snap != _settings.SnapGridSize)
        {
            _settings.SnapGridSize = snap;
            changed = true;
        }

        bool strict = StrictDefaultCheck.IsChecked == true;
        if (strict != _settings.StrictLayoutDefault)
        {
            _settings.StrictLayoutDefault = strict;
            changed = true;
        }
        bool confirm = ConfirmCloseOthersCheck.IsChecked == true;
        if (confirm != _settings.ConfirmCloseOthers)
        {
            _settings.ConfirmCloseOthers = confirm;
            changed = true;
        }
        bool sounds = SoundCheck.IsChecked == true;
        if (sounds != _settings.InterfaceSounds)
        {
            _settings.InterfaceSounds = sounds;
            Sfx.Enabled = sounds;
            changed = true;
        }

        if (changed) _settings.Save();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.DataDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.DataDir}\"") { UseShellExecute = true });
    }

    // ==================== Helpers ====================

    internal static string FriendlyName(string path)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription!;
                if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName!;
            }
        }
        catch
        {
            // Fall through to the file name.
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}

/// <summary>Row wrapper so app lists can show icon + layout chip without INPC machinery.</summary>
public sealed class AppRow
{
    public AppEntry Entry { get; private init; } = null!;
    public BitmapSource? Icon { get; private init; }

    public string Name => Entry.Name;
    public string PathText => Entry.Path;
    public string LayoutText => Entry.Window is { } w
        ? (w.IsMaximized ? $"Mon {w.Monitor + 1} · max" : $"Mon {w.Monitor + 1} · {w.Width}×{w.Height}")
        : "";
    // Nothing saved yet for this app isn't worth a badge — only show the chip
    // once there's an actual position to report.
    public Visibility LayoutChipVisibility => Entry.Window != null ? Visibility.Visible : Visibility.Collapsed;

    public static AppRow From(AppEntry entry) =>
        new() { Entry = entry, Icon = IconLoader.GetSmallIcon(entry.Path) };
}

/// <summary>Compact project list row: name, summary, last-used, icon cluster.</summary>
public sealed class ProjectRow
{
    public required DeskifyProject Project { get; init; }
    public string Name => Project.Name;
    public string SummaryText => Project.SummaryText;
    public string LastUsedText => Project.LastUsedText;
    public bool Pinned => Project.Pinned;
    public Visibility PinnedVisibility => Pinned ? Visibility.Visible : Visibility.Collapsed;
    public List<BitmapSource> Icons { get; init; } = [];

    public static ProjectRow From(DeskifyProject project) =>
        new() { Project = project, Icons = IconsFor(project) };

    /// <summary>Up to five app icons for the cluster preview.</summary>
    public static List<BitmapSource> IconsFor(DeskifyProject project) =>
        project.Apps
            .Select(a => IconLoader.GetSmallIcon(a.Path))
            .Where(i => i != null)
            .Cast<BitmapSource>()
            .Take(5)
            .ToList();
}
