using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace YellowEditorX.Core;

public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One check box / toggle for a bit flag.</summary>
public sealed class FlagOption : Notifier
{
    readonly Action<FlagOption> changed;
    bool isChecked;
    public long Value { get; }
    public string Text { get; }
    public FlagOption(long value, string text, Action<FlagOption> changed)
    {
        Value = value;
        Text = text;
        this.changed = changed;
    }
    public bool IsChecked
    {
        get => isChecked;
        set { if (Set(ref isChecked, value)) changed(this); }
    }
    internal void SetSilently(bool v) => Set(ref isChecked, v, nameof(IsChecked));
}

public sealed class StrSlot : Notifier
{
    readonly Action changed;
    string text = "";
    public int Index { get; }
    public string Label => Index.ToString();
    public StrSlot(int index, Action changed) { Index = index; this.changed = changed; }
    public string Text
    {
        get => text;
        set { if (Set(ref text, value ?? "")) changed(); }
    }
}

/// <summary>A setcode: hex text box and name drop-down that follow each other.</summary>
public sealed class SetcodeSlot : Notifier
{
    readonly Action changed;
    readonly IReadOnlyList<InfoItem> names;
    string hex = "";
    InfoItem? selected;
    bool syncing;
    public SetcodeSlot(IReadOnlyList<InfoItem> names, Action changed) { this.names = names; this.changed = changed; }
    public IReadOnlyList<InfoItem> Names => names;

    public long Value => long.TryParse(hex.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Trim()[2..] : hex.Trim(),
        NumberStyles.HexNumber, null, out long v) ? v & 0xffff : 0;

    public string Hex
    {
        get => hex;
        set
        {
            if (!Set(ref hex, value ?? "")) return;
            if (!syncing)
            {
                syncing = true;
                long v = Value;
                Selected = names.FirstOrDefault(n => n.Value == v);
                syncing = false;
            }
            changed();
        }
    }

    public InfoItem? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value)) return;
            if (!syncing && value != null)
            {
                syncing = true;
                Hex = value.Value == 0 ? "" : value.Value.ToString("x");
                syncing = false;
            }
        }
    }

    internal void Load(long v)
    {
        syncing = true;
        hex = v == 0 ? "" : v.ToString("x");
        Raise(nameof(Hex));
        selected = names.FirstOrDefault(n => n.Value == v);
        Raise(nameof(Selected));
        syncing = false;
    }
}

/// <summary>
/// Everything the editor shows for one card. Fields are kept as the user typed them;
/// ToCard() turns them back into a database row. Bits the editor has no control for are kept as they were.
/// </summary>
public sealed class EditorModel : Notifier
{
    public CardInfo Info { get; }
    public ObservableCollection<StrSlot> Strings { get; } = new();
    public List<FlagOption> SubTypes { get; } = new();
    public List<FlagOption> Markers { get; } = new();
    public List<FlagOption> Categories { get; } = new();
    public List<SetcodeSlot> Setcodes { get; } = new();
    public ObservableCollection<InfoItem> Attributes { get; }
    public ObservableCollection<InfoItem> Races { get; }
    /// <summary>Card group names shared by the four setcode drop-downs (can grow while the program runs).</summary>
    public ObservableCollection<InfoItem> SetnameItems { get; }

    /// <summary>After the card group list changed: every slot picks its name again from the new list.</summary>
    public void RefreshSetnames()
    {
        foreach (var s in Setcodes) s.Load(s.Value);
    }

    /// <summary>After the race or attribute list changed: the drop-downs pick the card's value again.</summary>
    public void RefreshChoices()
    {
        Raise(nameof(Attribute));
        Raise(nameof(Race));
    }

    string code = "", alias = "", name = "", desc = "";
    string level = "", lscale = "", rscale = "", atk = "", def = "";
    string maker = "", tags = "", pack = "", number = "", created = "", modified = "";
    List<string> extraFooter = new();
    long attribute, race;
    long extraType, extraCategory, extraLevel, ot = Card.RuleCustom;
    bool loading, isDirty;
    Card baseline = new();

    /// <summary>The card as it is in the database, or null for a card that hasn't been saved yet.</summary>
    public Card? Original { get; private set; }

    public event Action? Changed;

