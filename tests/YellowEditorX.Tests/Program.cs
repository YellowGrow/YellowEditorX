using Microsoft.Data.Sqlite;
using YellowEditorX.Core;

// Usage: YellowEditorX.Tests <cardinfo.txt> <folder with copies of .cdb files>
int failures = 0;
void Check(bool ok, string what)
{
    if (!ok) { failures++; Console.WriteLine("FAIL " + what); }
}

string infoPath = args[0];
string cdbRoot = args[1];
var info = CardInfo.Load(infoPath);
Console.WriteLine($"cardinfo: {info.Types.Count} types, {info.Categories.Count} categories, {info.Setnames.Count} setnames, {info.Races.Count} races, {info.Attributes.Count} attributes");
Check(info.Types.Count > 20 && info.Categories.Count == 32 && info.Setnames.Count > 0, "cardinfo counts");

string Diff(Card a, Card b)
{
    var d = new List<string>();
    if (a.Id != b.Id) d.Add($"id {a.Id}/{b.Id}");
    if (a.Ot != b.Ot) d.Add($"ot {a.Ot}/{b.Ot}");
    if (a.Alias != b.Alias) d.Add($"alias {a.Alias}/{b.Alias}");
    if (a.Setcode != b.Setcode) d.Add($"setcode {a.Setcode:x}/{b.Setcode:x}");
    if (a.Type != b.Type) d.Add($"type {a.Type:x}/{b.Type:x}");
    if (a.Atk != b.Atk) d.Add($"atk {a.Atk}/{b.Atk}");
    if (a.Def != b.Def) d.Add($"def {a.Def}/{b.Def}");
    if (a.Level != b.Level) d.Add($"level {a.Level:x}/{b.Level:x}");
    if (a.Race != b.Race) d.Add($"race {a.Race:x}/{b.Race:x}");
    if (a.Attribute != b.Attribute) d.Add($"attr {a.Attribute:x}/{b.Attribute:x}");
    if (a.Category != b.Category) d.Add($"cat {a.Category:x}/{b.Category:x}");
    if (a.Name != b.Name) d.Add("name");
    if (a.Desc != b.Desc) d.Add("desc");
    for (int i = 0; i < 16; i++) if (a.Str[i] != b.Str[i]) d.Add("str" + i);
    return string.Join(", ", d);
}

List<object?[]> RawRows(string db, string table)
{
    var rows = new List<object?[]>();
    using var con = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False");
    con.Open();
    using var cmd = con.CreateCommand();
    cmd.CommandText = $"SELECT * FROM {table} ORDER BY id";
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var row = new object?[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
        rows.Add(row);
    }
    return rows;
}

// The tail parsing of the card site (pendulums58/OricaWeb template/decode.js: splitDesc + parseMeta), for comparison.
(string Maker, string Pack, string Number, string Tags) SiteRead(string desc)
{
    var ls = desc.Replace("\r\n", "\n").Split('\n').ToList();
    while (ls.Count > 0 && ls[^1].Trim().Length == 0) ls.RemoveAt(ls.Count - 1);
    var meta = new List<string>();
    while (ls.Count > 0 && ls[^1].Trim() is var t && t.StartsWith("[") && t.EndsWith("]")) { meta.Insert(0, t); ls.RemoveAt(ls.Count - 1); }
    var number = new System.Text.RegularExpressions.Regex(@"^\[([A-Z0-9]{3,6})-([A-Z]{2})(\d{3})\]$");
    var labelled = new System.Text.RegularExpressions.Regex(@"^\[\s*([^\[\]:：]+?)\s*[:：]\s*(.*?)\s*\]$");
    var plain = new System.Text.RegularExpressions.Regex(@"^\[\s*([^\[\]]+?)\s*\]$");
    string maker = "", pack = "", num = "", tags = "";
    for (int i = 0; i < meta.Count; i++)
    {
        string l = meta[i];
        if (number.IsMatch(l)) { num = l; continue; }
        var m = labelled.Match(l);
        if (m.Success)
        {
            if (System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\s+", "") == "태그")
                tags = string.Join("|", m.Groups[2].Value.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0));
            continue;
        }
        m = plain.Match(l);
        if (m.Success) { if (i == 0) maker = m.Groups[1].Value; else pack = m.Groups[1].Value; }
    }
    return (maker, pack, num, tags);
}

