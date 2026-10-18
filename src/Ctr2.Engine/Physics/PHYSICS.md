# Candy, character, cutting, terrain and star physics slice

This is an altered C# subset of Erin Catto's Box2D 2.2.1. See
[the original license](BOX2D_LICENSE.txt). It implements always-awake circle and compound polygon bodies,
rope/soft-distance joints with rotating body-local anchors and circle/polygon contacts against
static convex terrain.
It is not a complete Box2D replacement or proof of original-game runtime parity.

Implementation research stays private and outside tracked files. Do not commit
decompiled game code or reconstructed reference harnesses. Verification artifacts
use synthetic inputs and numerical fixtures. No original game assets, level data
or decompiled game code are included in the fixtures.

## Behavior contract

The matching Windows master design supplies body radius, density, gravity scale,
damping, friction, restitution and rope parameters. Editor coordinates are doubled
into logical units; 200 logical units equal one meter. Positive Y points down. Each
tick is `0.0167f` seconds, with gravity `(0, 10)` and 15 velocity / 30 position
iterations. Linear slop is `0.001f`, with polygon skin radius `0.002f`.

Ropes preserve generated material points, the shorter final bone, and coincident
initial candy/final material-point centers. Rope updates run after the world step:
relative endpoint speeds feed a 60-sample damping average. Stretching replaces the
last rope constraint with a 10 Hz / 0.3 spring and adds a whole-rope spring.
Restoration follows the reaction-force sign check. Independent islands preserve
depth-first joint/body order and stop position iterations independently.

Contacts initialize masses and restitution bias before contact warm starting and
joint initialization. Velocity solving applies joint impulses before contact
friction/normal impulses. Position solving corrects contacts before joints.
Circle impulses persist across face and vertex transitions within a fixture.
Polygon impulses persist only for matching clipped manifold feature IDs. Touching contact edges use reverse creation order within each traversed
body. The broadphase is bounded exhaustive candidate testing, not native Box2D's
dynamic tree; contact lifetime and complex multi-fixture ordering still need
broader original-runtime comparison.

`CandySimulation` requires zero initial velocities, centered grab anchors and
unrotated candy/grab placements. Unsupported definitions fail explicitly. Candy
art uses Windows atlas 133 quads 0/1, restored trim geometry, native combined
`0.92 * 0.92` scale, and clockwise body rotation independently of fixture radius.
Grab and rope-point circles are sensors and do not collide with solid terrain.

## Cuts and input

An intact rope can be cut once. Replay cuts select a bone and logical crossing;
its constraint and any whole-rope spring are removed. Two material-point tips are
created at the crossing and connected independently to their respective adjacent
bodies using actual partial lengths. All internal rope-point masses are divided
by ten. Existing velocities and damping are retained; new tips use design damping
and start at zero velocity before an optional impulse is applied. The rope's
adaptive damping/stretch update stops after cutting.

Live pointer crossings choose the first crossed bone in authored order and use a
midpoint from twenty intervals along that bone. Each tip receives a `0.001` impulse
in the swipe direction, applied before density reduction. Replay records preserve
the accepted crossing, segment and impulse; they do not rerun hit testing.
Parallel/collinear and zero-length pointer segments do not cut. Cut flashes, sound
and attached-object gameplay callbacks are pending.

SDL primary mouse coordinates convert from window units into drawable pixels,
then through the same viewport inverse used for rendering. Presses in letterbox
margins are ignored. Swipes queue in event order and apply before the next fixed
tick; focus loss, minimization, resize and queue overflow cancel held/queued input.
The queue is capped at 1024 segments. Accepted cut records print on normal exit as
`--cut tick:rope:segment:x:y:impulseX:impulseY`. Touch input is pending.

`--release-at` remains a separate diagnostic: it removes final candy attachments
before a zero-based tick without creating fragments. It skips already cut ropes.
Live time accumulates fixed ticks; bounded catch-up retains unprocessed backlog.

## Terrain geometry

Static `polygon_materialized` shapes resolve their material in the Windows master
document. Paths include the implicit local origin, double editor vertices, orient
the boundary, and sample periodic chord-parameter Akima curves according to
`smoothFactor`. The original ear clipper runs on the local logical-unit path: it
continues from each clipped ear, rejects candidates whose doubled area is below
`1e-10` or that contain any remaining vertex (boundary included), and keeps the
triangles clipped so far if it stops finding ears. Each triangle is converted to
meters, swapped to counterclockwise order and placed with the static body's
rotation and translation. Ears that collapse to zero meter-space area are clipped
without a fixture, whereas the original keeps them as zero-area Box2D fixtures.
Translation and authored polygon rotation apply to both drawing and collision.
Material sensors remain visual-only in this solid-contact subset.

