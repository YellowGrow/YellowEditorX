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
# 'Stop'이면 PowerShell 5.1이 git의 경고 메시지까지 오류로 보고 멈추므로, 각 단계에서 직접 확인합니다.
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "버전은 1.2.0 같은 형식이어야 해요." }
$tag = "v$Version"
if (git tag --list $tag) { throw "$tag 태그가 이미 있어요. 더 높은 버전을 쓰세요." }

# 1. 버전 번호
$proj = Join-Path $PSScriptRoot 'src\YellowEditorX\YellowEditorX.csproj'
$text = [IO.File]::ReadAllText($proj)
$text = [Regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($proj, $text)

# 2. 빌드
$out = Join-Path $PSScriptRoot 'release\YellowEditorX'
if (Test-Path (Join-Path $PSScriptRoot 'release')) { Remove-Item (Join-Path $PSScriptRoot 'release') -Recurse -Force -ErrorAction Stop }
dotnet publish src\YellowEditorX -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $out
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

# 3. zip (개인 설정은 넣지 않음)
Get-ChildItem $out -Recurse -Include settings.json, *.bak | Remove-Item -Force -ErrorAction Stop
if (Test-Path (Join-Path $out 'data\github')) { Remove-Item (Join-Path $out 'data\github') -Recurse -Force -ErrorAction Stop }
$zip = Join-Path $PSScriptRoot "release\YellowEditorX-$Version.zip"
Compress-Archive -Path $out -DestinationPath $zip -ErrorAction Stop

# 4. 커밋, 태그, 릴리스
# 버전 번호뿐 아니라 고친 소스도 모두 같이 커밋해서, 릴리스 태그가 zip과 같은 코드를 가리키게 합니다.
# (bin, obj, release, publish 같은 빌드 결과는 .gitignore에 있어서 안 들어갑니다.)
git add -A 2>&1 | Out-Null
Write-Host "커밋할 파일:"
git diff --cached --name-status
git commit -q -m "YellowEditorX $Version" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "git commit 실패" }
# git은 gh의 GitHub 로그인을 빌려서 올립니다 (git 설정은 바꾸지 않음).
git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push origin HEAD
if ($LASTEXITCODE -ne 0) { throw "git push 실패" }
if ($Notes -eq "") { $Notes = "YellowEditorX $Version" }
# 태그는 GitHub에서 방금 올린 커밋에 만들어집니다.
gh release create $tag $zip --target (git rev-parse HEAD) --title "YellowEditorX $Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "릴리스 만들기 실패" }
git fetch --tags -q
Write-Host "올렸어요: $tag ($zip)"
