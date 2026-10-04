using System.Collections;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace NavisHelper.McpConfigurator;

internal static class CodexTomlConfig
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string ReadFile(string path) => File.Exists(path)
        ? StrictUtf8.GetString(File.ReadAllBytes(path)) : string.Empty;

    internal static TomlTable? ReadServer(string text) => Server(Parse(Body(text)));

    internal static string Remove(string text)
    {
        var body = Body(text);
        var before = Parse(body);
        if (Server(before) == null)
            return text;

        var document = SyntaxParser.Parse(body);
        if (document.HasErrors || document.ToString() != body)
            throw UnsafeLayout();

        var removals = new List<(int Start, int End)>();
        CollectKeys(document.KeyValues, Array.Empty<string>(), removals);
        foreach (var table in document.Tables)
        {
            var path = KeyPath(table.Name ?? throw UnsafeLayout());
            if (IsOwned(path))
                CollectRange(table.OpenBracket, table.EndOfLineToken ?? table.CloseBracket, removals);
            CollectKeys(table.Items, path, removals);
        }

        var edited = new StringBuilder(body);
        foreach (var range in removals.OrderByDescending(range => range.Start))
            edited.Remove(range.Start, range.End - range.Start);
        var result = edited.ToString();
        var after = Parse(result);
        if (Server(after) != null)
            throw UnsafeLayout(); // Inline ancestor tables cannot be edited independently.
        RemoveServer(before);
        RemoveServer(after);
        if (!Equivalent(before, after))
            throw UnsafeLayout();
        return Prefix(text) + result;
    }

    internal static string Configure(string text, string command)
    {
        var before = Parse(Body(text));
        var server = Server(before);
        if (server?.Count == 2 && server.TryGetValue("command", out var oldCommand) &&
            Equals(oldCommand, command) && server.TryGetValue("args", out var args) &&
            args is TomlArray { Count: 0 })
            return text;

        var remaining = Remove(text);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var replacement = new TomlTable { ["command"] = command, ["args"] = new TomlArray() };
        var addition = "[mcp_servers.navishelper]" + newline +
            TomlSerializer.Serialize(replacement).Replace("\r\n", "\n").Replace("\n", newline);
        var tail = Body(remaining);
        var separator = tail.Length == 0 || tail.EndsWith('\n') ? "" : newline;
        var result = remaining + separator + addition;
        var after = Parse(Body(result));
        if (!Equivalent(Server(after), replacement))
            throw UnsafeLayout();
        RemoveServer(before);
        RemoveServer(after);
        if (!Equivalent(before, after))
            throw UnsafeLayout();
        return result;
    }

    private static TomlTable Parse(string text)
    {
        try
        {
            SyntaxParser.ParseStrict(text);
            return TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException) { throw UnsafeLayout(); }
    }

    private static TomlTable? Server(TomlTable root)
    {
        if (!root.TryGetValue("mcp_servers", out var value))
            return null;
        if (value is not TomlTable servers)
            throw UnsafeLayout();
        if (!servers.TryGetValue("navishelper", out value))
            return null;
        return value as TomlTable ?? throw UnsafeLayout();
    }

    private static void RemoveServer(TomlTable root)
    {
        if (root.TryGetValue("mcp_servers", out var value) && value is TomlTable servers)
        {
            servers.Remove("navishelper");
            if (servers.Count == 0)
                root.Remove("mcp_servers");
        }
    }

    private static bool Equivalent(object? left, object? right)
    {
        if (left is TomlTable a && right is TomlTable b)
            return a.Count == b.Count && a.All(pair =>
                b.TryGetValue(pair.Key, out var value) && Equivalent(pair.Value, value));
        if (left is IEnumerable x && right is IEnumerable y && left is not string && right is not string)
        {
            var aItems = x.Cast<object>().ToArray();
            var bItems = y.Cast<object>().ToArray();
            return aItems.Length == bItems.Length && aItems.Zip(bItems).All(pair =>
                Equivalent(pair.First, pair.Second));
        }
        return Equals(left, right);
    }

    private static void CollectKeys(SyntaxList<KeyValueSyntax> items, string[] parent,
        List<(int Start, int End)> removals)
    {
        foreach (var item in items)
            if (IsOwned(parent.Concat(KeyPath(item.Key ?? throw UnsafeLayout())).ToArray()))
                CollectRange(item.Key, (SyntaxNode?)item.EndOfLineToken ?? item.Value, removals);
    }

    private static void CollectRange(SyntaxNode? first, SyntaxNode? last,
        List<(int Start, int End)> removals)
    {
        if (first == null || last == null)
            throw UnsafeLayout();
        removals.Add((first.Span.Offset, last.Span.Offset + last.Span.Length));
    }

    private static string[] KeyPath(KeySyntax key) => new[] { KeyText(key.Key) }
        .Concat(key.DotKeys.Select(item => KeyText(item.Key))).ToArray();

    private static string KeyText(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? throw UnsafeLayout(),
        StringValueSyntax quoted => quoted.Value!,
        _ => throw UnsafeLayout()
    };

    private static bool IsOwned(string[] path) => path.Length >= 2 &&
        path[0] == "mcp_servers" && path[1] == "navishelper";

    private static string Prefix(string text) => text.StartsWith('\uFEFF') ? "\uFEFF" : "";
    private static string Body(string text) => text.StartsWith('\uFEFF') ? text[1..] : text;
    private static InvalidDataException UnsafeLayout() => new(
        "Codex TOML configuration is invalid or cannot be edited without changing unrelated settings.");
}