string tmp = Path.Combine(Path.GetTempPath(), "orica_test_" + Environment.ProcessId);
Directory.CreateDirectory(tmp);
int total = 0, footerMakers = 0, footerTags = 0, footerPacks = 0, footerNumbers = 0;
var model = new EditorModel(info);
foreach (var db in Directory.GetFiles(cdbRoot, "*.cdb", SearchOption.AllDirectories))
{
    var cards = CardDb.ReadAll(db);
    total += cards.Count;
    // 1. editor round trip: showing a card and reading it back changes nothing,
    //    except that [카드군: …] lines are dropped from the footer and a blank line comes before it
    foreach (var c in cards)
    {
        model.Load(c);
        var back = model.ToCard();
        // (trailing spaces at the end of the text and of footer lines are trimmed too)
        var lines = (string s) => s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        var expected = lines(c.Desc).Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"^\[카드군\s*:.*\]\s*$")).ToList();
        Check(lines(back.Desc).SequenceEqual(expected), $"footer keeps lines {c.Id} {c.Name}");
        // what the card site reads must not change (commas inside tags stay part of the tag)
        Check(SiteRead(c.Desc) == SiteRead(back.Desc), $"site reads the same {c.Id}: {SiteRead(c.Desc)} / {SiteRead(back.Desc)}");
        Check(CardFooter.Join(CardFooter.Split(back.Desc).Body, CardFooter.Split(back.Desc).Footer) == back.Desc, $"footer stable {c.Id}");
        if (model.Maker.Length > 0) footerMakers++;
        if (model.Tags.Length > 0) footerTags++;
        if (model.Pack.Length > 0) footerPacks++;
        if (model.Number.Length > 0) footerNumbers++;
        back.Desc = c.Desc;
        Check(back.SameAs(c), $"editor round trip {Path.GetFileName(db)} {c.Id} {c.Name}: {Diff(c, back)}");
        Check(!model.IsDirty, $"dirty right after load {c.Id}");
        Check(model.Validate() == null, $"validate {c.Id}: {model.Validate()}");
    }
    // 2. database round trip: writing every card into a new file keeps every raw value
    string copy = Path.Combine(tmp, Guid.NewGuid() + ".cdb");
    CardDb.Create(copy);
    CardDb.Write(copy, cards);
    var again = CardDb.ReadAll(copy);
    Check(again.Count == cards.Count, $"db count {db}");
    var byId = again.ToDictionary(c => c.Id);
    foreach (var c in cards)
        Check(byId.TryGetValue(c.Id, out var got) && got.SameAs(c), $"db round trip {c.Id}");
    var origData = RawRows(db, "datas");
    var newData = RawRows(copy, "datas");
    var joined = cards.Select(c => c.Id).ToHashSet();
    origData = origData.Where(r => joined.Contains(Convert.ToInt64(r[0]))).ToList();
    Check(origData.Count == newData.Count, $"raw datas count {db}");
    for (int i = 0; i < Math.Min(origData.Count, newData.Count); i++)
        Check(origData[i].Select(Convert.ToString).SequenceEqual(newData[i].Select(Convert.ToString)),
            $"raw datas row {db} {origData[i][0]}: {string.Join(",", origData[i])} vs {string.Join(",", newData[i])}");
    Console.WriteLine($"{Path.GetFileName(db)}: {cards.Count} cards");
}
Console.WriteLine($"total cards: {total}; footer maker {footerMakers}, tags {footerTags}, pack {footerPacks}, number {footerNumbers}");

// 3. dirty tracking
var sample = new Card { Id = 123, Name = "테스트", Type = CardTypes.Monster | CardTypes.Effect, Level = 4, Atk = 1800, Def = 1000, Attribute = 0x20, Race = 0x2 };
model.Load(sample);
model.Name = "다른 이름";
Check(model.IsDirty, "dirty after name change");
model.Name = "테스트";
Check(!model.IsDirty, "clean after reverting name");
model.Strings[3].Text = "스트링";
Check(model.IsDirty && model.ToCard().Str[3] == "스트링", "string slot edit");
model.Strings[3].Text = "";
Check(!model.IsDirty, "clean after reverting string");

