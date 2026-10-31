namespace YellowEditorX.Core;

/// <summary>One undoable change: the rows as they were before, and the files that were created, moved or removed.</summary>
public sealed class HistoryEntry
{
    public string Description = "";
    /// <summary>db path → (id → row before the change, null when the row didn't exist).</summary>
    public readonly Dictionary<string, Dictionary<long, Card?>> Before = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> CreatedFiles = new();
    public readonly List<(string From, string To)> MovedFiles = new();
    public readonly List<CardFiles.Saved> RemovedFiles = new();

    /// <summary>Remembers the current rows of the ids before they are changed (only the first time per id).</summary>
    public void Capture(string db, IEnumerable<long> ids)
    {
        if (!Before.TryGetValue(db, out var map)) Before[db] = map = new();
        foreach (long id in ids)
            if (!map.ContainsKey(id)) map[id] = CardDb.Read(db, id);
    }

    public void Undo()
    {
        foreach (var f in CreatedFiles)
            if (File.Exists(f)) File.Delete(f);
        for (int i = MovedFiles.Count - 1; i >= 0; i--)
        {
            var (from, to) = MovedFiles[i];
            if (File.Exists(to) && !File.Exists(from))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(from)!);
                File.Move(to, from);
            }
        }
        CardFiles.Restore(RemovedFiles);
        foreach (var (db, map) in Before)
        {
            var restore = map.Values.Where(c => c != null).Select(c => c!).ToList();
            var delete = map.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
            CardDb.Write(db, restore, delete);
        }
    }
}

public sealed class History
{
    readonly List<HistoryEntry> entries = new();
    public const int Max = 50;
    public int Count => entries.Count;
    public string? Next => entries.Count > 0 ? entries[^1].Description : null;

    public void Push(HistoryEntry e)
    {
        entries.Add(e);
        if (entries.Count > Max) entries.RemoveAt(0);
    }

    public HistoryEntry? Undo()
    {
        if (entries.Count == 0) return null;
        var e = entries[^1];
        entries.RemoveAt(entries.Count - 1);
        e.Undo();
        return e;
    }
}
