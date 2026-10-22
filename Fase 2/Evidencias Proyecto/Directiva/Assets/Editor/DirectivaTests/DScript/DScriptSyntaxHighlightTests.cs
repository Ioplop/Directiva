using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Directiva.CodeUI;
using Directiva.UI;

namespace Directiva.EditorTests.DScript
{
    /// <summary>Pure tokenizer/rendering checks. Visual caret/scroll tests are manual in Play Mode.</summary>
    internal static class DScriptSyntaxHighlightTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            Add(tests, "KeywordAndFunction", "Definitions and keywords remain distinguishable", t =>
            {
                string s = "def calculate(value):\n    return value + 2";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "def", DScriptSyntaxKind.Keyword);
                Expect(t, doc, s, "calculate", DScriptSyntaxKind.Function);
                Expect(t, doc, s, "value", DScriptSyntaxKind.Variable);
                Expect(t, doc, s, "return", DScriptSyntaxKind.Keyword);
            });
            Add(tests, "NumbersAndOperators", "Number formats and symbolic operations", t =>
            {
                string s = "x = 12 + 2.5 * 7 // 2 == 13";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "12", DScriptSyntaxKind.Number);
                Expect(t, doc, s, "2.5", DScriptSyntaxKind.Number);
                Expect(t, doc, s, "+", DScriptSyntaxKind.Operator);
                Expect(t, doc, s, "//", DScriptSyntaxKind.Operator);
                Expect(t, doc, s, "==", DScriptSyntaxKind.Operator);
            });
            Add(tests, "BooleansAndNone", "Literal keywords have their own palette category", t =>
            {
                string s = "x = True or False and None";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "True", DScriptSyntaxKind.Literal);
                Expect(t, doc, s, "False", DScriptSyntaxKind.Literal);
                Expect(t, doc, s, "None", DScriptSyntaxKind.Literal);
                Expect(t, doc, s, "and", DScriptSyntaxKind.Keyword);
            });
            Add(tests, "QuotedHash", "Hash signs inside a quoted string are not comments", t =>
            {
                string s = "s = '# not a comment' # real comment";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "# not", DScriptSyntaxKind.String);
                Expect(t, doc, s, "# real", DScriptSyntaxKind.Comment);
            });
            Add(tests, "EscapedQuotes", "Escaped quotes do not change the comment boundary", t =>
            {
                string s = "s = \"quoted \\\" # still string\" # comment";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "# still", DScriptSyntaxKind.String);
                Expect(t, doc, s, "# comment", DScriptSyntaxKind.Comment);
            });
            Add(tests, "OnlyComment", "Indented and full-line comments remain visible", t =>
            {
                string s = "    # hello\n# again";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "# hello", DScriptSyntaxKind.Comment);
                Expect(t, doc, s, "# again", DScriptSyntaxKind.Comment);
                t.Equal(DScriptSyntaxKind.Plain, doc.KindAt(0), "Indentation is plain");
            });
            Add(tests, "UnterminatedString", "Unfinished string still has a stable colored span", t =>
            {
                string s = "s = 'incomplete # hello";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "# hello", DScriptSyntaxKind.String);
            });
            Add(tests, "SharedDeclarations", "Shared names are colored at declarations and references", t =>
            {
                string s = "def f():\n    return counter\nshared counter = 0";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "counter", DScriptSyntaxKind.Shared);
                Expect(t, doc, s, "counter", DScriptSyntaxKind.Shared, s.LastIndexOf("counter", StringComparison.Ordinal));
            });
            Add(tests, "ModuleAlias", "Imported module names and aliases", t =>
            {
                string s = "import utils.vectors as vec\nx = vec.length";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "utils", DScriptSyntaxKind.Module);
                Expect(t, doc, s, "vectors", DScriptSyntaxKind.Module);
                Expect(t, doc, s, "vec", DScriptSyntaxKind.Module, s.LastIndexOf("vec", StringComparison.Ordinal));
                Expect(t, doc, s, "length", DScriptSyntaxKind.Member);
            });
            Add(tests, "FromImport", "From-import targets are not forced to module colors", t =>
            {
                string s = "from package.util import calculate\nx = calculate(3)";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "package", DScriptSyntaxKind.Module);
                Expect(t, doc, s, "util", DScriptSyntaxKind.Module);
                Expect(t, doc, s, "calculate", DScriptSyntaxKind.Variable);
                Expect(t, doc, s, "calculate", DScriptSyntaxKind.Function, s.LastIndexOf("calculate", StringComparison.Ordinal));
            });
            Add(tests, "FromAlias", "From-import alias may be a function instead of a module", t =>
            {
                string s = "from lib import fun as alias\nalias(3)";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "lib", DScriptSyntaxKind.Module);
                Expect(t, doc, s, "alias", DScriptSyntaxKind.Variable);
                Expect(t, doc, s, "alias", DScriptSyntaxKind.Function, s.LastIndexOf("alias", StringComparison.Ordinal));
            });
            Add(tests, "MemberAndCall", "Methods highlight by call syntax, not inferred type", t =>
            {
                string s = "x.pop()\ny = x.value\nx.append(5)";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "pop", DScriptSyntaxKind.Function);
                Expect(t, doc, s, "value", DScriptSyntaxKind.Member);
                Expect(t, doc, s, "append", DScriptSyntaxKind.Function);
            });
            Add(tests, "DynamicName", "Unknown callable identifiers are colored without symbol analysis", t =>
            {
                string s = "mystery(5)\nunknown = 7";
                var doc = Inspect(t, s);
                Expect(t, doc, s, "mystery", DScriptSyntaxKind.Function);
                Expect(t, doc, s, "unknown", DScriptSyntaxKind.Variable);
            });
            Add(tests, "RichTextEscape", "Literal angle brackets cannot inject TextCore tags", t =>
            {
                string s = "s = '<color=red>& hi' # <b>& comment";
                var doc = Inspect(t, s);
                string markup = doc.FormatLine(0, 0, 150, k => "#112233FF");
                t.Contains("<noparse><</noparse>color=red>& hi", markup,
                    "Literal less-than in string is protected without HTML entities");
                t.Contains("<noparse><</noparse>b>& comment", markup,
                    "Literal less-than in comment is protected without HTML entities");
                t.Check(!markup.Contains("<b>"), "Source cannot inject bold tags");
                t.Check(!markup.Contains("<color=red>"), "Source cannot inject color tags");
                t.Equal(s, SourceGlyphs(markup), "Reconstructed visible text equals original source");
                var escaped = DScriptSyntaxHighlighter.Analyze("s = \"\\n\"");
                t.Contains("\\n", escaped.FormatLine(0, 0, 40, k => "#112233FF"),
                    "Literal backslash-n stays visible (parseEscapeSequences=false)");
            });
            Add(tests, "ComparisonAmpersand", "Comparison and ampersand characters remain single glyphs", t =>
            {
                string s = "if a < b and c > d: # x & y";
                string markup = Inspect(t, s).FormatLine(0, 0, 150, k => "#112233FF");
                t.Contains("<noparse><</noparse>", markup, "Less-than protected with noparse");
                t.Contains("c > d", SourceGlyphs(markup), "Greater-than is literal, not HTML escaped");
                t.Contains("x & y", markup, "Ampersand is literal, not HTML escaped");
                t.Check(!markup.Contains("&lt;") && !markup.Contains("&gt;") &&
                    !markup.Contains("&amp;"), "No injected HTML entity sequences");
                t.Equal(s, SourceGlyphs(markup), "Source glyphs and cursor columns coincide");
            });
            Add(tests, "LiteralHtmlEntities", "Entity-looking source text is not interpreted or rewritten", t =>
            {
                string s = "print('The tokens &lt; &gt; &amp; are literal')";
                string markup = Inspect(t, s).FormatLine(0, 0, 150, k => "#112233FF");
                t.Contains("&lt; &gt; &amp;", markup, "Existing entity text stays literal");
                t.Equal(s, SourceGlyphs(markup), "Visible source has original entity letters");
            });
            Add(tests, "NoparseInjection", "Source cannot break out of the literal tag protection", t =>
            {
                string s = "s = '</noparse><size=100><color=red><b>text</b>'";
                string markup = Inspect(t, s).FormatLine(0, 0, 150, k => "#112233FF");
                t.Contains("<noparse><</noparse>/noparse>", markup,
                    "Source closing noparse tag is rendered as literal text");
                t.Check(!markup.Contains("<size=100>"), "Source cannot change font size");
                t.Check(!markup.Contains("<color=red>"), "Source cannot override colors");
                t.Check(!markup.Contains("<b>"), "Source cannot change font weight");
                t.Equal(s, SourceGlyphs(markup), "No user markup is lost");
            });
            Add(tests, "UnicodePreservation", "Accents and Unicode remain unchanged through rich text formatting", t =>
            {
                string s = "print('áéíóú ñ Ñ ¡ ¿ < > & 🌿')";
                var doc = Inspect(t, s);
                t.Equal(s, doc.Source, "Analysis leaves Unicode intact");
                t.Equal(s, SourceGlyphs(doc.FormatLine(0, 0, 150, k => "#112233FF")),
                    "Output retains all UTF-16 code units");
            });
            Add(tests, "CroppedLiteralTag", "Viewport slicing across a tag keeps original characters", t =>
            {
                string s = "x = '<color=red>hello'";
                var doc = Inspect(t, s);
                int tag = s.IndexOf('<');
                string first = doc.FormatLine(0, tag, 3, k => "#112233FF");
                string middle = doc.FormatLine(0, tag + 2, 5, k => "#112233FF");
                t.Equal(s.Substring(tag, 3), SourceGlyphs(first), "Slice including opening bracket");
                t.Equal(s.Substring(tag + 2, 5), SourceGlyphs(middle), "Slice within tag-like text");
            });
            Add(tests, "NoColorLiteralTag", "Even plain-colored source cannot inject rich text", t =>
            {
                string s = "x = '<b>& raw'";
                var doc = Inspect(t, s);
                string markup = doc.FormatLine(0, 0, 80, k => null);
                t.Contains("<noparse><</noparse>b>& raw", markup, "Protection without color tags");
                t.Equal(s, SourceGlyphs(markup), "No-color output preserves visible text");
            });
            Add(tests, "WindowSlice", "Only requested columns are emitted with correct token colors", t =>
            {
                string s = "shared score = 12345 # end";
                var doc = Inspect(t, s);
                var html = doc.FormatLine(0, 15, 3, k => "#ABCDEF01");
                t.Equal("<color=#ABCDEF01>123</color>", html, "Selected text with its own color");
                t.Equal(string.Empty, doc.FormatLine(0, 100, 10, k => "#ABCDEF01"), "Beyond end of line");
            });
            Add(tests, "LineBreakNormalization", "CRLF and CR normalize to single editor newlines", t =>
            {
                var doc = Inspect(t, "x=1\r\ny=2\rz=3");
                t.Equal("x=1\ny=2\nz=3", doc.Source, "Normalized source");
                t.Equal(3, doc.LineCount, "Count of lines");
                t.Equal(3, doc.LineLength(1), "Second line length");
                t.Equal(DScriptSyntaxKind.Number, doc.KindAt(doc.Source.IndexOf('3')), "Last line number");
            });
            Add(tests, "BlankAndTrailingLines", "Empty document and trailing blank lines render", t =>
            {
                var empty = Inspect(t, "");
                t.Equal(1, empty.LineCount, "Empty document line count");
                t.Equal(string.Empty, empty.FormatLine(0, 0, 80, k => "#FFFFFF"), "Empty line");
                var trailing = Inspect(t, "x=1\n\n");
                t.Equal(3, trailing.LineCount, "Trailing empty lines");
                t.Equal(string.Empty, trailing.FormatLine(2, 0, 80, k => "#FFFFFF"), "Last empty line");
            });
            Add(tests, "IncompleteCode", "Lexer errors must not make the editor throw", t =>
            {
                var doc = Inspect(t, "def x(:\n    ??? # broken\n");
                t.Equal(3, doc.LineCount, "Invalid document lines");
                t.Check(doc.Spans.Count > 0, "Valid words still highlighted");
                Expect(t, doc, doc.Source, "# broken", DScriptSyntaxKind.Comment);
            });
            Add(tests, "LongLineWindow", "Long source line only renders visible columns", t =>
            {
                string s = "x = '" + new string('A', 12000) + "'";
                var doc = Inspect(t, s);
                var small = doc.FormatLine(0, 10000, 75, k => "#FF0000FF");
                t.Check(small.Length < 150, "Only 75 characters plus markup emitted");
            });
            Add(tests, "PaletteCopy", "Runtime option overrides do not mutate original UI Theme palette", t =>
            {
                var original = new DirectivaSyntaxColors();
                var customized = original.Copy();
                customized.enabled = false;
                customized.keyword = UnityEngine.Color.red;
                t.Check(original.enabled, "Original palette still enabled");
                t.Check(original.keyword != customized.keyword, "Original keyword color unchanged");
            });
            Add(tests, "NativeTextOrigin", "Text origin uses the stationary input wrapper, border and pixel padding", t =>
            {
                var stack = new UnityEngine.Rect(100f, 50f, 600f, 400f);
                var input = new UnityEngine.Rect(115f, 62f, 570f, 380f);
                var origin = CodeEditorView.CalculateTextOrigin(
                    input, stack, 1f, 2f, 6f, 4f);
                t.Equal(22f, origin.x, "Input content X in editor coordinates");
                t.Equal(18f, origin.y, "Input content Y in editor coordinates");
            });
            Add(tests, "OriginInvariantUnderScroll", "A stationary TextField input must not add ScrollView offsets into the source origin", t =>
            {
                var stack = new UnityEngine.Rect(30f, 40f, 600f, 400f);
                var input = new UnityEngine.Rect(42f, 50f, 500f, 300f);
                var origin = CodeEditorView.CalculateTextOrigin(input, stack,
                    0f, 0f, 4f, 3f);
                t.Equal(16f, origin.x, "Initial X origin");
                t.Equal(13f, origin.y, "Initial Y origin");
                foreach (float scrollY in new[] { 0f, 100f, 350f })
                {
                    float screenY = origin.y + (20 * 18f) - scrollY;
                    float expected = input.yMin - stack.yMin + 3f + (20 * 18f) - scrollY;
                    t.Equal(expected, screenY,
                        "Scrolling changes display position but not input origin: y=" + scrollY);
                }
            });
            Add(tests, "ZoomReflowsInput", "Genuine parent geometry changes update the source origin without scroll compensation", t =>
            {
                var stack = new UnityEngine.Rect(10f, 20f, 800f, 600f);
                var oldInput = new UnityEngine.Rect(18f, 32f, 700f, 450f);
                var reflowedInput = new UnityEngine.Rect(22f, 39f, 700f, 450f);
                var before = CodeEditorView.CalculateTextOrigin(oldInput, stack,
                    0f, 0f, 5f, 6f);
                var after = CodeEditorView.CalculateTextOrigin(reflowedInput, stack,
                    0f, 0f, 5f, 6f);
                t.Equal(4f, after.x - before.x, "Horizontal origin shift after actual geometry change");
                t.Equal(7f, after.y - before.y, "Vertical origin shift after actual geometry change");
            });
            Add(tests, "ZoomScrollCaretGrid", "Overlay uses one scroll subtraction at different zooms and scroll positions", t =>
            {
                var stack = new UnityEngine.Rect(10f, 20f, 900f, 600f);
                var input = new UnityEngine.Rect(30f, 40f, 840f, 500f);
                var origin = CodeEditorView.CalculateTextOrigin(input, stack,
                    0f, 0f, 4f, 3f);
                foreach (float advance in new[] { 8f, 14f, 25f })
                {
                    foreach (float scrollY in new[] { 0f, 120f, 360f })
                    {
                        foreach (float scrollX in new[] { 0f, 45f, 160f })
                        {
                            // The input remains stationary. Only its nested content
                            // scrolls, and the overlay must subtract that offset once.
                            float overlayX = origin.x + 12 * advance - scrollX;
                            float expectedX = input.xMin - stack.xMin + 4f + 12 * advance - scrollX;
                            float overlayY = origin.y + 8 * advance * 1.7f - scrollY;
                            float expectedY = input.yMin - stack.yMin + 3f + 8 * advance * 1.7f - scrollY;
                            t.Check(UnityEngine.Mathf.Abs(overlayX - expectedX) < 0.01f,
                                "X at zoom " + advance + " with horizontal scroll " + scrollX);
                            t.Check(UnityEngine.Mathf.Abs(overlayY - expectedY) < 0.01f,
                                "Y at zoom " + advance + " with vertical scroll " + scrollY);
                        }
                    }
                }
            });
            Add(tests, "ZoomWhileScrolled", "Resampling the stationary input after zoom cannot pull line one into view", t =>
            {
                var stack = new UnityEngine.Rect(0f, 0f, 900f, 500f);
                var input = new UnityEngine.Rect(70f, 0f, 820f, 500f);
                var originBefore = CodeEditorView.CalculateTextOrigin(input, stack,
                    0f, 0f, 4f, 4f);
                var originAfter = CodeEditorView.CalculateTextOrigin(input, stack,
                    0f, 0f, 4f, 4f);
                t.Equal(originBefore.y, originAfter.y,
                    "Zoom of a scrolled viewport does not shift the document origin");
                float scrollY = 440f;
                float lineZeroAtZoomOut = originAfter.y - scrollY;
                float line20AtZoomOut = originAfter.y + 20 * 18f - scrollY;
                t.Check(lineZeroAtZoomOut < 0f,
                    "First line is still outside the viewport at a deep scroll position");
                t.Check(line20AtZoomOut > lineZeroAtZoomOut,
                    "Subsequent lines remain in source order");
            });
            Add(tests, "ZoomScrollEndRange", "Last source line projects into the viewport at the final valid scroll position", t =>
            {
                const int lineCount = 30;
                const float topPadding = 4f;
                const float bottomPadding = 4f;
                const float viewportHeight = 300f;
                var input = new UnityEngine.Rect(70f, 0f, 820f, 500f);
                var stack = new UnityEngine.Rect(0f, 0f, 900f, 500f);
                var origin = CodeEditorView.CalculateTextOrigin(input, stack,
                    0f, 0f, 4f, topPadding);
                foreach (float lineHeight in new[] { 16f, 21f, 33f })
                {
                    float contentHeight = topPadding + lineCount * lineHeight + bottomPadding;
                    float scrollY = UnityEngine.Mathf.Max(0f, contentHeight - viewportHeight);
                    float lastLineY = origin.y + (lineCount - 1) * lineHeight - scrollY;
                    t.Check(lastLineY >= 0f && lastLineY < viewportHeight,
                        "Last line stays reachable at line height " + lineHeight);
                }
            });
            Add(tests, "ZoomKeepsCenterLine", "Zoom in and out keeps the fractional center line fixed", t =>
            {
                const float viewportTop = 3f;
                const float viewportHeight = 280f;
                const float oldOrigin = 6f;
                const float oldLineHeight = 19f;
                const float oldScrollY = 410f;
                float anchor = CodeEditorView.CalculateZoomAnchorLine(
                    oldScrollY, viewportTop, viewportHeight, oldOrigin, oldLineHeight);
                foreach (float newLineHeight in new[] { 14f, 25f, 37f })
                {
                    float newScroll = CodeEditorView.CalculateZoomAnchoredScrollY(
                        anchor, viewportTop, viewportHeight, oldOrigin,
                        newLineHeight, 100 * newLineHeight + 12f);
                    float restored = CodeEditorView.CalculateZoomAnchorLine(
                        newScroll, viewportTop, viewportHeight, oldOrigin, newLineHeight);
                    t.Check(UnityEngine.Mathf.Abs(restored - anchor) < 0.001f,
                        "Center line preserved at line height " + newLineHeight);
                }
            });
            Add(tests, "ZoomKeepsFractionalLine", "A center between lines stays in place instead of snapping to a line", t =>
            {
                const float viewportHeight = 240f;
                float centerLine = 24.75f;
                float offset = CodeEditorView.CalculateZoomAnchoredScrollY(
                    centerLine, 0f, viewportHeight, 5f, 31f, 2000f);
                float newAnchor = CodeEditorView.CalculateZoomAnchorLine(
                    offset, 0f, viewportHeight, 5f, 31f);
                t.Check(UnityEngine.Mathf.Abs(centerLine - newAnchor) < 0.001f,
                    "Fractional line preserved");
            });
            Add(tests, "ZoomViewportReflow", "Center anchor survives changes to the viewport bounds and text origin", t =>
            {
                float anchor = CodeEditorView.CalculateZoomAnchorLine(
                    392f, 4f, 300f, 8f, 20f);
                float zoomedScroll = CodeEditorView.CalculateZoomAnchoredScrollY(
                    anchor, 11f, 260f, 15f, 28f, 2800f);
                float observed = CodeEditorView.CalculateZoomAnchorLine(
                    zoomedScroll, 11f, 260f, 15f, 28f);
                t.Check(UnityEngine.Mathf.Abs(anchor - observed) < 0.001f,
                    "Same center source line after viewport reflow");
            });
            Add(tests, "ZoomClampsToDocumentBounds", "Anchor cannot scroll past the first or last line", t =>
            {
                float beforeStart = CodeEditorView.CalculateZoomAnchoredScrollY(
                    -10f, 0f, 300f, 4f, 18f, 1000f);
                float pastEnd = CodeEditorView.CalculateZoomAnchoredScrollY(
                    100f, 0f, 300f, 4f, 18f, 1000f);
                float shortDocument = CodeEditorView.CalculateZoomAnchoredScrollY(
                    5f, 0f, 300f, 4f, 18f, 200f);
                t.Equal(0f, beforeStart, "No negative scroll offset");
                t.Equal(700f, pastEnd, "No scrolling beyond last document pixel");
                t.Equal(0f, shortDocument, "Short documents always begin at the top");
            });
            Add(tests, "ZoomBurstAnchorsOriginalLine", "Multiple font-size changes use the original logical anchor", t =>
            {
                const float viewportHeight = 280f;
                float original = CodeEditorView.CalculateZoomAnchorLine(
                    350f, 0f, viewportHeight, 4f, 18f);
                float lastOffset = 0f;
                foreach (float height in new[] { 19f, 20f, 21f, 22f })
                {
                    lastOffset = CodeEditorView.CalculateZoomAnchoredScrollY(
                        original, 0f, viewportHeight, 4f, height, 2500f);
                }
                float finalLine = CodeEditorView.CalculateZoomAnchorLine(
                    lastOffset, 0f, viewportHeight, 4f, 22f);
                t.Check(UnityEngine.Mathf.Abs(original - finalLine) < 0.001f,
                    "Burst zoom retains original center source line");
            });
            Add(tests, "NoChangeToCompilerTokens", "Syntax highlighting never rewrites a DScript source buffer", t =>
            {
                string source = "x = 'hi & <tag>' # real comment\n";
                var doc = Inspect(t, source);
                t.Equal(source, doc.Source, "Original characters remain intact");
                t.Check(doc.Spans.Count > 0, "Highlight metadata available");
            });
        }

        // Test-only TextCore markup projection. Each formatting tag is generated by
        // FormatLine itself; arbitrary source '<' is protected separately.
        // It does not replace visual verification in Unity Play Mode.
        private static string SourceGlyphs(string richText) =>
            Regex.Replace(richText,
                @"<color=#[0-9A-Fa-f]{8}>|</color>|<noparse>|</noparse>",
                string.Empty);

        private static void Add(List<DirectivaTestCase> tests, string id, string description,
            Action<DirectivaTestContext> action) =>
            DScriptTestHarness.Add(tests, "SyntaxHighlight", id, description, action);

        private static DScriptSyntaxDocument Inspect(DirectivaTestContext t, string source)
        {
            var doc = DScriptSyntaxHighlighter.Analyze(source);
            t.Attach("Original DScript", source);
            return doc;
        }

        private static void Expect(DirectivaTestContext t, DScriptSyntaxDocument document,
            string source, string fragment, DScriptSyntaxKind expected, int from = 0)
        {
            int at = source.IndexOf(fragment, Math.Max(0, from), StringComparison.Ordinal);
            t.Check(at >= 0, "Missing test fragment: " + fragment);
            t.Equal(expected, document.KindAt(at), "Syntax category at " + fragment + " (position " + at + ")");
        }
    }
}
