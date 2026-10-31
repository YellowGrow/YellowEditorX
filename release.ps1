# 새 버전 내보내기: 버전 번호를 올리고, 빌드해서 zip으로 묶고, GitHub 릴리스로 올립니다.
# 에디터는 켤 때 이 릴리스를 보고 업데이트 안내창을 띄웁니다.
#
#   .\release.ps1 1.2.0 "- 숨김 체크박스 추가`n- 검색 개선"
#
# 필요한 것: .NET 8 SDK, git, GitHub CLI(gh, 로그인된 상태)
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "버전은 1.2.0 같은 형식이어야 해요." }
$tag = "v$Version"
if (git tag --list $tag) { throw "$tag 태그가 이미 있어요. 더 높은 버전을 쓰세요." }

# 1. 버전 번호
$proj = 'src\YellowEditorX\YellowEditorX.csproj'
$text = [IO.File]::ReadAllText($proj)
$text = [Regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($proj, $text)

# 2. 빌드
$out = Join-Path $PSScriptRoot 'release\YellowEditorX'
if (Test-Path (Join-Path $PSScriptRoot 'release')) { Remove-Item (Join-Path $PSScriptRoot 'release') -Recurse -Force }
dotnet publish src\YellowEditorX -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $out
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

# 3. zip (개인 설정은 넣지 않음)
Get-ChildItem $out -Recurse -Include settings.json, *.bak | Remove-Item -Force
if (Test-Path (Join-Path $out 'data\github')) { Remove-Item (Join-Path $out 'data\github') -Recurse -Force }
$zip = Join-Path $PSScriptRoot "release\YellowEditorX-$Version.zip"
Compress-Archive -Path $out -DestinationPath $zip

# 4. 커밋, 태그, 릴리스
git add $proj
git commit -m "YellowEditorX $Version"
git tag $tag
git push
git push origin $tag
if ($Notes -eq "") { $Notes = "YellowEditorX $Version" }
gh release create $tag $zip --title "YellowEditorX $Version" --notes $Notes
Write-Host "올렸어요: $tag ($zip)"
