using Microsoft.Win32;

namespace MigrateSQLServerDatabases.Services.Manager;

/// <summary>SQL Server instances installed on the machine running this app (for the server name suggestions).</summary>
public static class LocalServers
{
    public static List<string> Find()
    {
        var servers = new List<string>();
        if (!OperatingSystem.IsWindows())
            return servers;

        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using (var instances = machine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                {
                    if (instances is not null)
                        foreach (var name in instances.GetValueNames())
                            servers.Add(name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "localhost" : $@".\{name}");
                }

                using var localDb = machine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions");
                if (localDb is not null && localDb.GetSubKeyNames().Length > 0)
                    servers.Add(@"(localdb)\MSSQLLocalDB");
            }
        }
        catch (Exception)
        {
            // No registry access; the user types the name
        }

        return servers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
