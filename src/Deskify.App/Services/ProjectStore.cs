using System.IO;
using System.Text.Json;
using Deskify.Models;

namespace Deskify.Services;

/// <summary>JSON persistence: one file per project in %APPDATA%\Deskify\projects.</summary>
public static class ProjectStore
{
    public static string ProjectsDir => Path.Combine(AppSettings.DataDir, "projects");

    public static List<DeskifyProject> LoadAll()
    {
        var projects = new List<DeskifyProject>();
        try
        {
            Directory.CreateDirectory(ProjectsDir);
            foreach (var file in Directory.EnumerateFiles(ProjectsDir, "*.json"))
            {
                try
                {
                    var project = JsonSerializer.Deserialize<DeskifyProject>(File.ReadAllText(file), ProjectStoreJson.Options);
                    if (project != null)
                    {
                        project.FilePath = file;
                        // Self-heal: older or hand-edited project files can be missing
                        // the hidden entries that let Edit Layout/Launch find a window
                        // for each folder or website (see LaunchEngine.SyncAutoLinkedEntries).
                        // Persist the fix so it sticks instead of re-diffing every launch.
                        int before = project.Apps.Count;
                        LaunchEngine.SyncAutoLinkedEntries(project);
                        if (project.Apps.Count != before) Save(project);
                        projects.Add(project);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Skipping unreadable project file {file}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to enumerate projects", ex);
        }

        // Most recently used first, then alphabetical.
        projects.Sort((a, b) =>
        {
            int cmp = Nullable.Compare(b.LastUsed, a.LastUsed);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return projects;
    }

    /// <summary>Writes the project file. Never throws — a failure here (locked
    /// file, full disk, permissions) shouldn't take down whatever the caller was
    /// in the middle of doing, and shouldn't look identical to a real success.
    /// Callers that report per-item results (e.g. Save Layout) must check this
    /// and say plainly that the save to disk itself failed, distinct from any
    /// individual item that couldn't be captured.</summary>
    public static bool Save(DeskifyProject project)
    {
        try
        {
            Directory.CreateDirectory(ProjectsDir);
            project.FilePath ??= UniquePath(project.Name);
            File.WriteAllText(project.FilePath, JsonSerializer.Serialize(project, ProjectStoreJson.Options));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save project \"{project.Name}\"", ex);
            return false;
        }
    }

    /// <summary>Deletes the project file. Returns false if the delete failed
    /// (locked file, permissions) so the caller can keep the project in the list
    /// instead of showing it gone while the file still exists on disk.</summary>
    public static bool Delete(DeskifyProject project)
    {
        try
        {
            if (project.FilePath != null && File.Exists(project.FilePath))
                File.Delete(project.FilePath);
            project.FilePath = null;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to delete project \"{project.Name}\"", ex);
            return false;
        }
    }

    private static string UniquePath(string name)
    {
        var slug = Slug(name);
        var path = Path.Combine(ProjectsDir, slug + ".json");
        int n = 2;
        while (File.Exists(path))
            path = Path.Combine(ProjectsDir, $"{slug}-{n++}.json");
        return path;
    }

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "project" : slug;
    }
}
