using System.Text.RegularExpressions;

namespace YellowEditorX.Core;

/// <summary>
/// The 제작 정보 lines at the end of the card text, in the format the OricaWeb card site reads
/// (pendulums58/OricaWeb, template/decode.js):
/// <code>
/// [제작자]                 ← the first [ ] line is always the maker
/// [태그: 태그1 | 태그2]     ← tags are split on |
/// [팩 이름]                ← any later plain line is the pack name (the last one wins)
/// [ABCD-AM001]             ← card number; the site groups packs by it
/// </code>
/// The editor shows them as separate fields; the database keeps them in desc, so YGOPro shows them too.
/// </summary>
public sealed class CardFooter
{
    public string Maker = "", Tags = "", Pack = "", Number = "";
    /// <summary>Footer lines there is no field for, written back between the tags and the pack name.</summary>
    public List<string> Extra = new();
    /// <summary>[제작일: …] / [수정일: …] typed into the text; the dates belong in the card_dates table.</summary>
    public string Created = "", Modified = "";

    static readonly Regex Line = new(@"^\s*\[(.*)\]\s*$");
    // Same patterns as the site: NUMBER and LABELLED in decode.js.
    static readonly Regex NumberRx = new(@"^[A-Z0-9]{3,6}-[A-Z]{2}\d{3}$");
    static readonly Regex Labeled = new(@"^([^\[\]:：]+?)\s*[:：]\s*(.*)$");

    /// <summary>True for a card number the site accepts (ABCD-AM001), or an empty one.</summary>
    public static bool IsNumber(string s) => s.Trim().Length == 0 || NumberRx.IsMatch(s.Trim().ToUpperInvariant());

    /// <summary>Splits desc into the card text and the trailing [ ] lines, reading them the way the site does.</summary>
    public static (string Body, CardFooter Footer) Split(string desc)
    {
        var f = new CardFooter();
        var lines = desc.Replace("\r\n", "\n").Split('\n');
        int end = lines.Length;
        while (end > 0 && lines[end - 1].Trim().Length == 0) end--;
        int start = end;
        while (start > 0 && Line.IsMatch(lines[start - 1])) start--;
        if (start == end) return (desc, f);

        var inner = lines[start..end].Select(l => Line.Match(l).Groups[1].Value.Trim()).ToList();
        for (int i = 0; i < inner.Count; i++)
        {
            string s = inner[i];
            if (s.Length == 0) continue;
            if (NumberRx.IsMatch(s))
            {
                if (f.Number.Length > 0) f.Extra.Add(f.Number);
                f.Number = s;
                continue;
            }
            var m = Labeled.Match(s);
            if (m.Success)
            {
                string value = m.Groups[2].Value.Trim();
                switch (Regex.Replace(m.Groups[1].Value, @"\s+", ""))
                {
                    // Written as is: tags are split on | only, a comma can be part of a tag.
                    case "태그" when f.Tags.Length == 0: f.Tags = value; break;
                    case "카드군": break; // the setcodes already say this
                    case "제작일": f.Created = value; break;
                    case "수정일": f.Modified = value; break;
                    default: f.Extra.Add(s); break;
                }
            }
            else if (i == 0) f.Maker = s;
            else
            {
                if (f.Pack.Length > 0) f.Extra.Add(f.Pack);
                f.Pack = s;
            }
        }
        string body = string.Join("\r\n", lines[..start]).TrimEnd();
        return (body, f);
    }

    /// <summary>The card text with the footer lines after a blank line (just the text when there are none).</summary>
    public static string Join(string body, CardFooter f)
    {
        var lines = new List<string>();
        void Add(string s) { if (s.Trim().Length > 0) lines.Add("[" + s.Trim() + "]"); }
        Add(f.Maker);
        if (f.Tags.Trim().Length > 0) Add("태그: " + f.Tags.Trim());
        foreach (var s in f.Extra) Add(s);
        Add(f.Pack);
        Add(f.Number.Trim().ToUpperInvariant());
        if (lines.Count == 0) return body;
        string text = body.TrimEnd();
        return (text.Length > 0 ? text + "\r\n\r\n" : "") + string.Join("\r\n", lines);
    }
}
