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

    /// <summary>
    /// The orica (custom) type bits the card has, in list order: 액세스, 페어링, 하이잭, 기프트, then any type added
    /// later on a bit above 링크 (from the cardinfo/GitHub list), then 키-해금됨 or else 키 (해금됨 is always a 키 too).
    /// </summary>
    List<long> CustomTypes(Card c)
    {
        var bits = new List<long>(CardTypes.CustomOrder);
        foreach (var t in Types.Select(t => t.Value).Where(v => v > CardTypes.Link).OrderBy(v => v))
            if (!bits.Contains(t)) bits.Add(t);
        var res = bits.Where(b => c.IsType(b)).ToList();
        if (c.IsType(CardTypes.Unlocked)) res.Add(CardTypes.Unlocked);
        else if (c.IsType(CardTypes.Key)) res.Add(CardTypes.Key);
        return res;
    }

    string Names(IEnumerable<long> bits, string sep) =>
        string.Join(sep, bits.Select(b => TypeName(b).Trim()).Where(s => s.Length > 0));

    /// <summary>Short type description for the card list, e.g. "펜듈럼/효과", "페어링", "액세스/하이잭" or "마법·속공".</summary>
    public string TypeText(Card c)
    {
        var custom = CustomTypes(c);
        if (c.IsType(CardTypes.Monster))
        {
            var parts = new List<long>();
            // 액세스 and 페어링 cards carry the 융합 bit as well; the orica kind is the one to show then.
            bool oricaKind = custom.Any(b => b != CardTypes.Key && b != CardTypes.Unlocked);
            long[] kinds = { CardTypes.Link, CardTypes.Xyz, CardTypes.Synchro, CardTypes.Fusion, CardTypes.Ritual };
            foreach (var k in kinds)
                if (c.IsType(k) && !(k == CardTypes.Fusion && oricaKind)) { parts.Add(k); break; }
            parts.AddRange(custom);
            if (parts.Count == 0)
                parts.Add(c.IsType(CardTypes.Token) ? CardTypes.Token
                        : c.IsType(CardTypes.Effect) ? CardTypes.Effect : CardTypes.Normal);
            string s = Names(parts, "/");
            if (c.IsType(CardTypes.Pendulum)) s = TypeName(CardTypes.Pendulum).Trim() + "/" + s;
            return s;
        }
        long main = CardTypes.MainOf(c.Type);
        if (main == 0) return "";
        long[] subs = main == CardTypes.Spell ? CardTypes.SpellSubs : CardTypes.TrapSubs;
        var shown = new List<long> { main };
        long sub = subs.FirstOrDefault(k => c.IsType(k));
        if (sub != 0) shown.Add(sub);
        shown.AddRange(custom.Where(b => !shown.Contains(b)));
        return Names(shown, "·");
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
