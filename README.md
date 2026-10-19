# Cut the Rope 2 PC port

An early C# port using selected framework code from [CutTheRopeDX](https://github.com/yell0wsuit/cuttherope-dx). PC comes first; a web host is planned later.

**Current build: local asset import, Windows resource metadata extraction, desktop previews and an early playable gameplay slice. Full game behavior and presentation are still in progress.** The reused framework subset consists of DX's matrix stack and adapted Flash easing math; full framework, input and host extraction will follow the vertical slice. See [provenance](src/Ctr2.Engine/Framework/PROVENANCE.md).

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

## Play a level

After importing a full Windows package, start with pack 1, entry 1 or 2:

```sh
dotnet run --project src/Ctr2.Desktop -- play-level cache/windows-alpha 1 2
```

Drag the primary mouse button across ropes or balloons; click a frog to open or close its tongue, or a balloon to pop it. **R** restarts, **Space** pauses or
resumes, and **Escape** exits. Focus loss and minimization pause and cancel held
input; press Space to resume. The display reports collected stars and the result.
Restart restores the initial physics, stars, animation, input and replay clock.

This first slice supports one candy and one Om Nom, stationary stars, basic grabs,
a tap or swipe hint, static terrain, and static or dynamic wood rectangles and circles,
including ropes attached to wood at authored offsets, free-standing ropes between
named bodies, pins (hinges between bodies
or to the world), frogs whose tongues form bridges and balloons, in a dry level (wood,
pins, frogs and balloons are drawn diagnostically). Gameplay is independent of the
authored location; currently 17 levels across the forest and canyon packs use nothing but supported
mechanics. Physical candy contact starts eating; candy fades for 0.2 seconds, followed by chewing and completion. Candy
loss plays FAIL before the result; character loss ends the level immediately.
Pack 1/entry 1 preserves the authored off-screen arrival and adds the Windows
leaf burst. The authored hint appears after its delay; an early pointer
press dismisses it, while an active hint fades after a press. No invented input
lock or camera movement is applied. Other mechanics report an unsupported-scope
error. Falling/landing and airborne eating use source velocity
history and terrain contacts. Static terrain impacts add procedural squash and rolling.
Stand-up preparation applies the source center and angular impulses after its
short squash/stretch animation. Forest-location scenes use the Windows forest background (other locations' backgrounds are pending)
and stationary star IDLE/collection animations. Stars loop with three staggered
phases and the source container pulse; collection playback follows fixed ticks,
including pause and restart. The wide background fits the logical scene height
uniformly and uses its original resolution 2 image at either foreground density.
Selected costumes, textured terrain, rope/holder artwork, emitted star particles,
star glow blend overrides and periodic flare, sound, saves and original-game parity
remain pending. Terrain and ropes still use diagnostic drawing; static and
simulation previews retain their diagnostic presentation.

`--ticks`, repeatable `--cut tick:rope:segment:x:y[:impulseX:impulseY]`,
`--tap tick:x:y` and `--pop tick:balloon`,
`--width`, `--height`, `--resolution`, `--snapshot` and `--frames` support
deterministic checks. Animation completion uses fixed ticks rather than render
cadence. A terminal result freezes session ticks. Restart replays scheduled cuts
from tick zero. As with previews, snapshot output must be a new file.

The supported Windows pack 1/entry 2 replay below collected 3/3 stars and completed
at tick 313 (feeding at tick 183). This verifies the port's current slice, not
original-game physics or visual parity:

```sh
dotnet run --project src/Ctr2.Desktop -- play-level cache/windows-alpha 1 2 --ticks 600 --cut 0:0:0:519.25:343.5 --cut 0:1:0:138.66667:156.33333 --snapshot local/forest-win.png
```

Canyon pack 2/entry 2 completed with 3/3 stars at tick 364 (feeding at tick 234)
using `--tap 1:92:694 --cut 10:0:0:382:170 --tap 150:556:358`.

Pack 1/entry 1 also completed with 3/3 stars at tick 238 (feeding at tick 108)
using `--ticks 600 --cut 30:0:0:400:138`. Leaf randomness is deterministic for
repeatable diagnostics; native randomness and visual parity remain unverified.

## Static level layout preview

Select a level using its one-based pack and entry in the Windows desktop level list:

```sh
dotnet run --project src/Ctr2.Desktop -- preview-level cache/windows-alpha 1 2
```

Use `--width 640 --height 960 --snapshot local/level.png` for a deterministic offscreen image, `--resolution 4` for higher-density Om Nom textures, or `--frames 60` for a bounded desktop run. `--time` changes the character pose only. The loader uses `sortedlist_desktop.xml`; it preserves unknown objects/settings and reports pending mechanics.

This is a **static diagnostic layout**, with assembled Om Nom, colored candy/star/grab markers, straight rope links and unsmoothed polygons. Editor coordinates and rope lengths double into logical units; texture density is independent. The canvas fits uniformly with centered margins. This does not yet reproduce the original background, camera, physics, interactions or level completion. Pack 1 level 1 intentionally starts Om Nom above the screen for its unimplemented special intro; level 2 shows his visible authored placement. See [level contracts and limitations](src/Ctr2.Assets/LEVEL_FORMAT.md).

## Conversion and scaling

Pipeline: **user's package dump → safe extraction → RAW decompression → straight RGBA PNG → local cache manifest**. Existing PNG/JPEG, protobuf animations, levels, audio and fonts are preserved. RAW continuation files (`.raw1`, `.raw2`, etc.) are joined before zlib decompression. The importer validates header marker, dimensions, format, decoded size and checksum. Packed RGBA RAW pixels store premultiplied colors; conversion unpremultiplies RGB once for PNG export, preserving alpha. This prevents dark translucent edges when PNG decoders apply alpha during rendering. It supports RGBA8888, RGB565, RGBA4444, RGB5A1 and A8. Unknown formats/builds fail with an error instead of silently guessing. Cache schema 1 records converted RAW texture dimensions, original-parts SHA-256 and known density; the texture list does not yet inventory existing PNG/JPEG files.

Atlas rectangles and offsets are **not in RAW files**. When the selected package root or archive contains `Game.exe`, import reads the matching executable's resource table and `ImageInfo` initializers statically and writes `resources.json`. The executable is never loaded or executed. The initial verified profile supports Windows x86 1.0.0.46 by exact SHA-256; an unknown executable fails with a diagnostic and import rolls back. Asset-directory-only imports still produce the original texture cache without resource metadata.

The sidecar preserves original resource IDs, resolution enums, quad order, sample rectangles, trim offsets, and canvas sizes. Images are bound to cached bytes by SHA-256. Font and animation resource references are included; font glyph metadata is pending. Original Windows tables contain unused/placeholder quads outside some RAW textures: these retain their indices and coordinates and are marked unavailable rather than sampled. Native PNG/JPEG bounds are checked when previewing. See [extraction details](src/Ctr2.Assets/WINDOWS_METADATA.md).

Preview a quad by its original resource ID and resolution enum:

```sh
dotnet run --project src/Ctr2.Desktop -- preview-resource cache/windows 215 2 0 --width 640 --height 960
```

In the supported build, ID 215 is Om Nom's atlas; quad 0 is an individual antenna part, not the assembled character. Resolution 2 uses density 1; resolution 4 uses density 1.56. Windows-specific resolution 6 retains unknown density and cannot be selected through this command until its scaling is traced. Add `--snapshot local/quad.png` for an offscreen render or `--frames 3` for a bounded desktop run. Reimport a full package into a new cache to add metadata to an older cache.

Preview an assembled character scene using its animation resource ID and exact scene name:

```sh
dotnet run --project src/Ctr2.Desktop -- preview-animation cache/windows 85 IDLE
```

In the supported Windows build, animation ID 85 is Om Nom. Use `--time 0.7 --snapshot local/omnom.png` for a deterministic pose, or `--frames 60` for bounded live playback. `--resolution 4` selects higher-density image resources; the animation coordinates and authored root canvas stay in logical units. Unknown scene names report available names. Playback runs once and holds the terminal pose. The preview retains the authored default costume; game-selected hats and other resource substitutions are not applied.

The animation reader supports bounded protobuf element trees, scenes, position/scale/color/skew tracks and visibility/quad/anchor/rotation-center/delay actions. Equal-time actions are preserved, repeated property tracks append, and interpolation respects authored frame durations. Animated parts use trimmed dimensions and custom anchors; atlas trim offsets apply only when restoring the original canvas, which the source animation loader does not enable. Nested transforms and tint follow the source tree. Textures are checked against catalog hashes and bounded to a total decoded budget of 128 MiB. See [animation contracts](src/Ctr2.Assets/ANIMATION_FORMAT.md).

This is a diagnostic character preview. Gameplay scene switching, callbacks, resource replacement, sound, and original-screen visual parity remain pending. The desktop window uses SDL and Skia, and is currently a separate preview host.

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

Audit every entry in the imported Windows desktop list against the current
gameplay session constructor:

```sh
dotnet run --project src/Ctr2.Desktop -- audit-levels cache/windows-alpha
```

The command emits JSON in authored pack/entry order, including object counts,
location, whether construction succeeds, and the first failure for each entry.
An unreadable or unsupported level is reported while later entries are still
checked. An invalid index or master design fails the command. This measures
session construction, not completion, asset rendering, or original-game parity.
The supplied Windows 1.0.0.46 content has 168 desktop entries; currently only
13 pass (pack 1/entries 1–4, 6, 7 and 9; pack 2/entries 2–4, 11, 17 and 20). Further work includes loader coverage, physical
objects and general collision, moving stars, balloons, pumps, water, magnets,
the other characters, level selection, progression, audio and saves.

```sh
dotnet build Ctr2.slnx -c Release
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll
git diff --check
```

Tests use synthetic data and exercise conversion, malformed dumps, cache rollback, trim geometry, viewport mapping and rendered pixel readback. No original data is required for tests.

The desktop includes candy/rope simulation with Windows master body definitions,
fixed stepping, damping, stretch transitions, physical cut fragments, swept
candy contacts against smoothed static terrain and stationary star collection. Drag the primary mouse button
across a rope in the live preview; accepted cuts print replay arguments on exit.

```sh
dotnet run --project src/Ctr2.Desktop -- simulate-level cache/windows-alpha 1 1
dotnet run --project src/Ctr2.Desktop -- simulate-level cache/windows-alpha 1 1 --ticks 120 --cut 30:0:0:400:138 --snapshot local/cut.png
```

Use preview sizing/resolution options to inspect other viewports. `--ticks` advances
before the first frame. Repeat `--cut tick:rope:segment:x:y[:impulseX:impulseY]` for
recorded input: ticks, ropes and pre-cut segments are zero-based; crossing points
are logical coordinates, and optional tip impulses use kilogram-meters/second.
Cuts run before their selected tick. Only one cut per rope is supported.
`--release-at` retains the earlier diagnostic that removes the final candy link.

See [physics scope and fixture provenance](src/Ctr2.Engine/Physics/PHYSICS.md).
Synthetic numerical fixtures from C++ Box2D validate supported scenarios;
original-game runtime, animation and visual parity remain unverified. Swept
circle/static-convex contacts prevent tunneling through thin terrain in the tested
scenarios. This uses an analytical sweep, with bounded contact-only substeps, rather
than the complete Box2D time-of-impact solver.

Stationary stars use the matching master sensor radius and current candy position
at the start of each fixed tick. Collection removes the gold diagnostic marker;
the console reports zero-based contact events and the collected/total count.
Fast passages can skip sensors, matching their unswept contact behavior. Moving
stars fail explicitly. Star artwork, particles, sound and Om Nom collection remain
pending.

Om Nom now uses a dynamic compound polygon body with authored density, gravity,
damping and fixed rotation. Triangle fixtures collide with static terrain; artwork
follows the simulated body origin and angle, independently of its offset center of
mass. Polygon contacts are discrete and can tunnel at high speed. Broader intro
sequences, remaining gameplay adjustments and general dynamic-body collision remain pending.

The opt-in `play-level` slice adds character/candy contact, consumption and completion.
Development now prioritizes physics verification before additional presentation.
Rotating rope/spring anchors have headless native comparisons; the engine remains
a subset, with broadphase/contact ordering, general dynamic-body collision and
native continuous collision still pending. Next: verify those physics paths, then
game object bodies/materials/attachments and physics-based mechanics. Presentation
and web work follow the relevant physics gates. See [physics scope and verification](src/Ctr2.Engine/Physics/PHYSICS.md).
