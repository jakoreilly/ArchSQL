using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Detects a handful of body-level code characteristics from an object's source text: a
/// single tokenizing pass, no second AST traversal. Token-based (not substring search) so a keyword
/// embedded inside a longer identifier is never mistaken for the keyword itself.</summary>
public static class CodeFlagsScanner
{
    public static CodeFlags Scan(string source)
    {
        if (string.IsNullOrEmpty(source)) { return new CodeFlags(); }
        var tokens = Tokenize(source);
        var codeTokens = Tokenize(StripCommentsAndStrings(source));
        return new CodeFlags
        {
            Scanned = true,
            UsesNolock = tokens.Contains("NOLOCK") || HasAdjacent(tokens, "READ", "UNCOMMITTED"),
            UsesCursor = tokens.Contains("CURSOR"),
            UsesAtAtIdentity = tokens.Contains("@@IDENTITY"),
            HasSetNoCount = HasAdjacent(tokens, "SET", "NOCOUNT", "ON"),
            UsesExecuteAs = ContainsExecuteAs(codeTokens),
        };
    }

    private static bool ContainsExecuteAs(List<string> tokens) =>
        HasAdjacent(tokens, "EXECUTE", "AS") || HasAdjacent(tokens, "EXEC", "AS");

    /// <summary>Blanks out line comments (--…), block comments (/*…*/) and single-quoted string
    /// bodies so a keyword appearing only in a comment or literal is not mistaken for code. Linear,
    /// no regex; unterminated constructs are blanked to end-of-input.</summary>
    private static string StripCommentsAndStrings(string source)
    {
        var sb = new System.Text.StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (c == '-' && next == '-')
            {
                while (i < source.Length && source[i] != '\n') { sb.Append(' '); i++; }
            }
            else if (c == '/' && next == '*')
            {
                sb.Append("  "); i += 2;
                while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                { sb.Append(source[i] == '\n' ? '\n' : ' '); i++; }
                if (i < source.Length) { sb.Append("  "); i += 2; }
            }
            else if (c == '\'')
            {
                sb.Append(' '); i++;
                while (i < source.Length && source[i] != '\'') { sb.Append(source[i] == '\n' ? '\n' : ' '); i++; }
                if (i < source.Length) { sb.Append(' '); i++; }
            }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    /// <summary>Splits source into uppercased identifier-like tokens (letters, digits, '@', '_'),
    /// discarding everything else (whitespace, punctuation, string/comment content is not
    /// distinguished from code — a false match inside a comment is a rare, harmless over-flag).</summary>
    private static List<string> Tokenize(string source)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch is '@' or '_')
            {
                current.Append(char.ToUpperInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) { tokens.Add(current.ToString()); }
        return tokens;
    }

    private static bool HasAdjacent(List<string> tokens, params string[] sequence)
    {
        for (var i = 0; i + sequence.Length <= tokens.Count; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (tokens[i + j] != sequence[j]) { match = false; break; }
            }
            if (match) { return true; }
        }
        return false;
    }
}
