using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace ArchSql.Analysis;

/// <summary>Loss-safe T-SQL formatter: parse -> regenerate via ScriptDom's built-in generator.
/// A batch that fails to parse is passed through byte-for-byte unchanged (Hard Constraint 11) —
/// the formatter must never corrupt or drop SQL it did not understand.</summary>
public static class TSqlFormatter
{
    public static string Format(string content)
    {
        using var reader = new StringReader(content);
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        if (errors.Count > 0 || fragment is null)
        {
            return content; // unparseable: pass through unchanged, never corrupt
        }

        var options = new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IndentationSize = 4,
            AlignClauseBodies = true,
            NewLineBeforeFromClause = true,
            NewLineBeforeWhereClause = true,
            NewLineBeforeGroupByClause = true,
            NewLineBeforeOrderByClause = true,
        };
        var generator = new Sql160ScriptGenerator(options);
        generator.GenerateScript(fragment, out var formatted);
        return formatted;
    }
}
