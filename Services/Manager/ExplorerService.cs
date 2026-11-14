using Microsoft.Data.SqlClient;
using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>Loads Object Explorer children from the catalog views (SQL Server 2005 and later).</summary>
public sealed class ExplorerService
{
    public static ExplorerNode ServerNode(ManagedServer server) => new()
    {
        Kind = NodeKind.Server,
        Server = server,
        Label = server.DisplayName,
        Detail = $"{server.Info.FriendlyVersion} - {server.Login}",
        CanExpand = true
    };

    public async Task<List<ExplorerNode>> LoadChildrenAsync(ExplorerNode node) => node.Kind switch
    {
        NodeKind.Server => ServerFolders(node),
        NodeKind.DatabasesFolder => await DatabasesAsync(node, system: false),
        NodeKind.SystemDatabasesFolder => await DatabasesAsync(node, system: true),
        NodeKind.Database => DatabaseFolders(node),
        NodeKind.TablesFolder => await TablesAsync(node),
        NodeKind.Table => ObjectFolders(node, NodeKind.ColumnsFolder, NodeKind.KeysFolder, NodeKind.ConstraintsFolder, NodeKind.TriggersFolder, NodeKind.IndexesFolder),
        NodeKind.View => ObjectFolders(node, NodeKind.ColumnsFolder, NodeKind.TriggersFolder, NodeKind.IndexesFolder),
        NodeKind.ColumnsFolder => await ColumnsAsync(node),
        NodeKind.KeysFolder => await KeysAsync(node),
        NodeKind.ConstraintsFolder => await ConstraintsAsync(node),
        NodeKind.TriggersFolder => await TriggersAsync(node),
        NodeKind.IndexesFolder => await IndexesAsync(node),
        NodeKind.ViewsFolder => await SchemaObjectsAsync(node, NodeKind.View,
            "SELECT s.name, v.name, NULL FROM sys.views v JOIN sys.schemas s ON s.schema_id = v.schema_id WHERE v.is_ms_shipped = 0 ORDER BY 1, 2"),
        NodeKind.SynonymsFolder => await SchemaObjectsAsync(node, NodeKind.Synonym,
            "SELECT s.name, y.name, y.base_object_name FROM sys.synonyms y JOIN sys.schemas s ON s.schema_id = y.schema_id ORDER BY 1, 2"),
        NodeKind.ProgrammabilityFolder => ProgrammabilityFolders(node),
        NodeKind.ProceduresFolder => await SchemaObjectsAsync(node, NodeKind.Procedure,
            "SELECT s.name, p.name, NULL FROM sys.procedures p JOIN sys.schemas s ON s.schema_id = p.schema_id WHERE p.is_ms_shipped = 0 ORDER BY 1, 2"),
        NodeKind.FunctionsFolder => await SchemaObjectsAsync(node, NodeKind.Function,
            "SELECT s.name, o.name, o.type FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE o.type IN ('FN','IF','TF','FS','FT','AF') AND o.is_ms_shipped = 0 ORDER BY 1, 2"),
        NodeKind.Procedure or NodeKind.Function => await ParametersAsync(node),
        NodeKind.SequencesFolder => await SchemaObjectsAsync(node, NodeKind.Sequence,
            "SELECT s.name, q.name, TYPE_NAME(q.user_type_id) FROM sys.sequences q JOIN sys.schemas s ON s.schema_id = q.schema_id ORDER BY 1, 2"),
        NodeKind.TypesFolder => await TypesAsync(node),
        NodeKind.DbSecurityFolder => Folders(node, NodeKind.UsersFolder, NodeKind.RolesFolder, NodeKind.SchemasFolder),
        NodeKind.UsersFolder => await PrincipalsAsync(node, NodeKind.User,
            "SELECT name, type_desc, type, default_schema_name FROM sys.database_principals WHERE type IN ('S','U','G','E','X','C','K') ORDER BY name"),
        NodeKind.RolesFolder => await PrincipalsAsync(node, NodeKind.Role,
            "SELECT name, type_desc, type, CAST(is_fixed_role AS int) FROM sys.database_principals WHERE type IN ('R','A') ORDER BY is_fixed_role DESC, name"),
        NodeKind.SchemasFolder => await PrincipalsAsync(node, NodeKind.Schema,
            "SELECT s.name, p.name, NULL, NULL FROM sys.schemas s LEFT JOIN sys.database_principals p ON p.principal_id = s.principal_id ORDER BY s.name"),
        NodeKind.ServerSecurityFolder => Folders(node, NodeKind.LoginsFolder, NodeKind.ServerRolesFolder),
        NodeKind.LoginsFolder => await PrincipalsAsync(node, NodeKind.Login,
            "SELECT name, type_desc, type, CAST(is_disabled AS int) FROM sys.server_principals WHERE type IN ('S','U','G','C','K','E','X') ORDER BY name", server: true),
        NodeKind.ServerRolesFolder => await PrincipalsAsync(node, NodeKind.ServerRole,
            "SELECT name, type_desc, type, NULL FROM sys.server_principals WHERE type = 'R' ORDER BY name", server: true),
        NodeKind.AgentFolder => Folders(node, NodeKind.JobsFolder),
        NodeKind.JobsFolder => await JobsAsync(node),
        _ => []
    };

