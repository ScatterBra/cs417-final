using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

public sealed class VRPlayerMode : MonoBehaviour
{
    [SerializeField] GameObject playerRig;
    [SerializeField] GameObject deviceSimulator;
    [SerializeField] Camera desktopCamera;
    [SerializeField] WalkController desktopWalk;
    [SerializeField] BuildManager buildManager;
    [SerializeField] GameUI gameUI;

    readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    bool ready;
    public bool VRActive { get; private set; }
    public bool SimulationActive { get; private set; }
    public GameObject PlayerRig => playerRig;

    public void Configure(GameObject rig, GameObject simulator, Camera camera,
        WalkController walk, BuildManager building, GameUI ui)
    {
        playerRig = rig;
        deviceSimulator = simulator;
        desktopCamera = camera;
        desktopWalk = walk;
        buildManager = building;
        gameUI = ui;
    }

    IEnumerator Start()
    {
        // The desktop scripts cache their camera and construct their UI in Start.
        yield return null;
        ready = true;
        RefreshMode();
    }

    void Update()
    {
        if (!ready) return;
#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            SetSimulation(!SimulationActive);
#endif
        RefreshMode();
    }

    bool HeadsetRunning()
    {
        SubsystemManager.GetSubsystems(displays);
        foreach (var display in displays)
            if (display.running) return true;
        return false;
    }

    public void SetSimulation(bool enabled)
    {
        // Simulated devices must not replace live headset/controller input.
        SimulationActive = enabled && !HeadsetRunning();
        deviceSimulator.SetActive(SimulationActive);
        if (ready) RefreshMode();
    }

    void RefreshMode()
    {
        bool headset = HeadsetRunning();
        if (headset && SimulationActive)
        {
            SimulationActive = false;
            deviceSimulator.SetActive(false);
        }
        bool useVR = headset || SimulationActive;
        if (useVR == VRActive) return;
        if (desktopWalk.Walking) desktopWalk.ExitWalk();
        VRActive = useVR;
        desktopWalk.enabled = !useVR;
        desktopCamera.gameObject.SetActive(!useVR);
        playerRig.SetActive(useVR);
        buildManager.SetVRWalkthrough(useVR);
        gameUI.SetVRWalkthrough(useVR);
    }
}