Geometry validates finite coordinates, duplicate edges, nonadjacent intersections,
and zero area. Paths are bounded to 512 sampled vertices,
smoothing factors 1–16, and 4096 total triangles. Dynamic materials and normalized
paths fail explicitly. The diagnostic fill shares the smoothed boundary; native
texturing and scratches remain unverified.
Analytic synthetic geometry tests check curved square midpoints and concave area;
these are not an original-runtime geometry capture.

## Swept terrain contacts

`PhysicsWorld.ContinuousPhysics` defaults to true. The discrete step retains each
solid dynamic circle's initial transform; a subsequent analytical sweep finds the
first entry into static convex terrain expanded by the target circle radius.
Faces use ray/offset-plane intersection; rounded vertices use ray/circle roots.
The target is `max(linearSlop, circleRadius - linearSlop)`, matching the polygon
skin contribution to Box2D's time-of-impact target. Existing overlaps remain with
the discrete solver. Static anchors and sensor circles skip the sweep.

At an impact, circle position and angle advance to the hit fraction. Touching
terrain contacts receive up to 20 position iterations (Baumgarte 0.75, tolerance
1.5 slops), then 15 velocity iterations without warm starting. The remaining tick
integrates the resulting velocity without repeating gravity, damping or joint
solving. Further impacts repeat this process, capped at 32 per circle per tick.
Budget exhaustion retains the last safe transform. Time-of-impact impulses are
not cached into the next discrete step. A manifold disagreement also retains the
safe transform rather than continuing through the fixture.

This is an analytical circle/static-convex subset, not a port of Box2D's general
GJK/separation-function TOI solver. Circle/circle impacts, polygon sweeps, native
contact lifetime/order and arbitrary multi-body TOI islands remain unsupported.
The switch can disable sweeps for isolated discrete fixture verification.

## Stationary stars

Master and placement attributes merge for star sensors, including radius overrides.
Supported stars are stationary kinematic circle sensors with default collision
filtering. Nonempty paths, normalized paths, nonzero velocities, unsupported body
types and invalid radii fail explicitly. Geometry is in meters; marker drawing
converts to logical scene units independently of image density.

Star overlap runs before world integration, matching contact-begin timing. Each
star collects once through a candy circle; rope points and grab sensors cannot
collect. Collection stores an immutable star snapshot and a zero-based
`StarCollection(Tick, Star, Candy)` record. Simultaneous contacts use authored
star/candy order as a deterministic diagnostic tie-break; native callback order
is not claimed. Sensor overlap accepts ten floating-point epsilons of distance,
as Box2D's sensor distance test does. Sensors are not swept: a fast candy can pass
between contact samples without collecting a star. Star sensors add no solver
bodies and cannot alter candy/rope trajectories or island traversal.

The preview hides collected markers and reports events/counts on the console.
Star animation/particles/audio, moving stars, Om Nom collection and score/save
integration are pending. Level 1's off-screen Om Nom uses a dynamic polygon and
uses the explicit first-entry arrival in opt-in play; the diagnostic remains physical-only.

## Dynamic compound characters

`ConvexPolygon` owns three to eight strictly convex counterclockwise meter-space
vertices and outward normals. Static terrain uses the same validation. Geometry
rejects duplicate/short edges, invalid winding, nonconvex outlines and coordinates
outside ten thousand meters. World-vertex snapshots use the authored body origin;
`PhysicsBody.Origin` recovers that origin from the integrated center of mass and
rotated local centroid.

Mass data preserves fixture mass, local centroid and moments of inertia about
both the authored origin and the center of mass. Triangle integration uses a
nearby reference point, as licensed Box2D 2.2.1 does. Center inertia is calculated
before shifting to the original origin, avoiding cancellation from subtracting
large origin moments. Zero density still yields the geometric centroid. Convex
circle overlap includes the polygon skin and sensor-distance epsilon, handles
rounded vertices, and supports rotated body origins.

