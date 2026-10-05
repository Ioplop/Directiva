using System.Collections.Generic;

namespace Directiva.CodeUI
{
    public interface IScriptStorage
    {
        string RootPath { get; }

        void EnsureRoot();

        IEnumerable<string> EnumerateDirectories(string relativeFolder = "");
        IEnumerable<string> EnumerateScripts(string relativeFolder = "");

        bool DirectoryExists(string relativePath);
        bool ScriptExists(string relativeScriptPath);
        bool BackupExists(string relativeScriptPath);

        string ReadSaved(string relativeScriptPath);
        string ReadBackup(string relativeScriptPath);

        void WriteSaved(string relativeScriptPath, string content);
        void WriteBackup(string relativeScriptPath, string content);
        void DeleteBackup(string relativeScriptPath);

        string CreateScript(string relativeFolder, string baseName, string initialContent = "");
        string CreateFolder(string relativeFolder, string folderName);

        void DeleteScript(string relativeScriptPath);
        void DeleteFolder(string relativeFolderPath);

        string RenameScript(string relativeScriptPath, string newBaseName);
        string RenameFolder(string relativeFolderPath, string newName);

        string MoveScript(string relativeScriptPath, string targetFolder);
        string MoveFolder(string relativeFolderPath, string targetFolder);

        string DuplicateScript(string relativeScriptPath, string desiredBaseName, string contentToDuplicate);
        string DuplicateFolder(string relativeFolderPath, string desiredName);
    }
}
