using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using YellowEditorX.Core;

namespace YellowEditorX;

/// <summary>Kept in settings.json next to the exe, so the program stays portable.</summary>
public sealed class Settings
{
    /// <summary>The cdb files in the list on the left, in the order they were opened.</summary>
    public List<string> OpenDbs { get; set; } = new();
    public string? LastDb { get; set; }
    public string? CardInfo { get; set; }
    public double Zoom { get; set; } = 1.0;
    public double ListWidth { get; set; } = 430;
    public double StringsWidth { get; set; } = 0;
    public bool DeleteWithFiles { get; set; } = true;
    /// <summary>제작자 of the last saved card, filled into new cards.</summary>
    public string? LastMaker { get; set; }
    /// <summary>Read new card groups, races and attributes from the CyanPro GitHub repositories at start.</summary>
    public bool GitHubSync { get; set; } = true;
    /// <summary>Look for a new YellowEditorX release at start.</summary>
    public bool UpdateCheck { get; set; } = true;
    /// <summary>A version the user chose to skip.</summary>
    public string? SkipVersion { get; set; }
    /// <summary>Set while the updater replaces the files, so the next start can say how it went.</summary>
    public string? UpdatingTo { get; set; }

    // Older versions listed every cdb under a workspace folder plus extra files; both become OpenDbs.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Workspace { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? ExtraDbs { get; set; }

    static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        s.MigrateWorkspace();
        return s;
    }

    internal void MigrateWorkspace()
    {
        // The last cdb used stays in the list even if it was outside the workspace.
        if ((Workspace != null || ExtraDbs != null) && LastDb != null && File.Exists(LastDb)) Add(LastDb);
        // A workspace on a drive that isn't there right now is kept for the next start.
        if (Workspace != null && Directory.Exists(Workspace))
        {
            foreach (var db in Core.Workspace.Scan(Workspace)) Add(db.Path);
            Workspace = null;
        }
        if (ExtraDbs != null)
        {
            foreach (var p in ExtraDbs) Add(p);
            ExtraDbs = null;
        }
    }

    public void Add(string path)
    {
        path = Path.GetFullPath(path);
        if (!OpenDbs.Any(p => string.Equals(Path.GetFullPath(p), path, StringComparison.OrdinalIgnoreCase))) OpenDbs.Add(path);
    }

    public void Remove(string path) =>
        OpenDbs.RemoveAll(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

    /// <summary>The self test runs on copies and must not touch the real settings file.</summary>
    [JsonIgnore] public bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        }
        catch { }
    }
}
