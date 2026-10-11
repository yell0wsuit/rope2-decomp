# Cut the Rope 2 PC port

An early C# port using selected framework code from [CutTheRopeDX](https://github.com/yell0wsuit/cuttherope-dx). PC comes first; a web host is planned later.

**Current build: local asset import, Windows resource metadata extraction, and desktop atlas/resource preview. Gameplay and animation playback are not implemented yet.** The reused framework subset currently consists of DX's matrix stack; full rendering, animation, input and host reuse will follow the vertical slice. See [provenance](src/Ctr2.Engine/Framework/PROVENANCE.md).

## Your game assets

Supply a package dump from your own copy of Cut the Rope 2. This repository contains no game assets, packages, levels, reconstructed source or downloader. Import works offline and leaves the selected dump unchanged. Keep local material in the ignored `imports/`, `cache/` or `local/` directories.

Development uses **Windows assets first**, from an existing installation/package dump of the delisted Windows version. Planned source adapters include:

- Android package `com.zeptolab.ctr2.f2p.google`.
- iOS Free: <https://apps.apple.com/us/app/cut-the-rope-2-om-noms-quest/id789631430>.
- Windows: <https://apps.microsoft.com/detail/9nblggh0j0dc?hl=en-US&gl=US> (delisted, but still downloadable if previously purchased).
- iOS Gold, the paid edition: <https://apps.apple.com/us/app/cut-the-rope-2/id681814050>.

Only the Windows adapter is implemented and tested. Mobile editions and versions can have different resources and atlas packing; their metadata must match their own textures.

## Run

Requires the .NET 10 SDK. Build with `dotnet build Ctr2.slnx`.

Import an extracted Windows package root (containing `Assets/assets`), that asset directory itself, or a ZIP-based `.appx`, `.msix` or `.zip` dump:

```sh
dotnet run --project src/Ctr2.Desktop -- import "/path/to/your/windows-dump" cache/windows
```

For `.appxbundle`/`.msixbundle`, select the individual game package after unpacking the bundle. Import requires a new output directory. Conversion occurs in staging; errors or cancellation remove the partial cache. The reusable `Ctr2.Assets.GameImport` library accepts progress and cancellation callbacks for the future in-game first-run setup. The current host exposes it through the command line.

Preview an imported atlas in a resizable desktop window:

```sh
dotnet run --project src/Ctr2.Desktop -- preview cache/windows/assets/omnom_640_960.png
```

For offscreen verification, add `--width 640 --height 960 --snapshot local/atlas.png`. Create the `local/` directory first; snapshots do not overwrite existing files. `--frames 3` runs a bounded desktop presentation smoke test.

## Conversion and scaling

Pipeline: **user's package dump → safe extraction → RAW decompression → straight RGBA PNG → local cache manifest**. Existing PNG/JPEG, protobuf animations, levels, audio and fonts are preserved. RAW continuation files (`.raw1`, `.raw2`, etc.) are joined before zlib decompression. The importer validates header marker, dimensions, format, decoded size and checksum. Packed RGBA RAW pixels store premultiplied colors; conversion unpremultiplies RGB once for PNG export, preserving alpha. This prevents dark translucent edges when PNG decoders apply alpha during rendering. It supports RGBA8888, RGB565, RGBA4444, RGB5A1 and A8. Unknown formats/builds fail with an error instead of silently guessing. Cache schema 1 records converted RAW texture dimensions, original-parts SHA-256 and known density; the texture list does not yet inventory existing PNG/JPEG files.

Atlas rectangles and offsets are **not in RAW files**. When the selected package root or archive contains `Game.exe`, import reads the matching executable's resource table and `ImageInfo` initializers statically and writes `resources.json`. The executable is never loaded or executed. The initial verified profile supports Windows x86 1.0.0.46 by exact SHA-256; an unknown executable fails with a diagnostic and import rolls back. Asset-directory-only imports still produce the original texture cache without resource metadata.

The sidecar preserves original resource IDs, resolution enums, quad order, sample rectangles, trim offsets, and canvas sizes. Images are bound to cached bytes by SHA-256. Font and animation resource references are included, but font glyph metadata and animation playback are pending. Original Windows tables contain unused/placeholder quads outside some RAW textures: these retain their indices and coordinates and are marked unavailable rather than sampled. Native PNG/JPEG bounds are checked when previewing. See [extraction details](src/Ctr2.Assets/WINDOWS_METADATA.md).

Preview a quad by its original resource ID and resolution enum:

```sh
dotnet run --project src/Ctr2.Desktop -- preview-resource cache/windows 215 2 0 --width 640 --height 960
```

In the supported build, ID 215 is Om Nom's atlas; quad 0 is an individual antenna part, not the assembled character. Resolution 2 uses density 1; resolution 4 uses density 1.56. Windows-specific resolution 6 retains unknown density and cannot be selected through this command until its scaling is traced. Add `--snapshot local/quad.png` for an offscreen render or `--frames 3` for a bounded desktop run. Reimport a full package into a new cache to add metadata to an older cache.

Caches generated before the alpha-conversion correction must be reimported into a new directory; changing the renderer cannot repair their already converted PNGs. Local development now uses `cache/windows-alpha`.

The default whole-atlas preview remains useful for inspection and does not represent a gameplay screen.

Optional `--frame local/frame.json` previews one quad using a sidecar from the **matching build's** `ImageInfo`. Coordinates, offsets and pre-cut dimensions below are in source pixels; density is explicit:

```json
{
    "X": 0,
    "Y": 0,
    "Width": 156,
    "Height": 78,
    "OffsetX": 31.2,
    "OffsetY": 15.6,
    "PreCutWidth": 312,
    "PreCutHeight": 156,
    "PixelsPerLogicalUnit": 1.56
}
```

These are illustrative values, not a shipped game quad. Sampling uses pixel rectangles; layout divides dimensions, offsets and the pre-cut canvas by density. The original resource manager uses `1.0` for 640×960 and `1.56` for 1200×1600, rather than their nominal width ratio. Windows landscape/UI variants retain an unknown density until their own metadata is traced. Viewport fitting uses uniform scaling, centered margins and inverse input mapping through the reused DX transform stack. Actual Windows gameplay screen/camera behavior still needs comparison against the original.

## Verification and next work

```sh
dotnet build Ctr2.slnx -c Release
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll
git diff --check
```

Tests use synthetic data and exercise conversion, malformed dumps, cache rollback, trim geometry, viewport mapping and rendered pixel readback. No original data is required for tests.

Next: load `.pb` animation trees/timelines using the extracted Windows resource IDs and render one animated character through additional DX framework seams. Then implement Box2D 2.2.1 with the identified CTR2 modifications, reproduce recorded candy/rope inputs at the original `0.0167` timestep and `15/30` solver iterations, and complete one original level. Physics, animation and visual parity have not been established. Web work follows the PC vertical slice.
