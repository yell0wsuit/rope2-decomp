# Candy, cutting, terrain and star physics slice

This is an altered C# subset of Erin Catto's Box2D 2.2.1. See
[the original license](BOX2D_LICENSE.txt). It implements always-awake circle bodies,
center-anchored rope/soft-distance joints and circle/static-convex contacts.
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
Polygon/circle impulses persist across face and vertex transitions within a
fixture. Touching contact edges use reverse creation order within each traversed
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
`smoothFactor`. Ear clipping produces counterclockwise triangle fixtures in meters.
Translation and authored polygon rotation apply to both drawing and collision.
Material sensors remain visual-only in this solid-contact subset.

Geometry validates finite coordinates, duplicate edges, nonadjacent intersections,
zero area and triangulation failure. Paths are bounded to 512 sampled vertices,
smoothing factors 1–16, and 4096 total triangles. Dynamic materials and normalized
paths fail explicitly. The diagnostic fill shares the smoothed boundary; native
texturing, scratches and the original triangle ordering remain unverified.
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
GJK/separation-function TOI solver. Circle/circle impacts, dynamic polygons, native
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
requires body/intro support before consumption and completion can be faithful.

## Numerical fixtures

The private C++ harness uses licensed upstream Box2D 2.2.1 with linear slop changed
from `0.005f` to `0.001f`. No other CTR2 patches are applied. Harnesses and tools stay
under ignored local storage; only CSV states and synthetic C# scene definitions
belong in commits. Tests require neither native compilation nor game assets.

| Fixture | Scenarios and columns |
| --- | --- |
| `PhysicsReference.csv` | Free fall/damping, slack/taut ropes, chains, springs, multiple anchors, independent islands, rope updates and diagnostic release. `scenario,tick,body,x,y,vx,vy` |
| `RopeCutReference.csv` | Synthetic `game_cut` chain, cut before zero-based tick 30 with crossing `(30,20)` logical units and tip impulse `(0.001,0)`. Same columns as above; new tips are body indices 4/5. |
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
masks and use the original 0.00002 position tolerance.

## Remaining acceptance work

There is no general Box2D continuous solver, sleeping, circle/circle collision,
dynamic terrain, noncenter anchors, candy consumption, moving stars, Om Nom
collection, hazards, intro or completion logic. Complex contact
persistence/order, native triangle ordering and original recorded-input parity
need further verification. The rope draws physical polylines without native
texturing; candy flare/skins/masks and cut presentation remain pending. Web work
follows a playable PC vertical slice.