    public async Task<List<string>> GetDatabaseNamesAsync(ManagedServer server) =>
        await Db.QueryAsync(server.Master,
            "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name",
            r => r.GetString(0));

    public async Task<List<(string Schema, string Name)>> GetTableNamesAsync(ManagedServer server, string database) =>
        await Db.QueryAsync(server.ConnectionStringFor(database),
            "SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0 ORDER BY 1, 2",
            r => (r.GetString(0), r.GetString(1)));

    private static ExplorerNode Folder(ExplorerNode parent, NodeKind kind) => new()
    {
        Kind = kind,
        Server = parent.Server,
        Parent = parent,
        LabelKey = $"Folder{kind.ToString().Replace("Folder", "")}",
        Database = parent.Database,
        ParentSchema = parent.Kind is NodeKind.Table or NodeKind.View ? parent.Schema : parent.ParentSchema,
        ParentName = parent.Kind is NodeKind.Table or NodeKind.View ? parent.Name : parent.ParentName,
        ParentKind = parent.Kind is NodeKind.Table or NodeKind.View ? parent.Kind : parent.ParentKind,
        CanExpand = true
    };

    private static List<ExplorerNode> Folders(ExplorerNode parent, params NodeKind[] kinds) =>
        kinds.Select(k => Folder(parent, k)).ToList();

    private static List<ExplorerNode> ServerFolders(ExplorerNode node)
    {
        var folders = new List<NodeKind> { NodeKind.DatabasesFolder, NodeKind.ServerSecurityFolder };
        if (node.Server.HasAgent)
            folders.Add(NodeKind.AgentFolder);
        return Folders(node, [.. folders]);
    }

    private static List<ExplorerNode> DatabaseFolders(ExplorerNode node) =>
        Folders(node, NodeKind.TablesFolder, NodeKind.ViewsFolder, NodeKind.SynonymsFolder, NodeKind.ProgrammabilityFolder, NodeKind.DbSecurityFolder);

    private static List<ExplorerNode> ProgrammabilityFolders(ExplorerNode node)
    {
        var folders = new List<NodeKind> { NodeKind.ProceduresFolder, NodeKind.FunctionsFolder };
        if (node.Server.Major >= 11)
            folders.Add(NodeKind.SequencesFolder);
        folders.Add(NodeKind.TypesFolder);
        return Folders(node, [.. folders]);
    }

    private static List<ExplorerNode> ObjectFolders(ExplorerNode node, params NodeKind[] kinds) => Folders(node, kinds);

    private static ExplorerNode Child(ExplorerNode parent, NodeKind kind, string? schema, string name, string? detail = null,
        string? subType = null, bool canExpand = false, bool disabled = false, bool system = false, long? rows = null, string? state = null) => new()
    {
        Kind = kind,
        Server = parent.Server,
        Parent = parent,
        Label = schema is null ? name : $"{schema}.{name}",
        Database = kind == NodeKind.Database ? name : parent.Database,
        Schema = schema,
        Name = name,
        Detail = detail,
        SubType = subType,
        ParentSchema = parent.ParentSchema,
        ParentName = parent.ParentName,
        ParentKind = parent.ParentKind,
        CanExpand = canExpand,
        IsDisabled = disabled,
        IsSystem = system,
        RowCount = rows,
        State = state
    };

