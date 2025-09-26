# SQL Server 診斷工具

一個簡單易用的 SQL Server 健康檢查工具，能快速診斷常見的資料庫效能問題並提供解決建議。

## 功能特色

- **一鍵診斷** - 開啟程式即可自動檢測問題
- **智能建議** - 不只顯示問題，更提供具體的解決方案
- **簡潔輸出** - 清楚明瞭的文字報告，無複雜圖表
- **安全無害** - 只進行查詢操作，不會修改任何資料
- **Windows 整合** - 使用 Windows 身份驗證，無需額外設定帳密

## 檢查項目

### 1. 記憶體使用監控
- 檢查 In-Memory OLTP (XTP) 記憶體使用量
- 當使用量超過 1GB 時發出警告
- 有助於識別記憶體洩漏或過度使用問題

### 2. 長時間交易偵測
- 自動找出持續時間超過 5 分鐘的活躍交易
- 顯示詳細的會話資訊（Session ID、程式名稱、主機、使用者）
- 分析交易類型和狀態
- 提供處理建議和風險警告

### 3. 資料庫日誌等待分析
- 檢查資料庫交易日誌無法重複使用的原因
- 識別常見問題如：活躍交易阻塞、需要日誌備份等
- 提供對應的解決方案

## 系統需求

- **.NET 8** 或更新版本
- **Windows** 作業系統（使用 Windows 身份驗證）
- 對目標 SQL Server 具有 **VIEW SERVER STATE** 權限
- **Microsoft.Data.SqlClient** NuGet 套件

## 安裝與使用

### 1. 下載專案
```bash
git clone https://github.com/gamer99122/sql-server-diagnostic-tool.git
cd sql-server-diagnostic-tool
```

### 2. 安裝相依套件
```bash
dotnet add package Microsoft.Data.SqlClient
```

### 3. 修改連接設定
編輯 `Program.cs` 中的連接字串：
```csharp
string connectionString = "Server=你的伺服器名稱;Database=master;Integrated Security=true;Connection Timeout=30;";
```

### 4. 編譯並執行
```bash
dotnet build
dotnet run
```

## 輸出範例

```
SQL Server 診斷工具
2024-09-25 14:30:22

資料庫連接成功，開始診斷...

記憶體使用情況檢查:
  XTP 記憶體使用: 2.30 GB
  警告: XTP 記憶體使用量較高

長時間交易檢查:
  發現長時間交易!
    Session ID: 2710
    持續時間: 47 分鐘
    程式名稱: MyApp.exe
    主機名稱: SERVER01
    使用者: AppUser
    交易類型: Read/Write
    交易狀態: Active

  建議: 這個交易已經運行 47 分鐘，請檢查:
    1. 程式 'MyApp.exe' 是否正常運作
    2. 是否需要手動結束此 Session
    注意: 強制結束可能造成資料遺失

資料庫日誌等待檢查:
  資料庫: DialerComm
  日誌等待原因: ACTIVE_TRANSACTION
  問題: 有活躍的長時間交易阻止日誌截斷
  建議: 檢查長時間運行的交易並適當處理

診斷完成，按任意鍵退出...
```

## SQL 查詢說明

### 記憶體使用查詢
```sql
SELECT 
    type, 
    SUM(pages_kb)/1024 as memory_gb 
FROM sys.dm_os_memory_clerks 
WHERE type = 'MEMORYCLERK_XTP'
GROUP BY type
```
**用途**: 監控 In-Memory OLTP 引擎的記憶體使用量，幫助識別記憶體相關的效能問題。

### 長時間交易查詢
```sql
SELECT 
    s.session_id,
    s.program_name,
    s.host_name,
    s.login_name,
    t.transaction_begin_time,
    DATEDIFF(MINUTE, t.transaction_begin_time, GETDATE()) as duration_minutes,
    -- 交易類型和狀態的解碼...
FROM sys.dm_tran_active_transactions t
JOIN sys.dm_tran_session_transactions st ON t.transaction_id = st.transaction_id
JOIN sys.dm_exec_sessions s ON st.session_id = s.session_id
WHERE DATEDIFF(MINUTE, t.transaction_begin_time, GETDATE()) > 5
ORDER BY duration_minutes DESC
```
**用途**: 
- 找出長時間未提交的交易
- 識別可能造成鎖定和阻塞的根源
- 追蹤應用程式的異常行為
- 預防交易日誌膨脹

### 資料庫日誌等待查詢
```sql
SELECT 
    name as database_name,
    log_reuse_wait_desc
FROM sys.databases 
WHERE log_reuse_wait_desc != 'NOTHING'
    AND name NOT IN ('master', 'model', 'msdb', 'tempdb')
```
**用途**: 
- 診斷為什麼交易日誌無法被截斷重複使用
- 識別日誌空間膨脹的原因
- 提前預防磁碟空間不足的問題

## 常見問題解決

### 連接失敗
- 確認 SQL Server 服務正在運行
- 檢查 Windows 身份驗證是否啟用
- 驗證當前 Windows 帳號是否有連接權限

### 權限不足
需要以下權限才能執行診斷：
- **VIEW SERVER STATE** - 查看伺服器狀態
- **CONNECT SQL** - 連接到 SQL Server

### 無資料顯示
- 某些檢查項目在正常情況下可能沒有資料（例如：沒有長時間交易）
- 這表示系統運行正常

---

**備註**: 此程式碼由 Claude AI 協助產生