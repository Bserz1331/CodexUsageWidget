# 發布檢查表

## 自動檢查

- [ ] 執行 `powershell -ExecutionPolicy Bypass -File .\建立發布包.ps1`
- [ ] 所有自動化測試通過
- [ ] 確認發布包只含 EXE、README、LICENSE、PRIVACY
- [ ] 核對 `SHA256SUMS.txt`
- [ ] 搜尋並排除個人路徑、權杖、密碼、session 與診斷記錄

## Windows 實機檢查

- [ ] 正常啟動並顯示浮窗與系統匣圖示
- [ ] 第二次啟動會叫出既有浮窗，不產生第二個程序
- [ ] 關閉浮窗後可由系統匣重新顯示
- [ ] 拖曳位置、鎖定、透明度與滑鼠穿透可保存
- [ ] 登出或重新開機後，開機啟動正常
- [ ] 休眠再喚醒後，額度資料與檔案監控會刷新
- [ ] Explorer 重新啟動後，系統匣圖示可恢復
- [ ] 執行三小時資源穩定性測試並檢查記憶體、Handle、GDI 物件沒有持續上升

## GitHub 發布

- [ ] 公開儲存庫包含 README、LICENSE、PRIVACY、SECURITY、CONTRIBUTING
- [ ] GitHub Actions 測試成功
- [ ] 建立版本標籤與 Release
- [ ] 上傳 EXE、ZIP、SHA256SUMS.txt
- [ ] Release 說明註明 Windows x64、未簽章 SmartScreen 提示及本機資料來源限制
- [ ] 加入介面截圖

