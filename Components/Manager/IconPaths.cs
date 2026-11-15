using MigrateSQLServerDatabases.Models.Manager;

namespace MigrateSQLServerDatabases.Components.Manager;

/// <summary>16x16 stroke icons (SVG path data).</summary>
public static class IconPaths
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["server"] = """<rect x="2.5" y="2" width="11" height="5" rx="1"/><rect x="2.5" y="9" width="11" height="5" rx="1"/><path d="M5 4.5h.01M5 11.5h.01"/>""",
        ["folder"] = """<path d="M1.75 4.25a1 1 0 0 1 1-1h3.5l1.5 1.5h5.5a1 1 0 0 1 1 1v6.75a1 1 0 0 1-1 1h-10.5a1 1 0 0 1-1-1z"/>""",
        ["database"] = """<ellipse cx="8" cy="3.75" rx="5.25" ry="2"/><path d="M2.75 3.75v8.5c0 1.1 2.35 2 5.25 2s5.25-.9 5.25-2v-8.5M2.75 8c0 1.1 2.35 2 5.25 2s5.25-.9 5.25-2"/>""",
        ["table"] = """<rect x="1.75" y="2.75" width="12.5" height="10.5" rx="1"/><path d="M1.75 6.25h12.5M1.75 9.75h12.5M6 6.25v7"/>""",
        ["view"] = """<rect x="1.75" y="2.75" width="12.5" height="10.5" rx="1"/><path d="M4 8.5c1.3-2.2 6.7-2.2 8 0-1.3 2.2-6.7 2.2-8 0z"/><circle cx="8" cy="8.5" r=".9"/>""",
        ["column"] = """<rect x="5.25" y="1.75" width="5.5" height="12.5" rx="1"/><path d="M5.25 5.5h5.5M5.25 9h5.5"/>""",
        ["key"] = """<circle cx="5" cy="8" r="2.75"/><path d="M7.75 8h6.5M12 8v2.5M14.25 8v2"/>""",
        ["index"] = """<path d="M2 3.5h7M2 7h5M2 10.5h3M12 2.5v10.5M10 11l2 2 2-2"/>""",
        ["constraint"] = """<path d="M8 1.75l5.25 2v4c0 3.3-2.3 5.7-5.25 6.5-2.95-.8-5.25-3.2-5.25-6.5v-4z"/><path d="M5.5 8l1.75 1.75 3.25-3.5"/>""",
        ["trigger"] = """<path d="M9.25 1.5L3.5 9h4.25l-1 5.5 5.75-7.5H8.25z"/>""",
        ["procedure"] = """<path d="M5.5 2.5c-1.5 0-2 .7-2 2v1.5c0 1-.5 2-1.5 2 1 0 1.5 1 1.5 2v1.5c0 1.3.5 2 2 2M10.5 2.5c1.5 0 2 .7 2 2v1.5c0 1 .5 2 1.5 2-1 0-1.5 1-1.5 2v1.5c0 1.3-.5 2-2 2"/>""",
        ["function"] = """<path d="M10.5 2.25c-2 0-2.6 1-3 3l-1.4 6.5c-.4 1.7-1 2.25-2.6 2.25M4.5 6.25h5.5"/><path d="M10 9.5l3 3.5M13 9.5l-3 3.5"/>""",
        ["parameter"] = """<circle cx="8" cy="8" r="2.25"/><path d="M10.25 8v1c0 1.4 2.75 1.4 2.75-1a5 5 0 1 0-2 4"/>""",
        ["synonym"] = """<path d="M6.5 9.5l3-3M7 4.5l1-1a2.5 2.5 0 0 1 3.5 3.5l-1 1M9 11.5l-1 1A2.5 2.5 0 0 1 4.5 9l1-1"/>""",
        ["sequence"] = """<path d="M3 4h10M3 8h10M3 12h10"/><path d="M1.5 4h.01M1.5 8h.01M1.5 12h.01"/>""",
        ["type"] = """<path d="M3 3.25h10M8 3.25v9.5M6 12.75h4"/>""",
        ["user"] = """<circle cx="8" cy="5" r="2.75"/><path d="M2.5 14c.5-3 2.7-4.5 5.5-4.5s5 1.5 5.5 4.5"/>""",
        ["role"] = """<circle cx="6" cy="5.5" r="2.25"/><circle cx="11.25" cy="6" r="1.75"/><path d="M1.5 13.5c.4-2.5 2.2-3.75 4.5-3.75s4.1 1.25 4.5 3.75M10.75 9.75c2 0 3.4 1 3.75 3"/>""",
        ["schema"] = """<rect x="1.75" y="1.75" width="5" height="5" rx="1"/><rect x="9.25" y="9.25" width="5" height="5" rx="1"/><path d="M4.25 6.75v5H9.25"/>""",
        ["login"] = """<circle cx="6.5" cy="5" r="2.5"/><path d="M1.5 13.5c.5-2.7 2.4-4 5-4 .9 0 1.7.2 2.4.5"/><circle cx="12" cy="9.75" r="1.5"/><path d="M12 11.25v3M12 13h1.5"/>""",
        ["lock"] = """<rect x="3" y="7" width="10" height="7" rx="1"/><path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2"/>""",
        ["job"] = """<circle cx="8" cy="8" r="6"/><path d="M8 4.5V8l2.5 1.5"/>""",
        ["query"] = """<path d="M3.75 1.5h5.75l2.75 3v10H3.75z"/><path d="M9.5 1.5v3h2.75M6 8h4.5M6 10.75h4.5"/>""",
        ["activity"] = """<path d="M1.5 8.5h3l2-5 3 9 2-4h3"/>""",
        ["edit"] = """<path d="M11 2.25l2.75 2.75-8 8H3v-2.75z"/><path d="M9.5 3.75l2.75 2.75"/>""",
        ["play"] = """<path d="M4.5 2.75v10.5l8.5-5.25z" fill="currentColor"/>""",
        ["stop"] = """<rect x="3.75" y="3.75" width="8.5" height="8.5" rx="1" fill="currentColor"/>""",
        ["check"] = """<path d="M3 8.5l3 3 7-7"/>""",
        ["plus"] = """<path d="M8 3v10M3 8h10"/>""",
        ["refresh"] = """<path d="M13.25 8a5.25 5.25 0 1 1-1.6-3.75M13.5 2.5v3h-3"/>""",
        ["plug"] = """<path d="M6 1.75v3.5M10 1.75v3.5M4 5.25h8v2.5a4 4 0 0 1-8 0zM8 11.75v2.5"/>""",
        ["unplug"] = """<path d="M6 1.75v3.5M10 1.75v3.5M4 5.25h8v2.5a4 4 0 0 1-8 0zM8 11.75v2.5M2 14L14 2"/>""",
        ["open"] = """<path d="M1.75 4.25a1 1 0 0 1 1-1h3.5l1.5 1.5h4.5a1 1 0 0 1 1 1v1.5M1.75 4.25v8.5h10.5l2-5.75H4z"/>""",
        ["save"] = """<path d="M2.5 2.5h9l2 2v9h-11z"/><path d="M5 2.5v3.5h5V2.5M5 13.5v-4h6v4"/>""",
        ["more"] = """<path d="M3.5 8h.01M8 8h.01M12.5 8h.01" stroke-width="2.5"/>""",
        ["chevron"] = """<path d="M6 3.5l4.5 4.5L6 12.5"/>""",
        ["close"] = """<path d="M4 4l8 8M12 4l-8 8"/>""",
        ["copy"] = """<rect x="5.25" y="5.25" width="8.5" height="8.5" rx="1"/><path d="M10.75 5.25v-2a1 1 0 0 0-1-1h-6.5a1 1 0 0 0-1 1v6.5a1 1 0 0 0 1 1h2"/>""",
        ["download"] = """<path d="M8 2v8.5M4.5 7l3.5 3.5L11.5 7M2.5 13.5h11"/>""",
        ["agent"] = """<rect x="3" y="5" width="10" height="8" rx="2"/><path d="M8 2.5V5M6 9h.01M10 9h.01"/>""",
        ["file"] = """<path d="M3.75 1.5h5.75l2.75 3v10H3.75z"/><path d="M9.5 1.5v3h2.75"/>""",
        ["drive"] = """<rect x="1.75" y="5" width="12.5" height="6" rx="1"/><path d="M11 8h.01"/>""",
        ["up"] = """<path d="M8 13V3M3.5 7.5L8 3l4.5 4.5"/>""",
        ["backup"] = """<ellipse cx="7" cy="3.75" rx="4.75" ry="1.75"/><path d="M2.25 3.75v7.5c0 1 2.1 1.75 4.75 1.75M11.75 3.75v3.5M12 9v5M10 12l2 2 2-2"/>"""
    };

    public static string Get(string name) => Paths.TryGetValue(name, out var path) ? path : Paths["folder"];

    /// <summary>Icon name for an Object Explorer node.</summary>
    public static string For(ExplorerNode node) => node.Kind switch
    {
        NodeKind.Server => "server",
        NodeKind.Database => "database",
        NodeKind.Table => "table",
        NodeKind.View => "view",
        NodeKind.Column => node.SubType is "PK" or "FK" ? "key" : "column",
        NodeKind.Key => "key",
        NodeKind.Constraint => "constraint",
        NodeKind.Trigger => "trigger",
        NodeKind.Index => "index",
        NodeKind.Procedure or NodeKind.Package => "procedure",
        NodeKind.Function => "function",
        NodeKind.Parameter => "parameter",
        NodeKind.Synonym => "synonym",
        NodeKind.Sequence => "sequence",
        NodeKind.UserType => "type",
        NodeKind.User => "user",
        NodeKind.Role or NodeKind.ServerRole => "role",
        NodeKind.Schema => "schema",
        NodeKind.Login => "login",
        NodeKind.Job => "job",
        NodeKind.ServerSecurityFolder or NodeKind.DbSecurityFolder => "lock",
        NodeKind.AgentFolder => "agent",
        _ => "folder"
    };
}