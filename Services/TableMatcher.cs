using System.Text.RegularExpressions;
using MigrateSQLServerDatabases.Models;

namespace MigrateSQLServerDatabases.Services;

/// <summary>
/// Pairs source tables with destination tables. In order: the same name; a near name in the same
/// schema; a near name in any schema. Near names ignore case, spaces, underscores and invisible
/// characters, and English singular/plural endings per word (Customers ↔ Customer,
/// "Expenses History" ↔ ExpensesHistory). A near match is only used when it is unambiguous on both sides.
/// </summary>
public static partial class TableMatcher
{
    /// <returns>Destination table for each matched source table, keyed by the source full name.</returns>
    public static Dictionary<string, TableInfo> Match(IReadOnlyList<TableInfo> sourceTables, IReadOnlyList<TableInfo> destinationTables)
    {
        var matches = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);

        var byName = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in destinationTables)
            byName.TryAdd(table.FullName, table);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<TableInfo>();
        foreach (var table in sourceTables)
        {
            if (byName.TryGetValue(table.FullName, out var same))
            {
                matches.TryAdd(table.FullName, same);
                used.Add(same.FullName);
            }
            else
            {
                unmatched.Add(table);
            }
        }

        MatchNear(unmatched, destinationTables, used, matches, t => $"{Clean(t.Schema)}.{NameKey(t.Name)}");
        MatchNear(unmatched, destinationTables, used, matches, t => NameKey(t.Name));

        return matches;
    }

    private static void MatchNear(
        List<TableInfo> unmatched, IReadOnlyList<TableInfo> destinationTables, HashSet<string> used,
        Dictionary<string, TableInfo> matches, Func<TableInfo, string> key)
    {
        // Destination tables already taken can't take another source table
        // (source has Customer and Customers, destination only Customer)
        var candidates = destinationTables.Where(t => !used.Contains(t.FullName)).ToLookup(key);
        var sourceKeys = unmatched.ToLookup(key);

        foreach (var table in unmatched.ToList())
        {
            var tableKey = key(table);
            if (sourceKeys[tableKey].Count() != 1 || candidates[tableKey].Count() != 1)
                continue;

            var match = candidates[tableKey].First();
            matches[table.FullName] = match;
            used.Add(match.FullName);
            unmatched.Remove(table);
        }
    }

    private static string NameKey(string name) =>
        string.Concat(WordRegex().Matches(name).Select(m => char.IsAsciiLetter(m.Value[0]) ? Stem(m.Value) : Clean(m.Value)));

    /// <summary>Lower-case letters and digits only; drops spaces, underscores and invisible characters.</summary>
    private static string Clean(string text) =>
        new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// Reduces singular and plural forms of a word to the same stem:
    /// customers/customer → customer, categories/category → categori, houses/house → hous, statuses/status → status.
    /// </summary>
    private static string Stem(string word)
    {
        var stem = word.ToLowerInvariant();

        if (stem.Length > 3 && stem.EndsWith("ies"))
            stem = stem[..^3] + "i";
        else if (stem.Length > 1 && stem.EndsWith('s') && !stem.EndsWith("ss") && !stem.EndsWith("us") && !stem.EndsWith("is"))
            stem = stem[..^1];

        if (stem.Length > 1 && stem.EndsWith('e'))
            stem = stem[..^1];
        else if (stem.Length > 1 && stem.EndsWith('y'))
            stem = stem[..^1] + "i";

        return stem;
    }

    // PascalCase words, acronyms, digits and anything else (spaces, "_", non-English letters) as separate tokens
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+|[^A-Za-z0-9]+")]
    private static partial Regex WordRegex();
}
