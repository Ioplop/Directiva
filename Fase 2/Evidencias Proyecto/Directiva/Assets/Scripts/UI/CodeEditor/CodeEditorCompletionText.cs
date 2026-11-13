using System;

namespace Directiva.CodeUI
{
    /// <summary>Pure text transformation shared by Enter/Space and Tab completion.</summary>
    public readonly struct CodeEditorCompletionText
    {
        public string Text { get; }
        public int Caret { get; }
        public CodeEditorCompletionText(string text, int caret)
        { Text = text; Caret = caret; }

        public static CodeEditorCompletionText Build(string source, int start, int end,
            DScriptCompletionItem item, bool addSpace)
        {
            source ??= string.Empty;
            if (item == null || start < 0 || end < start || end > source.Length)
                return new CodeEditorCompletionText(source, Math.Max(0, Math.Min(start, source.Length)));

            string inserted = item.Name;
            int caret = start + inserted.Length;
            string suffix = source.Substring(end);
            // Recheck the live insertion context, not just the cached suggestion.
            // A function name in a `from ... import` or a Function-typed argument
            // must remain a reference even if popup metadata was computed earlier.
            if (item.InsertParentheses &&
                DScriptCompletionEngine.ShouldInsertCallParentheses(source, start, item))
            {
                // Completing an already typed call must not duplicate its parentheses.
                if (suffix.Length == 0 || suffix[0] != '(')
                {
                    inserted += "()";
                    caret += item.TakesArguments ? 1 : 2;
                }
                else if (item.TakesArguments)
                    caret += 1;
                else if (suffix.Length >= 2 && suffix[1] == ')')
                    caret += 2;
            }
            string text = source.Substring(0, start) + inserted + suffix;
            if (addSpace)
            {
                // Insert at the chosen caret, inside () for parameterized calls.
                text = text.Insert(caret, " ");
                caret++;
            }
            return new CodeEditorCompletionText(text, caret);
        }
    }
}