    public EditorModel(CardInfo info)
    {
        Info = info;
        for (int i = 0; i < Card.StrCount; i++) Strings.Add(new StrSlot(i, OnChanged));
        // 몬스터, 마법 and 함정 are chips like the rest (first, before 일반) and can be combined freely.
        foreach (var t in info.Types.Where(t => (t.Value & CardTypes.MainTypes) != 0).Concat(info.Types))
            if (t.Value != 0 && SubTypes.All(s => s.Value != t.Value))
                SubTypes.Add(new FlagOption(t.Value, t.Text.Trim(), OnSubTypeChanged));
        for (int i = 0; i < 9; i++)
            Markers.Add(new FlagOption(CardTypes.MarkerGrid[i], CardTypes.MarkerArrows[i], _ => OnMarkerChanged()));
        foreach (var c in info.Categories)
            if (c.Value != 0 && Categories.All(s => s.Value != c.Value))
                Categories.Add(new FlagOption(c.Value, c.Text, _ => OnChanged()));
        SetnameItems = new(info.Setnames.Count > 0 && info.Setnames[0].Value == 0
            ? info.Setnames : new List<InfoItem> { new(0, "") }.Concat(info.Setnames));
        for (int i = 0; i < Card.SetcodeCount; i++) Setcodes.Add(new SetcodeSlot(SetnameItems, OnChanged));
        Attributes = new(info.Attributes);
        Races = new(info.Races);
        Load(null);
    }

    #region fields
    public string Code { get => code; set { if (Set(ref code, value ?? "")) OnChanged(); } }
    public string Alias { get => alias; set { if (Set(ref alias, value ?? "")) OnChanged(); } }
    public string Name { get => name; set { if (Set(ref name, value ?? "")) OnChanged(); } }
    public string Desc { get => desc; set { if (Set(ref desc, value ?? "")) OnChanged(); } }
    public string Level { get => level; set { if (Set(ref level, value ?? "")) OnChanged(); } }
    public string LScale { get => lscale; set { if (Set(ref lscale, value ?? "")) OnChanged(); } }
    public string RScale { get => rscale; set { if (Set(ref rscale, value ?? "")) OnChanged(); } }
    public string Atk { get => atk; set { if (Set(ref atk, value ?? "")) OnChanged(); } }
    public string Def { get => def; set { if (Set(ref def, value ?? "")) OnChanged(); } }
    // 제작 정보: kept at the end of desc (see CardFooter); the dates go to the card_dates table.
    public string Maker { get => maker; set { if (Set(ref maker, value ?? "")) OnChanged(); } }
    public string Tags { get => tags; set { if (Set(ref tags, value ?? "")) OnChanged(); } }
    public string Pack { get => pack; set { if (Set(ref pack, value ?? "")) OnChanged(); } }
    public string Number { get => number; set { if (Set(ref number, value ?? "")) OnChanged(); } }
    public string Created { get => created; set { if (Set(ref created, value ?? "")) OnChanged(); } }
    public string Modified { get => modified; set { if (Set(ref modified, value ?? "")) OnChanged(); } }
    public long Attribute { get => attribute; set { if (Set(ref attribute, value)) OnChanged(); } }
    public long Race { get => race; set { if (Set(ref race, value)) OnChanged(); } }
    /// <summary>CyanPro SCOPE_HIDDEN: the card isn't listed in the deck editor.</summary>
    public bool IsHidden
    {
        get => (ot & Card.ScopeHidden) != 0;
        set
        {
            if (value == IsHidden) return;
            ot = value ? ot | Card.ScopeHidden : ot & ~Card.ScopeHidden;
            Raise();
            OnChanged();
        }
    }

    public bool IsMonster => HasSub(CardTypes.Monster);
    // Scales and link markers follow their own chips, whatever else the card is (a 펜듈럼 spell, say).
    public bool IsPendulum => HasSub(CardTypes.Pendulum);
    public bool IsLink => HasSub(CardTypes.Link);
    public string LevelLabel => IsLink ? "링크" : HasSub(CardTypes.Xyz) ? "랭크" : "레벨";
    public bool IsDirty { get => isDirty; private set => Set(ref isDirty, value); }
    public bool IsNew => Original == null;
    /// <summary>Changes, or a new card with something typed in, that would be lost by showing another card.</summary>
    public bool HasUnsavedWork => IsDirty || IsNew && (name.Trim().Length > 0 || desc.Trim().Length > 0);
    #endregion

    bool HasSub(long bit) => SubTypes.Any(s => s.Value == bit && s.IsChecked);

