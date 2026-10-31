namespace YellowEditorX.Core;

/// <summary>Image and script files that belong to a card, next to its .cdb (pics/, pics/thumbnail/, pics/field/, script/).</summary>
public sealed class CardFiles
{
    public readonly string Root;
    public CardFiles(string cdbPath) => Root = Path.GetDirectoryName(Path.GetFullPath(cdbPath))!;

    public string Picture(long id) => Path.Combine(Root, "pics", id + ".jpg");
    public string Thumbnail(long id) => Path.Combine(Root, "pics", "thumbnail", id + ".jpg");
    public string FieldPicture(long id) => Path.Combine(Root, "pics", "field", id + ".png");
    public string Script(long id) => Path.Combine(Root, "script", "c" + id + ".lua");
    public string ScriptFolder => Path.Combine(Root, "script");

    public string[] All(long id) => new[] { Picture(id), Thumbnail(id), FieldPicture(id), Script(id) };

    /// <summary>Content of a file that was removed, so it can be put back.</summary>
    public sealed record Saved(string Path, byte[] Data);

    /// <summary>Moves (or copies) every existing file of oldId to newId. Returns the files created.</summary>
    public static List<string> Transfer(CardFiles from, long oldId, CardFiles to, long newId, bool move, bool overwrite)
    {
        var created = new List<string>();
        var src = from.All(oldId);
        var dst = to.All(newId);
        for (int i = 0; i < src.Length; i++)
        {
            if (!File.Exists(src[i]) || string.Equals(Path.GetFullPath(src[i]), Path.GetFullPath(dst[i]), StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(dst[i]) && !overwrite) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dst[i])!);
            if (move) File.Move(src[i], dst[i], overwrite);
            else File.Copy(src[i], dst[i], overwrite);
            created.Add(dst[i]);
        }
        return created;
    }

    /// <summary>Reads then deletes every file of the card; the returned content allows undo.</summary>
    public List<Saved> Remove(long id)
    {
        var saved = new List<Saved>();
        foreach (var f in All(id))
        {
            if (!File.Exists(f)) continue;
            saved.Add(new Saved(f, File.ReadAllBytes(f)));
            File.Delete(f);
        }
        return saved;
    }

    public static void Restore(IEnumerable<Saved> files)
    {
        foreach (var s in files)
        {
            if (File.Exists(s.Path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(s.Path)!);
            File.WriteAllBytes(s.Path, s.Data);
        }
    }

    /// <summary>Creates the script file from the same template the old editor used, if it doesn't exist.</summary>
    public string EnsureScript(Card c, out bool created)
    {
        long id = c.ScriptId;
        string lua = Script(id);
        created = false;
        if (File.Exists(lua)) return lua;
        Directory.CreateDirectory(Path.GetDirectoryName(lua)!);
        File.WriteAllText(lua,
            "--" + c.Name + "\r\nlocal s,id=GetID()\r\nfunction s.initial_effect(c)\r\n\t\r\nend\r\n",
            new System.Text.UTF8Encoding(false));
        created = true;
        return lua;
    }
}
