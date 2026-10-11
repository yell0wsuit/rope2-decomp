# Cut the Rope 2 PC port

An early C# port using selected framework code from [CutTheRopeDX](https://github.com/yell0wsuit/cuttherope-dx). PC comes first; a web host is planned later.

**Current build: local asset import and desktop atlas/frame preview. Gameplay and animation playback are not implemented yet.** The reused framework subset currently consists of DX's matrix stack; full rendering, animation, input and host reuse will follow the vertical slice. See [provenance](src/Ctr2.Engine/Framework/PROVENANCE.md).

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

Pipeline: **user's package dump → safe extraction → RAW decompression → straight RGBA PNG → local cache manifest**. Existing PNG/JPEG, protobuf animations, levels, audio and fonts are preserved. RAW continuation files (`.raw1`, `.raw2`, etc.) are joined before zlib decompression. The importer validates header marker, dimensions, format, decoded size and checksum. It supports RGBA8888, RGB565, RGBA4444, RGB5A1 and A8. Unknown formats/builds fail with an error instead of silently guessing. Cache schema 1 records converted RAW texture dimensions, original-parts SHA-256 and known density; the texture list does not yet inventory existing PNG/JPEG files.

Atlas rectangles and offsets are **not in RAW files**. Executable resource-ID and `ImageInfo` extraction is still pending. The default preview fits the whole atlas for inspection and does not represent a gameplay screen.

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

Next: extract Windows resource IDs/quad metadata, load `.pb` animations and render one animated character through additional DX framework seams. Then implement Box2D 2.2.1 with the identified CTR2 modifications, reproduce recorded candy/rope inputs at the original `0.0167` timestep and `15/30` solver iterations, and complete one original level. Physics, animation and visual parity have not been established. Web work follows the PC vertical slice.