    void OnSubTypeChanged(FlagOption o)
    {
        // Turning on 키-해금됨 turns on 키 as well; turning it off leaves 키 alone.
        if (!loading && o.Value == CardTypes.Unlocked && o.IsChecked
            && SubTypes.FirstOrDefault(s => s.Value == CardTypes.Key) is { IsChecked: false } key)
            key.IsChecked = true;
        OnTypeChanged();
    }

    void OnTypeChanged()
    {
        Raise(nameof(IsMonster)); Raise(nameof(IsPendulum)); Raise(nameof(IsLink)); Raise(nameof(LevelLabel));
        if (!loading && IsLink) Level = MarkerValue().PopCount().ToString();
        OnChanged();
    }

    void OnMarkerChanged()
    {
        // The link rating follows the number of arrows.
        if (!loading && IsLink) Level = MarkerValue().PopCount().ToString();
        OnChanged();
    }

    long MarkerValue()
    {
        long v = 0;
        foreach (var m in Markers) if (m.IsChecked) v |= m.Value;
        return v;
    }

    void OnChanged()
    {
        if (loading) return;
        IsDirty = !ToCard().SameAs(baseline);
        Changed?.Invoke();
    }

    /// <summary>Shows a card. Pass a card that is in the database as fromDb so saving knows to update it.</summary>
    public void Load(Card? c, bool fromDb = true)
    {
        loading = true;
        Original = c != null && fromDb ? c.Clone() : null;
        c ??= new Card();
        ot = c.Ot;
        Raise(nameof(IsHidden));
        Code =c.Id == 0 ? "" : c.Id.ToString();
        Alias = c.Alias == 0 ? "" : c.Alias.ToString();
        Name = c.Name;
        var (body, footer) = CardFooter.Split(c.Desc);
        Desc = body;
        Maker = footer.Maker;
        Tags = footer.Tags;
        Pack = footer.Pack;
        Number = footer.Number;
        extraFooter = footer.Extra;
        // Dates typed into the text by hand move to the table on the next save.
        Created = c.Created.Length > 0 ? c.Created : footer.Created;
        Modified = c.Modified.Length > 0 ? c.Modified : footer.Modified;
        for (int i = 0; i < Card.StrCount; i++) Strings[i].Text = c.Str[i];

        long known = 0;
        foreach (var s in SubTypes)
        {
            s.SetSilently((c.Type & s.Value) == s.Value);
            known |= s.Value;
        }
        extraType = c.Type & ~known;

        EnsureItem(Attributes, c.Attribute);
        EnsureItem(Races, c.Race);
        Attribute = c.Attribute;
        Race = c.Race;

        bool link = c.IsType(CardTypes.Link);
        Level = (c.Level & 0xff) == 0 && !c.IsType(CardTypes.Monster) ? "" : (c.Level & 0xff).ToString();
        LScale = ((c.Level >> 24) & 0xff).ToString();
        RScale = ((c.Level >> 16) & 0xff).ToString();
        // Bits the editor doesn't show: for a pendulum card the scales are fields, otherwise any high bits are kept as is.
        extraLevel = c.IsType(CardTypes.Pendulum) ? c.Level & 0xff00 : c.Level & ~0xffL;
        Atk = CardInfo.StatText(c.Atk);
        Def = link ? "" : CardInfo.StatText(c.Def);
        foreach (var m in Markers) m.SetSilently(link && m.Value != 0 && (c.Def & m.Value) == m.Value);

        long[] sc = c.GetSetcodes();
        for (int i = 0; i < Card.SetcodeCount; i++) Setcodes[i].Load(sc[i]);

        long knownCat = 0;
        foreach (var cat in Categories)
        {
            cat.SetSilently((c.Category & cat.Value) == cat.Value);
            knownCat |= cat.Value;
        }
        extraCategory = c.Category & ~knownCat;

        OnTypeChanged();
        loading = false;
        baseline = ToCard();
        IsDirty = false;
        Raise(nameof(IsNew));
        Changed?.Invoke();
    }

    /// <summary>Marks the current contents as saved (after writing them to the database).</summary>
    public void MarkSaved(Card saved)
    {
        Original = saved.Clone();
        loading = true;
        Created = saved.Created;
        Modified = saved.Modified;
        loading = false;
        baseline = ToCard();
        IsDirty = false;
        Raise(nameof(IsNew));
    }

    static void EnsureItem(ObservableCollection<InfoItem> list, long v)
    {
        if (list.All(i => i.Value != v)) list.Add(new InfoItem(v, "0x" + v.ToString("x")));
    }

    public static bool TryStat(string s, out long v)
    {
        s = s.Trim();
        v = 0;
        if (s == "?" || s == "？") { v = -2; return true; }
        if (s.Length == 0) return true;
        return long.TryParse(s, out v);
    }