    private async Task<List<ExplorerNode>> DatabasesAsync(ExplorerNode node, bool system)
    {
        var rows = await Db.QueryAsync(node.Server.Master, $"""
            SELECT d.name, d.state_desc, d.user_access_desc, CAST(d.is_read_only AS int), CAST(ISNULL(HAS_DBACCESS(d.name), 0) AS int)
            FROM sys.databases d
            WHERE {(system ? "d.database_id <= 4" : "d.database_id > 4")}
            ORDER BY d.name
            """,
            r => (Name: r.GetString(0), State: r.GetString(1), Access: r.Str(2), ReadOnly: r.Int(3) == 1, HasAccess: r.Int(4) == 1));

        var nodes = new List<ExplorerNode>();
        if (!system)
            nodes.Add(Folder(node, NodeKind.SystemDatabasesFolder));

        foreach (var db in rows)
        {
            var notes = new List<string>();
            if (db.State != "ONLINE") notes.Add(db.State);
            if (db.Access != "MULTI_USER") notes.Add(db.Access);
            if (db.ReadOnly) notes.Add("READ_ONLY");
            nodes.Add(Child(node, NodeKind.Database, null, db.Name,
                detail: notes.Count > 0 ? string.Join(", ", notes) : null,
                canExpand: db.State == "ONLINE" && db.HasAccess,
                system: system,
                state: db.State));
        }

        return nodes;
    }

