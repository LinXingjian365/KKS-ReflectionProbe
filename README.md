# KKS Reflection Probe

Realtime Unity Built-in reflection probe helper for Koikatsu Sunshine CharaStudio.

## Controls

- `Ctrl+R`: enable/disable the probe.
- `Alt+R`: open the configuration panel.

The probe uses Unity's native `ReflectionProbe` component. It does not use ReShade and it does not replace KKS materials. Only materials/shaders that read Unity reflection probes can show the result.

## Performance and diagnostics

The probe can refresh after camera movement and at a slower interval while the camera is static. Cubemap faces are time-sliced by default so a 512px probe does not submit all six faces in one frame. `UpdateEveryNFrames` still provides a hard frame gate, while `StaticRefreshSeconds` limits refreshes when the view is unchanged.

The plugin logs the Unity render request ID, probe position, cubemap size, time-slicing mode, request time, and failed request count every ten seconds. A negative `renderId` means the request failed; a positive ID confirms that Unity accepted the render request. The texture can remain null during the first time-sliced faces and becomes valid after the cubemap finishes.

## Build

```text
dotnet build -c Release
```

Target: Unity 2019.4 / net46.
