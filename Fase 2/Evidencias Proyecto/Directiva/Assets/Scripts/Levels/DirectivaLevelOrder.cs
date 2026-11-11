using System;
using System.Collections.Generic;

namespace Directiva.Levels
{
    /// <summary>Level display order: number, unlock cost, name, original list index.</summary>
    public static class DirectivaLevelOrder
    {
        private struct IndexedLevel
        {
            public DirectivaLevel Level;
            public int Index;
        }

        public static List<DirectivaLevel> Sort(IReadOnlyList<DirectivaLevel> input)
        {
            var entries = new List<IndexedLevel>();
            if (input != null)
                for (int i = 0; i < input.Count; i++)
                    if (input[i] != null)
                        entries.Add(new IndexedLevel { Level = input[i], Index = i });

            entries.Sort((a, b) =>
            {
                int comparison = a.Level.LevelNumber.CompareTo(b.Level.LevelNumber);
                if (comparison != 0) return comparison;
                comparison = a.Level.RequiredStars.CompareTo(b.Level.RequiredStars);
                if (comparison != 0) return comparison;
                comparison = string.Compare(a.Level.LevelName, b.Level.LevelName,
                    StringComparison.CurrentCultureIgnoreCase);
                return comparison != 0 ? comparison : a.Index.CompareTo(b.Index);
            });

            var result = new List<DirectivaLevel>(entries.Count);
            foreach (IndexedLevel entry in entries) result.Add(entry.Level);
            return result;
        }
    }
}