    static long Num(string s) => long.TryParse(s.Trim(), out long v) ? v : 0;

    public const string DateFormat = "yyyy-MM-dd";
    static bool IsDate(string s) => s.Trim().Length == 0 ||
        DateTime.TryParseExact(s.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>What is wrong with the typed values, or null when they can be saved.</summary>
    public string? Validate()
    {
        if (!long.TryParse(code.Trim(), out long id) || id <= 0) return "카드 코드를 숫자로 입력해 주세요.";
        if (alias.Trim().Length > 0 && !long.TryParse(alias.Trim(), out _)) return "동명 취급 코드는 숫자여야 합니다.";
        // Spells and traps can carry stats too (custom cards), so these are checked for every kind.
        if (level.Trim().Length > 0 && (!long.TryParse(level.Trim(), out long lv) || lv < 0 || lv > 255)) return LevelLabel + "은(는) 0~255 사이 숫자여야 합니다.";
        if (!TryStat(atk, out _)) return "공격력은 숫자 또는 ? 로 입력해 주세요.";
        if (!IsLink && !TryStat(def, out _)) return "수비력은 숫자 또는 ? 로 입력해 주세요.";
        if (IsPendulum && (Num(lscale) is < 0 or > 255 || Num(rscale) is < 0 or > 255)) return "펜듈럼 스케일은 0~255 사이 숫자여야 합니다.";
        // The card site reads the first [ ] line as the maker and groups packs by the card number.
        if (!CardFooter.IsNumber(number)) return "카드 번호는 ABCD-AM001처럼 입력해 주세요.\n(영문 대문자·숫자 3~6자 - 영문 대문자 2자 + 숫자 3자리, 비워 둬도 돼요)";
        if (maker.Trim().Length == 0 && (pack.Trim().Length > 0 || extraFooter.Count > 0) && tags.Trim().Length == 0)
            return "제작자가 비어 있으면 도감 사이트가 팩 이름을 제작자로 읽어요.\n제작자를 입력해 주세요.";
        if (!IsDate(created)) return "제작일은 2026-10-30처럼 입력해 주세요. (비워 둬도 돼요)";
        if (!IsDate(modified)) return "수정일은 2026-10-30처럼 입력해 주세요. (비워 둬도 돼요)";
        return null;
    }

    public Card ToCard()
    {
        var c = new Card
        {
            Id = Num(code),
            Ot = ot,
            Alias = Num(alias),
            Name = name,
            Desc = CardFooter.Join(desc, new CardFooter { Maker = maker, Tags = tags, Pack = pack, Number = number, Extra = extraFooter }),
            Created = created.Trim(),
            Modified = modified.Trim(),
            Attribute = attribute,
            Race = race,
        };
        for (int i = 0; i < Card.StrCount; i++) c.Str[i] = Strings[i].Text;
        long type = extraType;
        foreach (var s in SubTypes) if (s.IsChecked) type |= s.Value;
        c.Type = type;
        long lv = Num(level) & 0xff;
        if (IsPendulum)
            lv |= ((Num(lscale) & 0xff) << 24) | ((Num(rscale) & 0xff) << 16) | (extraLevel & 0xff00);
        else
            lv |= extraLevel;
        c.Level = lv;
        TryStat(atk, out c.Atk);
        if (IsLink) c.Def = MarkerValue();
        else TryStat(def, out c.Def);
        c.Setcode = Card.PackSetcodes(Setcodes.Select(s => s.Value).ToList());
        long cat = extraCategory;
        foreach (var o in Categories) if (o.IsChecked) cat |= o.Value;
        c.Category = cat;
        return c;
    }

    /// <summary>
    /// The saved rule is always 오리카 (0x20), plus 숨김 when checked. A new card gets today as 제작일,
    /// and a changed card gets today as 수정일 unless that box was edited by hand.
    /// </summary>
    public Card ToSaveCard(DateTime today)
    {
        var c = ToCard();
        c.Ot = Card.RuleCustom | (c.Ot & Card.ScopeHidden);
        string d = today.ToString(DateFormat, CultureInfo.InvariantCulture);
        if (IsNew && c.Created.Length == 0) c.Created = d;
        if ((IsNew || IsDirty) && c.Modified == baseline.Modified) c.Modified = d;
        return c;
    }
}

static class LongExt
{
    public static int PopCount(this long v) => CardTypes.PopCount(v);
}
