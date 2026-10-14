using Directiva.MainMenu;
using UnityEditor;
using UnityEngine;

namespace Directiva.UI.Editor
{
    /// <summary>Convenience access and one-time migration for existing menu-star sprites.</summary>
    public static class DirectivaUIThemeMenu
    {
        private const string ThemeAssetPath = "Assets/Resources/UI/DirectivaUITheme.asset";

        [MenuItem("Directiva/UI/Select Shared Theme")]
        public static void SelectTheme()
        {
            var asset = AssetDatabase.LoadAssetAtPath<DirectivaUITheme>(ThemeAssetPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Directiva", "No se encontró " + ThemeAssetPath, "OK");
                return;
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Directiva/UI/Copy Existing Level Sprites To Theme")]
        public static void CopyLevelSprites()
        {
            var asset = AssetDatabase.LoadAssetAtPath<DirectivaUITheme>(ThemeAssetPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Directiva", "Falta " + ThemeAssetPath, "OK");
                return;
            }

            var menus = Object.FindObjectsByType<MainMenuController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int copied = 0;
            Undo.RecordObject(asset, "Copy Directiva level icons");
            foreach (var menu in menus)
            {
                var serialized = new SerializedObject(menu);
                copied += CopyIfEmpty(serialized, "earnedStarSprite", ref asset.icons.earnedStar);
                copied += CopyIfEmpty(serialized, "unearnedStarSprite", ref asset.icons.unearnedStar);
                copied += CopyIfEmpty(serialized, "requiredStarsSprite", ref asset.icons.requiredStar);
                copied += CopyIfEmpty(serialized, "lockedLevelSprite", ref asset.icons.lockedLevel);
            }
            if (copied > 0)
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = asset;
            EditorUtility.DisplayDialog("Directiva",
                $"Se copiaron {copied} sprites desde el menú abierto. Se conservaron las referencias antiguas como respaldo.",
                "OK");
        }

        private static int CopyIfEmpty(SerializedObject from, string propertyName, ref Sprite destination)
        {
            if (destination != null) return 0;
            var property = from.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue is not Sprite sprite) return 0;
            destination = sprite;
            return 1;
        }
    }
}
