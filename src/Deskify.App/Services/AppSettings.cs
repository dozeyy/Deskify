using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deskify.Models;

namespace Deskify.Services;

public sealed class AppSettings
{
    [JsonPropertyName("detectTimeoutSeconds")] public int DetectTimeoutSeconds { get; set; } = 15;
    [JsonPropertyName("retryIntervalMs")] public int RetryIntervalMs { get; set; } = 500;
    /// <summary>New projects start with Strict Layout on: self-restoring single-instance
    /// apps (Discord, browsers, FL Studio) re-assert their own remembered window bounds a
    /// few seconds after launch, and the longer strict settle window is what reliably wins
    /// that back to the project's saved position. Users can still turn it off per-project.</summary>
    [JsonPropertyName("strictLayoutDefault")] public bool StrictLayoutDefault { get; set; } = true;

    /// <summary>Grid size (px) used when "Snap" is on in the layout editor. 0 = no snapping.</summary>
    [JsonPropertyName("snapGridSize")] public int SnapGridSize { get; set; } = 16;

    /// <summary>Color theme name — see <see cref="ThemeManager.Available"/>. Applies instantly.</summary>
    [JsonPropertyName("themeName")] public string ThemeName { get; set; } = ThemeManager.Default;

    /// <summary>Whether "Close Other Apps" shows a confirmation checklist first.
    /// False once the user checks "Don't ask again" on that dialog.</summary>
    [JsonPropertyName("confirmCloseOthers")] public bool ConfirmCloseOthers { get; set; } = true;

    /// <summary>Exe names (lowercase, e.g. "flstudio.exe") that Deskify must never close
    /// on a workspace switch or "Close Other Apps" — a safety list for apps the user
    /// keeps open across workspaces or that hold unsaved work. Matched by the owning
    /// PROCESS (exe name plus process-tree ancestry), never the window title, so an app
    /// stays protected across title changes, updates (Discord's transient "Update.exe"
    /// window), different tabs, and foreground/background — see WindowCloser.IsNeverClose.</summary>
    [JsonPropertyName("neverCloseApps")] public List<string> NeverCloseApps { get; set; } = [];

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deskify");

    private static string SettingsPath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), ProjectStoreJson.Options);
                if (loaded != null)
                {
                    // Sanitize hand-edited values.
                    loaded.DetectTimeoutSeconds = Math.Clamp(loaded.DetectTimeoutSeconds, 1, 120);
                    loaded.RetryIntervalMs = Math.Clamp(loaded.RetryIntervalMs, 100, 5000);
                    loaded.SnapGridSize = loaded.SnapGridSize <= 0 ? 0 : Math.Clamp(loaded.SnapGridSize, 2, 200);
                    if (!ThemeManager.Available.Contains(loaded.ThemeName)) loaded.ThemeName = ThemeManager.Default;
                    loaded.NeverCloseApps = (loaded.NeverCloseApps ?? [])
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(NeverCloseMatch.Normalize)
                        .Where(n => n.Length > 0)
                        .Distinct()
                        .ToList();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load settings, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, ProjectStoreJson.Options));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }
}