// 4. switching kind drops sub types that don't fit
model.SubTypes.First(s => s.Value == CardTypes.Tuner).IsChecked = true;
model.IsSpellType = true;
var spell = model.ToCard();
Check(spell.Type == CardTypes.Spell, $"spell type after switch: {spell.Type:x}");
Check(model.SubTypes.First(s => s.Value == CardTypes.Tuner).IsVisible == false, "tuner hidden for spell");
model.SubTypes.First(s => s.Value == CardTypes.QuickPlay).IsChecked = true;
Check(model.ToCard().Type == (CardTypes.Spell | CardTypes.QuickPlay), "quick-play spell");

// 5. link markers drive the rating and are stored in def
model.Load(sample);
model.SubTypes.First(s => s.Value == CardTypes.Link).IsChecked = true;
Check(model.IsLink && model.LevelLabel == "링크", "link mode");
model.Markers[1].IsChecked = true;
model.Markers[7].IsChecked = true;
model.Markers[8].IsChecked = true;
var link = model.ToCard();
Check(link.Def == (0x80 | 0x2 | 0x4) && (link.Level & 0xff) == 3, $"link card def {link.Def:x} level {link.Level}");

// 6. pendulum scales
model.Load(sample);
model.SubTypes.First(s => s.Value == CardTypes.Pendulum).IsChecked = true;
model.LScale = "3"; model.RScale = "5"; model.Level = "7";
var pend = model.ToCard();
Check(pend.Level == ((3L << 24) | (5L << 16) | 7), $"pendulum level {pend.Level:x}");
model.Atk = "?";
Check(model.ToCard().Atk == -2, "atk ?");
model.Atk = "abc";
Check(model.Validate() != null, "invalid atk rejected");

// 7. setcode slots
model.Load(sample);
model.Setcodes[0].Hex = "301";
Check(model.Setcodes[0].Selected?.Text == "도와줘", "setcode hex selects name");
model.Setcodes[1].Selected = info.Setnames.First(s => s.Text == "스카이워커");
Check(model.Setcodes[1].Hex == "302", "setcode name fills hex");
Check(model.ToCard().Setcode == (0x301 | (0x302L << 16)), "setcode packed");
Check(model.ToSaveCard(new DateTime(2026, 10, 30)).Ot == 0x20, "rule is 0x20");

