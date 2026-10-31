using System.Text.Json;

namespace YellowEditorX.Core;

/// <summary>A newer version published as a GitHub release: its version, what changed and the zip to download.</summary>
public sealed record UpdateInfo(Version Version, string Notes, string ZipUrl);

/// <summary>
/// New versions are GitHub releases of YellowGrow/YellowEditorX: tag "v1.2.0" with the published folder as a zip.
/// </summary>
public static class Updates
{
    public const string Repository = "YellowGrow/YellowEditorX";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>Files of the user that an update never replaces.</summary>
    public static readonly string[] KeepFiles = { "settings.json" };

    /// <summary>The release from the GitHub API answer, or null when it isn't newer than <paramref name="current"/> or has no zip.</summary>
    public static UpdateInfo? Parse(string releaseJson, Version current)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        if (!root.TryGetProperty("tag_name", out var tag) || !TryVersion(tag.GetString(), out var v)) return null;
        if (Normalize(v) <= Normalize(current)) return null;
        string? zip = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
                if (a.GetProperty("name").GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                {
                    zip = a.GetProperty("browser_download_url").GetString();
                    break;
                }
        if (zip == null) return null;
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new UpdateInfo(v, notes.Replace("\r\n", "\n").Trim(), zip);
    }

    /// <summary>"v1.2" / "1.2.0" → 1.2.0.</summary>
    public static bool TryVersion(string? tag, out Version v)
    {
        v = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        string s = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(s, out var parsed)) return false;
        v = Normalize(parsed);
        return true;
    }

    /// <summary>1.2 and 1.2.0.0 count as the same version.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>
    /// The folder inside an extracted zip that holds the program: the zip root, or its only subfolder when the
    /// files were zipped together with their folder.
    /// </summary>
    public static string ProgramFolder(string extracted)
    {
        string dir = extracted;
        while (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir) is { Length: 1 } only) dir = only[0];
        return dir;
    }

    /// <summary>
    /// The PowerShell script that waits for the editor to close, copies the new files over the old ones
    /// (keeping <see cref="KeepFiles"/> and a .bak of a changed cardinfo) and starts the editor again.
    /// </summary>
    public static string UpdaterScript(int processId, string newFolder, string appFolder, string exeName, string logPath)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        string keep = string.Join(",", KeepFiles.Select(Q));
        return $@"$ErrorActionPreference = 'Stop'
$src = {Q(newFolder)}
$dst = {Q(appFolder)}
$log = {Q(logPath)}
try {{ Wait-Process -Id {processId} -Timeout 60 -ErrorAction SilentlyContinue }} catch {{ }}
Start-Sleep -Milliseconds 300
try {{
    $keep = @({keep})
    $info = Join-Path $dst 'data\cardinfo_korean.txt'
    $newInfo = Join-Path $src 'data\cardinfo_korean.txt'
    if ((Test-Path $info) -and (Test-Path $newInfo) -and ((Get-FileHash $info).Hash -ne (Get-FileHash $newInfo).Hash)) {{
        Copy-Item $info ($info + '.bak') -Force
    }}
    Get-ChildItem $src -Recurse -File | ForEach-Object {{
        $rel = $_.FullName.Substring($src.Length).TrimStart('\')
        if ($keep -contains $rel) {{ return }}
        $target = Join-Path $dst $rel
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        for ($i = 0; $i -lt 10; $i++) {{
            try {{ Copy-Item $_.FullName $target -Force; break }} catch {{ if ($i -eq 9) {{ throw }}; Start-Sleep -Milliseconds 500 }}
        }}
    }}
    'ok' | Set-Content $log
}} catch {{
    ('failed: ' + $_) | Set-Content $log
}}
Start-Process (Join-Path $dst {Q(exeName)})
";
    }
}
