namespace YellowEditorX.Core;

/// <summary>One row of datas + texts in a .cdb file.</summary>
public sealed class Card
{
    public const int StrCount = 16;
    public const int SetcodeCount = 4;
    /// <summary>The rule every card saved from this editor gets (오리카).</summary>
    public const long RuleCustom = 0x20;
    /// <summary>CyanPro SCOPE_HIDDEN: the deck editor doesn't list the card.</summary>
    public const long ScopeHidden = 0x1000;

    public long Id;
    public long Ot;
    public long Alias;
    public long Setcode;
    public long Type;
    public long Atk;
    public long Def;
    public long Level;
    public long Race;
    public long Attribute;
    public long Category;
    public string Name = "";
    public string Desc = "";
    public string[] Str = NewStrings();
    /// <summary>yyyy-MM-dd or empty. Kept in the card_dates table, which YGOPro never reads.</summary>
    public string Created = "";
    public string Modified = "";

    public static string[] NewStrings()
    {
        var s = new string[StrCount];
        Array.Fill(s, "");
        return s;
    }

    public bool IsType(long t) => (Type & t) != 0;

    public long[] GetSetcodes()
    {
        var r = new long[SetcodeCount];
        for (int i = 0; i < SetcodeCount; i++)
            r[i] = (Setcode >> (i * 16)) & 0xffff;
        return r;
    }

    public static long PackSetcodes(IReadOnlyList<long> codes)
    {
        long v = 0;
        for (int i = 0; i < Math.Min(codes.Count, SetcodeCount); i++)
            v |= (codes[i] & 0xffff) << (i * 16);
        return v;
    }

    public Card Clone()
    {
        var c = (Card)MemberwiseClone();
        c.Str = (string[])Str.Clone();
        return c;
    }

    public bool SameAs(Card? o)
    {
        if (o is null) return false;
        if (Id != o.Id || Ot != o.Ot || Alias != o.Alias || Setcode != o.Setcode || Type != o.Type
            || Atk != o.Atk || Def != o.Def || Level != o.Level || Race != o.Race
            || Attribute != o.Attribute || Category != o.Category || Name != o.Name || Desc != o.Desc
            || Created != o.Created || Modified != o.Modified)
            return false;
        for (int i = 0; i < StrCount; i++)
            if (Str[i] != o.Str[i]) return false;
        return true;
    }

    /// <summary>
    /// Script id: a card whose alias is a near code (alt art) shares the alias's script.
    /// A 키 card's unlocked version also has the locked card as alias, but it has a script of its own.
    /// </summary>
    public long ScriptId => Alias != 0 && Math.Abs(Id - Alias) < 10 && !IsType(CardTypes.Key) ? Alias : Id;
}
