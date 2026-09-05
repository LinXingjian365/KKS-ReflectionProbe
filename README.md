# KKS Reflection Probe

Realtime Unity Built-in reflection probe helper for Koikatsu Sunshine CharaStudio.

## Controls

- `Ctrl+R`: enable/disable the probe.
- `Alt+R`: open the configuration panel.

The probe uses Unity's native `ReflectionProbe` component. It does not use ReShade and it does not replace KKS materials. Only materials/shaders that read Unity reflection probes can show the result.

## Diagnostics

The plugin logs a render result, probe position, cubemap size, enabled state, and texture state every five seconds. `result=0` is the first value to inspect if the toggle appears to do nothing.

## Build

```text
dotnet build -c Release
```

Target: Unity 2019.4 / net46.
