using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates Zonglin Wu's catalog prefab and connects it to the shared scene.</summary>
public static class InteriorDecorationSetup
{
    public const string PrefabPath = "Assets/Prefabs/InteriorDecorationRoot.prefab";
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("UIKEA/Interior Decoration/Create Prefab and Add to Shared Scene")]
    public static void SetupSharedScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Directory.CreateDirectory("Assets/Prefabs");
            var root = new GameObject("InteriorDecorationRoot", typeof(RectTransform));
            root.AddComponent<InteriorDecorationCatalog>().CreateLayout();
            prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();
        var catalog = roots.SelectMany(root => root.GetComponentsInChildren<InteriorDecorationCatalog>(true)).FirstOrDefault();
        if (catalog == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add interior decoration prefab");
            catalog = instance.GetComponent<InteriorDecorationCatalog>();
        }

        var ui = roots.SelectMany(root => root.GetComponentsInChildren<GameUI>(true)).FirstOrDefault();
        if (ui != null)
        {
            var serializedUI = new SerializedObject(ui);
            serializedUI.FindProperty("furnitureCatalog").objectReferenceValue = catalog;
            serializedUI.ApplyModifiedProperties();
            if (PrefabUtility.IsPartOfPrefabInstance(ui)) PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = catalog.gameObject;
        Debug.Log("InteriorDecorationRoot added to SampleScene and connected to GameUI.");
    }
}