`PolygonBodyDefinition.LoadCharacters` merges the Om Nom polygon master with its
placement and includes the implicit local zero. The original Om Nom initializer
always sets `normalizeShape`, overriding authored values: every logical-unit point,
including the implicit zero and an optional closing zero, is centered on its
vertex mean while the body origin stays at the placement. The loader then removes
the closing zero and normalizes winding while preserving the first vertex. Editor
positions and vertices divide by 100 to meters; authored angles convert to
clockwise radians. Validated material, damping, gravity, fixed-rotation, mass and
centroid data stay separate from simulation state. Unsupported initial motion,
smoothing, paths, sensor/bullet modes and collision filters fail explicitly.

The original polygon body loader creates compound triangle fixtures, including
convex outlines. `PhysicsWorld.AddPolygon` triangulates the supported convex outline
in cyclic ear order, combines fixture mass data in creation order and preserves
internal fixture skin edges. Synthetic five-vertex geometry checks the triangle
sequence. Compound inertia sums centroid moments and parallel-axis offsets directly
to retain precision for small shapes far from their local origin. `compound: false` creates a single convex fixture for isolated solver
verification. The world limits bodies to 4096 and dynamic polygon fixtures to 8192;
failed additions do not mutate either budget. Rope and soft-distance joints support
circle and polygon bodies. Explicit anchors use local meters relative to each
body origin, not its center of mass. Default overloads retain center-of-mass anchors.
The solver rotates the anchor-minus-local-center lever arms, includes both bodies'
angular inertia in effective mass, and applies angular velocity/position impulses.
Fixed-rotation bodies retain zero inverse inertia. Soft-distance constraints apply
the raw incremental velocity impulse as native Box2D does; rope limits apply the
clamped accumulated-impulse difference.

`CandySimulation.CharacterDefinitions` retains immutable initial definitions;
`Characters` holds the corresponding integrated bodies in authored order. Character
bodies use authored density, gravity, damping, friction, restitution and fixed
rotation. Linear motion integrates the center of mass. Artwork follows the body
origin and clockwise angle, retaining the authored animation canvas offset and
texture density independently of viewport scaling. The static `preview-level`
command still draws the initial authored placement.

Polygon/static-convex collision selects a reference edge, clips its incident edge
to a one/two-point manifold and restores impulses by feature ID. Friction precedes
the coupled two-point unilateral normal solve; ill-conditioned manifolds retain
one velocity point. Position correction uses both manifold points and centroid
inertia. Each compound fixture creates an independent terrain constraint. Eleven
invented single/compound trajectories compare against upstream Box2D,
including fixed rotation, bounce, slope, offset centers, free fall, triangulated
terrain and two independent bodies sharing a floor. These do not
prove native broadphase/contact-order parity or original Om Nom movement.

Character polygons use discrete contacts even when circle CCD is enabled. Polygon
CCD, general dynamic-body collision, remaining gameplay adjustments and character
star collection remain pending. The opt-in gameplay layer below adds feeding,
first-entry arrival and completion without a general dynamic-contact solver.

## Numerical fixtures

The original private C++ oracle uses licensed upstream Box2D 2.2.1 with linear
slop changed from `0.005f` to `0.001f`. A separate Android numerical profile adds
the audited AABB/angular settings below. Both joint fixture sets were independently
rebuilt with that profile and matched their slop-only versions byte for byte.
This does not validate the settings that those contact-free scenarios cannot
exercise. Harnesses and tools stay under ignored local storage; only CSV states
and synthetic C# scene definitions belong in commits. Tests require neither native
compilation nor game assets.

| Fixture | Scenarios and columns |
| --- | --- |
| `PhysicsReference.csv` | Free fall/damping, slack/taut ropes, chains, springs, multiple anchors, independent islands, rope updates and diagnostic release. `scenario,tick,body,x,y,vx,vy` |
| `JointAnchorReference.csv` | Offset circles, shifted-centroid rotating polygons, fixed rotation and two dynamic endpoints for rope and soft-distance constraints. `scenario,tick,body,centerX,centerY,vx,vy,angle,angularVelocity,anchorX,anchorY,forceX,forceY` |
| `JointTopologyReference.csv` | Coincident anchors, zero/short ropes, short springs, multiple attachments, independent islands and joint removal/recreation. `scenario,tick,body,centerX,centerY,vx,vy,angle,angularVelocity,anchorX,anchorY,forceX,forceY,jointActive` |
| `RopeCutReference.csv` | Synthetic `game_cut` chain, cut before zero-based tick 30 with crossing `(30,20)` logical units and tip impulse `(0.001,0)`. Same columns as above; new tips are body indices 4/5. |
| `PolygonContactReference.csv` | Flat/tilted/bouncing/offset/compound/fixed-rotation/slope/free-fall polygon bodies, triangulated terrain and two bodies sharing a floor. `scenario,tick,originX,originY,centerX,centerY,vx,vy,angle,angularVelocity,mass,localCenterX,localCenterY` |
| `PolygonMassReference.csv` | Four invented convex shapes at densities 0, 0.5 and 3.25. `scenario,density,mass,centerX,centerY,originInertia,centerInertia` |
| `PolygonOverlapReference.csv` | The same four shapes with seven transformed circle queries each, including face skin/tangency and vertex misses. `scenario,circleIndex,centerX,centerY,radius,originX,originY,angle,overlaps` |
| `ContinuousReference.csv` | Thin floor, restitution, slope, rounded vertex and repeated floor/ceiling impacts. `scenario,tick,x,y,vx,vy,angle,angularVelocity` |
| `StarReference.csv` | Falling candy, fast unswept sensor passage, tangent and small miss. `scenario,tick,collectedMask,x,y`; zero-based contact ticks, state sampled after integration. |
| `TerrainReference.csv` | Floor, bounce, slope friction/spin, vertex, rope-attached candy and simultaneous sloped-floor/wall contacts. `scenario,tick,x,y,vx,vy,angle,angularVelocity` |

