using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

public sealed record DefaultPaths(string Data, string Log, string Backup);

/// <summary>Server and database administration queries.</summary>
public sealed class AdminService
{
    public static readonly string[] FixedDatabaseRoles =
    [
        "db_owner", "db_datareader", "db_datawriter", "db_ddladmin",
        "db_securityadmin", "db_accessadmin", "db_backupoperator", "db_denydatareader", "db_denydatawriter"
    ];

    public static readonly string[] MappingRoles = ["db_owner", "db_datareader", "db_datawriter", "db_ddladmin"];

    public static IReadOnlyList<(int Level, string Label)> CompatibilityLevels(ManagedServer server)
    {
        var max = Math.Max(server.Major * 10, 80);
        var levels = new List<(int, string)>();
        foreach (var (level, name) in new[]
                 {
                     (170, "SQL Server 2025"), (160, "SQL Server 2022"), (150, "SQL Server 2019"), (140, "SQL Server 2017"),
                     (130, "SQL Server 2016"), (120, "SQL Server 2014"), (110, "SQL Server 2012"), (100, "SQL Server 2008"),
                     (90, "SQL Server 2005"), (80, "SQL Server 2000")
                 })
        {
            if (level <= max && level >= max - 80)
                levels.Add((level, $"{name} ({level})"));
        }

        return levels;
    }

    public async Task<DefaultPaths> GetDefaultPathsAsync(ManagedServer server)
    {
        var rows = await Db.QueryAsync(server.Master, """
            SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)),
                   CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512)),
                   CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512)),
                   (SELECT TOP (1) physical_name FROM sys.master_files WHERE database_id = 1 AND file_id = 1)
            """,
            r => (Data: r.NStr(0), Log: r.NStr(1), Backup: r.NStr(2), Master: r.NStr(3)));

        var row = rows[0];
        var masterDir = row.Master is null ? string.Empty : DirectoryOf(row.Master);
        var data = row.Data ?? masterDir;
        var log = row.Log ?? data;
        var backup = row.Backup;

        if (backup is null)
        {
            // Before SQL Server 2019 the backup folder is only in the registry
            try
            {
                var registry = await Db.QueryAsync(server.Master, """
                    DECLARE @dir nvarchar(4000);
                    EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory', @dir OUTPUT;
                    SELECT @dir;
                    """, r => r.NStr(0));
                backup = registry.FirstOrDefault();
            }
            catch (SqlException)
            {
            }
        }

        return new DefaultPaths(WithSeparator(data), WithSeparator(log), WithSeparator(backup ?? data));
    }

    public static string DirectoryOf(string path)
    {
        var index = path.LastIndexOfAny(['\\', '/']);
        return index < 0 ? string.Empty : path[..(index + 1)];
    }

