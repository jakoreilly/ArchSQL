namespace ArchSql.Analysis;

/// <summary>Case-insensitive glob matcher supporting '*' (any run of characters) and '?' (exactly
/// one character). Iterative backtracking, no regex — avoids catastrophic-backtracking risk on
/// untrusted patterns from a config file.</summary>
public static class Glob
{
    public static bool IsMatch(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || Eq(pattern[p], text[t]))) { t++; p++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; }
            else if (star != -1) { p = star + 1; t = ++mark; }
            else { return false; }
        }
        while (p < pattern.Length && pattern[p] == '*') { p++; }
        return p == pattern.Length;
    }

    private static bool Eq(char a, char b) => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
