# YellowEditorX

CyanPro 오리카용 카드 DB(.cdb) 에디터.

- 여러 cdb를 열어 두고 카드 편집, 복사·이동, 이미지와 스크립트 관리
- 카드 텍스트 아래 제작 정보(제작자, 태그, 팩 이름, 카드 번호)를 따로 입력, 제작일·수정일은 cdb 안의 별도 표에 저장
- 카드군·종류·종족·속성 목록을 CyanPro GitHub와 똑같이 맞춤
- 켤 때 새 버전이 있으면 안내하고, 확인을 누르면 업데이트 후 다시 켜짐

## 설치

[Releases](https://github.com/YellowGrow/YellowEditorX/releases)에서 zip을 받아 원하는 폴더에 풀고 `YellowEditorX.exe`를 실행합니다.
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)이 필요합니다.
Program Files처럼 쓰기가 막힌 폴더에 두면 자동 업데이트가 안 됩니다.

## 새 버전 내보내기

```powershell
.\release.ps1 1.2.0 "- 바뀐 점 1`n- 바뀐 점 2"
```

버전 번호를 올리고, 빌드해서 zip으로 묶고, GitHub 릴리스로 올립니다. 업데이트할 때 각자의 `settings.json`은 그대로 남습니다.

## 개발

```powershell
dotnet build src\YellowEditorX
dotnet run --project tests\YellowEditorX.Tests -- <cardinfo.txt> <cdb 사본 폴더>
YellowEditorX.exe --selftest <cdb 사본 폴더> <결과 파일>
```
