`MatrixStack.cs` is adapted from CutTheRopeDX:

- Repository: https://github.com/yell0wsuit/cuttherope-dx
- Revision: `84ee61147caac92846746da58cff76503a1a6700`
- Path: `src/CutTheRopeDX.Core/Framework/Platform/MatrixStack.cs`

The namespace and formatting follow this repository; the transform implementation is preserved.

This is the first small framework seam reused by the CTR2 prototype. It preserves DX's transform order and Flash skew math without bringing CTR1 boot, resources or preferences into this assembly. The full DX framework/renderer/desktop host has not been extracted. Expand reuse as the animation vertical slice establishes requirements. No reconstructed CTR2 source is vendored.

`AnimationPose.cs` additionally adapts the quadratic Flash interpolation formulas from DX's
`src/CutTheRopeDX.Core/Framework/Visual/Track.cs` at the same revision. The formulas were checked
against CTR2's `calculatePercentTime` behavior. Timeline sampling is new: CTR2 quantizes each
segment by its authored frame duration, has distinct serialized property IDs, and stores actions
as separate equal-time keys. DX's full Track/Timeline/BaseElement classes are not imported.
