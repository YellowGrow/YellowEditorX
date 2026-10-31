namespace YellowEditorX.Core;

public sealed class DbEntry
{
    public string Path { get; }
    public string Title { get; }
    public string Detail { get; }

    public DbEntry(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        // Each archetype has its own folder, so the folder name (겔지, 결연희...) says more than the file name.
        string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path)) ?? "";
        Title = folder.Length > 0 ? folder : System.IO.Path.GetFileNameWithoutExtension(Path);
        Detail = System.IO.Path.GetFileName(Path);
    }

    public override string ToString() => Title;
}

/// <summary>Helpers over a set of card databases.</summary>
public static class Workspace
{
    /// <summary>Every .cdb under a folder (for example 오리카, whose sub folders each hold one archetype).</summary>
    public static List<DbEntry> Scan(string root, int maxDepth = 3)
    {
        var list = new List<DbEntry>();
        if (!Directory.Exists(root)) return list;
        Walk(root, 0, maxDepth, list);
        list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCulture));
        return list;
    }

    static void Walk(string dir, int depth, int maxDepth, List<DbEntry> list)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.cdb"))
                list.Add(new DbEntry(f));
            if (depth >= maxDepth) return;
            foreach (var d in Directory.GetDirectories(dir))
            {
                string name = System.IO.Path.GetFileName(d);
                if (name.StartsWith(".") || name.Equals("pics", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("script", StringComparison.OrdinalIgnoreCase))
                    continue;
                Walk(d, depth + 1, maxDepth, list);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>Other databases that already use the id, as "title" strings.</summary>
    public static List<string> FindIdElsewhere(IEnumerable<DbEntry> dbs, string currentPath, long id)
    {
        var hits = new List<string>();
        foreach (var db in dbs)
        {
            if (string.Equals(db.Path, System.IO.Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (CardDb.ReadIds(db.Path).Contains(id)) hits.Add(db.Title);
            }
            catch { }
        }
        return hits;
    }

    /// <summary>The first unused code after start (or after the largest code when start is 0).</summary>
    public static long NextFreeCode(ICollection<long> used, long start)
    {
        if (start <= 0)
            start = used.Count > 0 ? used.Max() : 100000000 - 1;
        long id = start + 1;
        while (used.Contains(id)) id++;
        return id;
    }
}
