using System.Text.RegularExpressions;

namespace YellowEditorX.Core;

/// <summary>
/// The card group, type, race and attribute lists of the CyanPro project on GitHub. The editor keeps its lists
/// the same as CyanPro's, so whatever is added or taken out there shows up here without touching cardinfo:
/// card groups and names in CyanPro_CardDB/strings.conf ("!setname 0x300	마칭 파이어", "!system 1090 액세스"),
/// the values in CyanPro gframe/ocgapi_constants.h ("#define TYPE_ACCESS 0x08000000"),
/// which name an orica type uses in gframe/data_manager.cpp ("{TYPE_ACCESS,  1090},").
/// </summary>
public static class RemoteInfo
{
    public const string StringsUrl = "https://raw.githubusercontent.com/pendulums58/CyanPro_CardDB/main/strings.conf";
    public const string ConstantsUrl = "https://raw.githubusercontent.com/pendulums58/CyanPro/orica-phase2/gframe/ocgapi_constants.h";
    public const string DataManagerUrl = "https://raw.githubusercontent.com/pendulums58/CyanPro/orica-phase2/gframe/data_manager.cpp";

    static readonly Regex SetnameLine = new(@"^!setname\s+(0x[0-9a-fA-F]+|\d+)\s+(.+?)\s*$");
    static readonly Regex SystemLine = new(@"^!system\s+(\d+)\s+(.+?)\s*$");
    static readonly Regex DefineLine = new(@"^#define\s+([A-Z][A-Z0-9_]*)\s+(0x[0-9a-fA-F]+|\d+)\s*(//.*)?$");
    static readonly Regex OricaTypeLine = new(@"\{\s*(TYPE_[A-Z0-9_]+)\s*,\s*(\d+)\s*\}");

    // Flags the engine sets on cards while dueling; no card in a cdb has them.
    static readonly HashSet<string> EngineOnly = new() { "TYPE_TRAPMONSTER" };

    // Names for when strings.conf has none (the client's own strings aren't on GitHub; same names as the OricaWeb site).
    static readonly Dictionary<long, string> AttributeNames = new()
    {
        [0x1] = "땅", [0x2] = "물", [0x4] = "화염", [0x8] = "바람", [0x10] = "빛", [0x20] = "어둠", [0x40] = "신",
    };
    static readonly Dictionary<long, string> RaceNames = new()
    {
        [0x1] = "전사족", [0x2] = "마법사족", [0x4] = "천사족", [0x8] = "악마족", [0x10] = "언데드족", [0x20] = "기계족",
        [0x40] = "물족", [0x80] = "화염족", [0x100] = "암석족", [0x200] = "비행야수족", [0x400] = "식물족", [0x800] = "곤충족",
        [0x1000] = "번개족", [0x2000] = "드래곤족", [0x4000] = "야수족", [0x8000] = "야수전사족", [0x10000] = "공룡족",
        [0x20000] = "어류족", [0x40000] = "해룡족", [0x80000] = "파충류족", [0x100000] = "사이킥족", [0x200000] = "환신야수족",
        [0x400000] = "창조신족", [0x800000] = "환룡족", [0x1000000] = "사이버스족", [0x2000000] = "환상마족",
        // CyanPro took out the Rush Duel races and 요괴족 (0x4000000 and up); a race added on those bits
        // later must not get their old names.
    };
    static readonly Dictionary<long, string> TypeNames = new()
    {
        [0x1] = "몬스터", [0x2] = "마법", [0x4] = "함정", [0x8] = "키", [0x10] = "일반", [0x20] = "효과", [0x40] = "융합",
        [0x80] = "의식", [0x200] = "스피릿", [0x400] = "유니온", [0x800] = "듀얼", [0x1000] = "튜너", [0x2000] = "싱크로",
        [0x4000] = "토큰", [0x8000] = "키-해금됨", [0x10000] = "속공", [0x20000] = "지속", [0x40000] = "장착", [0x80000] = "필드",
        [0x100000] = "카운터", [0x200000] = "리버스", [0x400000] = "툰", [0x800000] = "엑시즈", [0x1000000] = "펜듈럼",
        [0x2000000] = "특수 소환", [0x4000000] = "링크", [0x8000000] = "액세스", [0x10000000] = "기프트",
        [0x20000000] = "하이잭", [0x40000000] = "페어링",
    };

