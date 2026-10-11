`MatrixStack.cs` is adapted from CutTheRopeDX:

- Repository: https://github.com/yell0wsuit/cuttherope-dx
- Revision: `84ee61147caac92846746da58cff76503a1a6700`
- Path: `src/CutTheRopeDX.Core/Framework/Platform/MatrixStack.cs`

The namespace and formatting follow this repository; the transform implementation is preserved.

This is the first small framework seam reused by the CTR2 prototype. It preserves DX's transform order and Flash skew math without bringing CTR1 boot, resources or preferences into this assembly. The full DX framework/renderer/desktop host has not been extracted. Expand reuse as the animation vertical slice establishes requirements. No reconstructed CTR2 source is vendored.
