using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Sidecar persisted next to a script as <c>file.dil.editor</c>.
    /// Contains editor-only state and never participates in DIL parsing/execution.
    /// </summary>
    [Serializable]
    internal sealed class EditorDocumentMetadata
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string entryFunction = "main";
        public EditorUndoHistoryData undoHistory = new EditorUndoHistoryData();
    }

    [Serializable]
    internal sealed class EditorUndoHistoryData
    {
        public int index;
        public List<EditorUndoSnapshotData> states = new List<EditorUndoSnapshotData>();
    }

    [Serializable]
    internal sealed class EditorUndoSnapshotData
    {
        public string text = string.Empty;
        public int cursor;
        public int select;
    }

    /// <summary>
    /// Reads and writes editor metadata without coupling the script parser or VM to editor state.
    /// </summary>
    internal sealed class EditorDocumentMetadataStore
    {
        private readonly string _rootPath;

        public EditorDocumentMetadataStore(string rootPath)
        {
            _rootPath = Path.GetFullPath(rootPath ?? throw new ArgumentNullException(nameof(rootPath)));
        }

        public EditorDocumentMetadata Load(string relativeScriptPath)
        {
            var path = GetMetadataPath(relativeScriptPath);
            if (!File.Exists(path))
                return new EditorDocumentMetadata();

            try
            {
                var json = File.ReadAllText(path);
                var metadata = JsonUtility.FromJson<EditorDocumentMetadata>(json);

                if (metadata == null || metadata.version != EditorDocumentMetadata.CurrentVersion)
                    return new EditorDocumentMetadata();

                metadata.entryFunction ??= "main";
                metadata.undoHistory ??= new EditorUndoHistoryData();
                metadata.undoHistory.states ??= new List<EditorUndoSnapshotData>();
                return metadata;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not read editor metadata '{path}': {ex.Message}");
                return new EditorDocumentMetadata();
            }
        }

        public void Save(string relativeScriptPath, EditorDocumentMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            var path = GetMetadataPath(relativeScriptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            metadata.version = EditorDocumentMetadata.CurrentVersion;
            metadata.entryFunction ??= "main";
            metadata.undoHistory ??= new EditorUndoHistoryData();
            metadata.undoHistory.states ??= new List<EditorUndoSnapshotData>();

            File.WriteAllText(path, JsonUtility.ToJson(metadata, true));
        }

        private string GetMetadataPath(string relativeScriptPath)
        {
            var normalized = (relativeScriptPath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            var scriptPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
            var relative = Path.GetRelativePath(_rootPath, scriptPath);

            if (Path.IsPathRooted(relative) ||
                relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Editor metadata path is outside the scripts root.");
            }

            if (!scriptPath.EndsWith(FileSystemScriptStorage.ScriptExtension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Invalid script path for editor metadata: {relativeScriptPath}");

            return scriptPath + FileSystemScriptStorage.EditorSuffix;
        }
    }
}
