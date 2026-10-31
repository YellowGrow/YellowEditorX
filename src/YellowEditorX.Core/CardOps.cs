namespace YellowEditorX.Core;

/// <summary>Database changes the editor makes. Each returns a history entry that undoes it.</summary>
public static class CardOps
{
    /// <summary>Inserts or overwrites the card under its own code.</summary>
    public static HistoryEntry Save(string db, Card c)
    {
        var h = new HistoryEntry { Description = $"저장: {c.Name}" };
        h.Capture(db, new[] { c.Id });
        CardDb.Write(db, new[] { c });
        return h;
    }

    /// <summary>
    /// Saves an edited card under a new code. move: the old code is deleted and its image/script are renamed;
    /// otherwise the old card stays and its files are copied.
    /// </summary>
    public static HistoryEntry ChangeCode(string db, long oldId, Card c, bool move)
    {
        var h = new HistoryEntry { Description = move ? $"코드 변경: {oldId} → {c.Id}" : $"복사본 저장: {c.Id}" };
        h.Capture(db, move ? new[] { oldId, c.Id } : new[] { c.Id });
        var files = new CardFiles(db);
        var created = CardFiles.Transfer(files, oldId, files, c.Id, move, overwrite: false);
        if (move)
            foreach (var to in created)
                h.MovedFiles.Add((FromOf(files, oldId, c.Id, to), to));
        else
            h.CreatedFiles.AddRange(created);
        CardDb.Write(db, new[] { c }, move ? new[] { oldId } : null);
        return h;
    }

    static string FromOf(CardFiles f, long oldId, long newId, string to)
    {
        var news = f.All(newId);
        var olds = f.All(oldId);
        int i = Array.FindIndex(news, n => string.Equals(n, to, StringComparison.OrdinalIgnoreCase));
        return olds[i];
    }

    public static HistoryEntry Delete(string db, IReadOnlyList<Card> cards, bool withFiles)
    {
        var h = new HistoryEntry { Description = cards.Count == 1 ? $"삭제: {cards[0].Name}" : $"{cards.Count}장 삭제" };
        h.Capture(db, cards.Select(c => c.Id));
        if (withFiles)
        {
            var files = new CardFiles(db);
            foreach (var c in cards) h.RemovedFiles.AddRange(files.Remove(c.Id));
        }
        CardDb.Write(db, Array.Empty<Card>(), cards.Select(c => c.Id));
        return h;
    }

    /// <summary>
    /// Copies (or moves) cards with their image and script to another database.
    /// Cards whose code already exists there are overwritten only when overwrite is true.
    /// </summary>
    public static HistoryEntry CopyTo(string fromDb, IReadOnlyList<Card> cards, string toDb, bool overwrite, bool move, out int copied)
    {
        var h = new HistoryEntry { Description = (move ? "이동: " : "복사: ") + $"{cards.Count}장 → {Path.GetFileName(toDb)}" };
        var existing = CardDb.ReadIds(toDb);
        var todo = cards.Where(c => overwrite || !existing.Contains(c.Id)).ToList();
        copied = todo.Count;
        if (todo.Count == 0) return h;
        h.Capture(toDb, todo.Select(c => c.Id));
        if (move) h.Capture(fromDb, todo.Select(c => c.Id));
        var src = new CardFiles(fromDb);
        var dst = new CardFiles(toDb);
        foreach (var c in todo)
        {
            string[] s = src.All(c.Id), d = dst.All(c.Id);
            // Files that get overwritten are kept so undo can put them back.
            for (int i = 0; i < d.Length; i++)
                if (overwrite && File.Exists(d[i]) && File.Exists(s[i]))
                    h.RemovedFiles.Add(new CardFiles.Saved(d[i], File.ReadAllBytes(d[i])));
            foreach (var to in CardFiles.Transfer(src, c.Id, dst, c.Id, move, overwrite))
            {
                int i = Array.FindIndex(d, x => string.Equals(x, to, StringComparison.OrdinalIgnoreCase));
                if (move) h.MovedFiles.Add((s[i], to));
                else h.CreatedFiles.Add(to);
            }
        }
        CardDb.Write(toDb, todo);
        if (move) CardDb.Write(fromDb, Array.Empty<Card>(), todo.Select(c => c.Id));
        return h;
    }

    /// <summary>Replaces the card image; write() is given the picture path and the thumbnail path (null when there is no thumbnail folder).</summary>
    public static HistoryEntry ReplaceImage(string db, long id, Action<string, string?> write)
    {
        var h = new HistoryEntry { Description = $"이미지 변경: {id}" };
        var files = new CardFiles(db);
        string pic = files.Picture(id), thumb = files.Thumbnail(id);
        bool hasThumbs = Directory.Exists(Path.GetDirectoryName(thumb));
        foreach (var f in hasThumbs ? new[] { pic, thumb } : new[] { pic })
        {
            if (File.Exists(f)) h.RemovedFiles.Add(new CardFiles.Saved(f, File.ReadAllBytes(f)));
            else h.CreatedFiles.Add(f);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(pic)!);
        // Undo first deletes created files, then restores removed ones: for overwritten files that means
        // deleting the new image and writing the old bytes back.
        foreach (var r in h.RemovedFiles) h.CreatedFiles.Add(r.Path);
        write(pic, hasThumbs ? thumb : null);
        return h;
    }
}