// 7b. footer fields and dates
{
    var (body, f) = CardFooter.Split("효과.\r\n\r\n[이피 / YiPi]\r\n[카드군: 결연희]\r\n[태그: 가, 나]\r\n[퓨처 터미널 03 - 스파킹]\r\n[AWAS-AM001]\r\n[제작일: 2025-01-02]");
    Check(body == "효과." && f.Maker == "이피 / YiPi" && f.Tags == "가, 나" && f.Pack == "퓨처 터미널 03 - 스파킹"
          && f.Number == "AWAS-AM001" && f.Created == "2025-01-02" && f.Extra.Count == 0, "footer split (comma stays inside the tag)");
    Check(CardFooter.Join(body, f) == "효과.\r\n\r\n[이피 / YiPi]\r\n[태그: 가, 나]\r\n[퓨처 터미널 03 - 스파킹]\r\n[AWAS-AM001]", "footer join");
    Check(CardFooter.Split("효과.\r\n\r\n[퓨처 터미널 03 - 스파킹]\r\n[카드군 : SS]").Footer.Maker == "퓨처 터미널 03 - 스파킹", "first line is the maker, like the site");
    Check(CardFooter.Split("효과.\r\n\r\n[시안색새벽]\r\n[태그 ： 범용 | 단짝]\r\n[RS01-AM001]").Footer is { Maker: "시안색새벽", Tags: "범용 | 단짝", Number: "RS01-AM001" }, "site example (full-width colon)");
    Check(CardFooter.Split("효과.\r\n\r\n[나]\r\n[AB-1]").Footer is { Pack: "AB-1", Number: "" }, "loose number is not a card number");
    Check(CardFooter.Split("효과.").Body == "효과." && CardFooter.Join("효과.\r\n", new CardFooter()) == "효과.\r\n", "no footer keeps text");
    var ex4 = CardFooter.Split("A\r\n[x]\r\n[y]\r\n[z]\r\n[w]");
    Check(ex4.Footer is { Maker: "x", Pack: "w" } && ex4.Footer.Extra.Count == 2 && CardFooter.Join(ex4.Body, ex4.Footer) == "A\r\n\r\n[x]\r\n[y]\r\n[z]\r\n[w]", "extra lines kept, last plain line stays the pack");
    Check(CardFooter.Join("", new CardFooter { Maker = "m", Number = "awas-am001" }) == "[m]\r\n[AWAS-AM001]", "card number upper-cased");

    model.Load(new Card { Id = 900, Name = "날짜", Desc = "본문\r\n\r\n[나 / Me]\r\n[태그: t]" });
    Check(model.Desc == "본문" && model.Maker == "나 / Me" && model.Tags == "t" && !model.IsDirty, "footer fields on load");
    model.Pack = "팩";
    Check(model.IsDirty && model.ToCard().Desc == "본문\r\n\r\n[나 / Me]\r\n[태그: t]\r\n[팩]", "footer field edit");
    model.Number = "AB-1";
    Check(model.Validate()?.Contains("카드 번호") == true, "loose card number rejected");
    model.Number = "awas-am001";
    Check(model.Validate() == null, "lower-case card number accepted");
    model.Maker = "";
    model.Tags = "";
    Check(model.Validate()?.Contains("제작자") == true, "pack without maker rejected");
    model.Tags = "t";
    Check(model.Validate() == null, "pack after tags without maker is fine for the site");
    model.Maker = "나 / Me";
    var day = new DateTime(2026, 10, 30);
    var s1 = model.ToSaveCard(day);
    Check(s1.Modified == "2026-10-30" && s1.Created == "", "existing card: modified today, created left empty");
    model.MarkSaved(s1);
    Check(model.Modified == "2026-10-30" && !model.IsDirty, "saved dates shown");
    Check(model.ToSaveCard(day.AddDays(1)).Modified == "2026-10-30", "unchanged card keeps modified");
    model.Modified = "2026-01-01"; model.Name = "x";
    Check(model.ToSaveCard(day).Modified == "2026-01-01", "hand-typed modified kept");
    model.Created = "2026/1/1";
    Check(model.Validate() != null, "bad date rejected");
    model.Load(new Card { Id = 901, Name = "새" }, fromDb: false);
    var s2 = model.ToSaveCard(day);
    Check(s2.Created == "2026-10-30" && s2.Modified == "2026-10-30", "new card gets both dates");
    model.Load(new Card { Id = 902, Desc = "a\r\n\r\n[수정일: 2024-05-06]" });
    Check(model.Modified == "2024-05-06" && model.ToCard().Desc == "a", "date lines move to fields");

    // dates table: only made when needed, follows writes and deletes, invisible to datas/texts
    string ddb = Path.Combine(tmp, "dates.cdb");
    CardDb.Create(ddb);
    CardDb.Write(ddb, new[] { new Card { Id = 1, Name = "a" } });
    bool HasTable(string p)
    {
        using var con = new SqliteConnection($"Data Source={p};Mode=ReadOnly;Pooling=False");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM sqlite_master WHERE name='{CardDb.DatesTable}'";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
    Check(!HasTable(ddb) && CardDb.Read(ddb, 1)!.Created == "", "no dates table until needed");
    CardDb.Write(ddb, new[] { new Card { Id = 2, Name = "b", Created = "2026-10-01", Modified = "2026-10-30" } });
    var r2 = CardDb.Read(ddb, 2)!;
    Check(HasTable(ddb) && r2.Created == "2026-10-01" && r2.Modified == "2026-10-30" && CardDb.Read(ddb, 1)!.Modified == "", "dates written and read");
    Check(RawRows(ddb, "datas").All(r => r.Length == 11) && RawRows(ddb, "texts").All(r => r.Length == 19), "datas/texts columns unchanged");
    CardDb.Write(ddb, Array.Empty<Card>(), new long[] { 2 });
    Check(RawRows(ddb, CardDb.DatesTable).Count == 0, "delete removes dates");
    var dh = new History();
    CardDb.Write(ddb, new[] { new Card { Id = 3, Name = "c", Created = "2026-10-02" } });
    dh.Push(CardOps.Delete(ddb, new[] { CardDb.Read(ddb, 3)! }, withFiles: false));
    dh.Undo();
    Check(CardDb.Read(ddb, 3)?.Created == "2026-10-02", "undo delete restores dates");
    dh.Push(CardOps.ChangeCode(ddb, 3, new Card { Id = 4, Name = "c", Created = "2026-10-02" }, move: true));
    Check(CardDb.Read(ddb, 4)?.Created == "2026-10-02" && RawRows(ddb, CardDb.DatesTable).Count == 1, "dates follow code change");
}

// 7c. card groups / races / attributes from the CyanPro GitHub files
{
    var sn = RemoteInfo.ParseSetnames("#setnames\n!setname 0x300\t마칭 파이어\n!setname 0x305 벨루티나\r\n!counter 0x91 스피드 카운터\n!system 1093 기프트\n");
    Check(sn.Count == 2 && sn[0] == new InfoItem(0x300, "마칭 파이어") && sn[1] == new InfoItem(0x305, "벨루티나"), "setnames parsed");
    var races = RemoteInfo.ParseBits("#define RACE_WARRIOR      0x1\n#define RACE_YOKAI             0x4000000000000000\n#define RACE_MAX               RACE_GALAXY\n#define RACE_ALL (RACE_WARRIOR)\n#define RACE_X 0x3\n#define ATTRIBUTE_DARK   0x20\n", "RACE");
    Check(races.Count == 2 && races[0].Text == "전사족" && races[1] == new InfoItem(0x4000000000000000, "요괴족"), "races parsed (single bits only)");
    var list = new List<InfoItem> { new(-1, "(정의되지 않음)"), new(0, ""), new(0x300, "옛 이름"), new(0xef3, "결연희"), new(0xef4, "쓰는 중") };
    Check(RemoteInfo.Sync(list, sn, 0xef4) == 3 && list.Select(i => i.Text).SequenceEqual(new[] { "(정의되지 않음)", "", "마칭 파이어", "쓰는 중", "벨루티나" }),
          "setnames synced: renamed, added, removed unless in use: " + string.Join(",", list.Select(i => i.Text)));
    var rl = new List<InfoItem> { new(0, "(종족)"), new(0x1, "전사"), new(0x2, "마법사족") };
    Check(RemoteInfo.Sync(rl, races) == 3 && rl.Select(i => i.Value).SequenceEqual(new[] { 0L, 0x1, 0x4000000000000000 }) && rl[1].Text == "전사족",
          "races synced to CyanPro");
    Check(RemoteInfo.Sync(rl, new List<InfoItem>()) == 0 && rl.Count == 3, "an empty remote list changes nothing");
    // Type names: orica types through the data_manager table, others by bit position, cardinfo name as fallback.
    string th = "#define TYPE_MONSTER 0x1\n#define TYPE_TRAPMONSTER 0x100\n#define TYPE_UNLOCKED 0x00008000\n#define TYPE_ACCESS   0x08000000\n#define TYPE_NEWKIND 0x80000000\n";
    var types = RemoteInfo.ParseBits(th, "TYPE", "!system 1090 액세스X\n!system 1050 몬스터M\n", "{TYPE_ACCESS,  1090},", new[] { new InfoItem(0x80000000, "새 종류 ") });
    Check(types.Select(t => t.Text).SequenceEqual(new[] { "몬스터M", "해방됨", "액세스X", "새 종류" }), "types named: " + string.Join(",", types.Select(t => t.Text)));
    var unnamed = RemoteInfo.ParseBits(th, "TYPE");
    Check(unnamed[^1].Text == "TYPE_NEWKIND", "type with no name anywhere shows its define");
    // optional: the real files downloaded next to the cdb copies
    string realStrings = Path.Combine(cdbRoot, "..", "strings.conf"), realHeader = Path.Combine(cdbRoot, "..", "ocgapi_constants.h");
    string realDm = Path.Combine(cdbRoot, "..", "data_manager.cpp");
    if (File.Exists(realStrings) && File.Exists(realHeader) && File.Exists(realDm))
    {
        string s = File.ReadAllText(realStrings), h = File.ReadAllText(realHeader), d = File.ReadAllText(realDm);
        var rs = RemoteInfo.ParseSetnames(s);
        var rr = RemoteInfo.ParseBits(h, "RACE", s);
        var ra = RemoteInfo.ParseBits(h, "ATTRIBUTE", s);
        var rt = RemoteInfo.ParseBits(h, "TYPE", s, d);
        Console.WriteLine($"GitHub files: {rs.Count} setnames, {rr.Count} races, {ra.Count} attributes, types: {string.Join(",", rt.Select(t => t.Text))}");
        Check(rs.Count > 0 && rr.Count >= 32 && ra.Count == 7 && rt.Any(t => t == new InfoItem(0x8000000, "액세스")) && rt.All(t => !t.Text.Contains('_')),
              "real GitHub files parsed");
    }
}

// 7d. updates: GitHub release answer, version compare, and the updater script on a temp folder
{
    string Release(string tag, string asset = "YellowEditorX-1.2.0.zip", bool pre = false) =>
        "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (pre ? "true" : "false") + ",\"draft\":false,\"body\":\"- 숨김 체크박스\\r\\n- 아이콘\","
        + "\"assets\":[{\"name\":\"notes.txt\",\"browser_download_url\":\"https://x/notes.txt\"},{\"name\":\"" + asset + "\",\"browser_download_url\":\"https://x/" + asset + "\"}]}";
    var cur = new Version(1, 1, 0);
    var u = Updates.Parse(Release("v1.2.0"), cur);
    Check(u != null && u.Version == new Version(1, 2, 0, 0) && u.ZipUrl == "https://x/YellowEditorX-1.2.0.zip" && u.Notes == "- 숨김 체크박스\n- 아이콘", "newer release found");
    Check(Updates.Parse(Release("v1.1"), cur) == null && Updates.Parse(Release("1.0.9"), cur) == null, "same or older release ignored");
    Check(Updates.Parse(Release("v1.2.0", pre: true), cur) == null && Updates.Parse(Release("v1.2.0", "x.7z"), cur) == null && Updates.Parse(Release("latest"), cur) == null,
          "prerelease, no zip, bad tag ignored");
    Check(Updates.Parse(Release("v1.1.0.1"), new Version(1, 1, 0, 0)) != null, "fourth number counts");

    string upd = Path.Combine(Path.GetTempPath(), "yex_update_test");
    if (Directory.Exists(upd)) Directory.Delete(upd, true);
    string app = Path.Combine(upd, "앱 폴더"), extracted = Path.Combine(upd, "new");
    string inner = Path.Combine(extracted, "YellowEditorX");
    Directory.CreateDirectory(Path.Combine(app, "data"));
    Directory.CreateDirectory(Path.Combine(inner, "data", "github"));
    File.WriteAllText(Path.Combine(app, "settings.json"), "mine");
    File.WriteAllText(Path.Combine(app, "old.dll"), "old");
    File.WriteAllText(Path.Combine(app, "data", "cardinfo_korean.txt"), "edited");
    File.WriteAllText(Path.Combine(inner, "settings.json"), "theirs");
    File.WriteAllText(Path.Combine(inner, "old.dll"), "new");
    File.WriteAllText(Path.Combine(inner, "data", "cardinfo_korean.txt"), "released");
    File.WriteAllText(Path.Combine(inner, "data", "github", "strings.conf"), "s");
    File.WriteAllText(Path.Combine(inner, "start.cmd"), "@echo started> \"%~dp0started.txt\"");
    string folder = Updates.ProgramFolder(extracted);
    Check(folder == inner, "zipped folder found inside the zip");
    string log = Path.Combine(upd, "update.log"), script = Path.Combine(upd, "update.ps1");
    File.WriteAllText(script, Updates.UpdaterScript(999999, folder, app, "start.cmd", log), new System.Text.UTF8Encoding(true));
    var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
        $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true })!;
    ps.WaitForExit(60000);
    for (int i = 0; i < 50 && !File.Exists(Path.Combine(app, "started.txt")); i++) Thread.Sleep(100);
    string R(params string[] p) => File.Exists(Path.Combine(p)) ? File.ReadAllText(Path.Combine(p)).Trim() : "(none)";
    Check(R(log) == "ok" && R(app, "old.dll") == "new" && R(app, "settings.json") == "mine" && R(app, "data", "github", "strings.conf") == "s",
          $"updater copied the files, kept settings: log={R(log)} dll={R(app, "old.dll")} settings={R(app, "settings.json")}");
    Check(R(app, "data", "cardinfo_korean.txt") == "released" && R(app, "data", "cardinfo_korean.txt.bak") == "edited", "changed cardinfo backed up");
    Check(R(app, "started.txt") == "started", "editor started again");
    Directory.Delete(upd, true);
}