    public static List<InfoItem> ParseSetnames(string stringsConf)
    {
        var list = new List<InfoItem>();
        foreach (var raw in stringsConf.Split('\n'))
        {
            var m = SetnameLine.Match(raw.Trim());
            if (m.Success && CardInfo.TryParseValue(m.Groups[1].Value, out long v) && v > 0)
                list.Add(new InfoItem(v, m.Groups[2].Value));
        }
        return list;
    }

    /// <summary>The "!system 1090 액세스" lines: the names CyanPro shows.</summary>
    public static Dictionary<int, string> ParseSystem(string? stringsConf)
    {
        var map = new Dictionary<int, string>();
        foreach (var raw in (stringsConf ?? "").Split('\n'))
        {
            var m = SystemLine.Match(raw.Trim());
            if (m.Success && int.TryParse(m.Groups[1].Value, out int id)) map[id] = m.Groups[2].Value;
        }
        return map;
    }

    /// <summary>
    /// Single-bit #define values with the given prefix ("TYPE", "RACE", "ATTRIBUTE"). The name is the one CyanPro
    /// shows when strings.conf has it, else the one in <paramref name="known"/> (cardinfo), else a built-in one.
    /// </summary>
    public static List<InfoItem> ParseBits(string header, string prefix, string? stringsConf = null,
                                           string? dataManager = null, IEnumerable<InfoItem>? known = null)
    {
        var builtIn = prefix switch { "RACE" => RaceNames, "ATTRIBUTE" => AttributeNames, "TYPE" => TypeNames, _ => new() };
        var system = ParseSystem(stringsConf);
        var defines = new Dictionary<string, long>();
        var list = new List<InfoItem>();
        foreach (var raw in header.Split('\n'))
        {
            var m = DefineLine.Match(raw.Trim());
            if (!m.Success || !CardInfo.TryParseValue(m.Groups[2].Value, out long v)) continue;
            defines[m.Groups[1].Value] = v;
        }
        // The orica types have their own string numbers in the client; the rest go by bit position.
        var oricaIds = new Dictionary<long, int>();
        if (prefix == "TYPE" && dataManager != null)
            foreach (Match m in OricaTypeLine.Matches(dataManager))
                if (defines.TryGetValue(m.Groups[1].Value, out long v)) oricaIds[v] = int.Parse(m.Groups[2].Value);
        foreach (var (define, v) in defines)
        {
            if (!define.StartsWith(prefix + "_") || EngineOnly.Contains(define) || CardTypes.PopCount(v) != 1) continue;
            if (list.Any(i => i.Value == v)) continue;
            int bit = System.Numerics.BitOperations.TrailingZeroCount(v);
            int? id = prefix switch
            {
                "ATTRIBUTE" => 1010 + bit,
                "RACE" => bit < 30 ? 1020 + bit : 2470 + bit,
                "TYPE" => oricaIds.TryGetValue(v, out int o) ? o : v <= 0x4000000 ? 1050 + bit : null,
                _ => null,
            };
            string? name = id != null && system.TryGetValue(id.Value, out var s) ? s
                         : known?.FirstOrDefault(k => k.Value == v)?.Text.Trim() is { Length: > 0 } k ? k
                         : builtIn.TryGetValue(v, out var b) ? b : define;
            list.Add(new InfoItem(v, name));
        }
        return list;
    }

    /// <summary>
    /// Makes the list the same as CyanPro's: values CyanPro doesn't have are taken out (except those in
    /// <paramref name="keep"/>, which a card on screen uses), names follow CyanPro and new values go at the end.
    /// Placeholder entries (value 0 or less, like "(종족)") stay. Returns how many entries changed.
    /// </summary>
    public static int Sync(IList<InfoItem> list, IReadOnlyCollection<InfoItem> remote, params long[] keep)
    {
        if (remote.Count == 0) return 0; // a file that couldn't be read must not empty the list
        int changed = 0;
        foreach (var r in remote)
        {
            int at = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].Value == r.Value) { at = i; break; }
            if (at < 0) { list.Add(r); changed++; }
            else if (list[at].Text != r.Text) { list[at] = r; changed++; }
        }
        for (int i = list.Count - 1; i >= 0; i--)
        {
            long v = list[i].Value;
            if (v > 0 && !keep.Contains(v) && remote.All(r => r.Value != v)) { list.RemoveAt(i); changed++; }
        }
        return changed;
    }
}
