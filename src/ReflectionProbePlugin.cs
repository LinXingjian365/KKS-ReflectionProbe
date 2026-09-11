using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace KKS_ReflectionProbe
{
    [BepInPlugin("com.user.kks_reflectionprobe", "KKS Realtime Reflection Probe", "1.3.0")]
    public class ReflectionProbePlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        // Config
        public static ConfigEntry<bool> EnableProbe;
        public static ConfigEntry<int> Resolution;
        public static ConfigEntry<float> ProbeHeight;
        public static ConfigEntry<float> BoxSizeX;
        public static ConfigEntry<float> BoxSizeY;
        public static ConfigEntry<float> BoxSizeZ;
        public static ConfigEntry<int> UpdateEveryNFrames;
        public static ConfigEntry<float> NearClip;
        public static ConfigEntry<float> FarClip;
        public static ConfigEntry<int> ShadowDistance;
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<bool> ShowUI;
        public static ConfigEntry<KeyboardShortcut> UIKey;
        public static ConfigEntry<float> Intensity;
        public static ConfigEntry<bool> UseSceneOrigin;
        public static ConfigEntry<bool> UseTimeSlicing;
        public static ConfigEntry<bool> UpdateOnCameraMotion;
        public static ConfigEntry<float> CameraMoveThreshold;
        public static ConfigEntry<float> CameraRotateThreshold;
        public static ConfigEntry<float> StaticRefreshSeconds;

        private GameObject _probeObj;
        private ReflectionProbe _probe;
        private int _frameCount;
        private bool _showWindow;
        private Rect _windowRect = new Rect(20, 200, 300, 420);
        private int _windowId;
        private Vector2 _scroll;
        private float _nextDiagnostic;
        private int _renderCount;
        private int _failedRenderCount;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private bool _hasCameraSample;
        private float _nextRefreshTime;
        private int _appliedResolution = -1;
        private float _appliedProbeHeight = float.NaN;
        private float _appliedBoxX = float.NaN;
        private float _appliedBoxY = float.NaN;
        private float _appliedBoxZ = float.NaN;
        private float _appliedNearClip = float.NaN;
        private float _appliedFarClip = float.NaN;
        private float _appliedIntensity = float.NaN;
        private bool _appliedTimeSlicing;

        private void Awake()
        {
            Log = Logger;
            _windowId = new System.Random().Next(10000, 99999);

            EnableProbe = Config.Bind("General", "Enable", true, "Enable realtime reflection probe");
            Resolution = Config.Bind("General", "Resolution", 256, "Cubemap resolution (128/256/512/1024/2048)");
            ProbeHeight = Config.Bind("General", "ProbeHeightY", 0f, "Y position of probe (floor level usually 0)");
            BoxSizeX = Config.Bind("General", "BoxSizeX", 50f, "Reflection probe box X size");
            BoxSizeY = Config.Bind("General", "BoxSizeY", 30f, "Reflection probe box Y size");
            BoxSizeZ = Config.Bind("General", "BoxSizeZ", 50f, "Reflection probe box Z size");
            UpdateEveryNFrames = Config.Bind("Performance", "UpdateEveryNFrames", 3, "Update probe every N frames (1=every frame, 2=every other frame)");
            NearClip = Config.Bind("Performance", "NearClip", 0.1f, "Probe camera near clip");
            FarClip = Config.Bind("Performance", "FarClip", 200f, "Probe camera far clip");
            Intensity = Config.Bind("General", "Intensity", 1.0f, "Reflection intensity multiplier");
            UseSceneOrigin = Config.Bind("General", "UseSceneOrigin", true, "Place probe at scene origin instead of camera position");
            UseTimeSlicing = Config.Bind("Performance", "UseTimeSlicing", true, "Render one cubemap face at a time to avoid frame-time spikes");
            UpdateOnCameraMotion = Config.Bind("Performance", "UpdateOnCameraMotion", true, "Refresh immediately after the camera moves");
            CameraMoveThreshold = Config.Bind("Performance", "CameraMoveThreshold", 0.05f, "Camera movement in meters required to trigger a refresh");
            CameraRotateThreshold = Config.Bind("Performance", "CameraRotateThreshold", 0.5f, "Camera rotation in degrees required to trigger a refresh");
            StaticRefreshSeconds = Config.Bind("Performance", "StaticRefreshSeconds", 1.0f, "Refresh interval while the camera is static");
            ToggleKey = Config.Bind("Hotkeys", "ToggleProbe", new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl), "Toggle reflection probe");
            UIKey = Config.Bind("Hotkeys", "ToggleUI", new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt), "Toggle config UI");

            Logger.LogInfo("KKS Reflection Probe v1.3.0 loaded - Ctrl+R toggle, Alt+R config");
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                EnableProbe.Value = !EnableProbe.Value;
                Logger.LogInfo("Reflection probe " + (EnableProbe.Value ? "enabled" : "disabled"));
                if (!EnableProbe.Value)
                {
                    _frameCount = 0;
                    _hasCameraSample = false;
                }
            }
            if (UIKey.Value.IsDown())
            {
                _showWindow = !_showWindow;
            }

            if (EnableProbe.Value)
            {
                try
                {
                    EnsureProbe();
                    UpdateProbe();
                }
                catch (Exception ex)
                {
                    Logger.LogError("Reflection probe update failed; removing probe safely: " + ex.GetType().Name + ": " + ex.Message);
                    RemoveProbe();
                }
            }
            else
            {
                RemoveProbe();
            }
        }

        private void EnsureProbe()
        {
            if (_probeObj != null) return;

            _probeObj = new GameObject("KKS_RealtimeReflectionProbe");
            _probeObj.tag = "EditorOnly";
            _probe = _probeObj.AddComponent<ReflectionProbe>();

            _probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            _probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = UseTimeSlicing.Value
                ? UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces
                : UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            _probe.hdr = true;
            _probe.boxProjection = true;
            _probe.importance = 10;
            _probe.blendDistance = 1f;
            // Exclude UI (layer 5) and Ignore Raycast (layer 2) to reduce frustum errors
            _probe.cullingMask = ~((1 << 5) | (1 << 2));
            _probe.enabled = true;

            ApplyConfig();

            Logger.LogInfo("Reflection probe created at Y=" + ProbeHeight.Value + ", resolution=" + _probe.resolution + ", cullingMask=0x" + _probe.cullingMask.ToString("X8"));
        }

        private void ApplyConfig()
        {
            if (_probe == null) return;

            bool changed = _appliedResolution != Resolution.Value
                || !Mathf.Approximately(_appliedProbeHeight, ProbeHeight.Value)
                || !Mathf.Approximately(_appliedBoxX, BoxSizeX.Value)
                || !Mathf.Approximately(_appliedBoxY, BoxSizeY.Value)
                || !Mathf.Approximately(_appliedBoxZ, BoxSizeZ.Value)
                || !Mathf.Approximately(_appliedNearClip, NearClip.Value)
                || !Mathf.Approximately(_appliedFarClip, FarClip.Value)
                || !Mathf.Approximately(_appliedIntensity, Intensity.Value)
                || _appliedTimeSlicing != UseTimeSlicing.Value;
            if (!changed) return;

            _probe.resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(Resolution.Value, 128, 2048));
            _probe.nearClipPlane = Mathf.Max(0.01f, NearClip.Value);
            _probe.farClipPlane = Mathf.Max(_probe.nearClipPlane + 1f, FarClip.Value);
            _probe.intensity = Mathf.Clamp(Intensity.Value, 0f, 3f);
            _probe.center = new Vector3(0, Mathf.Max(1f, BoxSizeY.Value) * 0.5f, 0);
            _probe.size = new Vector3(Mathf.Max(1f, BoxSizeX.Value), Mathf.Max(1f, BoxSizeY.Value), Mathf.Max(1f, BoxSizeZ.Value));
            _probe.timeSlicingMode = UseTimeSlicing.Value
                ? UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces
                : UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;

            _appliedResolution = Resolution.Value;
            _appliedProbeHeight = ProbeHeight.Value;
            _appliedBoxX = BoxSizeX.Value;
            _appliedBoxY = BoxSizeY.Value;
            _appliedBoxZ = BoxSizeZ.Value;
            _appliedNearClip = NearClip.Value;
            _appliedFarClip = FarClip.Value;
            _appliedIntensity = Intensity.Value;
            _appliedTimeSlicing = UseTimeSlicing.Value;

            // Shadow distance for probe camera
            var probeCam = _probe.GetComponent<Camera>();
            if (probeCam != null)
            {
                probeCam.farClipPlane = FarClip.Value;
            }
        }

        private void UpdateProbe()
        {
            if (_probe == null) return;

            _frameCount++;
            int frameInterval = Mathf.Max(1, UpdateEveryNFrames.Value);
            if (_frameCount < frameInterval) return;
            _frameCount = 0;

            Camera cameraForMotion = Camera.main;
            bool cameraMoved = false;
            if (cameraForMotion != null)
            {
                cameraMoved = !_hasCameraSample
                    || Vector3.Distance(cameraForMotion.transform.position, _lastCameraPosition) >= Mathf.Max(0f, CameraMoveThreshold.Value)
                    || Quaternion.Angle(cameraForMotion.transform.rotation, _lastCameraRotation) >= Mathf.Max(0f, CameraRotateThreshold.Value);
                _lastCameraPosition = cameraForMotion.transform.position;
                _lastCameraRotation = cameraForMotion.transform.rotation;
                _hasCameraSample = true;
            }
            bool refreshDue = Time.unscaledTime >= _nextRefreshTime;
            if (UpdateOnCameraMotion.Value && !cameraMoved && !refreshDue) return;

            // Position probe
            if (UseSceneOrigin.Value)
            {
                _probeObj.transform.position = new Vector3(0, ProbeHeight.Value, 0);
            }
            else
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var pos = cam.transform.position;
                    pos.y = ProbeHeight.Value;
                    _probeObj.transform.position = pos;
                }
            }

            ApplyConfig();

            // Render probe
            float requestStart = Time.realtimeSinceStartup;
            int renderResult = _probe.RenderProbe();
            float requestMs = (Time.realtimeSinceStartup - requestStart) * 1000f;
            _renderCount++;
            if (renderResult == 0) _failedRenderCount++;
            _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, StaticRefreshSeconds.Value);
            if (Time.unscaledTime >= _nextDiagnostic)
            {
                _nextDiagnostic = Time.unscaledTime + 10f;
                var texture = _probe.texture;
                Logger.LogInfo("Reflection probe render #" + _renderCount + ": result=" + renderResult +
                    ", enabled=" + _probe.enabled + ", mode=" + _probe.mode + ", refresh=" + _probe.refreshMode +
                    ", position=" + _probeObj.transform.position +
                    ", texture=" + (texture != null ? texture.width + "x" + texture.height : "null") +
                    ", intensity=" + _probe.intensity +
                    ", timeSlicing=" + _probe.timeSlicingMode +
                    ", requestMs=" + requestMs.ToString("F2") +
                    ", failed=" + _failedRenderCount);
            }
        }

        private void RemoveProbe()
        {
            if (_probeObj != null)
            {
                Destroy(_probeObj);
                _probeObj = null;
                _probe = null;
                _hasCameraSample = false;
                _nextRefreshTime = 0f;
                Logger.LogInfo("Reflection probe removed");
            }
        }

        private void OnGUI()
        {
            if (!_showWindow) return;
            _windowRect = GUILayout.Window(_windowId, _windowRect, DrawWindow, "Realtime Reflection Probe [Alt+R]", GUILayout.Width(300));
        }

        private void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);

            bool en = GUILayout.Toggle(EnableProbe.Value, "  Enable Reflection Probe");
            if (en != EnableProbe.Value) EnableProbe.Value = en;

            GUILayout.Space(5);
            GUILayout.Label("Resolution: " + Resolution.Value);
            Resolution.Value = (int)GUILayout.HorizontalSlider(Resolution.Value, 128, 2048);
            Resolution.Value = Mathf.ClosestPowerOfTwo(Resolution.Value);

            GUILayout.Label("Intensity: " + Intensity.Value.ToString("F2"));
            Intensity.Value = GUILayout.HorizontalSlider(Intensity.Value, 0f, 3f);

            GUILayout.Label("Probe Height Y: " + ProbeHeight.Value.ToString("F1"));
            ProbeHeight.Value = GUILayout.HorizontalSlider(ProbeHeight.Value, -10f, 10f);

            GUILayout.Label("Box Size X: " + BoxSizeX.Value.ToString("F0"));
            BoxSizeX.Value = GUILayout.HorizontalSlider(BoxSizeX.Value, 5f, 200f);

            GUILayout.Label("Box Size Y: " + BoxSizeY.Value.ToString("F0"));
            BoxSizeY.Value = GUILayout.HorizontalSlider(BoxSizeY.Value, 5f, 100f);

            GUILayout.Label("Box Size Z: " + BoxSizeZ.Value.ToString("F0"));
            BoxSizeZ.Value = GUILayout.HorizontalSlider(BoxSizeZ.Value, 5f, 200f);

            GUILayout.Label("Update Every N Frames: " + UpdateEveryNFrames.Value);
            UpdateEveryNFrames.Value = (int)GUILayout.HorizontalSlider(UpdateEveryNFrames.Value, 1, 10);

            UpdateOnCameraMotion.Value = GUILayout.Toggle(UpdateOnCameraMotion.Value, "  Refresh on camera motion");
            UseTimeSlicing.Value = GUILayout.Toggle(UseTimeSlicing.Value, "  Time-slice cubemap faces");

            GUILayout.Label("Static refresh: " + StaticRefreshSeconds.Value.ToString("F1") + " s");
            StaticRefreshSeconds.Value = GUILayout.HorizontalSlider(StaticRefreshSeconds.Value, 0.1f, 5f);

            GUILayout.Label("Far Clip: " + FarClip.Value.ToString("F0"));
            FarClip.Value = GUILayout.HorizontalSlider(FarClip.Value, 50f, 500f);

            UseSceneOrigin.Value = GUILayout.Toggle(UseSceneOrigin.Value, "  Use Scene Origin (uncheck = follow camera)");

            GUILayout.Space(8);
            if (GUILayout.Button("Reset to Defaults", GUILayout.Height(25)))
            {
                Resolution.Value = 256;
                Intensity.Value = 1.0f;
                ProbeHeight.Value = 0f;
                BoxSizeX.Value = 50f;
                BoxSizeY.Value = 30f;
                BoxSizeZ.Value = 50f;
                UpdateEveryNFrames.Value = 3;
                UpdateOnCameraMotion.Value = true;
                UseTimeSlicing.Value = true;
                StaticRefreshSeconds.Value = 1.0f;
                FarClip.Value = 200f;
                UseSceneOrigin.Value = true;
            }

            GUILayout.Space(5);
            GUILayout.Label("Status: " + (_probe != null ? "Active (rendering)" : "Inactive"));
            if (_probe != null)
            {
                GUILayout.Label("Position: " + _probeObj.transform.position.ToString("F1"));
                GUILayout.Label("Resolution: " + _probe.resolution);
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private void OnDestroy()
        {
            RemoveProbe();
        }
    }
}