CSV rows are headerless. Positions are meters, velocities meters/second, angles
radians and angular velocities radians/second. Samples cover tick 1 and every
tenth tick through 180. Component tolerance is `0.00002`. Sleeping is disabled;
terrain references also disable continuous physics to isolate supported discrete
contacts. `game` names identify invented test scenes, not original-game captures.
Scene definitions are in the corresponding C# tests. Continuous fixtures sample
every completed tick 1–10 under extreme gravity (800) and the two-meter translation
clamp, producing speeds near 120 m/s. Analytical root finding differs from native
TOI tolerance/search; continuous-fixture tolerances are 0.001 meters/radians and
0.01 meters/second or radians/second, while discrete/contact-free tolerances remain
unchanged. Sensor fixtures sample zero-based ticks 0–119, require exact collected
masks and use the original 0.00002 position tolerance. Polygon mass fixtures require
0.00002 component tolerance; circle overlap fixtures require exact Boolean results.
Polygon fixtures contain only invented geometry, not Om Nom game vertices.

Joint-anchor fixtures sample every tick 1–180 for both bodies in eight independent
scenarios: 2,880 states. State/anchor tolerance is 0.00002; reaction-force tolerance
is 0.0002 newtons. Every component must be finite. The native harness is
`local/physics-reference/JointAnchorReference.cpp`, compiled with clang C++11,
`-O0 -ffp-contract=off` against the local 2.2.1 tree. An archive comparison confirmed
only `b2Settings.h` differs; the profile changes linear slop to 0.001. Fixture
SHA-256: `d590d946c02efcd127133ac2af65704ae67532da2405dc357d87c2664c57472d`.
These contact-free scenarios verify rotational joint behavior; they do not verify
game-specific broadphase, collision callbacks or general TOI behavior.

Joint-topology fixtures add eleven scenarios, sampled every tick 1–240, with
6,720 body states. They retain the same state/anchor and reaction-force tolerances,
require finite values and complete tick/body ordering, and verify active-joint
state. Warm-started joints are removed before step 61 and recreated before step 91
with a changed local anchor and length. Multi-attachment scenarios observe both
joint reaction forces and the extra joint's first anchor; its second anchor on
the shared body is not separately sampled. Independent-island scenarios observe
both endpoints of each joint. No additional engine changes were needed for these
comparisons. Fixture SHA-256:
`0bf54256e755ef6b22f3403f53a251eb7e0772b87f49fc23886e7ca93b9ef125`.

## Audited Android numerical profile

The supplied Android decompilation establishes these settings within the inspected
dynamic-tree, shape, joint, contact-solver, TOI and world-step code. This is a
bounded audit, not proof that all game patches or control-flow differences are known.

| Setting | Upstream 2.2.1 | Android reference | Evidence scope |
| --- | --- | --- | --- |
| Linear slop | 0.005 m | 0.001 m | Rope/distance initialization and convergence thresholds |
| Polygon skin | 0.01 m | 0.002 m | Polygon/edge/chain constructors; twice linear slop |
| Fixed AABB padding | 0.1 m | 0 m | Dynamic-tree proxy creation and movement |
| AABB prediction multiplier | 2 | 1 | Dynamic-tree displacement expansion |
| Joint angular slop | 2 degrees | 1 degree | Revolute, weld and prismatic limit correction/termination |
| Angular sleep tolerance | 2 degrees | 2 degrees | Island sleep test; independent of joint angular slop |

