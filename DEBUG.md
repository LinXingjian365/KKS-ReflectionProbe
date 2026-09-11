# Debugging

1. Close KKS before replacing the DLL.
2. Start Studio and enter a scene.
3. Press `Ctrl+R` and look for `Reflection probe enabled`.
4. Confirm `Reflection probe render #...` appears every five seconds.
5. Confirm `result=1`, `timeSlicing=IndividualFaces`, and `texture=512x512` (or the configured resolution).
6. With the camera static, confirm the render counter advances roughly once per `StaticRefreshSeconds`; moving the camera should trigger the next permitted update.

If rendering succeeds but the material does not change, the material shader is not sampling Unity reflection probes, or the probe is positioned outside the reflective object. Disable `UseSceneOrigin` to follow the camera and test again.
