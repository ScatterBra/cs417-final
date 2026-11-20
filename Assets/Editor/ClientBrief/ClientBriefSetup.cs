using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates the ClientEvaluationRoot prefab, places it in the shared scene,
/// and can render a preview screenshot. Menu: UIKEA/Client Brief/...</summary>
public static class ClientBriefSetup
{
    const string PrefabPath = "Assets/Prefabs/ClientEvaluationRoot.prefab";
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("UIKEA/Client Brief/Create Prefab And Add To Scene")]
    public static void CreatePrefabAndAddToScene()
    {
        Directory.CreateDirectory("Assets/Prefabs");
        var root = new GameObject("ClientEvaluationRoot");
        root.AddComponent<ClientBriefPanel>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == "ClientEvaluationRoot") Object.DestroyImmediate(go);
        PrefabUtility.InstantiatePrefab(prefab, scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ClientBrief] Prefab created and added to " + ScenePath);
    }

    [MenuItem("UIKEA/Client Brief/Render Preview PNG")]
    public static void RenderPreview()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("PreviewCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.62f, 0.68f);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var panel = inst.GetComponent<ClientBriefPanel>();
        panel.renderMode = RenderMode.ScreenSpaceCamera;
        panel.Build();
        panel.Canvas.worldCamera = cam;
        panel.Canvas.planeDistance = 1;
        panel.SetRequirementMet(0, true);

        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        var outPath = System.Environment.GetEnvironmentVariable("CLIENT_BRIEF_PNG") ?? "ClientBriefPreview.png";
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        RenderTexture.active = null;
        Debug.Log("[ClientBrief] Preview written to " + outPath);
    }

    public static void BatchAll()
    {
        CreatePrefabAndAddToScene();
        RenderPreview();
    }
}
