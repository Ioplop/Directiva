using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Directiva.Progress
{
    /// <summary>
    /// Versioned binary save: magic (UInt32), version (Byte), count (Int32),
    /// then sorted (levelNumber: Int32, earnedObjectiveBits: Byte) entries.
    /// This is intentionally not BinaryFormatter or a serialization of Unity objects.
    /// </summary>
    public sealed class LevelProgressStore
    {
        private const uint Magic = 0x31565344; // ASCII "DSV1" (little endian)
        private const byte Version = 1;
        private const int MaxEntries = 4096;

        private enum ReadResult { Missing, Valid, Invalid, FutureVersion, IOError }

        private Dictionary<int, LevelStarFlags> stars = new Dictionary<int, LevelStarFlags>();
        private bool readOnly;

        public string FilePath { get; }
        public bool IsReadOnly => readOnly;

        public LevelProgressStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Save path cannot be empty.", nameof(filePath));
            FilePath = filePath;
            Load();
        }

        public LevelStarFlags GetStars(int levelNumber) =>
            stars.TryGetValue(levelNumber, out LevelStarFlags value) ? value : LevelStarFlags.None;

        /// <summary>Atomically merge newly completed objectives; never remove old stars.</summary>
        /// <returns>False when the result could not be persisted. The in-memory score is unchanged.</returns>
        public bool TryAwardStars(int levelNumber, LevelStarFlags achieved, out LevelStarFlags newlyEarned)
        {
            if (levelNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(levelNumber));
            newlyEarned = LevelStarFlags.None;
            achieved &= LevelStarFlags.All;
            if (achieved == LevelStarFlags.None) return true;
            if (readOnly)
            {
                Debug.LogWarning("[Directiva Save] Save data is read-only; progress was not changed.");
                return false;
            }

            LevelStarFlags previous = GetStars(levelNumber);
            LevelStarFlags combined = previous | achieved;
            if (combined == previous) return true;

            var updated = new Dictionary<int, LevelStarFlags>(stars) { [levelNumber] = combined };
            if (!TryWrite(updated)) return false;
            stars = updated;
            newlyEarned = combined & ~previous;
            return true;
        }

        private void Load()
        {
            ReadResult primary = TryRead(FilePath, out Dictionary<int, LevelStarFlags> loaded);
            if (primary == ReadResult.Valid)
            {
                stars = loaded;
                return;
            }
            if (primary == ReadResult.FutureVersion || primary == ReadResult.IOError)
            {
                // Never clobber a newer version or a file that could not be opened.
                readOnly = true;
                Debug.LogWarning("[Directiva Save] Cannot safely read " + FilePath +
                    "; not overwriting it.");
                return;
            }

            ReadResult backup = TryRead(FilePath + ".bak", out loaded);
            if (backup == ReadResult.Valid)
            {
                stars = loaded;
                Debug.LogWarning("[Directiva Save] Recovered level progress from backup.");
            }
            else if (backup == ReadResult.FutureVersion || backup == ReadResult.IOError)
            {
                readOnly = true;
                Debug.LogWarning("[Directiva Save] Backup could not be safely read; refusing to overwrite it.");
            }
            else if (primary == ReadResult.Invalid || backup == ReadResult.Invalid)
            {
                // A damaged save is preserved with a .corrupt-* suffix on the next write.
                Debug.LogWarning("[Directiva Save] No valid progress data found. " +
                    "Starting empty; damaged files will be preserved on save.");
            }
        }

        private static ReadResult TryRead(string path, out Dictionary<int, LevelStarFlags> result)
        {
            result = null;
            try
            {
                if (!File.Exists(path)) return ReadResult.Missing;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (stream.Length < 9 || reader.ReadUInt32() != Magic)
                        return ReadResult.Invalid;
                    byte version = reader.ReadByte();
                    if (version > Version) return ReadResult.FutureVersion;
                    if (version != Version) return ReadResult.Invalid;
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaxEntries || stream.Length - stream.Position != (long)count * 5)
                        return ReadResult.Invalid;
                    var entries = new Dictionary<int, LevelStarFlags>();
                    for (int i = 0; i < count; i++)
                    {
                        int id = reader.ReadInt32();
                        byte flags = reader.ReadByte();
                        if (id <= 0 || (flags & ~(byte)LevelStarFlags.All) != 0 ||
                            entries.ContainsKey(id)) return ReadResult.Invalid;
                        entries.Add(id, (LevelStarFlags)flags);
                    }
                    result = entries;
                    return ReadResult.Valid;
                }
            }
            catch (EndOfStreamException) { return ReadResult.Invalid; }
            catch (IOException exception)
            {
                Debug.LogWarning("[Directiva Save] Read failed: " + exception.Message);
                return ReadResult.IOError;
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogWarning("[Directiva Save] Read failed: " + exception.Message);
                return ReadResult.IOError;
            }
        }

        private bool TryWrite(Dictionary<int, LevelStarFlags> updated)
        {
            if (updated.Count > MaxEntries)
            {
                Debug.LogError("[Directiva Save] Too many levels for this save format.");
                return false;
            }
            string temporary = FilePath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(updated.Count);
                    var ids = new List<int>(updated.Keys);
                    ids.Sort();
                    foreach (int levelNumber in ids)
                    {
                        writer.Write(levelNumber);
                        writer.Write((byte)updated[levelNumber]);
                    }
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(FilePath))
                {
                    // Never overwrite the last healthy backup with a broken primary.
                    ReadResult original = TryRead(FilePath, out _);
                    if (original == ReadResult.Valid)
                        File.Copy(FilePath, FilePath + ".bak", true);
                    else if (original == ReadResult.Invalid)
                        File.Copy(FilePath, FilePath + ".corrupt-" +
                            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"), false);
                    else
                    {
                        Debug.LogError("[Directiva Save] The existing save is no longer readable; " +
                            "refusing to replace it.");
                        return false;
                    }

                    try { File.Replace(temporary, FilePath, null); }
                    catch (PlatformNotSupportedException) { File.Copy(temporary, FilePath, true); }
                    catch (IOException) { File.Copy(temporary, FilePath, true); }
                }
                else
                {
                    File.Move(temporary, FilePath);
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException ||
                exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogError("[Directiva Save] Could not save progress: " + exception.Message);
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { /* Best effort. */ }
                catch (UnauthorizedAccessException) { /* Best effort. */ }
            }
        }
    }
}
