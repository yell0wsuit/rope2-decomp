# Candy, character, cutting, terrain and star physics slice

This is an altered C# subset of Erin Catto's Box2D 2.2.1. See
[the original license](BOX2D_LICENSE.txt). It implements always-awake circle and compound polygon bodies,
circle center-anchored rope/soft-distance joints and circle/polygon contacts against
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
failed additions do not mutate either budget. Polygon joints fail explicitly until
local anchor constraints are supported.

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
CCD, general dynamic-body collision, standing/gameplay adjustments and character
star collection remain pending. The opt-in gameplay layer below adds feeding,
first-entry arrival and completion without a general dynamic-contact solver.

## Numerical fixtures

The private C++ harness uses licensed upstream Box2D 2.2.1 with linear slop changed
from `0.005f` to `0.001f`. No other CTR2 patches are applied. Harnesses and tools stay
under ignored local storage; only CSV states and synthetic C# scene definitions
belong in commits. Tests require neither native compilation nor game assets.

| Fixture | Scenarios and columns |
| --- | --- |
| `PhysicsReference.csv` | Free fall/damping, slack/taut ropes, chains, springs, multiple anchors, independent islands, rope updates and diagnostic release. `scenario,tick,body,x,y,vx,vy` |
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

This is not the full Om Nom state machine: procedural bump/rolling/standing, turning,
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