The angular-slop degree expression is inferred from consistent rounded decompiler
literals; their display does not establish exact original float bits. The separate
native profile uses `(1 / 180) * pi`, preserving upstream expression structure.
Polygon skin and inspected TOI/contact thresholds already follow from the linear
slop patch; they must not be independently scaled a second time. Inspected native
TOI target/tolerance, correction/iteration limits, velocity threshold and ordinary
world clamps are consistent with upstream formulas after that patch.

A synthetic native dynamic-tree probe confirms that the Android numerical profile
creates an unpadded `(0,0)`–`(1,1)` proxy. A small `(0.05,0.05)` translation causes
reinsertion; the slop-only profile retains its padded proxy. This establishes a
reference behavior for future managed broadphase tests, not observed execution of
the original Android or Windows game.

Two custom Android behaviors also require future coverage: mouse-joint
`SetAnchorB` transforms a world point to a local anchor without a visible wake/reset,
and contact creation returns null if either fixture has a null shape. The settings-only
oracle profile does not implement these APIs/guards. No gameplay caller of the mouse
setter was found within the bounded supplied cpp/h search; do not invent one.

The game overrides world sleeping to false, keeps continuous physics enabled and
steps at `0.0167f` with 15 velocity and 30 ordinary position iterations. Native TOI
position solving uses its separate 20-iteration limit. These policies must be kept
distinct from constructor defaults and the ordinary step's iteration counts.

Windows behavioral equivalence remains unknown. Static artifact identity confirms
the supplied Windows executable and ARM Android library, but available deep native
analysis providers were unavailable during this audit. Windows assets continue to
use Windows metadata; the behavioral evidence above is Android-only. Original-runtime
traces or Windows instruction-level analysis remain necessary before claiming
cross-build or bitwise parity.

## Physics-first verification priority

Further development prioritizes source-faithful physics before game object or
presentation expansion. The managed engine remains a bounded subset, not a
complete or proven 1:1 replacement for the game's physics. The audited numerical
profile and synthetic comparisons still leave control-flow and runtime differences
to verify.

Next gates are remaining joint APIs/limited-angle cases, source-compatible broadphase and
contact lifetime/order, general two-body contact solving, distance/GJK and native
TOI islands. Then verify Windows game body construction, materials, fixtures,
attachments and physics-based gameplay against authored input traces. Passing
synthetic fixtures is evidence for those scenarios; original-game trace comparisons
are a separate acceptance gate. Presentation expansion follows those gates.

## Opt-in forest gameplay

`CandySimulation(..., play: true)` adds a bounded ordinary dry-forest slice with
one candy and one Om Nom. Other mechanics and multiple protagonists/candies fail
explicitly. The desktop host selects first-entry arrival only for pack 1/entry 1;
other negative spawns still fail. Diagnostics remain opt-out
and retain their numerical fixture behavior.

Current compound polygon/circle overlap at pre-step positions starts consumption
once. Stars are processed first in deterministic authored order; original native
callback ordering is not claimed. Eating candy becomes a sensor, loses gravity,
and receives velocity toward Om Nom's body origin. It fades for 0.2 seconds before
post-step removal clears world joints/contacts and all terminal rope attachments,
including cut tails. It cannot collect further stars during this phase.

Ground presentation follows `MOUTH_CLOSE` into `CHEWING`. Matching scene/revision
completion wins once; the traced 2.8-second timer is a fallback. Mouth opening and
closing use 150/170 logical-unit distance thresholds. A lost candy requests `FAIL`
and waits for its animation callback; a character leaving the dry canvas loses
immediately, including while chewing. Dry bounds use the source margin plus 150
logical units, with zero screen offset in this fitted diagnostic canvas.

Falling presentation retains a 59-entry zero-initialized velocity history. Newest
5/29/59-sample averages drive the source directional fall threshold and sustained
settling conditions. Terrain contact latches through the supported world contact
manifolds; sensor terrain cannot land a character. Airborne mouth/chewing scenes
retain distinct completion callbacks and stale-revision guards. Restart discards
the motion history and contact latch. This does not change any body velocity,
collision constant or solver iteration count.

This is not the full Om Nom state machine: sliding, turning,
costume substitutions, particles/audio, native contact ordering and general CCD
remain pending. Physical overlap is a discrete compound fixture test, not the
complete native dynamic-contact graph. The original fade completion queues
destruction; this subset retires after the completed fixed tick reaches 0.2 seconds.

