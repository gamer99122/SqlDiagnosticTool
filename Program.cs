using System;
using Microsoft.Data.SqlClient;
using System.Data;

// 連接字串 - 使用 Windows 驗證
string connectionString = "Server=172.17.2.31;Database=master;Integrated Security=true;TrustServerCertificate=true;";

Console.WriteLine("SQL Server 健康診斷工具");
Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
Console.WriteLine();

try
{
    // 測試資料庫連接
    if (!TestConnection())
    {
        Console.WriteLine("X 無法連接到 SQL Server，請檢查連接設定");
        Console.WriteLine("按任意鍵退出...");
        Console.ReadKey();
        return;
    }

    Console.WriteLine("資料庫連接成功，開始診斷...");
    Console.WriteLine();

    // 開始診斷
    RunDiagnosis();
}
catch (Exception ex)
{
    Console.WriteLine("X 程式執行錯誤: " + ex.Message);
}

Console.WriteLine();
Console.WriteLine("診斷完成，按任意鍵退出...");
Console.ReadKey();

bool TestConnection()
{
    try
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            return true;
        }
    }
    catch
    {
        return false;
    }
}

void RunDiagnosis()
{
    // 1. 檢查記憶體使用
    CheckMemoryUsage();

    Console.WriteLine();

    // 2. 檢查長時間交易
    CheckLongRunningTransactions();

    Console.WriteLine();

    // 3. 檢查資料庫日誌等待
    CheckDatabaseLogWait();
}

void CheckMemoryUsage()
{
    Console.WriteLine("記憶體使用情況檢查:");

    string sql = @"
        SELECT 
            type, 
            SUM(pages_kb)/1024 as memory_gb 
        FROM sys.dm_os_memory_clerks 
        WHERE type = 'MEMORYCLERK_XTP'
        GROUP BY type";

    try
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    bool hasData = false;
                    while (reader.Read())
                    {
                        hasData = true;
                        double memoryGb = Convert.ToDouble(reader["memory_gb"]);
                        Console.WriteLine("  XTP 記憶體使用: " + memoryGb.ToString("F2") + " GB");

                        if (memoryGb > 1.0)
                        {
                            Console.WriteLine("  警告: XTP 記憶體使用量較高");
                        }
                    }

                    if (!hasData)
                    {
                        Console.WriteLine("  XTP 記憶體: 未使用或資料不可用");
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("  記憶體檢查失敗: " + ex.Message);
    }
}

void CheckLongRunningTransactions()
{
    Console.WriteLine("長時間交易檢查:");

    string sql = @"
        SELECT 
            s.session_id,
            s.program_name,
            s.host_name,
            s.login_name,
            t.transaction_begin_time,
            DATEDIFF(MINUTE, t.transaction_begin_time, GETDATE()) as duration_minutes,
            CASE t.transaction_type
                WHEN 1 THEN 'Read/Write'
                WHEN 2 THEN 'Read-Only'  
                WHEN 3 THEN 'System'
                WHEN 4 THEN 'Distributed'
                ELSE 'Unknown'
            END as transaction_type,
            CASE t.transaction_state
                WHEN 0 THEN 'Uninitialized'
                WHEN 1 THEN 'Initialized'
                WHEN 2 THEN 'Active'
                WHEN 3 THEN 'Ended'
                WHEN 4 THEN 'Commit initiated'
                WHEN 5 THEN 'Prepared'
                WHEN 6 THEN 'Committed'
                WHEN 7 THEN 'Rolling back'
                WHEN 8 THEN 'Rolled back'
                ELSE 'Unknown'
            END as transaction_state
        FROM sys.dm_tran_active_transactions t
        JOIN sys.dm_tran_session_transactions st ON t.transaction_id = st.transaction_id
        JOIN sys.dm_exec_sessions s ON st.session_id = s.session_id
        WHERE DATEDIFF(MINUTE, t.transaction_begin_time, GETDATE()) > 5
        ORDER BY duration_minutes DESC";

    try
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    bool foundLongTransaction = false;

                    while (reader.Read())
                    {
                        foundLongTransaction = true;
                        int sessionId = Convert.ToInt32(reader["session_id"]);
                        int duration = Convert.ToInt32(reader["duration_minutes"]);
                        string programName = reader["program_name"].ToString();
                        string hostName = reader["host_name"].ToString();
                        string loginName = reader["login_name"].ToString();
                        string transactionType = reader["transaction_type"].ToString();
                        string transactionState = reader["transaction_state"].ToString();

                        Console.WriteLine("  發現長時間交易!");
                        Console.WriteLine("    Session ID: " + sessionId);
                        Console.WriteLine("    持續時間: " + duration + " 分鐘");
                        Console.WriteLine("    程式名稱: " + programName);
                        Console.WriteLine("    主機名稱: " + hostName);
                        Console.WriteLine("    使用者: " + loginName);
                        Console.WriteLine("    交易類型: " + transactionType);
                        Console.WriteLine("    交易狀態: " + transactionState);
                        Console.WriteLine();

                        // 提供建議
                        if (duration > 30)
                        {
                            Console.WriteLine("  建議: 這個交易已經運行 " + duration + " 分鐘，請檢查:");
                            Console.WriteLine("    1. 程式 '" + programName + "' 是否正常運作");
                            Console.WriteLine("    2. 是否需要手動結束此 Session");
                            Console.WriteLine("    注意: 強制結束可能造成資料遺失");
                        }

                        Console.WriteLine("  ----------------------------------------");
                    }

                    if (!foundLongTransaction)
                    {
                        Console.WriteLine("  沒有發現長時間運行的交易 (>5分鐘)");
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("  長時間交易檢查失敗: " + ex.Message);
    }
}

void CheckDatabaseLogWait()
{
    Console.WriteLine("資料庫日誌等待檢查:");

    string sql = @"
        SELECT 
            name as database_name,
            log_reuse_wait_desc
        FROM sys.databases 
        WHERE log_reuse_wait_desc != 'NOTHING'
            AND name NOT IN ('master', 'model', 'msdb', 'tempdb')";

    try
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    bool foundIssue = false;

                    while (reader.Read())
                    {
                        foundIssue = true;
                        string dbName = reader["database_name"].ToString();
                        string waitDesc = reader["log_reuse_wait_desc"].ToString();

                        Console.WriteLine("  資料庫: " + dbName);
                        Console.WriteLine("  日誌等待原因: " + waitDesc);

                        // 根據等待原因提供建議
                        switch (waitDesc)
                        {
                            case "ACTIVE_TRANSACTION":
                                Console.WriteLine("  問題: 有活躍的長時間交易阻止日誌截斷");
                                Console.WriteLine("  建議: 檢查長時間運行的交易並適當處理");
                                break;
                            case "LOG_BACKUP":
                                Console.WriteLine("  問題: 需要進行交易日誌備份");
                                Console.WriteLine("  建議: 執行交易日誌備份");
                                break;
                            case "CHECKPOINT":
                                Console.WriteLine("  問題: 等待檢查點操作完成");
                                Console.WriteLine("  建議: 通常會自動解決，可手動執行 CHECKPOINT");
                                break;
                            default:
                                Console.WriteLine("  建議: 請查閱 SQL Server 文件了解 '" + waitDesc + "' 的含義");
                                break;
                        }

                        Console.WriteLine("  ----------------------------------------");
                    }

                    if (!foundIssue)
                    {
                        Console.WriteLine("  所有使用者資料庫的日誌都可以正常重複使用");
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("  資料庫日誌檢查失敗: " + ex.Message);
    }
}