# Windows level layout contract

The level preview selects **one-based pack IDs and level entries** from `assets/maps/sortedlist_desktop.xml`. Mobile `sortedlist.xml` and alphabetical filenames are not interchangeable with desktop order. In the supported Windows dump, pack 1 starts with `y01b.xml`, then `y02b.xml`.

`LevelReader` preserves flat settings and object attributes in read-only collections. It accepts the original optional bracketed 32-hex-character prefix as metadata, without treating it as an authenticated signature. XML parsing prohibits DTDs and external entity resolution. Documents are limited to 4 MiB, 20,000 XML nodes, depth 8, 64 attributes per element and 65,536 characters per settings/object attribute. Canvas dimensions are 1–16,384 editor units. Index paths must be flat `.xml` filenames; selected cache components cannot be symbolic links/reparse points. Missing files, duplicate pack IDs and malformed known coordinates fail explicitly. As in the original scene loader, objects come from every non-settings layer in document order regardless of layer name (Windows maps use `Objects`, `Layer 1` or `Layer 4`). Only Om Nom, throwers, Bookas, candy, fireballs and circle/rectangle/polygon shapes register editor IDs; a later duplicate replaces an earlier one. A grab whose target name is not registered keeps no rope and is reported as a diagnostic.

## Coordinates traced from reconstructed native code

The local reconstructed Android source provides the initial contracts; Windows map data agrees with the first two layouts. Native Windows visual/physics parity remains unverified.

- `GameScene::loadMap` and `GrabGeneric::initWith`: multiply editor `x` and `y` by **2**. Negative coordinates remain negative.
- `GrabGeneric::initWith`: multiply authored rope `length` by **2**. The preview shows a straight connection, not a rope constrained to that rest length.
- `PhysicsHelper::toB2Vector` / `toVector`: **200 logical units per Box2D meter**, with no Y inversion.
- `PhysicsHelper::createBodyFor`: authored angles convert from degrees to radians. Downward-positive screen coordinates make positive angles appear clockwise.
- `PhysicsHelper` editor path parsing: double local polygon vertices. The preview translates those raw vertices by the doubled object position. Native smoothing, normalization, inserted center vertices, triangulation and textured decoration are pending; the filled preview outline is not collision geometry.
- `Omnom::initWith`: center-anchored animation with translation **(-8, -11)** logical units. Preview animation ID 85 is the supported Windows Om Nom resource, with scene `IDLE` and its default costume. The root canvas is centered before the character angle/translation and the shared viewport transform. Texture resolution does not change level coordinates.

For zero-offset grab attachments to a named candy, the preview connects the initial grab and candy centers. Nonzero attachment offsets and other target kinds are reported as unverified and their lines are omitted, rather than assuming a local/world coordinate frame. The original attributes are retained.

Pack 1 level 1 has a 320×480 editor canvas, fitted as **640×960 logical units**. Its candy starts at (394,294), grab at (400,86), rope rest length 210, and Om Nom at **(128,-194)**. The native first-level introduction moves Om Nom from above the scene; the static preview keeps this off-screen starting position. Pack 1 level 2 places Om Nom at (154,684), allowing the character placement to be inspected without implementing the intro.

## Diagnostic scope

`preview-level` clips the uniformly fitted authored canvas and uses the existing DX-derived viewport transform. It renders the assembled Om Nom and simple geometry: coral candy markers, gold star markers, wheat grab points/straight links, brown unsmoothed polygons and magenta unknown positioned objects. Objects without known positions remain in the data and diagnostics. These marker sizes/colors are diagnostic choices, not recovered game art or collision dimensions.

All scene geometry stays static; only the selected Om Nom pose time changes. Unsupported object kinds and off-screen starting positions are printed. Known marker kinds also lack their gameplay behavior. Ropes do not sag, collide or cut; stars are not collected, and no score, win condition, tutorial, special intro, background, original camera, game-selected hat or level completion is implemented. The next milestone is source-faithful candy/rope physics and recorded-input verification before one playable level.
