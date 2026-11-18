using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Parsing;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Rewrites only module paths in syntactically valid top-level import statements.
    /// Identifiers in comments, strings, aliases, and ordinary expressions are untouched.
    /// An unrelated syntax error does not prevent recovery of valid import statements.
    /// </summary>
    internal static class DScriptFolderImportRefactor
    {
        private readonly struct Edit
        {
            internal readonly int Start;
            internal readonly int Length;
            internal readonly string Replacement;
            internal Edit(int start, int length, string replacement)
            {
                Start = start;
                Length = length;
                Replacement = replacement;
            }
        }

        internal static string Rewrite(string source, string oldFolder, string newFolder)
        {
            source ??= string.Empty;
            string originalPrefix = QualifiedFolder(oldFolder);
            string renamedPrefix = QualifiedFolder(newFolder);
            if (originalPrefix.Length == 0 || renamedPrefix.Length == 0 ||
                string.Equals(originalPrefix, renamedPrefix, StringComparison.Ordinal))
                return source;

            // The directory itself is not a module. Only paths beneath it (F.Module)
            // refer to .dscript files in F; a separate F.dscript must not be rewritten.
            string qualifiedPrefix = originalPrefix + ".";
            var lexer = new DScriptLexer("refactor", source);
            var tokens = lexer.Tokenize();
            var parser = new DScriptParser("refactor", tokens);
            ModuleNode module = parser.Parse();
            var edits = new List<Edit>();

            foreach (Statement statement in module.Statements)
            {
                string path = statement is ImportStatement import ? import.ModuleName :
                    statement is FromImportStatement from ? from.ModuleName : null;
                if (path == null || !path.StartsWith(qualifiedPrefix, StringComparison.Ordinal))
                    continue;

                TokenKind firstKind = statement is ImportStatement
                    ? TokenKind.Import : TokenKind.From;
                int index = -1;
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i].Span.Start == statement.Span.Start &&
                        tokens[i].Kind == firstKind)
                    { index = i; break; }
                }
                if (index < 0 || index + 1 >= tokens.Count ||
                    tokens[index + 1].Kind != TokenKind.Identifier)
                    continue;

                int first = index + 1;
                int last = first;
                var readPath = new StringBuilder(tokens[first].Text);
                while (last + 2 < tokens.Count && tokens[last + 1].Kind == TokenKind.Dot &&
                    tokens[last + 2].Kind == TokenKind.Identifier)
                {
                    last += 2;
                    readPath.Append('.').Append(tokens[last].Text);
                }
                // Never use the wrong span if parsing recovered from broken syntax.
                if (!string.Equals(readPath.ToString(), path, StringComparison.Ordinal))
                    continue;

                int start = tokens[first].Span.Start;
                int end = tokens[last].Span.End;
                edits.Add(new Edit(start, end - start,
                    renamedPrefix + path.Substring(originalPrefix.Length)));
            }

            if (edits.Count == 0) return source;
            var result = new StringBuilder(source);
            // Reverse order preserves original character offsets and source formatting.
            foreach (Edit edit in edits.OrderByDescending(e => e.Start))
            {
                result.Remove(edit.Start, edit.Length);
                result.Insert(edit.Start, edit.Replacement);
            }
            return result.ToString();
        }

        private static string QualifiedFolder(string folder) =>
            (folder ?? string.Empty).Replace('\\', '/').Trim('/').Replace('/', '.');
    }
}
