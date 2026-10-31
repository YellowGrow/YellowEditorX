using System.Text;
using Microsoft.Data.Sqlite;

namespace YellowEditorX.Core;

/// <summary>Reads and writes YGOPro .cdb files (SQLite with datas and texts tables).</summary>
public static class CardDb
{
    const string CreateSql =
        "CREATE TABLE IF NOT EXISTS datas(id integer primary key,ot integer,alias integer,setcode integer," +
        "type integer,atk integer,def integer,level integer,race integer,attribute integer,category integer);" +
        "CREATE TABLE IF NOT EXISTS texts(id integer primary key,name text,desc text," +
        "str1 text,str2 text,str3 text,str4 text,str5 text,str6 text,str7 text,str8 text," +
        "str9 text,str10 text,str11 text,str12 text,str13 text,str14 text,str15 text,str16 text);";

    const string SelectSql =
        "SELECT d.id,d.ot,d.alias,d.setcode,d.type,d.atk,d.def,d.level,d.race,d.attribute,d.category," +
        "t.name,t.desc,t.str1,t.str2,t.str3,t.str4,t.str5,t.str6,t.str7,t.str8," +
        "t.str9,t.str10,t.str11,t.str12,t.str13,t.str14,t.str15,t.str16";

    /// <summary>
    /// Extra table for 제작일 / 수정일 (yyyy-MM-dd). YGOPro and the old DataEditorX only read datas and texts,
    /// so they never see it; it is made the first time a card with a date is saved.
    /// </summary>
    public const string DatesTable = "card_dates";
    const string CreateDatesSql = "CREATE TABLE IF NOT EXISTS card_dates(id integer primary key,created text,modified text)";

    static bool HasDates(SqliteConnection con)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='card_dates'";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    static SqliteConnection Open(string path, bool write)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = write ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
            // Without pooling the file is released as soon as we are done, so other
            // programs (YGOPro, the old editor, Explorer) never see it locked.
            Pooling = false,
        };
        var con = new SqliteConnection(cs.ToString());
        con.Open();
        return con;
    }

    public static void Create(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        using var con = new SqliteConnection(cs.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = CreateSql;
        cmd.ExecuteNonQuery();
    }

    public static List<Card> ReadAll(string path) => Query(path, "");

    public static Card? Read(string path, long id)
    {
        var list = Query(path, " WHERE d.id=$id", ("$id", id));
        return list.Count > 0 ? list[0] : null;
    }

    public static HashSet<long> ReadIds(string path)
    {
        var ids = new HashSet<long>();
        using var con = Open(path, false);
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT id FROM datas";
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    static List<Card> Query(string path, string where, params (string, object)[] args)
    {
        var list = new List<Card>();
        using var con = Open(path, false);
        using var cmd = con.CreateCommand();
        cmd.CommandText = HasDates(con)
            ? SelectSql + ",m.created,m.modified FROM datas d JOIN texts t ON d.id=t.id LEFT JOIN card_dates m ON m.id=d.id" + where
            : SelectSql + ",NULL,NULL FROM datas d JOIN texts t ON d.id=t.id" + where;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var c = new Card
            {
                Id = L(r, 0), Ot = L(r, 1), Alias = L(r, 2), Setcode = L(r, 3), Type = L(r, 4),
                Atk = L(r, 5), Def = L(r, 6), Level = L(r, 7), Race = L(r, 8), Attribute = L(r, 9),
                Category = L(r, 10),
                Name = S(r, 11),
                Desc = NormalizeNewlines(S(r, 12)),
            };
            for (int i = 0; i < Card.StrCount; i++)
                c.Str[i] = S(r, 13 + i);
            c.Created = S(r, 13 + Card.StrCount);
            c.Modified = S(r, 14 + Card.StrCount);
            list.Add(c);
        }
        return list;
    }

    static long L(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);
    static string S(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

    /// <summary>Card text is edited with Windows line breaks, like the old editor did.</summary>
    public static string NormalizeNewlines(string s) =>
        s.IndexOf('\n') < 0 ? s : s.Replace("\r\n", "\n").Replace("\n", "\r\n");

    /// <summary>Inserts or replaces the cards and deletes the given ids, all in one transaction.</summary>
    public static void Write(string path, IEnumerable<Card> upserts, IEnumerable<long>? deletes = null)
    {
        var cards = upserts.ToList();
        using var con = Open(path, true);
        bool dates = HasDates(con);
        using var tr = con.BeginTransaction();
        if (!dates && cards.Any(c => c.Created.Length > 0 || c.Modified.Length > 0))
        {
            using var create = con.CreateCommand();
            create.Transaction = tr;
            create.CommandText = CreateDatesSql;
            create.ExecuteNonQuery();
            dates = true;
        }
        if (deletes != null)
        {
            using var del = con.CreateCommand();
            del.Transaction = tr;
            del.CommandText = "DELETE FROM datas WHERE id=$id; DELETE FROM texts WHERE id=$id;" +
                (dates ? " DELETE FROM card_dates WHERE id=$id;" : "");
            var p = del.Parameters.Add("$id", SqliteType.Integer);
            foreach (long id in deletes)
            {
                p.Value = id;
                del.ExecuteNonQuery();
            }
        }
        using var data = con.CreateCommand();
        data.Transaction = tr;
        data.CommandText = "INSERT OR REPLACE INTO datas VALUES($id,$ot,$alias,$setcode,$type,$atk,$def,$level,$race,$attribute,$category)";
        using var text = con.CreateCommand();
        text.Transaction = tr;
        var sb = new StringBuilder("INSERT OR REPLACE INTO texts VALUES($id,$name,$desc");
        for (int i = 1; i <= Card.StrCount; i++) sb.Append(",$str").Append(i);
        text.CommandText = sb.Append(')').ToString();
        using var date = con.CreateCommand();
        date.Transaction = tr;
        foreach (var c in cards)
        {
            data.Parameters.Clear();
            data.Parameters.AddWithValue("$id", c.Id);
            data.Parameters.AddWithValue("$ot", c.Ot);
            data.Parameters.AddWithValue("$alias", c.Alias);
            data.Parameters.AddWithValue("$setcode", c.Setcode);
            data.Parameters.AddWithValue("$type", c.Type);
            data.Parameters.AddWithValue("$atk", c.Atk);
            data.Parameters.AddWithValue("$def", c.Def);
            data.Parameters.AddWithValue("$level", c.Level);
            data.Parameters.AddWithValue("$race", c.Race);
            data.Parameters.AddWithValue("$attribute", c.Attribute);
            data.Parameters.AddWithValue("$category", c.Category);
            data.ExecuteNonQuery();
            text.Parameters.Clear();
            text.Parameters.AddWithValue("$id", c.Id);
            text.Parameters.AddWithValue("$name", c.Name);
            text.Parameters.AddWithValue("$desc", c.Desc);
            for (int i = 0; i < Card.StrCount; i++)
                text.Parameters.AddWithValue("$str" + (i + 1), c.Str[i]);
            text.ExecuteNonQuery();
            if (!dates) continue;
            date.Parameters.Clear();
            date.Parameters.AddWithValue("$id", c.Id);
            if (c.Created.Length == 0 && c.Modified.Length == 0)
                date.CommandText = "DELETE FROM card_dates WHERE id=$id";
            else
            {
                date.CommandText = "INSERT OR REPLACE INTO card_dates VALUES($id,$created,$modified)";
                date.Parameters.AddWithValue("$created", c.Created);
                date.Parameters.AddWithValue("$modified", c.Modified);
            }
            date.ExecuteNonQuery();
        }
        tr.Commit();
    }
}
