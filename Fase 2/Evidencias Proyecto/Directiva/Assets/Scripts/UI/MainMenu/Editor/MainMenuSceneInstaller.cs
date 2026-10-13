#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Directiva.MainMenu.Editor
{
    /// <summary>Creates a ready-to-run MainMenu scene without modifying existing scenes.</summary>
    public static class MainMenuSceneInstaller
    {
        [MenuItem("Directiva/Main Menu/Create MainMenu Scene")]
        public static void Create()
        {
            const string scenePath = "Assets/Scenes/MainMenu.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
            {
                EditorUtility.DisplayDialog("Directiva", "MainMenu.unity already exists. Nothing was overwritten.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Main Menu UI");
            var document = go.AddComponent<UIDocument>();
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/CodeEditor.asset");
            if (panel == null)
            {
                Debug.LogError("[Directiva] Cannot find Assets/UI/CodeEditor.asset (PanelSettings).");
                Object.DestroyImmediate(go);
                return;
            }
            document.panelSettings = panel;
            var controller = go.AddComponent<MainMenuController>();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/Styles/MainMenu/MainMenu.uss");
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("styleSheet").objectReferenceValue = style;
            var levels = serialized.FindProperty("levels");
            levels.arraySize = 6;
            for (int i = 0; i < 6; i++)
            {
                var element = levels.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = "Nivel " + (i + 1);
                element.FindPropertyRelative("stars").intValue = i < 2 ? 3 : (i == 2 ? 2 : 0);
                element.FindPropertyRelative("locked").boolValue = i >= 3;
                element.FindPropertyRelative("sceneName").stringValue = "BasicCoding";
                element.FindPropertyRelative("launchId").stringValue = "level_" + (i + 1).ToString("00");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeGameObject = go;
            Debug.Log("[Directiva] Main menu created at " + scenePath + ". Add the scene and its destinations to Build Profiles before playing levels.");
        }
    }
}
#endif
