using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace KKS_ReflectionProbe
{
    [BepInPlugin("com.user.kks_reflectionprobe", "KKS Realtime Reflection Probe", "1.2.0")]
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

        private GameObject _probeObj;
        private ReflectionProbe _probe;
        private int _frameCount;
        private bool _showWindow;
        private Rect _windowRect = new Rect(20, 200, 300, 420);
        private int _windowId;
        private Vector2 _scroll;
        private float _nextDiagnostic;
        private int _renderCount;

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
            ToggleKey = Config.Bind("Hotkeys", "ToggleProbe", new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl), "Toggle reflection probe");
            UIKey = Config.Bind("Hotkeys", "ToggleUI", new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt), "Toggle config UI");

            Logger.LogInfo("KKS Reflection Probe v1.2.0 loaded - Ctrl+R toggle, Alt+R config");
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                EnableProbe.Value = !EnableProbe.Value;
                Logger.LogInfo("Reflection probe " + (EnableProbe.Value ? "enabled" : "disabled"));
                if (!EnableProbe.Value) _frameCount = 0;
            }
            if (UIKey.Value.IsDown())
            {
                _showWindow = !_showWindow;
            }

            if (EnableProbe.Value)
            {
                EnsureProbe();
                UpdateProbe();
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
            _probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
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

            _probe.resolution = Resolution.Value;
            _probe.nearClipPlane = NearClip.Value;
            _probe.farClipPlane = FarClip.Value;
            _probe.intensity = Intensity.Value;
            _probe.center = new Vector3(0, BoxSizeY.Value * 0.5f, 0);
            _probe.size = new Vector3(BoxSizeX.Value, BoxSizeY.Value, BoxSizeZ.Value);

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
            if (_frameCount < UpdateEveryNFrames.Value) return;
            _frameCount = 0;

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

            // Apply config changes
            ApplyConfig();

            // Render probe
            int renderResult = _probe.RenderProbe();
            _renderCount++;
            if (Time.unscaledTime >= _nextDiagnostic)
            {
                _nextDiagnostic = Time.unscaledTime + 5f;
                var texture = _probe.texture;
                Logger.LogInfo("Reflection probe render #" + _renderCount + ": result=" + renderResult +
                    ", enabled=" + _probe.enabled + ", mode=" + _probe.mode + ", refresh=" + _probe.refreshMode +
                    ", position=" + _probeObj.transform.position +
                    ", texture=" + (texture != null ? texture.width + "x" + texture.height : "null") +
                    ", intensity=" + _probe.intensity);
            }
        }

        private void RemoveProbe()
        {
            if (_probeObj != null)
            {
                Destroy(_probeObj);
                _probeObj = null;
                _probe = null;
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