    private async Task<List<ExplorerNode>> TablesAsync(ExplorerNode node) =>
        (await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), """
            SELECT s.name, t.name,
                   CAST(ISNULL((SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)), 0) AS bigint)
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name
            """,
            r => (Schema: r.GetString(0), Name: r.GetString(1), Rows: r.Long(2))))
        .Select(t => Child(node, NodeKind.Table, t.Schema, t.Name, canExpand: true, rows: t.Rows))
        .ToList();

    private async Task<List<ExplorerNode>> SchemaObjectsAsync(ExplorerNode node, NodeKind kind, string sql) =>
        (await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), sql,
            r => (Schema: r.GetString(0), Name: r.GetString(1), Extra: r.NStr(2))))
        .Select(o => kind switch
        {
            NodeKind.Function => Child(node, kind, o.Schema, o.Name, subType: o.Extra?.Trim(), canExpand: true),
            NodeKind.Procedure => Child(node, kind, o.Schema, o.Name, canExpand: true),
            NodeKind.View => Child(node, kind, o.Schema, o.Name, canExpand: true),
            _ => Child(node, kind, o.Schema, o.Name, detail: o.Extra)
        })
        .ToList();

    private async Task<List<ExplorerNode>> ColumnsAsync(ExplorerNode node)
    {
        var rows = await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), """
            SELECT c.name, tp.name, CAST(CASE WHEN tp.user_type_id <> tp.system_type_id THEN 1 ELSE 0 END AS int),
                   c.max_length, c.precision, c.scale, CAST(c.is_nullable AS int), CAST(c.is_identity AS int), CAST(c.is_computed AS int),
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                                          WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id) THEN 1 ELSE 0 END AS int),
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_key_columns f WHERE f.parent_object_id = c.object_id AND f.parent_column_id = c.column_id) THEN 1 ELSE 0 END AS int)
            FROM sys.columns c
            JOIN sys.types tp ON tp.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@name)
            ORDER BY c.column_id
            """,
            r =>
            {
                var parts = new List<string>();
                if (r.Int(9) == 1) parts.Add("PK");
                if (r.Int(10) == 1) parts.Add("FK");
                if (r.Int(8) == 1) parts.Add("Computed");
                if (r.Int(7) == 1) parts.Add("Identity");
                parts.Add(ScriptBuilder.FormatType(r.GetString(1), r.Int(2) == 1, null, r.Int(3), r.Int(4), r.Int(5)));
                parts.Add(r.Int(6) == 1 ? "null" : "not null");
                return (Name: r.GetString(0), Detail: string.Join(", ", parts), IsPk: r.Int(9) == 1);
            },
            ("@name", node.ParentQualifiedName));

        return rows.Select(c => Child(node, NodeKind.Column, null, c.Name, detail: c.Detail, subType: c.IsPk ? "PK" : null)).ToList();
    }

    private async Task<List<ExplorerNode>> KeysAsync(ExplorerNode node) =>
        (await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), """
            SELECT k.name, k.type, 0, CASE k.type WHEN 'PK' THEN 0 ELSE 1 END FROM sys.key_constraints k WHERE k.parent_object_id = OBJECT_ID(@name)
            UNION ALL
            SELECT f.name, 'F', CAST(f.is_disabled AS int), 2 FROM sys.foreign_keys f WHERE f.parent_object_id = OBJECT_ID(@name)
            ORDER BY 4, 1
            """,
            r => (Name: r.GetString(0), Type: r.GetString(1).Trim(), Disabled: r.Int(2) == 1),
            ("@name", node.ParentQualifiedName)))
        .Select(k => Child(node, NodeKind.Key, null, k.Name, detail: k.Type switch { "PK" => "Primary key", "UQ" => "Unique", _ => "Foreign key" },
            subType: k.Type, disabled: k.Disabled))
        .ToList();

    private async Task<List<ExplorerNode>> ConstraintsAsync(ExplorerNode node) =>
        (await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), """
            SELECT name, 'C', CAST(is_disabled AS int), definition FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(@name)
            UNION ALL
            SELECT name, 'D', 0, definition FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(@name)
            ORDER BY 2, 1
            """,
            r => (Name: r.GetString(0), Type: r.GetString(1), Disabled: r.Int(2) == 1, Definition: r.NStr(3)),
            ("@name", node.ParentQualifiedName)))
        .Select(c => Child(node, NodeKind.Constraint, null, c.Name, detail: (c.Type == "C" ? "Check " : "Default ") + c.Definition,
            subType: c.Type, disabled: c.Disabled))
        .ToList();

    private async Task<List<ExplorerNode>> TriggersAsync(ExplorerNode node) =>
        (await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database),
            "SELECT name, CAST(is_disabled AS int), CAST(is_instead_of_trigger AS int) FROM sys.triggers WHERE parent_id = OBJECT_ID(@name) ORDER BY name",
            r => (Name: r.GetString(0), Disabled: r.Int(1) == 1, InsteadOf: r.Int(2) == 1),
            ("@name", node.ParentQualifiedName)))
        .Select(t => Child(node, NodeKind.Trigger, null, t.Name, detail: t.InsteadOf ? "Instead of" : "After", disabled: t.Disabled))
        .ToList();

    private async Task<List<ExplorerNode>> IndexesAsync(ExplorerNode node) =>
        (await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), """
            SELECT name, type_desc, CAST(is_unique AS int), CAST(is_primary_key AS int), CAST(is_unique_constraint AS int), CAST(is_disabled AS int)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(@name) AND type > 0 AND name IS NOT NULL
            ORDER BY name
            """,
            r =>
            {
                var type = r.GetString(1).Replace('_', ' ').ToLowerInvariant();
                var detail = $"{(r.Int(2) == 1 ? "Unique" : "Non-Unique")}, {char.ToUpperInvariant(type[0])}{type[1..]}";
                if (r.Int(5) == 1) detail += ", Disabled";
                return (Name: r.GetString(0), Detail: detail, Sub: r.Int(3) == 1 ? "PK" : r.Int(4) == 1 ? "UQ" : "IX", Disabled: r.Int(5) == 1);
            },
            ("@name", node.ParentQualifiedName)))
        .Select(i => Child(node, NodeKind.Index, null, i.Name, detail: i.Detail, subType: i.Sub, disabled: i.Disabled))
        .ToList();

    private async Task<List<ExplorerNode>> ParametersAsync(ExplorerNode node)
    {
        var rows = await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), """
            SELECT p.parameter_id, p.name, tp.name, CAST(CASE WHEN tp.user_type_id <> tp.system_type_id THEN 1 ELSE 0 END AS int),
                   p.max_length, p.precision, p.scale, CAST(p.is_output AS int), CAST(p.has_default_value AS int)
            FROM sys.parameters p
            JOIN sys.types tp ON tp.user_type_id = p.user_type_id
            WHERE p.object_id = OBJECT_ID(@name)
            ORDER BY p.parameter_id
            """,
            r => (Id: r.Int(0), Name: r.Str(1), Type: ScriptBuilder.FormatType(r.GetString(2), r.Int(3) == 1, null, r.Int(4), r.Int(5), r.Int(6)),
                  Output: r.Int(7) == 1, HasDefault: r.Int(8) == 1),
            ("@name", node.QualifiedName));

        return rows.Select(p => new ExplorerNode
        {
            Kind = NodeKind.Parameter,
            Server = node.Server,
            Parent = node,
            Database = node.Database,
            Name = p.Id == 0 ? "Returns" : p.Name,
            Label = p.Id == 0 ? "Returns" : p.Name,
            Detail = p.Id == 0 ? p.Type : $"{p.Type}, {(p.Output ? "Input/Output" : "Input")}{(p.HasDefault ? ", Default" : "")}",
            ParentSchema = node.Schema,
            ParentName = node.Name,
            ParentKind = node.Kind
        }).ToList();
    }

    private async Task<List<ExplorerNode>> TypesAsync(ExplorerNode node)
    {
        var tableType = node.Server.Major >= 10 ? "CAST(t.is_table_type AS int)" : "0";
        return (await Db.QueryAsync(node.Server.ConnectionStringFor(node.Database), $"""
                SELECT s.name, t.name, {tableType}, CAST(t.is_assembly_type AS int), TYPE_NAME(t.system_type_id), t.max_length, t.precision, t.scale
                FROM sys.types t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_user_defined = 1
                ORDER BY 1, 2
                """,
                r => (Schema: r.GetString(0), Name: r.GetString(1), IsTable: r.Int(2) == 1, IsClr: r.Int(3) == 1,
                      Base: r.NStr(4), MaxLength: r.Int(5), Precision: r.Int(6), Scale: r.Int(7))))
            .Select(t => Child(node, NodeKind.UserType, t.Schema, t.Name,
                detail: t.IsTable ? "Table type" : t.IsClr ? "CLR type" : ScriptBuilder.FormatType(t.Base ?? "", false, null, t.MaxLength, t.Precision, t.Scale),
                subType: t.IsTable ? "TT" : t.IsClr ? "CLR" : "ALIAS"))
            .ToList();
    }

    private async Task<List<ExplorerNode>> PrincipalsAsync(ExplorerNode node, NodeKind kind, string sql, bool server = false)
    {
        var cs = server ? node.Server.Master : node.Server.ConnectionStringFor(node.Database);
        return (await Db.QueryAsync(cs, sql, r => (Name: r.GetString(0), Detail: r.NStr(1), Type: r.NStr(2)?.Trim(), Extra: r.NStr(3))))
            .Select(p => kind switch
            {
                NodeKind.Login => Child(node, kind, null, p.Name, detail: Pretty(p.Detail), subType: p.Type, disabled: p.Extra == "1",
                    system: p.Name.StartsWith("##", StringComparison.Ordinal) || p.Name.StartsWith("NT ", StringComparison.OrdinalIgnoreCase)),
                NodeKind.Role => Child(node, kind, null, p.Name, detail: p.Type == "A" ? "Application role" : null, subType: p.Type, system: p.Extra == "1" || p.Name == "public"),
                NodeKind.Schema => Child(node, kind, null, p.Name, detail: p.Detail is null ? null : $"Owner: {p.Detail}"),
                NodeKind.User => Child(node, kind, null, p.Name, detail: Pretty(p.Detail), subType: p.Type,
                    system: p.Name is "dbo" or "guest" or "INFORMATION_SCHEMA" or "sys"),
                _ => Child(node, kind, null, p.Name, subType: p.Type)
            })
            .ToList();
    }

    private static string? Pretty(string? typeDesc) => typeDesc?.Replace('_', ' ').ToLowerInvariant();

    private async Task<List<ExplorerNode>> JobsAsync(ExplorerNode node) =>
        (await Db.QueryAsync(node.Server.ConnectionStringFor("msdb"), """
            SELECT j.name, CAST(j.enabled AS int), c.name
            FROM msdb.dbo.sysjobs j
            LEFT JOIN msdb.dbo.syscategories c ON c.category_id = j.category_id
            ORDER BY j.name
            """,
            r => (Name: r.GetString(0), Enabled: r.Int(1) == 1, Category: r.NStr(2))))
        .Select(j => Child(node, NodeKind.Job, null, j.Name, detail: j.Category, disabled: !j.Enabled))
        .ToList();
}