// 8. next free code
Check(Workspace.NextFreeCode(new HashSet<long> { 10, 11, 13 }, 10) == 12, "next free after 10");
Check(Workspace.NextFreeCode(new HashSet<long> { 10, 11, 13 }, 0) == 14, "next free after max");
Check(Workspace.NextFreeCode(new HashSet<long>(), 0) == 100000000, "next free in empty db");

// 9. card files: move, copy, remove, restore
string a = Path.Combine(tmp, "a", "A.cdb"), b = Path.Combine(tmp, "b", "B.cdb");
CardDb.Create(a); CardDb.Create(b);
var fa = new CardFiles(a); var fb = new CardFiles(b);
Directory.CreateDirectory(Path.GetDirectoryName(fa.Picture(1))!);
File.WriteAllText(fa.Picture(1), "img");
var scr = fa.EnsureScript(new Card { Id = 1, Name = "카드" }, out bool made);
Check(made && File.ReadAllText(scr) == "--카드\r\nlocal s,id=GetID()\r\nfunction s.initial_effect(c)\r\n\t\r\nend\r\n", "script template");
CardFiles.Transfer(fa, 1, fa, 2, move: true, overwrite: false);
Check(!File.Exists(fa.Picture(1)) && File.Exists(fa.Picture(2)) && File.Exists(fa.Script(2)), "move files to new code");
CardFiles.Transfer(fa, 2, fb, 2, move: false, overwrite: false);
Check(File.Exists(fa.Picture(2)) && File.Exists(fb.Picture(2)), "copy files to other db");
var saved = fb.Remove(2);
Check(saved.Count == 2 && !File.Exists(fb.Picture(2)), "remove files");
CardFiles.Restore(saved);
Check(File.ReadAllText(fb.Picture(2)) == "img", "restore files");
Check(new Card { Id = 101, Alias = 100 }.ScriptId == 100 && new Card { Id = 500, Alias = 100 }.ScriptId == 500, "alias script id");
Check(new Card { Id = 101, Alias = 100, Type = CardTypes.Monster | CardTypes.Key }.ScriptId == 101, "key card (unlocked) uses its own script");

