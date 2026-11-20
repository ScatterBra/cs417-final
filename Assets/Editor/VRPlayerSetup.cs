using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

public static class VRPlayerSetup
{
    const string RigPath = "Assets/Prefabs/VR/PlayerRig.prefab";
    const string SimulatorPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/XR Device Simulator/XR Device Simulator.prefab";
    static string RequestPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../tmp/final-vr-setup.request"));

    [InitializeOnLoadMethod]
    static void CheckRequestedSetup()
    {
        if (File.Exists(RequestPath)) EditorApplication.delayCall += ApplyRequestedSetup;
    }

    static void ApplyRequestedSetup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += ApplyRequestedSetup;
            return;
        }
        if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(RequestPath);
        Apply();
    }

    [MenuItem("CS417/VR/Configure Player in SampleScene")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open SampleScene first.");
        var grid = UnityEngine.Object.FindFirstObjectByType<GridSystem>();
        grid.EnsureInit();
        PlayerSettings.runInBackground = true;
        var camera = UnityEngine.Object.FindFirstObjectByType<IsoCameraController>().GetComponent<Camera>();

        var contents = PrefabUtility.LoadPrefabContents(RigPath);
        var hud = contents.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "playerHud");
        if (hud != null) UnityEngine.Object.DestroyImmediate(hud.gameObject);
        contents.name = "playerRig";
        foreach (var controller in contents.GetComponentsInChildren<ControllerInputActionManager>(true))
        {
            var so = new SerializedObject(controller);
            so.FindProperty("m_SmoothMotionEnabled").boolValue = controller.name == "leftController";
            so.FindProperty("m_SmoothTurnEnabled").boolValue = controller.name == "rightController";
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var turn in contents.GetComponentsInChildren<SnapTurnProvider>(true)) turn.enabled = false;
        foreach (var turn in contents.GetComponentsInChildren<ContinuousTurnProvider>(true))
        {
            turn.enabled = turn is LinearTurnProvider;
            turn.turnSpeed = 60f;
        }
        foreach (var move in contents.GetComponentsInChildren<ContinuousMoveProvider>(true)) move.moveSpeed = 1.3f;
        foreach (var jump in contents.GetComponentsInChildren<JumpProvider>(true)) jump.enabled = false;
        foreach (var caster in contents.GetComponentsInChildren<SphereInteractionCaster>(true)) caster.physicsLayerMask = Physics.DefaultRaycastLayers;
        foreach (var caster in contents.GetComponentsInChildren<CurveInteractionCaster>(true)) caster.raycastMask = Physics.DefaultRaycastLayers;
        foreach (var ray in contents.GetComponentsInChildren<XRRayInteractor>(true))
        {
            ray.raycastMask = Physics.DefaultRaycastLayers;
            if (ray.name == "teleportInteractor") ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        }
        var headCamera = contents.GetComponentInChildren<Camera>(true);
        headCamera.tag = "MainCamera";
        headCamera.nearClipPlane = .05f;
        headCamera.orthographic = false;
        if (headCamera.GetComponent<UniversalAdditionalCameraData>() == null) headCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        PrefabUtility.SaveAsPrefabAsset(contents, RigPath);
        PrefabUtility.UnloadPrefabContents(contents);

        var mode = UnityEngine.Object.FindFirstObjectByType<VRPlayerMode>();
        if (mode == null) mode = new GameObject("vrPlayerMode").AddComponent<VRPlayerMode>();
        var rig = mode.PlayerRig;
        if (rig == null) rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath), scene);
        rig.name = "playerRig";
        rig.transform.SetPositionAndRotation(grid.transform.position + new Vector3(grid.width * grid.cellSize * .5f, .1f, -1f), Quaternion.identity);
        rig.SetActive(false);
        var simulator = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "vrDeviceSimulator");
        if (simulator == null) simulator = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPath), scene);
        simulator.name = "vrDeviceSimulator";
        simulator.SetActive(false);
        mode.Configure(rig, simulator, camera, UnityEngine.Object.FindFirstObjectByType<WalkController>(),
            UnityEngine.Object.FindFirstObjectByType<BuildManager>(), UnityEngine.Object.FindFirstObjectByType<GameUI>());
        EditorUtility.SetDirty(mode);
        if (UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>() == null)
            new GameObject("xrInteractionManager").AddComponent<XRInteractionManager>();

        var ground = grid.transform.Find("Ground").gameObject;
        var area = ground.GetComponent<TeleportationArea>();
        if (area == null) area = ground.AddComponent<TeleportationArea>();
        area.teleportationProvider = rig.GetComponentInChildren<TeleportationProvider>(true);
        area.interactionLayers = 1 << 31;
        area.matchOrientation = MatchOrientation.WorldSpaceUp;
        area.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
        area.filterSelectionByHitNormal = true;
        area.upNormalToleranceDegrees = 25f;
        area.colliders.Clear();
        area.colliders.Add(ground.GetComponent<Collider>());

        var boundary = grid.transform.Find("vrBoundary");
        if (boundary == null)
        {
            boundary = new GameObject("vrBoundary").transform;
            boundary.SetParent(grid.transform, false);
            float width = grid.width * grid.cellSize, depth = grid.height * grid.cellSize;
            AddBoundary(boundary, "south", new Vector3(width / 2f, 2f, -2f), new Vector3(width + 4.4f, 4f, .4f));
            AddBoundary(boundary, "north", new Vector3(width / 2f, 2f, depth + 2f), new Vector3(width + 4.4f, 4f, .4f));
            AddBoundary(boundary, "west", new Vector3(-2f, 2f, depth / 2f), new Vector3(.4f, 4f, depth + 4.4f));
            AddBoundary(boundary, "east", new Vector3(width + 2f, 2f, depth / 2f), new Vector3(.4f, 4f, depth + 4.4f));
        }
        foreach (string dep in AssetDatabase.GetDependencies(RigPath, true))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(dep);
            if (material == null || material.shader == null || material.shader.name != "Standard") continue;
            Color color = material.color;
            var texture = material.mainTexture;
            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
        }
        EditorBuildSettings.AddConfigObject("com.unity.xr.management.loader_settings", AssetDatabase.LoadMainAssetAtPath("Assets/XR/XRGeneralSettingsPerBuildTarget.asset"), true);
        EditorBuildSettings.AddConfigObject("com.unity.xr.openxr.settings4", AssetDatabase.LoadMainAssetAtPath("Assets/XR/Settings/OpenXR Package Settings.asset"), true);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("FINAL VR SETUP OK: MP2B rig, OpenXR, continuous move/turn, ground teleport, desktop fallback and F8 simulator saved.");
    }

    static void AddBoundary(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var wall = new GameObject(name);
        wall.layer = 2;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = position;
        wall.AddComponent<BoxCollider>().size = size;
    }

    [MenuItem("CS417/VR/Toggle Keyboard Simulator (Play Mode)")]
    public static void ToggleSimulator()
    {
        if (!EditorApplication.isPlaying) { Debug.Log("Enter Play Mode, then press F8 in the Game view."); return; }
        var mode = UnityEngine.Object.FindFirstObjectByType<VRPlayerMode>();
        mode.SetSimulation(!mode.SimulationActive);
    }
}
