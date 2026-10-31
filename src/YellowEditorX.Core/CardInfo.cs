using System.Globalization;

namespace YellowEditorX.Core;

public sealed record InfoItem(long Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>The value/name lists from DataEditorX's cardinfo_xxx.txt ("##section" then "0xVALUE\ttext" lines).</summary>
public sealed class CardInfo
{
    public List<InfoItem> Attributes = new(), Races = new(), Types = new(), Categories = new(), Setnames = new();

    public static CardInfo Load(string path) => Parse(File.ReadAllText(path));

    public static CardInfo Parse(string text)
    {
        var info = new CardInfo();
        List<InfoItem>? cur = null;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("##"))
            {
                cur = line[2..].Trim().ToLowerInvariant() switch
                {
                    "attribute" => info.Attributes,
                    "race" => info.Races,
                    "type" => info.Types,
                    "category" => info.Categories,
                    "setname" => info.Setnames,
                    _ => null,
                };
                continue;
            }
            if (cur == null || line.StartsWith("#")) continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (!TryParseValue(line[..tab].Trim(), out long v)) continue;
            string name = line[(tab + 1)..].Trim();
            if (name.Length == 0 || name.StartsWith("NULL")) continue;
            cur.Add(new InfoItem(v, name));
        }
        // "(속성)" style placeholder entries at 0 are kept as the "none" choice; -1 entries are dropped.
        info.Setnames.RemoveAll(s => s.Value < 0);
        return info;
    }

    public static bool TryParseValue(string s, out long v)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(s[2..], NumberStyles.HexNumber, null, out v);
        return long.TryParse(s, out v);
    }

    public string Name(List<InfoItem> list, long value)
    {
        foreach (var i in list)
            if (i.Value == value) return i.Text;
        return "";
    }

    public string TypeName(long bit) => Name(Types, bit);

    /// <summary>Short type description for the card list, e.g. "펜듈럼/효과" or "마법·속공".</summary>
    public string TypeText(Card c)
    {
        if (c.IsType(CardTypes.Monster))
        {
            long[] kinds = { CardTypes.Link, CardTypes.Xyz, CardTypes.Synchro, CardTypes.Fusion,
                             CardTypes.Ritual, CardTypes.Token, CardTypes.Effect };
            string s = TypeName(CardTypes.Normal);
            foreach (var k in kinds)
                if (c.IsType(k)) { s = TypeName(k); break; }
            if (c.IsType(CardTypes.Pendulum)) s = TypeName(CardTypes.Pendulum) + "/" + s;
            return s.Trim();
        }
        long main = CardTypes.MainOf(c.Type);
        if (main == 0) return "";
        long[] subs = main == CardTypes.Spell ? CardTypes.SpellSubs : CardTypes.TrapSubs;
        string str = TypeName(main).Trim();
        foreach (var k in subs)
            if (c.IsType(k)) return str + "·" + TypeName(k).Trim();
        return str;
    }

    public static string LevelText(Card c)
    {
        if (!c.IsType(CardTypes.Monster)) return "";
        long lv = c.Level & 0xff;
        if (c.IsType(CardTypes.Link)) return "L" + lv;
        if (c.IsType(CardTypes.Xyz)) return "R" + lv;
        return "★" + lv;
    }

    public static string AtkDefText(Card c)
    {
        if (!c.IsType(CardTypes.Monster)) return "";
        string atk = StatText(c.Atk);
        return c.IsType(CardTypes.Link) ? atk + " / -" : atk + " / " + StatText(c.Def);
    }

    public static string StatText(long v) => v == -2 ? "?" : v.ToString();
}
