# Windows resource metadata

`WindowsMetadata` statically reads the user's `Game.exe` with the BCL PE header reader and [Iced](https://github.com/icedland/iced) x86 decoder. No game executable or decompiler output is loaded as executable code, vendored, or required as a development tool.

The initial profile is the inspected Windows x86 1.0.0.46 executable:

- SHA-256: `565f380c7fba556b5f23adc3b9ca2f693069c980a068f3b9a2c5475b3461008b`.
- CRT initializer pointer table: VA `0x8e45fc`, 3,518 entries.
- Resource table: VA `0x8ee9e0`, 383 records. Each record contains an asset-variant pointer and count; variants occupy 16 bytes. The record index is the original numeric resource ID.
- ImageInfo: canvas dimensions at offsets 24/28, quad count at 32, quad/offset array pointers at 36/40. Other resource kinds have different layouts and are not interpreted as ImageInfo.
- Verified coverage: 256 image variants, 92 animation references, 16 font references, 4,156 image quads. This is not an inventory of sound, level, or other resource kinds.

Filename initializers supply the variant address, kind, and resolution. Image arrays are recovered from immediate stores and bounded integer-index loops in their builder functions. The supported operations are deliberately limited; unknown instructions, incomplete/duplicate stores, unsafe addresses, nonfinite geometry, ambiguous resource variants, and coverage mismatches fail. New builds require their own verified profile. Table addresses describe the extraction layout; game data and generated metadata stay in the user's local cache.

Resource sidecar schema 1 preserves IDs, original resolution enums, geometry in source pixels, explicit known density, and the source executable hash. Import binds each image to the SHA-256 of its cached image bytes. Preview requires an exact ID/resolution/quad and verifies that binding. It never substitutes another variant automatically.

The supported dump contains 81 RAW-backed placeholder rectangles extending beyond their actual textures. Import preserves their geometry/index and sets `IsAvailable` false; preview rejects them. This avoids treating unused UI data as valid samples or rejecting unrelated valid sprites. Native PNG/JPEG dimensions are checked by the preview renderer. No geometry or density is guessed.

Resolution 2 is density 1; resolution 4 is 1.56. Resolution 6 remains unknown. Font glyph layout, resource fallback selection, animation playback, original camera/layout behavior, and runtime visual parity are outside this reader's current scope.

Tests use a synthetic PE and synthetic x86 builders only. Original executable, tables, images, reference traces, and generated snapshots remain outside tracked files.
