using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deskify.Models;

/// <summary>A saved workspace: apps, folders, urls and their window layout.</summary>
public sealed class DeskifyProject
{
    public static readonly string[] DefaultOrder = ["apps", "urls", "folders"];

    [JsonPropertyName("name")] public string Name { get; set; } = "New Project";
    [JsonPropertyName("apps")] public List<AppEntry> Apps { get; set; } = [];
    [JsonPropertyName("folders")] public List<string> Folders { get; set; } = [];
    [JsonPropertyName("urls")] public List<string> Urls { get; set; } = [];

    /// <summary>Optional group order, e.g. ["apps","urls","folders"]. Null = default.</summary>
    [JsonPropertyName("launchOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LaunchOrder { get; set; }

    /// <summary>Strict = verify + re-apply positions after launch; soft = best effort.</summary>
    [JsonPropertyName("strictLayout")] public bool StrictLayout { get; set; }

    [JsonPropertyName("lastUsed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastUsed { get; set; }

    /// <summary>Free-form reminders — "things to do" for this workspace.</summary>
    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    /// <summary>Pinned projects sort first in the sidebar/project list.</summary>
    [JsonPropertyName("pinned")] public bool Pinned { get; set; }

    [JsonIgnore] public string? FilePath { get; set; }

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveLaunchOrder =>
        LaunchOrder is { Count: > 0 } order ? order : DefaultOrder;

    [JsonIgnore]
    public string LastUsedText =>
        LastUsed is { } t ? $"Last used {t.ToLocalTime():yyyy-MM-dd HH:mm}" : "Never launched";

    [JsonIgnore]
    public string SummaryText
    {
        get
        {
            var parts = new List<string>(3);
            if (Apps.Count > 0) parts.Add($"{Apps.Count} app{(Apps.Count == 1 ? "" : "s")}");
            if (Urls.Count > 0) parts.Add($"{Urls.Count} site{(Urls.Count == 1 ? "" : "s")}");
            if (Folders.Count > 0) parts.Add($"{Folders.Count} folder{(Folders.Count == 1 ? "" : "s")}");
            return parts.Count == 0 ? "Empty project" : string.Join("  ·  ", parts);
        }
    }

    public DeskifyProject Clone()
    {
        var json = JsonSerializer.Serialize(this, ProjectStoreJson.Options);
        var copy = JsonSerializer.Deserialize<DeskifyProject>(json, ProjectStoreJson.Options)!;
        copy.FilePath = FilePath;
        return copy;
    }
}

public sealed class AppEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";

    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Args { get; set; }

    [JsonPropertyName("window")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WindowLayout? Window { get; set; }

    /// <summary>True for File Explorer/browser entries auto-added alongside a
    /// folder or website — they exist so that window gets layout tracking, but
    /// they're opened via the Folders/Urls launch group, not launched independently
    /// (which would open a second, duplicate window).</summary>
    [JsonPropertyName("autoLinked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoLinked { get; set; }
}

/// <summary>Saved window placement. X/Y are physical pixels relative to the
/// target monitor's top-left corner, so layouts survive monitor rearrangement.</summary>
public sealed class WindowLayout
{
    [JsonPropertyName("monitor")] public int Monitor { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    /// <summary>"normal" or "maximized".</summary>
    [JsonPropertyName("state")] public string State { get; set; } = "normal";

    [JsonIgnore] public bool IsMaximized => string.Equals(State, "maximized", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Shared serializer options for all project/settings JSON.</summary>
public static class ProjectStoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