    public static string WithSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var separator = path.StartsWith('/') ? '/' : '\\';
        return path.EndsWith('\\') || path.EndsWith('/') ? path : path + separator;
    }

    public async Task<List<string>> GetCollationsAsync(ManagedServer server, string filter) =>
        await Db.QueryAsync(server.Master,
            "SELECT TOP (40) name FROM sys.fn_helpcollations() WHERE name LIKE @filter ORDER BY CASE WHEN name LIKE 'Arabic%' THEN 0 ELSE 1 END, name",
            r => r.GetString(0), ("@filter", $"%{filter}%"));

    public async Task<string?> GetServerCollationAsync(ManagedServer server) =>
        await Db.ScalarAsync(server.Master, "SELECT CAST(SERVERPROPERTY('Collation') AS nvarchar(128))") as string;

    public async Task<List<string>> GetLoginNamesAsync(ManagedServer server) =>
        await Db.QueryAsync(server.Master,
            "SELECT name FROM sys.server_principals WHERE type IN ('S','U','G','E','X') AND name NOT LIKE '##%' ORDER BY name",
            r => r.GetString(0));

    public async Task<DatabaseProperties> GetDatabasePropertiesAsync(ManagedServer server, string database)
    {
        var rows = await Db.QueryAsync(server.Master, """
            SELECT d.name, d.state_desc, d.user_access_desc, CAST(d.is_read_only AS int), d.recovery_model_desc, d.compatibility_level,
                   d.collation_name, SUSER_SNAME(d.owner_sid), d.create_date, d.page_verify_option_desc,
                   CAST(d.is_auto_close_on AS int), CAST(d.is_auto_shrink_on AS int), CAST(d.is_auto_create_stats_on AS int),
                   CAST(d.is_auto_update_stats_on AS int), d.snapshot_isolation_state_desc, CAST(d.is_read_committed_snapshot_on AS int),
                   CAST((SELECT SUM(CAST(f.size AS bigint)) * 8 / 1024.0 FROM sys.master_files f WHERE f.database_id = d.database_id) AS decimal(18, 2))
            FROM sys.databases d
            WHERE d.name = @db
            """,
            r => new DatabaseProperties
            {
                Name = r.GetString(0),
                State = r.GetString(1),
                UserAccess = r.Str(2),
                IsReadOnly = r.Int(3) == 1,
                RecoveryModel = r.Str(4),
                CompatibilityLevel = r.Int(5),
                Collation = r.NStr(6),
                Owner = r.NStr(7),
                Created = r.Date(8),
                PageVerify = r.Str(9),
                AutoClose = r.Int(10) == 1,
                AutoShrink = r.Int(11) == 1,
                AutoCreateStats = r.Int(12) == 1,
                AutoUpdateStats = r.Int(13) == 1,
                AllowSnapshotIsolation = r.Str(14) is "ON" or "IN_TRANSITION_TO_ON",
                ReadCommittedSnapshot = r.Int(15) == 1,
                SizeMb = r.Dec(16)
            },
            ("@db", database));

        if (rows.Count == 0)
            throw new InvalidOperationException($"Database {database} was not found.");

        var properties = rows[0];
        var backups = await TryAsync(() => Db.QueryAsync(server.ConnectionStringFor("msdb"), """
            SELECT MAX(CASE WHEN type = 'D' THEN backup_finish_date END), MAX(CASE WHEN type = 'L' THEN backup_finish_date END)
            FROM msdb.dbo.backupset WHERE database_name = @db
            """, r => (Full: r.Date(0), Log: r.Date(1)), ("@db", database)));

        var withBackups = new DatabaseProperties
        {
            Name = properties.Name, State = properties.State, UserAccess = properties.UserAccess, IsReadOnly = properties.IsReadOnly,
            RecoveryModel = properties.RecoveryModel, CompatibilityLevel = properties.CompatibilityLevel, Collation = properties.Collation,
            Owner = properties.Owner, Created = properties.Created, PageVerify = properties.PageVerify, AutoClose = properties.AutoClose,
            AutoShrink = properties.AutoShrink, AutoCreateStats = properties.AutoCreateStats, AutoUpdateStats = properties.AutoUpdateStats,
            AllowSnapshotIsolation = properties.AllowSnapshotIsolation, ReadCommittedSnapshot = properties.ReadCommittedSnapshot,
            SizeMb = properties.SizeMb,
            LastFullBackup = backups?.FirstOrDefault().Full,
            LastLogBackup = backups?.FirstOrDefault().Log,
            Users = properties.State == "ONLINE"
                ? Convert.ToInt32(await TryScalarAsync(server.ConnectionStringFor(database),
                    "SELECT COUNT(*) FROM sys.database_principals WHERE type IN ('S','U','G','E','X') AND principal_id > 4") ?? 0)
                : 0
        };

        var files = await Db.QueryAsync(server.Master, """
            SELECT f.file_id, f.name, f.type_desc, f.physical_name, CAST(CAST(f.size AS bigint) * 8 / 1024.0 AS decimal(18, 2)),
                   f.growth, CAST(f.is_percent_growth AS int), CAST(f.max_size AS bigint)
            FROM sys.master_files f WHERE f.database_id = DB_ID(@db) ORDER BY f.file_id
            """,
            r => (Id: r.Int(0), Name: r.GetString(1), Type: r.GetString(2), Path: r.GetString(3), Size: r.Dec(4), Growth: r.Int(5), Percent: r.Int(6) == 1, Max: r.Long(7)),
            ("@db", database));

        var used = properties.State == "ONLINE"
            ? await TryAsync(() => Db.QueryAsync(server.ConnectionStringFor(database), """
                SELECT df.file_id, CAST(CAST(FILEPROPERTY(df.name, 'SpaceUsed') AS bigint) * 8 / 1024.0 AS decimal(18, 2)), fg.name
                FROM sys.database_files df LEFT JOIN sys.filegroups fg ON fg.data_space_id = df.data_space_id
                """, r => (Id: r.Int(0), Used: r.IsDBNull(1) ? (decimal?)null : r.Dec(1), Group: r.NStr(2))))
            : null;

        foreach (var f in files)
        {
            var u = used?.FirstOrDefault(x => x.Id == f.Id);
            withBackups.Files.Add(new DatabaseFile(f.Id, f.Name, f.Type, u?.Group, f.Path, f.Size, u?.Used, f.Growth, f.Percent, f.Max));
        }

        return withBackups;
    }

    public async Task<List<TableSize>> GetTableSizesAsync(ManagedServer server, string database) =>
        await Db.QueryAsync(server.ConnectionStringFor(database), """
            SELECT s.name, t.name,
                   SUM(CASE WHEN ps.index_id IN (0, 1) THEN ps.row_count ELSE 0 END),
                   SUM(ps.reserved_page_count) * 8,
                   SUM(CASE WHEN ps.index_id IN (0, 1) THEN ps.in_row_data_page_count + ps.lob_used_page_count + ps.row_overflow_used_page_count ELSE 0 END) * 8,
                   SUM(ps.used_page_count) * 8
            FROM sys.dm_db_partition_stats ps
            JOIN sys.tables t ON t.object_id = ps.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            GROUP BY s.name, t.name
            ORDER BY SUM(ps.reserved_page_count) DESC, s.name, t.name
            """,
            r =>
            {
                var reserved = r.Long(3);
                var data = r.Long(4);
                var usedKb = r.Long(5);
                return new TableSize(r.GetString(0), r.GetString(1), r.Long(2), reserved, data, Math.Max(usedKb - data, 0), Math.Max(reserved - usedKb, 0));
            });

    public async Task<List<BackupSet>> ReadBackupHeaderAsync(ManagedServer server, string path)
    {
        await using var connection = await Db.OpenAsync(server.Master);
        await using var command = Db.Command(connection, "RESTORE HEADERONLY FROM DISK = @path", ("@path", path));
        command.CommandTimeout = 300;
        await using var r = await command.ExecuteReaderAsync();
        var sets = new List<BackupSet>();
        while (await r.ReadAsync())
        {
            sets.Add(new BackupSet(
                Position: r.Int("Position"),
                Type: r.Int("BackupType"),
                Name: r.NStr("BackupName"),
                DatabaseName: r.NStr("DatabaseName") ?? string.Empty,
                ServerName: r.NStr("ServerName") ?? string.Empty,
                Start: r.Date("BackupStartDate"),
                Finish: r.Date("BackupFinishDate"),
                SizeMb: Math.Round(r.Long("BackupSize") / 1024m / 1024m, 2),
                IsCopyOnly: HasColumn(r, "IsCopyOnly") && r.Int("IsCopyOnly") == 1));
        }

        return sets;
    }

    public async Task<List<BackupFile>> ReadBackupFilesAsync(ManagedServer server, string path, int position)
    {
        await using var connection = await Db.OpenAsync(server.Master);
        await using var command = Db.Command(connection, "RESTORE FILELISTONLY FROM DISK = @path WITH FILE = @position", ("@path", path), ("@position", position));
        command.CommandTimeout = 300;
        await using var r = await command.ExecuteReaderAsync();
        var files = new List<BackupFile>();
        while (await r.ReadAsync())
            files.Add(new BackupFile(r.NStr("LogicalName") ?? "", r.NStr("PhysicalName") ?? "", r.NStr("Type") ?? "D"));
        return files;
    }

    /// <summary>Database name and files stored in a detached .mdf (DBCC CHECKPRIMARYFILE, used by SSMS for Attach).</summary>
    public async Task<(string? Name, List<string> Files)> ReadPrimaryFileAsync(ManagedServer server, string mdfPath)
    {
        await using var connection = await Db.OpenAsync(server.Master);
        string? name = null;
        await using (var command = Db.Command(connection, $"DBCC CHECKPRIMARYFILE ({Sql.Lit(mdfPath)}, 2)"))
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                if (string.Equals(r.Str(0).Trim(), "Database name", StringComparison.OrdinalIgnoreCase))
                    name = r.NStr(1)?.Trim();
        }

        var files = new List<string>();
        await using (var command = Db.Command(connection, $"DBCC CHECKPRIMARYFILE ({Sql.Lit(mdfPath)}, 3)"))
        await using (var r = await command.ExecuteReaderAsync())
        {
            var fileColumn = r.GetOrdinal("filename");
            while (await r.ReadAsync())
                files.Add(r.Str(fileColumn).Trim());
        }

        return (name, files);
    }

    public async Task<List<string>> GetDrivesAsync(ManagedServer server) =>
        await Db.QueryAsync(server.Master, "EXEC master.dbo.xp_fixeddrives", r => r.GetString(0).Trim() + ":\\");

    /// <summary>Folders and files on the SQL Server machine (xp_dirtree; needs sysadmin).</summary>
    public async Task<List<FileEntry>> BrowseAsync(ManagedServer server, string path) =>
        (await Db.QueryAsync(server.Master, "EXEC master.sys.xp_dirtree @path, 1, 1",
            r => new FileEntry(r.GetString(0), r.Int(2) == 1), ("@path", path)))
        .OrderBy(e => e.IsFile).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public async Task<List<SessionActivity>> GetActivityAsync(ManagedServer server)
    {
        var database = server.Major >= 11 ? "DB_NAME(COALESCE(r.database_id, s.database_id))" : "DB_NAME(COALESCE(r.database_id, (SELECT TOP (1) p.dbid FROM sys.sysprocesses p WHERE p.spid = s.session_id)))";
        var openTran = server.Major >= 11 ? "s.open_transaction_count" : "ISNULL(r.open_transaction_count, 0)";
        return await Db.QueryAsync(server.Master, $"""
            SELECT s.session_id, s.login_name, s.host_name, s.program_name, {database},
                   s.status, r.command, r.wait_type, CAST(ISNULL(r.wait_time, 0) AS bigint), ISNULL(r.blocking_session_id, 0),
                   CAST(s.cpu_time AS bigint), CAST(s.reads AS bigint), CAST(s.writes AS bigint), CAST(s.logical_reads AS bigint),
                   CAST(ISNULL(r.total_elapsed_time, 0) AS bigint), CAST(r.percent_complete AS int),
                   s.login_time, s.last_request_start_time, t.text, {openTran}
            FROM sys.dm_exec_sessions s
            LEFT JOIN sys.dm_exec_requests r ON r.session_id = s.session_id
            LEFT JOIN sys.dm_exec_connections c ON c.session_id = s.session_id
            OUTER APPLY sys.dm_exec_sql_text(COALESCE(r.sql_handle, c.most_recent_sql_handle)) t
            WHERE s.is_user_process = 1
            ORDER BY CASE WHEN r.blocking_session_id > 0 THEN 0 WHEN r.session_id IS NOT NULL THEN 1 ELSE 2 END, s.session_id
            """,
            r => new SessionActivity(
                r.Int(0), r.NStr(1), r.NStr(2), r.NStr(3), r.NStr(4), r.NStr(5), r.NStr(6), r.NStr(7), r.Long(8), r.Int(9),
                r.Long(10), r.Long(11), r.Long(12), r.Long(13), r.Long(14), r.IsDBNull(15) ? null : r.Int(15),
                r.Date(16), r.Date(17), r.NStr(18), r.Int(19)));
    }

    public async Task<List<ExpensiveQuery>> GetExpensiveQueriesAsync(ManagedServer server) =>
        await Db.QueryAsync(server.Master, """
            SELECT TOP (30) qs.execution_count, qs.total_worker_time / 1000, qs.total_worker_time / NULLIF(qs.execution_count, 0) / 1000,
                   qs.total_logical_reads, qs.total_elapsed_time / 1000, qs.last_execution_time, DB_NAME(st.dbid),
                   SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
                       ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2) + 1)
            FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
            ORDER BY qs.total_worker_time DESC
            """,
            r => new ExpensiveQuery(r.Long(0), r.Long(1), r.Long(2), r.Long(3), r.Long(4), r.Date(5), r.NStr(6), r.NStr(7)));

    public async Task<List<(string Key, string? Value)>> GetServerPropertiesAsync(ManagedServer server)
    {
        var properties = new List<(string, string?)>();
        var rows = await Db.QueryAsync(server.Master, """
            SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(256)), CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)),
                   CAST(SERVERPROPERTY('InstanceName') AS nvarchar(256)), CAST(SERVERPROPERTY('Edition') AS nvarchar(256)),
                   CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('ProductUpdateLevel') AS nvarchar(128)), CAST(SERVERPROPERTY('Collation') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('IsClustered') AS int), CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int),
                   CAST(SERVERPROPERTY('IsHadrEnabled') AS int), CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)),
                   CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512)), CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512)),
                   @@VERSION, @@LANGUAGE, @@MAX_CONNECTIONS
            """,
            r => new (string, string?)[]
            {
                ("PropMachine", r.NStr(0)), ("PropServerName", r.NStr(1)), ("PropInstance", r.NStr(2) ?? "MSSQLSERVER"),
                ("PropEdition", r.NStr(3)), ("PropVersion", $"{r.NStr(4)} {r.NStr(5)} {r.NStr(6)}".Trim()),
                ("PropCollation", r.NStr(7)), ("PropClustered", r.Int(8) == 1 ? "True" : "False"),
                ("PropAuthentication", r.Int(9) == 1 ? "Windows" : "SQL Server and Windows"),
                ("PropHadr", r.IsDBNull(10) ? null : r.Int(10) == 1 ? "True" : "False"),
                ("PropDataPath", r.NStr(11)), ("PropLogPath", r.NStr(12)), ("PropBackupPath", r.NStr(13)),
                ("PropLanguage", r.NStr(15)), ("PropMaxConnections", r.Str(16)), ("PropFullVersion", r.NStr(14))
            });
        properties.AddRange(rows[0]);

        var memory = server.Major >= 11 ? "physical_memory_kb / 1024" : "physical_memory_in_bytes / 1048576";
        var start = server.Major >= 10 ? "CONVERT(nvarchar(30), sqlserver_start_time, 120)" : "NULL";
        var info = await TryAsync(() => Db.QueryAsync(server.Master,
            $"SELECT cpu_count, CAST({memory} AS bigint), {start} FROM sys.dm_os_sys_info",
            r => new (string, string?)[] { ("PropCpus", r.Str(0)), ("PropMemory", $"{r.Long(1):N0} MB"), ("PropStarted", r.NStr(2)) }));
        if (info is { Count: > 0 })
            properties.InsertRange(5, info[0]);

        return properties;
    }

    public async Task<List<ServerConfiguration>> GetConfigurationsAsync(ManagedServer server) =>
        await Db.QueryAsync(server.Master, """
            SELECT configuration_id, name, CAST(value AS bigint), CAST(value_in_use AS bigint), CAST(minimum AS bigint), CAST(maximum AS bigint),
                   CAST(is_dynamic AS int), CAST(is_advanced AS int), CAST(description AS nvarchar(512))
            FROM sys.configurations ORDER BY name
            """,
            r => new ServerConfiguration(r.Int(0), r.GetString(1), r.Long(2), r.Long(3), r.Long(4), r.Long(5), r.Int(6) == 1, r.Int(7) == 1, r.NStr(8)));

    public async Task<List<string>> GetServerRolesAsync(ManagedServer server) =>
        await Db.QueryAsync(server.Master, "SELECT name FROM sys.server_principals WHERE type = 'R' ORDER BY name", r => r.GetString(0));

    public async Task<LoginDetails?> GetLoginAsync(ManagedServer server, string name)
    {
        var rows = await Db.QueryAsync(server.Master, """
            SELECT p.name, p.type, CAST(p.is_disabled AS int), p.default_database_name,
                   CAST(ISNULL(l.is_policy_checked, 0) AS int), CAST(ISNULL(l.is_expiration_checked, 0) AS int)
            FROM sys.server_principals p LEFT JOIN sys.sql_logins l ON l.principal_id = p.principal_id
            WHERE p.name = @name
            """,
            r => (Name: r.GetString(0), Type: r.Str(1).Trim(), Disabled: r.Int(2) == 1, Db: r.NStr(3), Policy: r.Int(4) == 1, Expiration: r.Int(5) == 1),
            ("@name", name));
        if (rows.Count == 0)
            return null;

        var roles = await Db.QueryAsync(server.Master, """
            SELECT r.name FROM sys.server_role_members m
            JOIN sys.server_principals r ON r.principal_id = m.role_principal_id
            JOIN sys.server_principals u ON u.principal_id = m.member_principal_id
            WHERE u.name = @name
            """, r => r.GetString(0), ("@name", name));

        var row = rows[0];
        return new LoginDetails(row.Name, row.Type, row.Disabled, row.Db, row.Policy, row.Expiration, roles);
    }

    /// <summary>The user each online database has for the login (by SID), with its fixed-role memberships.</summary>
    public async Task<List<LoginMapping>> GetLoginMappingsAsync(ManagedServer server, string? login)
    {
        var databases = await Db.QueryAsync(server.Master,
            "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 AND name NOT IN ('tempdb') ORDER BY CASE WHEN database_id <= 4 THEN 1 ELSE 0 END, name",
            r => r.GetString(0));

        var mappings = new List<LoginMapping>();
        foreach (var database in databases)
        {
            var mapping = new LoginMapping { Database = database, User = login ?? string.Empty };
            if (login is not null)
            {
                try
                {
                    var db = Sql.Q(database);
                    var users = await Db.QueryAsync(server.Master, $"""
                        SELECT dp.name, dp.default_schema_name, r.name
                        FROM {db}.sys.database_principals dp
                        LEFT JOIN {db}.sys.database_role_members m ON m.member_principal_id = dp.principal_id
                        LEFT JOIN {db}.sys.database_principals r ON r.principal_id = m.role_principal_id
                        WHERE dp.sid = SUSER_SID(@login) AND dp.type IN ('S','U','G')
                        """,
                        r => (User: r.GetString(0), Schema: r.NStr(1), Role: r.NStr(2)),
                        ("@login", login));

                    if (users.Count > 0)
                    {
                        mapping = new LoginMapping
                        {
                            Database = database,
                            OriginalUser = users[0].User,
                            Mapped = true,
                            User = users[0].User,
                            DefaultSchema = users[0].Schema ?? "dbo"
                        };
                        foreach (var role in users.Where(u => u.Role is not null).Select(u => u.Role!))
                        {
                            mapping.OriginalRoles.Add(role);
                            mapping.Roles.Add(role);
                        }
                    }
                }
                catch (SqlException)
                {
                    continue;
                }
            }

            mappings.Add(mapping);
        }

        return mappings;
    }

    public async Task<List<(string Name, bool IsFixed)>> GetDatabaseRolesAsync(ManagedServer server, string database) =>
        await Db.QueryAsync(server.ConnectionStringFor(database),
            "SELECT name, CAST(is_fixed_role AS int) FROM sys.database_principals WHERE type = 'R' AND name <> 'public' ORDER BY is_fixed_role DESC, name",
            r => (r.GetString(0), r.Int(1) == 1));

    public async Task<List<string>> GetSchemasAsync(ManagedServer server, string database) =>
        await Db.QueryAsync(server.ConnectionStringFor(database), "SELECT name FROM sys.schemas ORDER BY name", r => r.GetString(0));

    /// <summary>Users and roles that can own a schema or be a role member.</summary>
    public async Task<List<(string Name, string Type)>> GetPrincipalsAsync(ManagedServer server, string database) =>
        await Db.QueryAsync(server.ConnectionStringFor(database),
            "SELECT name, type FROM sys.database_principals WHERE type IN ('S','U','G','R','E','X','C','K') AND name NOT IN ('public','sys','INFORMATION_SCHEMA') AND is_fixed_role = 0 ORDER BY type, name",
            r => (r.GetString(0), r.Str(1).Trim()));

    public async Task<UserDetails?> GetUserAsync(ManagedServer server, string database, string name)
    {
        var cs = server.ConnectionStringFor(database);
        var rows = await Db.QueryAsync(cs, """
            SELECT dp.name, dp.type, sp.name, dp.default_schema_name
            FROM sys.database_principals dp LEFT JOIN sys.server_principals sp ON sp.sid = dp.sid
            WHERE dp.name = @name
            """,
            r => (Name: r.GetString(0), Type: r.Str(1).Trim(), Login: r.NStr(2), Schema: r.NStr(3)),
            ("@name", name));
        if (rows.Count == 0)
            return null;

        var roles = await GetMembershipsAsync(cs, name);
        return new UserDetails(rows[0].Name, rows[0].Type, rows[0].Login, rows[0].Schema, roles);
    }

    private static Task<List<string>> GetMembershipsAsync(string cs, string member) =>
        Db.QueryAsync(cs, """
            SELECT r.name FROM sys.database_role_members m
            JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
            JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
            WHERE u.name = @name
            """, r => r.GetString(0), ("@name", member));

    public async Task<List<string>> GetRoleMembersAsync(ManagedServer server, string database, string role) =>
        await Db.QueryAsync(server.ConnectionStringFor(database), """
            SELECT u.name FROM sys.database_role_members m
            JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
            JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
            WHERE r.name = @name ORDER BY u.name
            """, r => r.GetString(0), ("@name", role));

    public async Task<string?> GetRoleOwnerAsync(ManagedServer server, string database, string role) =>
        await Db.ScalarAsync(server.ConnectionStringFor(database), """
            SELECT o.name FROM sys.database_principals r JOIN sys.database_principals o ON o.principal_id = r.owning_principal_id WHERE r.name = @name
            """, ("@name", role)) as string;

    public static string AddRoleMember(ManagedServer server, string role, string member) => server.SupportsAlterRole
        ? $"ALTER ROLE {Sql.Q(role)} ADD MEMBER {Sql.Q(member)};"
        : $"EXEC sp_addrolemember {Sql.Lit(role)}, {Sql.Lit(member)};";

    public static string DropRoleMember(ManagedServer server, string role, string member) => server.SupportsAlterRole
        ? $"ALTER ROLE {Sql.Q(role)} DROP MEMBER {Sql.Q(member)};"
        : $"EXEC sp_droprolemember {Sql.Lit(role)}, {Sql.Lit(member)};";

    public static string AddServerRoleMember(ManagedServer server, string role, string login) => server.SupportsAlterRole
        ? $"ALTER SERVER ROLE {Sql.Q(role)} ADD MEMBER {Sql.Q(login)};"
        : $"EXEC sp_addsrvrolemember {Sql.Lit(login)}, {Sql.Lit(role)};";

    public static string DropServerRoleMember(ManagedServer server, string role, string login) => server.SupportsAlterRole
        ? $"ALTER SERVER ROLE {Sql.Q(role)} DROP MEMBER {Sql.Q(login)};"
        : $"EXEC sp_dropsrvrolemember {Sql.Lit(login)}, {Sql.Lit(role)};";

    private static async Task<T?> TryAsync<T>(Func<Task<T>> action) where T : class
    {
        try
        {
            return await action();
        }
        catch (SqlException)
        {
            return null;
        }
    }

    private static async Task<object?> TryScalarAsync(string cs, string sql)
    {
        try
        {
            return await Db.ScalarAsync(cs, sql);
        }
        catch (SqlException)
        {
            return null;
        }
    }

    private static bool HasColumn(SqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
