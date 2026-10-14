#if UNITY_EDITOR
using Directiva.CodeUI;
using Directiva.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Directiva.SimulationUI.Editor
{
    /// <summary>Creates an editable mock game scene while preserving every existing scene.</summary>
    public static class SimulationSceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string MaterialsFolder = "Assets/Materials";

        [MenuItem("Directiva/Simulation/Create Game Scene")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorUtility.DisplayDialog("Directiva", "Game.unity ya existe. No se ha sobrescrito nada.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/CodeEditor.asset");
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Directiva", "Falta Assets/UI/CodeEditor.asset (PanelSettings).", "OK");
                return;
            }

            var regularFont = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(
                "Assets/Fonts/NaturalMono-Regular SDF.asset");
            var boldFont = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(
                "Assets/Fonts/NaturalMono-Bold SDF.asset");
            if (regularFont == null || boldFont == null)
            {
                EditorUtility.DisplayDialog("Directiva", "No se encontraron las fuentes NaturalMono SDF.", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateLight();
            CreateMockWorld(scene);

            var ui = new GameObject("Simulation UI");
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = panel;

            var editorObject = new GameObject("Code Editor (shared instance)");
            editorObject.transform.SetParent(ui.transform, false);
            var editor = editorObject.AddComponent<DirectivaCodeUIController>();
            var editorSerialized = new SerializedObject(editor);
            editorSerialized.FindProperty("normalFont").objectReferenceValue = regularFont;
            editorSerialized.FindProperty("boldFont").objectReferenceValue = boldFont;
            editorSerialized.ApplyModifiedPropertiesWithoutUndo();

            var simUI = ui.AddComponent<SimulationUIController>();
            var serialized = new SerializedObject(simUI);
            serialized.FindProperty("codeEditor").objectReferenceValue = editor;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var bootstrapObject = new GameObject("Level Bootstrapper (launch events)");
            var bootstrap = bootstrapObject.AddComponent<LevelBootstrapper>();
            var bootSerialized = new SerializedObject(bootstrap);
            var events = bootSerialized.FindProperty("launchEvents");
            events.arraySize = 6;
            for (int i = 0; i < 6; i++)
                events.GetArrayElementAtIndex(i).FindPropertyRelative("launchId").stringValue =
                    "level_" + (i + 1).ToString("00");
            bootSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = ui;
            Debug.Log("[Directiva] Game scene created at " + ScenePath +
                ". Add it to Build Profiles and configure each level's sceneName as Game.");
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 14f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.085f, 0.11f, 1f);
            cameraObject.transform.position = new Vector3(0, 21, -20);
            cameraObject.transform.LookAt(new Vector3(0, 0, 0));
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateMockWorld(Scene scene)
        {
            var world = new GameObject("Simulation Mockup (replace later)");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(world.transform, false);
            floor.transform.localScale = new Vector3(4.4f, 1, 3.4f);
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/MainMenu_Floor.mat");
            if (floorMat != null) floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            var blueNexus = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/MainMenu_Nexus.mat");
            var blueUnit = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/MainMenu_Unit.mat");
            var resourceMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/MainMenu_Resource.mat");
            var redMat = GetOrCreateRedMaterial();

            var blueGroup = new GameObject("Blue Team (visual only)");
            blueGroup.transform.SetParent(world.transform);
            var redGroup = new GameObject("Red Team (visual only)");
            redGroup.transform.SetParent(world.transform);
            var resources = new GameObject("Resources (visual only)");
            resources.transform.SetParent(world.transform);

            const string baseModel = "Assets/Models/square_pyramid.obj";
            const string unitModel = "Assets/Models/triangular_isosceles_prism.obj";
            const string resourceModel = "Assets/Models/regular_octahedron.obj";

            CreateModel(baseModel, blueGroup.transform, "Nexo azul", new Vector3(-9, 0, 5),
                new Vector3(3.1f, 2.6f, 3.1f), 0, blueNexus, scene);
            CreateModel(baseModel, redGroup.transform, "Nexo rojo", new Vector3(9, 0, -5),
                new Vector3(3.1f, 2.6f, 3.1f), 0, redMat, scene);

            Vector3[] blueUnits = {
                new Vector3(-5, 0, 2), new Vector3(-7, 0, -1),
                new Vector3(-3, 0, -4), new Vector3(-9, 0, -3),
                new Vector3(-3, 0, 5)
            };
            Vector3[] redUnits = {
                new Vector3(4, 0, 2), new Vector3(7, 0, 1),
                new Vector3(5, 0, -3), new Vector3(10, 0, 3),
                new Vector3(3, 0, -6)
            };
            Vector3[] crystals = {
                new Vector3(-4, 0, 0), new Vector3(-1, 0, -5),
                new Vector3(0, 0, 0), new Vector3(2, 0, 4),
                new Vector3(1, 0, 7), new Vector3(6, 0, 5),
                new Vector3(-8, 0, 8), new Vector3(7, 0, -7)
            };

            for (int i = 0; i < blueUnits.Length; i++)
                CreateModel(unitModel, blueGroup.transform, "Agente azul " + (i + 1),
                    blueUnits[i], new Vector3(1.3f, 1.0f, 1.7f), 75, blueUnit, scene);
            for (int i = 0; i < redUnits.Length; i++)
                CreateModel(unitModel, redGroup.transform, "Agente rojo " + (i + 1),
                    redUnits[i], new Vector3(1.3f, 1.0f, 1.7f), -75, redMat, scene);
            for (int i = 0; i < crystals.Length; i++)
                CreateModel(resourceModel, resources.transform, "Recurso " + (i + 1),
                    crystals[i] + Vector3.up * 0.72f, Vector3.one * 0.7f, 0, resourceMat, scene);
        }

        private static Material GetOrCreateRedMaterial()
        {
            string path = MaterialsFolder + "/Simulation_MVP_Red.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            material = new Material(shader) { name = "Simulation_MVP_Red" };
            Color red = new Color(0.91f, 0.2f, 0.28f, 1);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", red);
            if (material.HasProperty("_Color")) material.SetColor("_Color", red);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject CreateModel(string assetPath, Transform parent, string name,
            Vector3 localPosition, Vector3 localScale, float yRotation, Material material, Scene scene)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                Debug.LogWarning("[Directiva] Missing mockup model: " + assetPath);
                return null;
            }
            var instance = PrefabUtility.InstantiatePrefab(source, scene) as GameObject;
            if (instance == null) return null;
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localScale = localScale;
            instance.transform.localRotation = Quaternion.Euler(0, yRotation, 0);
            if (material != null)
            {
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = material;
            }
            return instance;
        }
    }
}
#endif
