using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deskify.Models;

namespace Deskify.Services;

/// <summary>Autosaves an in-progress wizard session (new project or edit) when
/// the app closes mid-edit, so nothing typed is lost. One draft at a time —
/// restored into the wizard on next startup, then cleared. Saving or cancelling
/// the wizard normally also clears it, so drafts never resurrect stale edits.</summary>
public static class DraftStore
{
    private static string DraftPath => Path.Combine(AppSettings.DataDir, "draft.json");

    public sealed class Draft
    {
        [JsonPropertyName("project")] public DeskifyProject Project { get; set; } = new();
        [JsonPropertyName("isNew")] public bool IsNew { get; set; }
        [JsonPropertyName("step")] public int Step { get; set; }
        /// <summary>Original project file when the draft is an edit of an existing
        /// project (DeskifyProject.FilePath itself is [JsonIgnore], so it's carried
        /// here) — lets the restored wizard save back over the right file.</summary>
        [JsonPropertyName("filePath")] public string? FilePath { get; set; }
    }

    public static void Save(Draft draft)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            File.WriteAllText(DraftPath, JsonSerializer.Serialize(draft, ProjectStoreJson.Options));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to autosave wizard draft", ex);
        }
    }

    public static Draft? Load()
    {
        try
        {
            if (!File.Exists(DraftPath)) return null;
            return JsonSerializer.Deserialize<Draft>(File.ReadAllText(DraftPath), ProjectStoreJson.Options);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load wizard draft", ex);
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(DraftPath)) File.Delete(DraftPath);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to clear wizard draft", ex);
        }
    }
}