// 10. write / delete in one transaction
CardDb.Write(a, new[] { sample });
Check(CardDb.Read(a, 123)?.Name == "테스트", "write then read");
CardDb.Write(a, Array.Empty<Card>(), new long[] { 123 });
Check(CardDb.Read(a, 123) == null && CardDb.ReadIds(a).Count == 0, "delete");

// 11. operations and undo
{
    string d1 = Path.Combine(tmp, "o1", "One.cdb"), d2 = Path.Combine(tmp, "o2", "Two.cdb");
    CardDb.Create(d1); CardDb.Create(d2);
    var f1 = new CardFiles(d1); var f2 = new CardFiles(d2);
    var hist = new History();
    var x = new Card { Id = 500, Name = "원본", Type = CardTypes.Spell };
    hist.Push(CardOps.Save(d1, x));
    Directory.CreateDirectory(Path.GetDirectoryName(f1.Picture(500))!);
    File.WriteAllText(f1.Picture(500), "p500");
    f1.EnsureScript(x, out _);
    var y = x.Clone(); y.Name = "수정";
    hist.Push(CardOps.Save(d1, y));
    Check(CardDb.Read(d1, 500)!.Name == "수정", "update");
    hist.Undo();
    Check(CardDb.Read(d1, 500)!.Name == "원본", "undo update");
    var moved = x.Clone(); moved.Id = 600;
    hist.Push(CardOps.ChangeCode(d1, 500, moved, move: true));
    Check(CardDb.Read(d1, 500) == null && CardDb.Read(d1, 600) != null && File.Exists(f1.Picture(600)) && !File.Exists(f1.Picture(500)), "change code move");
    hist.Undo();
    Check(CardDb.Read(d1, 500) != null && CardDb.Read(d1, 600) == null && File.Exists(f1.Picture(500)) && !File.Exists(f1.Picture(600)), "undo change code");
    hist.Push(CardOps.ChangeCode(d1, 500, moved, move: false));
    Check(CardDb.Read(d1, 500) != null && File.Exists(f1.Picture(600)) && File.Exists(f1.Picture(500)), "change code copy");
    hist.Undo();
    Check(CardDb.Read(d1, 600) == null && !File.Exists(f1.Picture(600)), "undo copy");
    // copy to another db, with an existing card that gets overwritten
    var old2 = new Card { Id = 500, Name = "대상 원래" };
    CardDb.Write(d2, new[] { old2 });
    Directory.CreateDirectory(Path.GetDirectoryName(f2.Picture(500))!);
    File.WriteAllText(f2.Picture(500), "old2");
    hist.Push(CardOps.CopyTo(d1, new[] { x }, d2, overwrite: false, move: false, out int n0));
    Check(n0 == 0 && CardDb.Read(d2, 500)!.Name == "대상 원래", "copy skip existing");
    hist.Push(CardOps.CopyTo(d1, new[] { x }, d2, overwrite: true, move: false, out int n1));
    Check(n1 == 1 && CardDb.Read(d2, 500)!.Name == "원본" && File.ReadAllText(f2.Picture(500)) == "p500" && File.Exists(f2.Script(500)), "copy overwrite with files");
    hist.Undo();
    Check(CardDb.Read(d2, 500)!.Name == "대상 원래" && File.ReadAllText(f2.Picture(500)) == "old2" && !File.Exists(f2.Script(500)), "undo copy overwrite");
    hist.Undo(); // the skipped copy (no change)
    var z = new Card { Id = 700, Name = "이동" };
    CardDb.Write(d1, new[] { z });
    File.WriteAllText(f1.Picture(700), "p700");
    hist.Push(CardOps.CopyTo(d1, new[] { z }, d2, overwrite: false, move: true, out _));
    Check(CardDb.Read(d1, 700) == null && CardDb.Read(d2, 700) != null && File.Exists(f2.Picture(700)) && !File.Exists(f1.Picture(700)), "move to other db");
    hist.Undo();
    Check(CardDb.Read(d1, 700) != null && CardDb.Read(d2, 700) == null && File.Exists(f1.Picture(700)) && !File.Exists(f2.Picture(700)), "undo move");
    hist.Push(CardOps.Delete(d1, new[] { x }, withFiles: true));
    Check(CardDb.Read(d1, 500) == null && !File.Exists(f1.Picture(500)) && !File.Exists(f1.Script(500)), "delete with files");
    hist.Undo();
    Check(CardDb.Read(d1, 500)!.Name == "원본" && File.ReadAllText(f1.Picture(500)) == "p500" && File.Exists(f1.Script(500)), "undo delete");
    Directory.CreateDirectory(Path.GetDirectoryName(f1.Thumbnail(500))!);
    hist.Push(CardOps.ReplaceImage(d1, 500, (p, t) => { File.WriteAllText(p, "new"); if (t != null) File.WriteAllText(t, "newt"); }));
    Check(File.ReadAllText(f1.Picture(500)) == "new" && File.ReadAllText(f1.Thumbnail(500)) == "newt", "replace image");
    hist.Undo();
    Check(File.ReadAllText(f1.Picture(500)) == "p500" && !File.Exists(f1.Thumbnail(500)), "undo replace image");
}

Directory.Delete(tmp, true);
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures == 0 ? 0 : 1;
