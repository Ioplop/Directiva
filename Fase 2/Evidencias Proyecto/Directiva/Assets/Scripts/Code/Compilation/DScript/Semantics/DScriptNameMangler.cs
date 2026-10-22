using System;
using System.Globalization;
using System.Text;

namespace DSCompilation.DScript.Semantics
{
    /// <summary>
    /// Injectively maps module-qualified globals to plain DIL identifiers.
    /// Current DILParser accepts '# GLOBAL : identifier', not qualified global names.
    /// Encoding every character (including '.' and '_') prevents module collisions.
    /// </summary>
    public static class DScriptNameMangler
    {
        public static string Global(string qualifiedName)
        {
            if (string.IsNullOrEmpty(qualifiedName))
                throw new ArgumentException("A global must have a qualified name.", nameof(qualifiedName));
            var result = new StringBuilder("__dsg_");
            foreach (char character in qualifiedName)
                result.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }
}
