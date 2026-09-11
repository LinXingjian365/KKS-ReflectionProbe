# Changelog

## v1.3.0

- Added camera-motion-aware refresh scheduling with a static-scene refresh interval.
- Enabled individual-face time slicing by default to reduce six-face render spikes.
- Avoided reapplying unchanged probe settings every refresh.
- Added request timing and failed-request counters to diagnostics.
- Added guarded update handling so a probe exception removes only the helper probe.

## v1.2.0

- Corrected the startup version banner.
- Added periodic RenderProbe result and cubemap diagnostics.
- Explicitly keeps the probe enabled and reports its culling mask, position, and texture.
