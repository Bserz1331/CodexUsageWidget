# Codex Usage Widget

適用於 Windows 的非官方 Codex Plus 每週使用量浮窗與系統匣工具。

> This is an unofficial community project. It is not affiliated with or endorsed by OpenAI.

## 功能

- 顯示 Codex 每週剩餘百分比與重設日期。
- 系統匣圖示直接顯示剩餘整數百分比。
- 30%以上為綠色、10～29%為橘色、低於10%為紅色。
- Codex 寫入本機 session 時立即更新，另有可調整的保底檢查。
- 支援透明度、位置鎖定、滑鼠穿透、開機啟動與隱藏浮窗。
- 保存視窗位置及設定。
- 防止較舊資料覆蓋新資料。
- 連續讀取失敗或額度資料停滯時顯示警告並寫入診斷記錄。

## 系統需求

- Windows 10或Windows 11 x64。
- Codex桌面應用程式或CLI曾在本機產生session紀錄。
- 發布版為.NET 8自包含單一EXE，不需要另外安裝.NET。

## 安裝與使用

1. 從GitHub Releases下載`CodexUsageWidget.exe`。
2. 將EXE放到固定資料夾。
3. 雙擊執行。
4. 在浮窗或系統匣圖示上按右鍵開啟設定。

程式會讀取：

```text
%USERPROFILE%\.codex\sessions
```

設定、歷史與診斷資料儲存在：

```text
%LOCALAPPDATA%\CodexUsageWidget
```

## 隱私

- 所有解析都在本機完成。
- 不會讀取`auth.json`。
- 不會上傳session內容、提示、程式碼或使用量。
- 不含遙測或分析服務。
- 詳細內容請參閱[PRIVACY.md](PRIVACY.md)。

## 從原始碼建置

需要.NET 8 SDK：

```powershell
dotnet restore CodexUsageWidget.sln
dotnet test CodexUsageWidget.sln -c Release
dotnet publish src/CodexUsageWidget/CodexUsageWidget.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/publish
```

也可以在Windows上執行`編譯EXE.cmd`。

## 相容性提醒

Codex本機session格式不是公開且保證穩定的API。Codex更新後，解析器可能需要同步調整。若發生問題，請在系統匣右鍵選擇「複製診斷資訊」並附在Issue中；請勿上傳完整session檔案。

## 開發

- 主程式：`src/CodexUsageWidget`
- 自動化測試：`tests/CodexUsageWidget.Tests`
- CI：`.github/workflows/build.yml`
- 目前版本：2.3.2

## 授權

[MIT License](LICENSE)