## First forest arrival and swipe hint

The first desktop forest entry preserves its authored negative character position
and ordinary dynamic-body fall. Source initialization starts idle and suppresses
the ordinary greeting. No separate camera trajectory, delay or input lock is
invented. Its 30-leaf background burst runs solely in logical presentation units:
angle 90 +/-60 degrees, speed 280 +/-120, radial acceleration 320 +/-160,
tangential acceleration 60 +/-20, gravity (0,400), lifetime 1.5 seconds, initial
independent scales 1.2 +/-0.3 and end scales 0.4 +/-0.1. Leaf variants, rotation
and initial position noise are bounded and deterministic; no particle enters the
physics world. Restart rebuilds the initial burst and fixed-tick timing.

The matching Windows executable uses image resource 163, quads 9 through 11.
The Android reference uses resource 162; that table is not used with Windows
textures. Sample rectangles and density come from the verified Windows catalog.
Leaves use trimmed dimensions about their centers, preserving original particle
layout. Native random sequences, exact color/blend rounding and original runtime
presentation remain unverified.

One authored `tutorial_swipe` is supported independently of arrival. Position
doubles into logical units and angle remains authored degrees. Delay expires on
fixed ticks, then Windows animation 104 `SWIPE_LOOP` repeats. Primary pointer down
before the delay removes the hint; afterward it fades over 0.4 seconds. Accepted
scheduled replay cuts apply the same dismissal notification. This replays accepted
cuts, not all original raw touch events. Pause freezes hint/particle timing, and
restart restores the delay. Other tutorial kinds and multiple hints fail explicitly.
Delay accumulation and subtracting the native remaining delay may differ by one
floating-point tick; exact original timing/visual parity still needs runtime comparison.

## Remaining acceptance work (broader engine)

There is no general Box2D continuous solver, polygon CCD, sleeping, dynamic-body solid collision,
dynamic terrain, noncenter anchors, moving stars, hazards or broader intro logic.
Broader consumption and completion mechanics beyond the opt-in forest slice,
complex contact
persistence/order, native triangle ordering and original recorded-input parity
need further verification. The rope draws physical polylines without native
texturing; candy flare/skins/masks and cut presentation remain pending. Web work
follows a playable PC vertical slice.

## Static terrain impact presentation

The supported polygon solver reports each manifold's first normal impulse and
first world point after position solving, in the current solver contact order.
Om Nom follows static LAND thresholds from `Omnom::postSolve`; XML material names
do not select the source dynamic WOOD branch. Upright idle impacts above .1 use
1.4 times the capped impulse. Falling/rolling/falling-mouth-close impacts above
.2 classify bottom/top/left/right from the first point relative to the body center.
The impulse cap is .5. The root deforms and shifts over .03 + .06 seconds without
changing physics or resetting authored child scene time. Procedural completion
owns its fixed clock and rejects authored scene callbacks. Motion at completion
selects rolling or idle; rolling settles using the source angular windows (newest
five samples, and older samples 29 through 58 divided by 59). These source windows
are intentionally asymmetric. Native dynamic-tree contact ordering, dynamic WOOD
reactions, sliding and original runtime visual comparison remain pending.

## Standing after settling

Idle and chewing bodies moving slower than .01 m/s prepare to stand when their
normalized, truncated integer angle lies strictly outside [-60,60] degrees.
The root stretches to (.85,1.3) and shifts -10 logical units on the left or +10 on
the right over .1 seconds, then returns over .05 seconds. The art offset (-8,-11)
is inside this scale, as in BaseElement::preDraw; root position is outside it.
Completion applies (0,-.19) at the center of mass and a signed angular impulse:
+.008 on the left at angles >= -110, otherwise +.014; -.008 on the right at
angles <= 110, otherwise -.014. Fixed rotation suppresses angular response through
zero inverse inertia. No position or orientation teleport is applied.

Ordinary preparation then selects Windows STAND_UP, whose current-revision
completion returns to IDLE. Chewing preparation retains its child scene instead,
as the source does, and the independent 2.8-second consumption timer remains active.
Replacing the scene cancels preparation; paused/restarted sessions cannot apply
an old preparation impulse. Tick durations remain .0167 seconds, so sampled poses
and completion quantize to the first fixed tick at or beyond each duration.
Authored inter-scene transitions, sliding/turning and original runtime visual and
trajectory parity remain pending.
