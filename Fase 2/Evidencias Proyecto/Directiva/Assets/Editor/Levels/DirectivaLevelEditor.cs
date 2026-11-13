using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Directiva.Levels;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Parsing;
using UnityEditor;
using UnityEngine;

namespace Directiva.Editor.Levels
{
    /// <summary>
    /// Asset-only UI: folder -> script -> function, discovered from the actual
    /// .dscript files. Editor types never appear in DirectivaLevel/player builds.
    /// </summary>
    [CustomEditor(typeof(DirectivaLevel))]
    public sealed class DirectivaLevelEditor : UnityEditor.Editor
    {
        private const string RootPath = "Assets/Resources/Levels";
        private SerializedProperty levelName;
        private SerializedProperty levelNumber;
        private SerializedProperty requiredStars;
        private SerializedProperty maxMatchDurationSeconds;
        private SerializedProperty fastVictorySeconds;
        private SerializedProperty scenePath;
        private SerializedProperty codeFolder;
        private SerializedProperty entryModule;
        private SerializedProperty entryFunction;
        private SerializedProperty worldLayout;
        private SerializedProperty simulationSettings;

        private struct ScriptChoice
        {
            public string Module;
            public string Label;
            public string Path;
        }

        private void OnEnable()
        {
            levelName = serializedObject.FindProperty("levelName");
            levelNumber = serializedObject.FindProperty("levelNumber");
            requiredStars = serializedObject.FindProperty("requiredStars");
            maxMatchDurationSeconds = serializedObject.FindProperty("maxMatchDurationSeconds");
            fastVictorySeconds = serializedObject.FindProperty("fastVictorySeconds");
            scenePath = serializedObject.FindProperty("scenePath");
            codeFolder = serializedObject.FindProperty("codeFolder");
            entryModule = serializedObject.FindProperty("entryModule");
            entryFunction = serializedObject.FindProperty("entryFunction");
            worldLayout = serializedObject.FindProperty("worldLayout");
            simulationSettings = serializedObject.FindProperty("simulationSettings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Nivel", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(levelName, new GUIContent("Nombre"));
            EditorGUILayout.PropertyField(levelNumber, new GUIContent("Número de nivel"));
            if (levelNumber.intValue <= 0)
                EditorGUILayout.HelpBox("El número de nivel debe ser positivo para guardar estrellas.",
                    MessageType.Error);
            EditorGUILayout.PropertyField(requiredStars, new GUIContent("Estrellas requeridas"));
            if (requiredStars.intValue < 0) requiredStars.intValue = 0;
            DrawScenePicker();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Objetivos de la partida", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(maxMatchDurationSeconds,
                new GUIContent("Duración máxima (segundos)"));
            EditorGUILayout.PropertyField(fastVictorySeconds,
                new GUIContent("Estrella rápida: menos de (segundos)"));
            maxMatchDurationSeconds.intValue = Mathf.Max(1, maxMatchDurationSeconds.intValue);
            fastVictorySeconds.intValue = Mathf.Max(1, fastVictorySeconds.intValue);
            if (fastVictorySeconds.intValue >= maxMatchDurationSeconds.intValue)
                EditorGUILayout.HelpBox(
                    "El tiempo para la estrella rápida debe ser menor que el límite de la partida " +
                    "para que el objetivo sea distinto de ganar.", MessageType.Warning);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Programa del enemigo", EditorStyles.boldLabel);
            DrawFolderPicker();
            DrawScriptPicker();
            DrawFunctionPicker();
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Cada nivel compila únicamente los .dscript de su carpeta dentro de " +
                "Resources/Levels. El programa del jugador se compila por separado.",
                MessageType.Info);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Diseño inicial y reglas", EditorStyles.boldLabel);
            if (GUILayout.Button("Abrir editor visual del nivel", GUILayout.Height(32)))
            {
                serializedObject.ApplyModifiedProperties();
                DirectivaLevelLayoutWindow.Open((DirectivaLevel)target);
            }
            EditorGUILayout.HelpBox(
                "El layout, los depósitos y los parámetros pertenecen al DirectivaLevel. " +
                "La escena solo conserva prefabs, cámara y presentación visual.", MessageType.Info);
            if (worldLayout != null)
                EditorGUILayout.PropertyField(worldLayout,
                    new GUIContent("Mapa / Layout (valores)"), includeChildren: true);
            if (simulationSettings != null)
                EditorGUILayout.PropertyField(simulationSettings,
                    new GUIContent("Parámetros de simulación"), includeChildren: true);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScenePicker()
        {
            string path = scenePath.stringValue;
            SceneAsset current = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            EditorGUI.BeginChangeCheck();
            var next = (SceneAsset)EditorGUILayout.ObjectField("Escena", current,
                typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck())
                scenePath.stringValue = next != null ? AssetDatabase.GetAssetPath(next) : "";

            path = scenePath.stringValue;
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                EditorGUILayout.HelpBox("Selecciona una escena válida.", MessageType.Warning);
            else if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == path))
                EditorGUILayout.HelpBox(
                    "La escena debe estar incluida y habilitada en Build Profiles > Scenes in Build.",
                    MessageType.Warning);
        }

        private void DrawFolderPicker()
        {
            if (!AssetDatabase.IsValidFolder(RootPath))
            {
                EditorGUILayout.HelpBox("Crea la carpeta Assets/Resources/Levels.", MessageType.Warning);
                return;
            }
            string[] folders = AssetDatabase.GetSubFolders(RootPath)
                .Select(path => path.Substring(RootPath.Length + 1))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            string[] options = new[] { "(Seleccionar carpeta)" }.Concat(folders).ToArray();
            int current = Array.IndexOf(folders, codeFolder.stringValue) + 1;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Carpeta", current, options);
            if (EditorGUI.EndChangeCheck())
            {
                codeFolder.stringValue = selected > 0 ? folders[selected - 1] : "";
                entryModule.stringValue = "";
                entryFunction.stringValue = "";
            }
            if (!string.IsNullOrEmpty(codeFolder.stringValue) && current == 0 &&
                !folders.Contains(codeFolder.stringValue))
                EditorGUILayout.HelpBox("La carpeta seleccionada ya no existe.", MessageType.Warning);
        }

        private List<ScriptChoice> GetScripts()
        {
            string root = RootPath + "/" + codeFolder.stringValue;
            var scripts = new List<ScriptChoice>();
            if (string.IsNullOrEmpty(codeFolder.stringValue) || !AssetDatabase.IsValidFolder(root))
                return scripts;
            string absoluteRoot = Path.Combine(Application.dataPath,
                "Resources", "Levels", codeFolder.stringValue);
            if (!Directory.Exists(absoluteRoot)) return scripts;
            foreach (string file in Directory.GetFiles(absoluteRoot, "*.dscript",
                SearchOption.AllDirectories))
            {
                string relative = file.Substring(absoluteRoot.Length + 1).Replace('\\', '/');
                string withoutExtension = relative.Substring(0,
                    relative.Length - ".dscript".Length);
                string[] segments = withoutExtension.Split('/');
                // Reject filenames such as "foo.bar.dscript": that would otherwise
                // resolve to the different path "foo/bar.dscript" at runtime.
                if (segments.Any(segment => !DScriptModuleLoader.IsValidName(segment)))
                    continue;
                scripts.Add(new ScriptChoice
                {
                    Module = string.Join(".", segments), Label = relative, Path = file
                });
            }
            scripts.Sort((a, b) => string.Compare(a.Label, b.Label,
                StringComparison.CurrentCultureIgnoreCase));
            return scripts;
        }

        private void DrawScriptPicker()
        {
            List<ScriptChoice> scripts = GetScripts();
            string[] options = new[] { "(Seleccionar .dscript)" }
                .Concat(scripts.Select(script => script.Label)).ToArray();
            int current = scripts.FindIndex(script => script.Module == entryModule.stringValue) + 1;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Script de entrada", current, options);
            if (EditorGUI.EndChangeCheck())
            {
                entryModule.stringValue = selected > 0 ? scripts[selected - 1].Module : "";
                entryFunction.stringValue = "";
            }
            if (scripts.Count == 0 && !string.IsNullOrEmpty(codeFolder.stringValue))
                EditorGUILayout.HelpBox("Esta carpeta no contiene archivos .dscript válidos.",
                    MessageType.Warning);
            if (!string.IsNullOrEmpty(entryModule.stringValue) && current == 0)
                EditorGUILayout.HelpBox("El script seleccionado ya no existe.", MessageType.Warning);
        }

        private void DrawFunctionPicker()
        {
            List<ScriptChoice> scripts = GetScripts();
            ScriptChoice selectedScript = scripts.FirstOrDefault(script =>
                script.Module == entryModule.stringValue);
            var functions = new List<FunctionStatement>();
            if (selectedScript.Path != null)
            {
                try
                {
                    string source = File.ReadAllText(selectedScript.Path);
                    var lexer = new DScriptLexer(selectedScript.Module, source);
                    var tokens = lexer.Tokenize();
                    var parser = new DScriptParser(selectedScript.Module, tokens);
                    ModuleNode ast = parser.Parse();
                    functions.AddRange(ast.Statements.OfType<FunctionStatement>());
                    if (lexer.Diagnostics.Count != 0 || parser.Diagnostics.Count != 0)
                        EditorGUILayout.HelpBox("El script contiene errores sintácticos. " +
                            "Las funciones listadas pueden estar incompletas.", MessageType.Warning);
                }
                catch (Exception ex)
                {
                    EditorGUILayout.HelpBox("No se pudo leer el script: " + ex.Message,
                        MessageType.Error);
                }
            }

            string[] options = new[] { "(Seleccionar función)" }.Concat(
                functions.Select(f => f.Name + "(" + string.Join(", ", f.Parameters) + ")"))
                .ToArray();
            int current = functions.FindIndex(f => f.Name == entryFunction.stringValue) + 1;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Función de entrada", current, options);
            if (EditorGUI.EndChangeCheck())
                entryFunction.stringValue = selected > 0 ? functions[selected - 1].Name : "";

            if (functions.Count == 0 && selectedScript.Path != null)
                EditorGUILayout.HelpBox("No se encontraron funciones 'def' en este script.",
                    MessageType.Warning);
            if (selected > 0 && functions[selected - 1].Parameters.Count != 0)
                EditorGUILayout.HelpBox("La función inicial del nexo debe tener cero parámetros.",
                    MessageType.Warning);
        }
    }
}
