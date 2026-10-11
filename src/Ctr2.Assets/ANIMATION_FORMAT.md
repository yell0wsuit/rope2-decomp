# Windows animation contracts

The `.pb` files are uncompressed protobuf-lite messages. `AnimationReader` decodes the local user's cached files; no original animation bytes or reconstructed game source are tracked.

| Message | Fields |
| --- | --- |
| Element | 1 type (0 container, 1 image, 2 Flash animation); 2 name; 3 resource ID; 4 quad; 5 anchor; 6 parent anchor; 7/8 logical width/height; 9 children; 10 timelines |
| Timeline | 1 ID; 2 name; 3 empty marker; 4 frame duration in seconds; 5 tracks |
| Track | 1 type; 2 keyframes |
| Keyframe | 1 delta seconds; 2–5 float parameters; 6 action ID; 7 transition ID |

Integers use varints, floats use fixed32, strings/messages use length-delimited fields. Unknown scalar and message fields are skipped. Unsupported wire kinds, invalid enum values, nonfinite values and malformed lengths fail with a diagnostic. Limits: 16 MiB input, depth 64, four million fields, 4096-byte names, one-day individual track duration. Defaults follow the element factory and runtime initialization: position/skew/anchor point zero, scale/color one, anchor left/top, parent anchor absolute, quad zero. Absent explicit image dimensions use the selected trimmed quad dimensions.

Serialized tracks are **0 position, 1 scale, 2 color, 3 skew, 4 action**. They must not be cast to DX's runtime TrackType. Repeated serialized tracks of the same property append their keys in source order; the Windows store and fireball files exercise this case. Key times are deltas, including zero-time groups. Actions are **0 visibility (param2), 1 quad (param1), 2 custom anchor (param1/2), 3 rotation center (param1/2), 4 delay**. Delay has no side effect but its delta contributes to track duration. All actions at a timestamp apply in order.

Transitions are **0 linear, 1 immediate, 2 quadratic ease-in, 3 quadratic ease-out, 4 ease-in-out, 5 ease-out-in, 6 hold**. The destination key selects the curve. Each segment's elapsed interpolation time is floored to the timeline frame duration. Completed tracks retain their terminal pose. The diagnostic host plays once; looping, scene transitions and gameplay delegates are not inferred from the data.

Child order is drawing order. Custom anchors subtract their point from position. Scale and Flash skew pivot around the trimmed image center plus the authored rotation-center offset. Descendants inherit transform and multiplicative tint. The loader creates images with `restoreCutTransparency=false`, so ImageInfo trim offsets/pre-cut canvases do not shift these parts. Resource density converts atlas pixel sizes to logical sizes, while protobuf coordinates remain logical. The preview fits the authored root canvas uniformly rather than inferring a gameplay camera.

These contracts were traced in the local reconstructed `ElementFactory`, `FlashAnimation`, `Timeline`, `BaseElement`, and `Image` implementations, with DX transform/easing reuse documented in [provenance](../Ctr2.Engine/Framework/PROVENANCE.md). Local verification decoded all **92 Windows files, 2227 elements and 244983 keyframes**, matching the independent wire-walking script's totals. Automated synthetic tests exercise malformed wire data, nested transforms, simultaneous actions, repeated tracks, timing and rendered pixels at two densities. Real Om Nom IDLE/CHEWING snapshots and a bounded SDL presentation ran on macOS. Original-game visual parity and Windows/Linux presentation are still unverified.
