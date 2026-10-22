# Candy, character, cutting, terrain and star physics slice

This is an altered C# subset of Erin Catto's Box2D 2.2.1. See
[the original license](BOX2D_LICENSE.txt). It covers the Box2D surface the game uses:
static, kinematic and dynamic circle and compound polygon bodies with native sleeping and waking with native
velocity, impulse, force, teleport, type and mass controls; rope, rigid and soft distance,
revolute and prismatic joints; circle/circle, polygon/circle and polygon/polygon contacts,
with static convex terrain as identity-frame static bodies; collision filters and sensors;
a native dynamic-tree broadphase; native time-of-impact continuous collision; ray casts,
AABB queries and point tests; and queued Begin/End events, world manifolds and PostSolve
reports for the game's contact callbacks. Every native-trace fixture is bit-identical to
the Android-profile oracle on its platform. It is still not proof of original-game runtime parity.

Implementation research stays private and outside tracked files. Do not commit
decompiled game code or reconstructed reference harnesses. Verification artifacts
use synthetic inputs and numerical fixtures. No original game assets, level data
or decompiled game code are included in the fixtures.

## Deliberately unported Box2D features

Do not port these just because Box2D 2.2.1 has them. Each was checked against the
game's own code (the decompiled `Local/` tree and `MainNativeHelper.cpp`, Box2D sources
excluded) and its level data, and nothing in the game reaches it. Porting them adds
untested surface with no reference behavior to verify against.

| Feature | Evidence it is unused |
| --- | --- |
| Mouse joint | `GameScene` only calls `SetTarget`/`DestroyJoint` behind a null check; no `b2MouseJointDef` is ever created. The CTR2 profile's custom `SetAnchorB` is likewise uncalled. |
| Weld, wheel, pulley, gear, friction and motor joints | No definitions or calls. The game uses only revolute, prismatic, distance and rope joints. |
| Edge and chain shapes | Only circle and polygon shapes are created. |
| Bullets | No `SetBullet` calls. `PhysicsHelper` reads `bodydef.bullet` from design data, but every authored body in the Windows maps and the Android assets has `bullet="false"`. |
| World gravity changes | Every world is created with (0, 10) and nothing calls `SetGravity`. Per-body `SetGravityScale` is used and ported. |
| Variable time steps, sub-stepping and disabled warm starting | Worlds always step with dt 0.0167 (zero `frameStep` pauses skip the step). No `SetSubStepping` or `SetWarmStarting` calls. |
| Contact filter callbacks, `PreSolve` and `SetEnabled` | No custom `b2ContactFilter`, no real `PreSolve` override and no contact disabling. The game's "pre-solve" delegates run from `BeginContact`. |
| Inactive bodies (`SetActive`), `ApplyForce`/`ApplyTorque` at runtime, `SetAutoClearForces`, `ShiftOrigin` | No calls. |
| Distance/rope runtime setters (`SetLength`, `SetFrequency`, `SetDampingRatio`, `SetMaxLength`) | No calls. Rope stretch changes destroy and recreate joints instead. |

Some of these are already present for other reasons (for example `ApplyForce` and
`ApplyTorque`); keep them, but do not extend them without a caller. Port one of the
features above only when a newly supported data source or game build demonstrably
reaches it: re-run the same call-site and data search first, record the evidence
here, and verify the new path against an independent native oracle like every other
checkpoint.

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
body. Candidates retain creation order until their proxy bounds separate, including
empty-manifold intervals. A lost manifold clears cached impulses. Broader original-runtime
comparison remains necessary for complex multi-fixture and callback ordering.

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
A press inside the canvas also queues a scene tap ahead of its swipe segments, so frogs see it
before the next tick; consumed taps print as `--tap tick:x:y`. The queue is capped at 1024 entries. Accepted cut records print on normal exit as
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
`TerrainGeometry` now serves drawing only; scene collision comes from the design
body factory's polygon branch (below), which keeps zero-area ears as fixtures, builds
sensor materials as sensor fixtures and supports dynamic polygon materials.

Geometry validates finite coordinates, duplicate edges, nonadjacent intersections,
and zero area. Paths are bounded to 512 sampled vertices,
smoothing factors 1–16, and 4096 total triangles. Dynamic materials and normalized
paths fail explicitly. The diagnostic fill shares the smoothed boundary; native
texturing and scratches remain unverified.
Analytic synthetic geometry tests check curved square midpoints and concave area;
these are not an original-runtime geometry capture.

## Native continuous collision (TOI)

`PhysicsWorld.ContinuousPhysics` defaults to true, as the game sets it, and runs a
port of Box2D 2.2.1 `b2World::SolveTOI` after each discrete step's proxy
synchronization and pair discovery. Bodies keep native sweeps: island solves store
each dynamic, kinematic and touched static body's start center and angle, and
teleports, mass resets and type changes reset them as native does. Each step clears
every body's start fraction and every contact's cached TOI and sub-step count.

The solver repeatedly scans candidates in native contact-list order (newest first).
It skips disabled contacts, contacts past eight sub-steps, sensors, static pairs and
dynamic pairs (the game authors no bullets). For the rest it aligns both sweeps to
the later start fraction and computes `b2TimeOfImpact`, caching the result. The
earliest TOI advances both bodies, and a mid-TOI `Update` recomputes the manifold,
carries matching stored impulses and may emit Begin/End events. A non-touching
result restores the sweeps and disables the contact until its next update. Otherwise
the TOI island gathers each dynamic body's other touching contacts with static and
kinematic bodies, tentatively advancing those neighbours. `b2Island::SolveTOI` then
runs 20 TOI position iterations, moves the TOI bodies' sweep starts to the corrected
positions, and solves 15 unwarmed velocity iterations on fresh constraints so stored
warm-start impulses stay untouched. It integrates the remaining sub-step and reports
PostSolve. Displaced dynamic bodies resynchronize proxies from their new sweep start
and invalidate their cached TOIs, and new pairs are discovered before the next scan.

`b2TimeOfImpact` is ported literally: sweep normalization, the conservative-advancement
outer loop over cached GJK distance without radii, point/face-A/face-B separation
functions, deepest-point push-back bounded by eight vertices, and 50-iteration
bisection/secant root finding. The native TOI position solver applies body A's mass
only for TOI bodies but always applies body B's; with no bullets, every other body in a
TOI island is static or kinematic, so ordinary masses give identical results.

`ToiReference.json` compares 2,636 standalone native TOI queries (random and
deterministic circle/polygon sweeps, partial `tMax`, nonzero start fractions and local
centers). `ContinuousWorldReference.json` compares five native continuous worlds
through tick 100: thin-terrain tunneling with a sensor layer, fast rotating boxes and
compounds against floors and walls, kinematic sweepers, fast bodies against static
circle bodies, and a burst of simultaneous impacts. It checks bodies, proxies,
TOI-phase events, TOI PostSolve reports and candidates. Both are bit-identical on the
oracle platform. Fixture SHA-256: `3bb4f54e5b1bdd56dcd5cc2d40ed30944f51aa6d41d6bf2404a863502d347a64` and `f05be01202b91910958eaf9df58768fa9411f0a74ec752bb4caa9a9a9778d401`.

The earlier analytical circle sweep has been removed. The continuous native
references from earlier checkpoints were generated with native TOI enabled, and they
now pass against the port as well.

## Stars

Master and placement attributes merge for star sensors, including radius overrides.
Supported stars are kinematic circle sensors with default collision filtering.
Normalized paths, nonzero authored velocities, unsupported body types and invalid
radii fail explicitly. A star with a `path` moves like a helicopter (see Scene
helicopters): its `MovableBodyObject` sets the body's velocity after each step, in
scene order after the helicopters, collected or not. The hint journals' star
snapshots match these paths exactly, cyclic and ping-pong alike. A fruit challenge
destroys every star after its first step (see Challenge variants). Geometry is in meters; marker drawing
converts to logical scene units independently of image density.

Star overlap runs before world integration, matching contact-begin timing. Each
star collects once through a candy circle or, as in `Star::handlePreSolveCollision`,
through Om Nom's polygon; rope points and grab sensors cannot collect. Collection
stores an immutable star snapshot and a zero-based `StarCollection(Tick, Star, Candy)`
record, with candy -1 for Om Nom. A candy's star excites Om Nom (`Omnom::excite`):
idle, he shows EXCITED, EXCITED_MORE from the second star and EXCITED_MOST from the
third, and returns to idle when it ends; carried by a helicopter, he shows
WITH_HELICOPTER_EXCITED. In any other state the star is only counted. While excited
he does not open his mouth, slide, turn or stand up, but can still start falling. Simultaneous contacts use authored
star/candy order as a deterministic diagnostic tie-break; native callback order
is not claimed. Sensor overlap accepts ten floating-point epsilons of distance,
as Box2D's sensor distance test does. Sensors are not swept: a fast candy can pass
between contact samples without collecting a star. As in the scene, each star
also owns a kinematic sensor body built by the design body factory and kept after
collection. It cannot push the candy, but its proxy takes broadphase IDs and so
takes part in pair ordering. Collection itself still uses the geometric test above.

The preview hides collected markers and reports events/counts on the console.
Star particles, audio and score/save integration are pending. Level 1's off-screen Om Nom uses a dynamic polygon and
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

The scene builds Om Nom like its initializer: the Om Nom master design, the authored
attributes and a forced `normalizeShape=true`, passed through the design body
factory's polygon branch, so its fixtures are the smoothed path's ear-clipped
triangles at the round-tripped editor origin. `PolygonBodyDefinition` below keeps
the validated authored data but no longer builds the scene body.

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
in cyclic ear order, combines fixture mass data in reverse creation order and preserves
internal fixture skin edges. Synthetic five-vertex geometry checks the triangle
sequence. Ordinary compound inertia sums float origin moments in native fixture-list
order, then shifts once to the center of mass. If cancellation makes that result
nonpositive, the managed small-shape extension recomputes centroid moments and
parallel-axis offsets with double intermediates; native Box2D asserts in that case.
This fallback is outside native parity claims. `compound: false` creates a single convex fixture for isolated solver
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

Character polygons take part in native TOI like every other dynamic body. Remaining
gameplay adjustments remain pending. The opt-in gameplay layer below adds feeding,
first-entry arrival and completion without a general dynamic-contact solver.

## Numerical fixtures

The original private C++ oracle uses licensed upstream Box2D 2.2.1 with linear
slop changed from `0.005f` to `0.001f`. A separate Android numerical profile adds
the audited AABB/angular settings below. Both joint fixture sets were independently
rebuilt with that profile and matched their slop-only versions byte for byte.
This does not validate the settings that those contact-free scenarios cannot
exercise. Harnesses and tools stay under ignored local storage; only synthetic CSV/JSON states
and synthetic C# scene definitions belong in commits. Tests require neither native
compilation nor game assets.

| Fixture | Scenarios and columns |
| --- | --- |
| `SleepReference.json` | 960 native ticks with sleeping allowed: settling stacks woken by new contacts, an intro-style teleported kinematic chain, every wake API and TOI wakes. Exact awake flags/proxy IDs/events/candidates; sleep timers, bounds and six body-state components. |
| `PrismaticReference.json` | 900 native ticks: banner limits/motor and runtime calls, rotated dynamic slider, equal limits, free slider, contacts with recreation and limit transitions. Exact limit states/candidates; anchors, reactions, motor force, translation, speed and six body-state components. |
| `ToiReference.json` | 2,636 standalone native time-of-impact queries over circle/polygon sweeps. Exact states and times. |
| `ContinuousWorldReference.json` | 500 native continuous ticks: tunneling, rotating polygons, kinematic sweepers, static circle bodies and impact bursts. Exact proxies/events/report identity/candidates; bodies, bounds and TOI impulses. |
| `QueryReference.json` | Five snapshots of a moving mixed world: 32 ray casts per snapshot under all/closest/stop/filter callbacks, 8 AABB queries with early stops, 96-point tests per fixture and swept fixture bounds. Exact report order and labels; points, normals, fractions, bounds and body states. |
| `DistanceJointReference.json` | 750 native ticks: rigid distance pendulums and compression, zero-rest-length and banner springs, colliding/blocking rope and spring pairs, world-anchor initialization and coincident anchors. Exact candidates; anchors, reaction forces and six body-state components. |
| `ContactReportReference.json` | 480 native ticks: terrain circles/compounds/boxes with restitution, multi-island body pairs, redundant two-point manifolds, sensors and teleports. Exact event/report identity, order and impulse counts; Begin-time and PostSolve world manifolds, impulses and six body-state components. |
| `BodyTypeReference.json` | 900 native ticks: static/dynamic fruit-style toggles, offset-COM polygon type and density resets, kinematic pushers and island bridges, explicit mass data and roped body freezing. Exact types/proxy IDs/candidates/events; mass, inertia, centers and six body-state components. |
| `BodyControlReference.json` | 900 native ticks: velocity setters, point/angular impulses, accumulated forces/torques, gravity-scale changes and teleports. Finite body states/proxy bounds, exact proxy IDs and pre-/post-step candidate/event metadata. |
| `RevoluteReference.json` | 2,340 native ticks: free/motor hinges, lower/upper/equal/narrow limits, equal-width threshold neighbors, fixed rotation, offset COM, contacts and topology changes. Finite body/anchor/reaction/motor state and exact limit/candidate/event metadata. |
| `FixtureSensorReference.json` | 720 native world ticks: mixed triangle/circle/polygon sensors, terrain initialization/toggles, body broadcasts, filters/joints and proxy reuse. Exact per-fixture flags/events/candidates/proxy IDs, finite bounds/body states and unchanged mass. |
| `SensorEventsReference.json` | 1,170 native world ticks: sensor passages, mixed/compound pairs, terrain orientation, sensor/solid toggles, masks/joints and destruction/replacement. Exact ordered Begin/End snapshots, all fixture candidates/manifold counts and finite body states. |
| `GjkReference.json` | 1,752 native standalone distance queries: fresh/warm caches, circle/segment/polygon hulls, transforms, radii on/off, near-threshold gaps and cached triangle jumps. Witnesses/distance/metric, exact iterations/simplex support IDs/order and overlap. |
| `CollisionFilterReference.json` | 720 native ticks: masks, signed groups, asymmetric acceptance, terrain, compound fixtures, joint suppression and early new-fixture discovery. Exact candidate lifetime/order, proxy IDs/bounds and six body-state components. |
| `PolygonBodyReference.json` | 1,800 native ticks: flat/tilted/reversed polygon pairs, restitution, fixed rotation, compound/offset shapes, stacks, replacement and narrow conditioning. Exact body/terrain candidates and clipped feature IDs/order, plus six body-state components. |
| `PolygonCircleReference.json` | 1,800 native ticks: face, left/right vertices, containment, rotated/fixed and compound polygons, offset COM, static circle and circle-first creation. Exact mixed/terrain candidate orientation/order/touching and six body-state components. |
| `CircleContactReference.json` | 1,800 native per-tick snapshots of static/dynamic circle impacts, oblique friction/spin, restitution, tangent/coincident centers, stacked contact islands, mixed joint/contact islands, joint refiltering and cached material changes. Exact circle/terrain candidate orientation/order/touching plus six body-state components. |
| `ContactLifecycleReference.json` | 1,680 native per-tick snapshots: corner, bounce, centered/offset rotating compounds, late terrain and body removal/replacement. Exact contact order/identity/touching and proxy IDs; predicted bounds and six body-state components. |
| `BroadPhaseReference.json` | 110 standalone native proxy snapshots: pool growth/reuse, motion, query order, candidate generation and teardown. |
| `PhysicsReference.csv` | Free fall/damping, slack/taut ropes, chains, springs, multiple anchors, independent islands, rope updates (including candy slowdown) and diagnostic release. `scenario,tick,body,x,y,vx,vy` |
| `BalloonReference.json` | 600 native ticks of balloons: tap and swipe pops, stratosphere gravity/damping blending, and fly-away on rope cut. Activity flags plus every alive body including gravity scale and damping. |
| `BalloonGeneratorReference.json` | 600 native ticks of balloon generators: a swinging candy spawning three fanned balloons (one born dead inside a static block), an untouched generator, a cut and a tap of generated balloons, and a falling candy beside an authored balloon. Rope count, balloon and generator flags plus every alive body including gravity scale and damping. |
| `MagnetReference.json` | 600 native ticks of magnets: a swinging candy between two magnets toggled by presses from the cached pose (one and two active), and a free candy pulled onto a magnet and released. Magnet states plus every alive body including the magnets' facing and the candy's damping. |
| `FrogReference.json` | 720 native ticks of frog tongues: tap open/close bridge with candy, a block closing the opening tongue, and an authored open tongue. Tongue state and extension plus every alive body. |
| `PinReference.json` | 240 native ticks of scene pins: locked, rotating, motorized and limit-locked hinges, plus pins that find one, three or no bodies. Every body including the main static body. |
| `RopeReference.json` | 480 native ticks of scene ropes: end-offset ropes to a rotated plank and a wood circle, a candy rope, a rope with no material points, an exact bone multiple, stretch/restore cycles and two replay cuts. Every body in creation order, including linear damping. |
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
clamp, producing speeds near 120 m/s. Their looser tolerances (0.001 meters/radians
and 0.01 meters/second or radians/second) date from the removed analytical sweep;
the native TOI port now runs these scenes too. Sensor fixtures sample zero-based ticks 0–119, require exact collected
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

The project uses the shared Windows/Android game code as its behavior reference,
as confirmed by the user. The supplied Android reconstruction and audited native
numerical profile therefore guide the Windows port too; separate Windows disassembly
is not an acceptance gate. Windows assets still use their own metadata and resource
IDs. Synthetic source-oracle comparisons verify their specified inputs; complete
authored-input trajectory acceptance remains necessary before declaring the port's
physics complete.

## Broadphase verification

`PhysicsBroadPhase` ports the licensed Box2D 2.2.1 dynamic tree with the audited
Android numerical settings: zero fixed padding and displacement multiplier one.
It preserves the shared leaf/internal-node pool, initial capacity 16, doubling,
free-list reuse, perimeter insertion costs, balancing ties, child-two-first query
order and sorted, deduplicated proxy-ID candidate order. Pair payloads retain
that proxy order rather than sorting user data.

`BroadPhaseReference.json` contains 110 synthetic native snapshots covering pool
growth, positive/negative movement, contained moves, boundary overlap, repeated
touches, destruction, ID reuse and teardown. Tests compare exact IDs, reinsertion,
height, query order and pairs, plus finite bounds within 0.000002 meters. Its
SHA-256 is `6f1b033d956395b067745371d25c7c19c36b75d5d73b272dd9e7123dc6165bdd`.
This compares against a separately compiled native oracle, not an original game run.

Native destruction invalidates only the first buffered move for an ID. A duplicate
entry can therefore reference a freed node. The managed component explicitly
rejects such a flush and retains pending moves; native-style reuse of the ID before
flushing is covered separately. This guard is a deliberate managed safety boundary.
Queries reject mutation and restore the guard after nested queries or exceptions.

Managed bounds require finite coordinates within one million meters. Limits are
16,384 live proxies, 65,536 pending moves and one million raw candidate pairs,
including duplicates. Capacity failures preserve pending work and leave rejected
creation/movement atomic; tests cover reducing workload and retrying.

`PhysicsWorld` registers every circle, polygon fixture and terrain fixture in the
same tree. Registration validates all body fixture bounds before exposing a body.
New fixtures discover candidates before the next discrete narrowphase update.
After solving, body/fixture traversal runs in reverse creation order, synchronizing
endpoint AABB unions with body-origin displacement, then discovering new candidates.
End-step candidates start without a manifold until the following discrete update.
Body removal destroys its proxies and retires candidate/manifold ownership.

New-fixture discovery flushes the move buffer before simulation when needed;
end-step synchronization flushes it every step. Ordinary successful world operations
therefore never accumulate duplicate pending moves for a leaf.
The world's body/fixture caps fit within the standalone proxy budget. A failed world
step is not transactional: an end-step candidate-budget exception can occur after
bodies have integrated, even though standalone pair generation retains pending work.

`CaptureProxies()` and `CaptureTerrainContacts()` return immutable snapshots with
stable, monotonically assigned world-local fixture labels, reusable tree IDs and
reverse candidate creation order. Contact snapshots include non-touching solid
candidates; sensors are excluded from these solid-contact diagnostics. Proxy
snapshots include sensors and static anchors. Generic queued Begin/End records
are described below; existing gameplay overlap helpers remain separate. Toggling
a body sensor suppresses solid manifolds at the next update without losing a
still-overlapping candidate.

`ContactLifecycleReference.json` compares 1,680 native world steps with sleeping
and continuous physics disabled. Contact labels/order/touching and proxy IDs match
exactly; bounds use a 0.000002-meter tolerance and six body state components use
0.00002. All compared floats must be finite. The offset compound case also verifies
origin displacement independently of center-of-mass displacement.
Fixture SHA-256: `59b775b2fe081a83a2b818c763fe69c0578b42b00aa6ea021c68d646cdebb71b`.

The stricter proxy trace exposed earlier arithmetic differences. Physics rotation
now uses scalar Box2D multiply/add operations rather than potentially fused matrix
operations. Compound mass order and ordinary inertia accumulation match native
float arithmetic. Polygon world-manifold points retain native radius operations;
the two-point velocity solver retains the native matrix-product grouping and
rounds inverse-matrix entries before multiplying. Algebraic simplification can
change a later proxy-containment decision despite tiny body-state differences.

Custom filter callbacks and tree rebuilding remain separate work. Native TOI proxy
updates are described in the continuous collision section. Complete authored-input trajectory coverage remains pending.

## Discrete circle pairs and contact-connected islands

Every supported circle fixture pair enters native candidate discovery unless both
bodies are static, a joint blocks collisions or the fixtures belong to the same
body. Candidate orientation follows the native proxy pair; fixture A can be static.
`CaptureBodyContacts()` snapshots supported solid body candidates in reverse creation order,
using the same sequence as `CaptureTerrainContacts()`. Sensors retain candidate
ownership but enter neither solid solving nor these solid-contact diagnostics.

Circle manifolds accept tangent boundaries. Velocity initialization computes the
radius-adjusted common contact midpoint and both center-of-mass arms, mixing
friction geometrically and restitution by maximum. Material values are mixed at
candidate creation and retained across empty-manifold updates; later fixture
coefficient changes affect new circle candidates. Explicit native contact material
reset APIs are not exposed. Friction solves before normal impulses; both linear
and angular velocities change. Fixed-step warm starting retains both impulses only
across consecutive touching updates. At coincident centers velocity geometry uses
normal `(1,0)`, while positional normalization leaves a zero normal, as native does.
The port does not invent an axis to split coincident bodies.

Touching solid contacts connect islands. Each body scans contact edges in reverse
candidate creation order before scanning joints in reverse creation order; LIFO
stack processing determines visitation. Each constraint enters once. Static nodes
do not propagate traversal, allowing independent islands to share terrain/static
circles and stop position iterations independently. Velocity iterations solve
joints before contacts; position iterations solve contacts before joints.

Rope/spring APIs use native default `collideConnected = false`; revolute definitions
can explicitly allow collisions. Suppression
is counted per body pair so removing one of multiple joints retains the block.
Adding a blocking joint retires an existing circle candidate on the next update.
Removing it does not touch proxies or force rediscovery: a previously rejected
pair may remain absent until movement reinserts a proxy, matching native behavior.
Body removal clears its candidates, proxies, attached joints and collision blocks.

`CircleContactReference.json` covers ten independent native scenes, every completed
tick through 180. Exact circle and terrain candidate labels, orientation, creation
order and touching state are checked along with six finite body-state components
at the existing 0.00002 tolerance. Boundary regressions also cover sensors, static
pairs, removal, multi-joint suppression and shared-static convergence isolation.
Fixture SHA-256: `5413839674e7599269c543df88e9a2a7104e209e8e1d04d1369c29624e96f647`.

## Discrete polygon-circle body contacts

The same one-point two-body solver now handles circle pairs and polygon-circle
pairs, with geometry-specific manifold construction. This extraction preserves
all earlier circle numeric tests. The factory always places the polygon at fixture
A and the circle at fixture B, including when the circle was created first or is
static. Supported polygon bodies are dynamic; their triangle fixtures retain
independent candidate identities and impulses rather than merging seam contacts.

Narrowphase transforms the circle center into polygon-local coordinates, selects
the greatest separating face using strict native tie comparisons, and tests the
face or endpoint Voronoi regions with polygon skin 0.002 meters. Containment,
face and both vertex branches all produce native face-A manifolds. Vertex normals
are normalized once and retained in local space through position iterations;
recomputing them from the corrected center would change native behavior. All
branches use feature ID zero, so face/vertex transitions preserve warm-start
impulses until a manifold is actually lost.

Velocity geometry uses the projected skin/radius midpoint and both COM arms.
Position geometry uses the circle center, the transformed stored polygon plane,
and native sequential radius subtraction. Both bodies receive linear/angular
impulses, while a fixed-rotation polygon retains zero inverse inertia. Existing
material caching, sensors, joint suppression, removal and contact-connected island
ordering apply to mixed pairs.

`PolygonCircleReference.json` compares ten native scenes, each completed tick
through 180, at the unchanged 0.00002 state-component tolerance. Exact body/terrain
candidate labels, polygon-first orientation, creation order and touching state are
checked independently. Scenes cover face and both vertex contacts, containment,
rotation/restitution, fixed rotation, compound triangles, shifted COM, static
circle and reversed creation order. Boundary regressions cover skin-only overlap
and a nearby miss, sensors on either shape, joint filtering without forced
rediscovery, proxy reuse, removal and immutable snapshots.
Fixture SHA-256: `b861c5a5d962c82385b0a0094d3b5ff30e5528059b21b3f9ffe3745a045543eb`.

Continuous body-pair collision, sleeping, custom filter callbacks and synchronous
native/gameplay callback integration remain separate work. Terrain
added through `AddTerrain` retains its separately verified paths. Complete authored
trajectory coverage remains pending beyond these source-oracle fixtures.

## Discrete polygon-polygon body contacts

Dynamic polygon pairs retain native proxy orientation, with each compound triangle
owning a separate candidate and clipped feature IDs. Geometry computes the native
shape centroid separately from the mass centroid, searches separating edges in
local coordinates and clips the incident edge against the selected reference face.
Reference selection retains the stock relative 0.98 and absolute 0.001 tolerances;
these are independent of linear slop. Each polygon contributes its 0.002 skin.
Local edge normalization, clipping order and packed feature IDs preserve native
float operations and tie behavior, including face-B orientation.

Both bodies receive friction, normal and positional impulses about their centers
of mass. Two-point manifolds use the native coupled normal solver. Poor matrix
conditioning reduces only the velocity solver to one point; position solving still
uses both points, and the unused second velocity impulse retains its cached value.
Warm starting transfers impulses only between matching feature IDs. The existing
terrain solver remains a separately verified path.

`CaptureBodyContactFeatures()` returns detached immutable records containing the
stable fixture labels, manifold point index and packed feature ID. Records follow
reverse candidate creation order, then native manifold point order. Like the solid
contact snapshots, they exclude sensors and empty manifolds. Circle and mixed
contacts expose their native feature ID zero through the same API.

`PolygonBodyReference.json` compares ten independent native scenes through tick
180: flat, tilted, reversed creation, restitution, fixed rotation, compound shapes,
offset centers, a stack, body replacement and a narrow contact. All 1,800 snapshots
check six finite state components per body at the unchanged 0.00002 tolerance,
plus exact body/terrain candidate identities, orientation, creation order, touching
state and manifold feature IDs. Boundary regressions cover combined skins,
compound self-collision exclusion, sensors on either body, joint suppression,
proxy reuse, removal and immutable diagnostics.

Private native instrumentation reproduced the fixture byte for byte and counted
40 entries into the one-point conditioning fallback in the narrow scene. The
instrumented scenes collectively exercised all four successful normal-solver
alternatives, including terrain contacts. They did not exercise the all-fail branch
or a fallback with a nonzero cached second impulse; those remain source-reviewed
rather than numerically covered. Fixture SHA-256:
`b556c7f355b8a735918cb66a396787cc45b4888bb366002965860d66bf28a5eb`.
Complete authored-input trajectory coverage remains pending.

## Fixture collision filters

Each circle, compound triangle and implicit terrain fixture has an independent
`PhysicsCollisionFilter`. Native defaults are category 1, mask 65535 and group 0;
use `PhysicsCollisionFilter.Default` to request these values explicitly. A zeroed
struct accepts no categories. Equal nonzero signed groups override masks: positive
groups allow contact, negative groups reject it. Otherwise both directional
category/mask intersections must be nonzero. Fixture filters never override
same-body, static/static or joint collision exclusions.

`CaptureProxies()` exposes stable world-local fixture labels used by
`GetFixtureFilter(label)` and `SetFixtureFilter(label, filter)`. Unknown or retired
labels throw before mutation. Changing one triangle leaves its siblings unchanged.
The setter stores the new value immediately, including an identical assignment,
but leaves existing candidates/manifolds in place until the next collision update.
Disallowed contacts are then retired. A newly allowed overlap normally becomes a
candidate at the end of the step, with narrowphase on the following step. If new
fixtures trigger discovery at the beginning of the step, the same refilter touch
can create and evaluate that candidate immediately. Preserved candidates retain
their creation sequence and material cache.

The always-awake subset rechecks current filter values during each collision
update; it does not expose native custom filter callbacks or sleep flags. World
refilter touches are coalesced when a leaf already has a pending query. Sorted
unique pair outputs are unchanged; this deliberately avoids the upstream hazard
where repeated touches followed by destruction can leave a stale move ID. The
standalone broadphase preserves its separately tested native duplicate behavior.
Native TOI considers only existing candidates, so filters apply to it too.

`CollisionFilterReference.json` contains eight independent native scenes, every
tick through 90 (720 snapshots). They cover initial rejection, signed-group
precedence, asymmetric masks, repeated setters, disabling/re-enabling, compound
triangles, terrain, joint suppression/removal and early new-fixture discovery.
Exact candidate labels/orientation/creation/touching and proxy IDs are compared;
proxy bounds use the existing 0.000001 tolerance and all six finite body-state
components use 0.00002. Additional boundary tests cover high category bits, signed
group limits, body exclusions, setter timing, retired ownership, repeated touches
followed by removal and filtered continuous sweeps. Fixture SHA-256:
`69490c911ed176f91dd5696dcb9d59484012eb1d6986d0c218167482f9711adc`.

Sensor overlap uses fresh-cache GJK distance with radii and a strict `10 * epsilon`
threshold, independently of solid manifold generation. Generic queued Begin/End
records now expose its discrete world lifecycle below. Existing gameplay overlap
helpers are unchanged; authored filters are imported by the design body factory below.

## Standalone native GJK distance and overlap

`PhysicsDistanceProxy` owns one through eight copied support vertices and a radius.
It represents their convex support hull: one point for a circle, two for a rounded
segment or several polygon support vertices. Stored vertex order is retained for
native support tie decisions. Segment support alone does not implement native
edge/chain fixture construction, adjacency, one-sided collision or gameplay.
Local vertices and world origins must be finite within +/-1,000,000 meters;
radii must be finite between zero and 1,000,000, and angles finite.

`PhysicsDistance.Measure(a, transformA, b, transformB, useRadii, cache)` returns world
witness points, distance and support-pair query count. Transforms use body origins,
independent of mass centroids. The optional `PhysicsSimplexCache` stores ordered
support indices and the native signed triangle metric. Reuse with changed geometry
is accepted only while cached indices still fit; otherwise reset it first.
Null proxies, invalid transforms and incompatible indices are rejected before
cache mutation. Proxies and cache diagnostics are detached read-only snapshots;
a cache itself is mutable and must not be shared concurrently.

The altered native algorithm preserves strict first-on-tie support selection,
Solve2/Solve3 region order, barycentric grouping, signed cache metrics, metric
invalidation and pre-reduction duplicate checks. Duplicate support attempts count
as iterations. The native distance-progress early exit remains disabled. A tiny
search direction or a containing triangle stops the loop; twenty attempts is the
native upper bound, with no extra reduction afterward. Tentative support geometry
retains the slot's earlier weight until the next reduction; managed stack storage
starts at zero. Numerical behavior at the twenty-attempt limit remains unverified.

Cache writes describe the core simplex before radius correction. With radii enabled,
strictly separated witnesses shift to the rounded surfaces; otherwise both collapse
to their midpoint with distance zero. `PhysicsDistance.Overlaps` always starts with
a fresh cache, uses radii and returns `distance < 10 * 1.1920929e-7f`. A small positive
distance may therefore count as overlap, and equality to that threshold does not.

`GjkReference.json` contains 1,752 independent native queries: 864 fresh-cache and
864 warm-cache cases across ordered circle/offset-circle/segment/box/triangle/
pentagon pairs, 12 radius-boundary cases, six tiny-gap cases and six cached-triangle
jumps. Queries include transformed/reversed pairs, separated/coincident/overlapping
hulls, radii enabled/disabled and cache invalidation. Finite witness components,
distance and cache metric use an absolute 0.000002 tolerance; support iteration
counts, ordered cached index pairs and fresh-cache overlap decisions match exactly.
Boundary regressions cover invalid inputs, incompatible cache reuse, immutable
ownership and radius-independent core caches.

Private native instrumentation produced byte-identical fixture output. Across
3,504 primary/overlap calls it exercised all three Solve2 and all seven Solve3
exits, count-one/multi-point cache reuse, all three invalidation causes, the three
ordinary termination paths and both radius-correction branches. No call reached
twenty attempts; primary queries used at most four. These counts do not establish
every compound-condition/equality boundary or managed branch coverage. Fixture
SHA-256: `dac725265d6cc252ec20253cd03adfcc1d57cd63df4cb439b0b07ffe78c764c9`.

The world now routes sensor narrowphase through this GJK query as described below.
Solid-contact geometry and existing gameplay overlap helpers retain their paths.
Native TOI/separation functions and complete authored-input trajectory comparisons
remain separate acceptance work.

## Sensor touching and queued contact transitions

Candidates store native touching independently of their solid manifold. If either
body is a sensor, narrowphase uses fresh-cache GJK with radii and stores no manifold,
features or impulses. Sensor contacts do not connect solver islands. A candidate
discovered at the end of a step starts not touching until its next narrowphase
update, even if the post-step geometry overlaps. Current solid-only diagnostic
APIs retain their behavior; `CaptureFixtureContacts()` adds all candidates in
reverse global creation order, including sensor touching and stored manifold count.
Native fixture A/B orientation is preserved for mixed pairs and for polygon terrain
created either before or after a body.

`PhysicsBody.IsSensor` remains a body-wide convenience affecting every owned fixture.
Changing it does not immediately clear stored touching/manifold state, touch proxies
or generate a transition. A sensor update clears the solid manifold; toggling twice
without an intervening update preserves the previous cache. If a sensor becomes
solid while still touching, a manifold can appear without a second Begin. Conversely,
GJK's small positive-gap overlap can disappear when switching to solid geometry,
producing an Update End despite unchanged transforms. Independent per-triangle
and implicit terrain controls are now available as described below.

Set `world.RecordContactEvents = true` to queue future discrete Begin/End transitions;
recording is off by default. `DrainContactEvents()` returns a detached read-only batch
and empties the pending queue. Drain regularly while recording. Enabling recording
mid-contact does not synthesize Begin records, and disabling retains pending ones.
Events contain stable native fixture labels, creation sequence, current sensor
classification, event kind/stage and the touching flag at emission. They remain
valid after removal; retired labels cannot be used to query a live fixture.

An Update Begin reports touching true; an Update End reports false. Contact
destruction from filtering, proxy separation or body removal emits End only for a
stored touching contact, with touching still true. Sensor classification is captured
from current flags even if the last narrowphase used the opposite sensor mode.
Body removal retires joints first, incident contacts in reverse creation order,
then proxies. No duplicate End is emitted for already non-touching/retired contacts.

This is an immutable queued adaptation, not synchronous native callback execution.
The independent terrain/body updates merge only their current traversal's records
in reverse global creation order. Previous steps and between-step removals retain
chronological order. This matches the tested always-awake subset with no callbacks
mutating the solve; sleeping/wake-dependent traversal requires further work.
TOI-phase Begin/End transitions are queued in occurrence order after the discrete
traversal's batch. PostSolve data is described under contact reports.

`SensorEventsReference.json` compares thirteen native scenes through tick 90:
1,170 snapshots and 49 emitted records (27 Begin, ten Update End, twelve Destroy End).
Cases cover sensor/sensor and circle/polygon/compound passages, terrain created
before/after bodies, sensor/solid toggles, masks, joints, replacement, interleaved
terrain/body order and a GJK-only positive-gap overlap. Ordered event metadata,
all candidate labels/creation/touching/current sensor/manifold counts match exactly;
six finite body-state components retain the 0.00002 tolerance. Extra regressions
cover opt-in recording, cross-step queue order, retained snapshots, repeated removal,
toggle-before-destruction and absence of sensor impulses/manifolds/features.
The private native listener produces events directly; observation-only contact
manager hooks label actual creation and destruction stages. Fixture SHA-256:
`ee95e37681ea089fca805909dffd6db33e0e670f214d1bbd9245fa4705aad574`.

## Per-fixture and terrain sensor controls

`GetFixtureSensor(label)` and `SetFixtureSensor(label, value)` address active body
or terrain fixtures using stable labels from `CaptureProxies()`. Unknown or retired
labels throw before changing state. Every compound triangle owns its sensor flag;
changing one leaves sibling fixtures, filters and density-derived mass unchanged.
`AddTerrain(vertices, friction, restitution, isSensor: true)` initializes a terrain
sensor directly; the optional flag defaults to false. Existing authored terrain
sensor import remains visual-only until gameplay body construction is verified.

`PhysicsBody.IsSensor` remains a managed convenience: its getter is true only when
all owned fixtures are sensors, and false for mixed bodies. Assignment broadcasts
to every fixture even if the getter already equals the requested value. In
particular, assigning false must clear sensor siblings in a mixed body. Retired
bodies retain their detached flag state; changing it cannot affect reused proxies.
Use the per-fixture getter to inspect mixed state without ambiguity.

Native SetSensor changes the flag only when needed and wakes its owning body;
this subset remains always awake. Sensor changes do not touch/refilter proxies,
clear cached manifolds/touching or emit immediate events. The next discrete update
uses the OR of the two actual fixture flags, independently of their bodies'
aggregate convenience values. A changed solid/sensor mode retains existing
candidate/material ownership. Sensor contacts use GJK and no solid solver; mixed
bodies can have sensor and solid contacts simultaneously. Solid-only diagnostics
exclude just the sensor pairs. Native TOI skips pairs with either fixture marked as a
sensor. Sleeping behavior remains separate work.

The body-before-terrain regression also exposed a preexisting reference-face bias
in the static polygon solver. Near-equal separating faces must favor native
fixture A, which can be the moving polygon when terrain was created afterward.
Polygon-terrain manifolds now run through the native polygon pair path in the
candidate's persistent native orientation (see bit-exact arithmetic below), so the
reference-face priority, normal sign, clipping order and warm-start feature IDs are
all native. The native scene that exposed the position mismatch passes, along with
earlier terrain/reference suites.

`FixtureSensorReference.json` checks eight native scenes through tick 90 (720
snapshots and 35 Begin/End records). Cases cover mixed triangle/circle/polygon
contacts, initial terrain sensors, terrain mode changes, terrain created after a
body, multiple terrain fixtures, body broadcasts, filters, joint removal and
body/proxy replacement. Per-fixture flags, ordered events, all candidate labels/
creation/touching/sensor/manifold counts and proxy IDs match exactly. Finite body
components/mass use 0.00002 tolerance; predicted bounds use 0.000002. Additional
regressions cover mixed broadcast normalization, retired ownership, deferred
snapshots, no-touch setter behavior, pair-specific diagnostics and sensor exclusion
from continuous sweep contacts. Fixture SHA-256:
`c939a65759435f9ffbfc89c6003ce7b319b23f3b4f35de4aa39c1352f5ab1dd8`.

## Revolute hinges, angular limits and motors

`AddRevolute(a, b, PhysicsRevoluteDefinition)` joins body-origin local anchors and
stores an explicit reference angle. The world-anchor overload derives both local
anchors and the current relative reference angle. `AddCircle` now accepts an
optional finite initial angle, default zero, for authored hinge configurations.
The common `PhysicsJoint` abstraction keeps native mixed joint/contact traversal;
existing rope/spring equations moved unchanged into their owned implementation.
Their public anchor/reaction/length APIs remain available. Revolute joints have
no length parameter; reading the inherited compatibility `Length` throws.

The hinge solves native 2x2 point constraints and coupled 3x3 point/angular limits.
Lower/upper unilateral constraints use the reduced 2x2 solve when the accumulated
angular impulse would cross its permitted sign. Scalar determinant/cross/dot
arithmetic, initialization-versus-position matrix multiplication order and warm
angular sum grouping are retained. Position solving corrects angular limits first,
then rebuilds rotating anchor arms for point correction, with the original
uncorrected-error convergence checks. Joint angular slop uses the audited one
degree expression; maximum angular correction retains the stock eight degrees.

`LimitEnabled` and `SetLimits(lower, upper)` clear the cached limit impulse only
when their values change. `LimitState` remains the most recent initialization's
state until another step. Limits narrower than twice angular slop use native
equal-limit behavior and target the lower endpoint. Entering Equal during
initialization does not invent an extra cache reset. Fixed rotation bypasses the
angular motor/limit while retaining point correction.

`MotorEnabled`, `MotorSpeed` and `MaxMotorTorque` retain the native motor accumulator
until velocity initialization/solving handles it. Torque clamps use fixed-step
duration times maximum torque. `MotorTorque` reports that accumulator per second;
`ReactionTorque` reports only the angular limit component. `ReactionForce`, world
anchors, relative `Angle` and `Speed` are available independently. All bodies remain
awake. Scalar parameters are finite within +/-1,000,000 (torque nonnegative), local
anchors within +/-10,000 meters, and limits ordered. Invalid creation/setters reject
before world ownership or prior parameter/cache values change.

A revolute definition's `CollideConnected` defaults false. An allowed hinge does
not override another blocking joint between the same bodies. Removing an allowed
hinge does not decrement the block count; generic joint/body removal keeps native
candidate/filter ownership and existing no-forced-rediscovery behavior.

`RevoluteReference.json` compares thirteen independent native scenes through tick 180
(2,340 snapshots): free pendulum, motor reversal/torque changes, lower/upper/equal/
narrow limits, fixed rotation, offset compound COM, contacts and mixed joint removal/
recreation. Three further scenes place the limit width one float below, exactly at
and one float above twice angular slop, the last starting exactly on its upper bound. Six finite body components plus anchors, reaction force/torque, motor
torque, relative angle/speed use the unchanged 0.00002 tolerance. Limit state and
ordered candidate/event metadata match exactly. Boundary regressions cover native
immediate setter cache behavior, world-anchor construction, invalid/foreign/retired
ownership, and multiple blocking joints alongside a colliding hinge. Mixed-scene
secondary ropes explicitly use native definition anchors (-1,0)/(1,0), rather than
the center-anchor convenience API.

Private native instrumentation produced byte-identical fixture output. All four
limit states, fixed rotation, lower/upper/equal positional branches, motor solve/
disabled/equal/fixed categories, full 3x3 limit alternatives and both reduced 2x2
unilateral clamps were observed, along with the strict equal-width threshold and an
exact-bound initialization. This does not establish every exact threshold,
singular 3x3, variable-timestep or disabled-warmstart case. Same-value motor setter
semantics have focused managed/source checks; the native fixture exercises changed
motor setter calls. Fixture SHA-256:
`cd3fee7e64235ff19952ea73c609b6e658d0b2c779674156ba83438581d0a5bb`.

The bounded game-source usage audit found revolute joints in Pin, Helicopter,
Thrower, Frog's tongue and intro objects; prismatic joints in PromoBanner. Submitted
distance joints also include zero-rest-length banner springs and data-driven
stretch frequencies. Prismatic and missing distance/rope runtime/zero-rest-length
APIs remain subsequent work. A configured initial rope-segment spring without a
visible creation call is not treated as proof that such a joint exists at runtime.
Authored gameplay joint construction remains a separate integration gate.

## Body velocity, impulse, force and transform controls

`PhysicsBody` exposes the native runtime controls the game calls on its bodies:
`SetLinearVelocity`, `SetAngularVelocity`, `ApplyLinearImpulse(impulse, point)`,
`ApplyAngularImpulse`, `ApplyForce(force, point)`, `ApplyForceToCenter`, `ApplyTorque`
and a settable `GravityScale`. Static bodies ignore every velocity, impulse and force
call, as native does. Point arms are measured from the center of mass. Impulses change
velocity immediately. Forces and torques accumulate until the next step, integrate as
`h * (gravityScale * gravity + invMass * force)` and `h * invI * torque` with native
grouping, and clear after every step. Arguments must be finite; gravity scale is
bounded to +/-1,000,000. Sleeping stays disabled, matching the game scene's
`SetAllowSleeping(false)`, so wake side effects have no managed counterpart.

`PhysicsWorld.SetTransform(body, origin, angle)` teleports an owned static or dynamic
body by its authored origin. Velocities and existing manifolds are kept. Each fixture
proxy moves through the tree in native fixture-list order with zero displacement, and
new pairs are discovered immediately, so candidates exist before the next step and
take their creation order from the teleport. `Origin` reads back exactly as given
until the next position or angle write. The step's start transform and the
end-of-step synchronization recompute it from the center of mass, as native
`SynchronizeTransform` does.

`BodyControlReference.json` compares five independent native scenes through tick 180
(900 snapshots): velocity replacement with damping and a rope, compound and fixed-
rotation impulses, center/point forces and torques beside terrain contacts,
gravity-scale changes around a spring, and teleports into terrain/body overlap
(including a static body, an in-place nudge and several teleports in one tick). Body
states and proxy bounds use the unchanged 0.00002 tolerance. Proxy IDs, pre-step and
post-step candidates, and events match exactly. A focused test confirms that a
teleported origin reads back exactly, which plain recomputation does not guarantee.
Fixture SHA-256: `e5edbb6ea6900b8ac494017b824ea8cf844c8e53ea1794ace13e1b478f4ceeea`.

The CTR2 profile's zero AABB extension and unit displacement multiplier make proxy
re-insertion knife-edge for a body in uniform motion. The fat box predicts exactly
the next tight box, so the containment test is decided by last-bit rounding, in the
native game as well. Any float difference in that regime can shift one re-insertion
by a step. Single-corner rocking contacts under sustained forcing amplify float
differences about 1.2x per tick, and predictive rope engagement multiplies position
differences by 1/h. These scenes were chosen before the arithmetic below became
bit-exact; they still avoid those regimes so that the tolerance checks remain
meaningful on platforms whose `sinf`/`cosf` differ from the oracle's.

The game's only mouse joint is guarded by a null check and is never created, so it is
not ported.

## Body types, kinematic bodies and mass resets

Bodies carry a native `PhysicsBodyType`: static, kinematic or dynamic. The Windows
master design document authors kinematic bodies (snail, star, helicopter, clover,
hook dragger) alongside static and dynamic ones, and gameplay toggles fruit and
fireballs between static and dynamic. `AddCircle` and `AddPolygon` accept an explicit
type; without one, a zero-density circle stays static as before. Static and kinematic
bodies have zero mass and may carry density for a later type change. A dynamic body
whose fixtures have no density gets native mass one and no rotational inertia.

Mass follows native `ResetMassData`. Fixture densities are summed in fixture-list
order, the center of mass is weighted and scaled by the reciprocal mass, and inertia
is centered once. Circle inertia now uses the native `mass * (0.5 * r * r)` grouping
instead of an algebraically equal shortcut. Moving the center keeps the cached
transform and adds `cross(angularVelocity, centerShift)` to linear velocity.
`SetFixtureDensity` changes only the stored density until `ResetMassData`. Rope cuts
now divide interior fixture densities by ten and reset mass, as the game does, rather
than scaling mass directly. `SetMassData` overrides dynamic mass properties (inertia
about the origin, nonpositive mass becoming one) and is ignored by other types.
`RotationalInertia` reports native `GetInertia`.

`SetBodyType` returns at once for an unchanged type, which matters because collected
fruit requests static every frame. Otherwise it resets mass and clears forces. A body
made static loses its velocity, collapses its center onto its origin and synchronizes
its proxies in place. Every fixture is then refiltered: contacts that no longer
involve a dynamic body end at the next step with destroy-phase events, and new pairs
for a newly dynamic body appear after that step's synchronization.

Kinematic bodies accept velocity setters but ignore gravity, damping, forces and
impulses. Like native, they seed and propagate islands, so joint/contact order across
a kinematic bridge matches. They integrate positions and synchronize proxies, and
they collide only with dynamic bodies.

`BodyTypeReference.json` compares five independent native scenes through tick 180
(900 snapshots), including pre-step mass, inertia, local center, center and velocity
after every mutation. Fixture SHA-256:
`de320ff6367bf0db1e3833592041f6c88c34d9b0003174bc6f8fb5886c0f3e12`.

## Bit-exact contact and joint arithmetic

Several paths computed the right quantities through different float operations, so
they drifted from native by an ulp per contact and then amplified in sensitive scenes.
They now follow native arithmetic:

- Circle-terrain contacts follow `b2CollidePolygonAndCircle` (including the face-region
  separation recheck), `b2WorldManifold` face-A point averaging, contact-solver mass
  grouping and the native `c + mB * ((-C / K) * n)` position update.
- Polygon-terrain contacts no longer use a separate world-space reimplementation. Each
  terrain fixture owns a static identity-frame body, as native ground bodies are, and
  collides through the already exact polygon pair path in native fixture orientation.
  Terrain candidates mix friction and restitution once at construction, as native
  contacts do. Character impact capture uses the native world-manifold point.
- All normalization uses native `b2Vec2::Normalize` (multiply by reciprocal length,
  tiny vectors unchanged), including polygon face normals at shape construction.
- Bodies cache their authored or teleported origin for narrowphase, as native `m_xf`
  does, and solvers rebuild the origin from the sweep after narrowphase.
- Rope/spring anchors use native `GetWorldPoint` (`xf.p + R * local`) and reaction
  forces use `(inv_dt * impulse) * u`.

With every native-trace comparison set to exact equality, every JSON fixture suite
(broadphase, circle, polygon-circle, polygon-pair, contact lifecycle, filters, sensors,
fixture sensors, revolute, body control, body type, contact report, distance joint, prismatic, sleep, world query, time of impact, continuous world, joint anchor and joint topology) is
bit-identical to the Android-profile native oracle for every compared tick on macOS
arm64, where .NET and the clang-built oracle share the platform `sinf`/`cosf`. The
committed tests keep their tolerances so they stay valid where the C runtime's
trigonometry differs; matching the original executables' trigonometry is outside
this source-profile verification. The CSV polygon fixture prints too few digits to
measure exactness.

## World queries

`RayCast(point1, point2, callback)` follows native `b2DynamicTree::RayCast`: segment-box
and separating-axis culling, LIFO traversal, and clipping by the callback's returned
fraction. Each leaf runs the native circle or polygon shape ray cast against the
body's cached transform (terrain uses the identity frame), and the world point is
`(1 - fraction) * p1 + fraction * p2`. Callbacks use native return values: -1 ignores
the hit, 0 stops, the hit fraction clips to the closest hit and 1 continues. Sensors
are reported, as in native. `QueryAabb` reports fixtures whose predicted proxy bounds
overlap, in tree order, until the callback returns false. `TestPoint` uses native exact
shape tests. `GetFixtureAabb` returns the fixture's swept tight bounds from its latest
synchronization, which differ from the predicted tree bounds under this profile's
zero extension and unit displacement multiplier. `GetFixtureBody` maps labels back
to bodies. Structural world changes from inside query callbacks throw before anything
mutates; velocity and impulse setters remain allowed.

`QueryReference.json` covers five snapshots of a moving mixed world and is bit-identical
on the oracle platform. Fixture SHA-256: `53bc0856fe5f95d0f916c94c91df08407fabf5c6a5a1f7b7e3831e025f68dba9`.

## World traversal

`Bodies`, `GetJoints(body)`, `GetContacts(body)` and `CaptureFixtureContacts` follow
native linked-list order, newest first, as the game's body, joint-edge and contact-edge
loops see it. Joints expose `BodyA`/`BodyB`. `GetWorldPoint`, `GetLocalPoint` and
`GetWorldVector` use the cached transform, as native does. `RemoveBody(body, jointRemoved)`
reports each attached joint, newest first, just before removing it, as native
`DestroyBody` calls the game's destruction listener.

## Rope and distance joint definitions

`AddRope(a, b, PhysicsRopeDefinition)` and `AddDistance(a, b, PhysicsDistanceDefinition)`
take native definitions with local anchors and `CollideConnected`. A distance joint with
zero frequency is rigid: it solves the bilateral velocity constraint without clamping
and corrects position by the clamped length error, as native `b2DistanceJoint` does.
Zero rest length is allowed for distance joints, as for the game's hook button spring.
`AddDistance(a, b, anchorA, anchorB, ...)` mirrors `b2DistanceJointDef::Initialize`:
world anchors become body-origin local anchors and the current separation becomes the
length. Rope position correction now follows native `Normalize`, which returns zero
length for coincident anchors. The older `AddRope`/`AddSpring` overloads keep their
behavior and native default of blocking connected collisions. Rope.cpp's colliding
head-tail rope and stretch spring need `CollideConnected = true`.

`DistanceJointReference.json` compares five independent native scenes through tick
150 (750 snapshots) and is bit-identical on the oracle platform. Fixture SHA-256:
`9e7e872c9b1ef6e8cefb79b18b31050979467e3b1629fd10d36c4985a6d17302`. Native runtime length/frequency setters remain unported because the
game never calls them.

## Prismatic sliders

`AddPrismatic(a, b, PhysicsPrismaticDefinition)` and the world-anchor/axis overload
(native `b2PrismaticJointDef::Initialize`) add sliders that lock relative rotation.
The local axis is normalized with native `Normalize`. Initialization, the 3x3
limit/2x2 point solves, the motor clamp at `dt * maxForce`, warm starting and the
position solve follow native 2.2.1 with its distinct groupings: velocity
initialization measures `(cB - cA) + rB - rA`, the position solve `cB + rB - cA - rA`.
Limit states, the equal-limit threshold of twice linear slop, and stale-state
position correction match native. `LimitEnabled` and `SetLimits` clear the cached
limit impulse only on change; motor setters always assign. `ReactionForce`,
`ReactionTorque`, `MotorForce`, `Translation` and `Speed` are the native diagnostics.
The game uses prismatic joints only for its promo banner (limits 0..2.675, motor -10
at 30 N, connected collision, runtime motor speed and limit changes).

`PrismaticReference.json` compares six independent native scenes through tick 150
(900 snapshots): the banner's settings and runtime calls, a dynamic slider on a rotated
axis with an offset compound, equal limits, a free slider, contacts with removal and
recreation, and lower/upper transitions. It is bit-identical on the oracle platform.
Fixture SHA-256: `122d0d8e32ff7c97f1fab21176566af4176c0725b3c1a25109b708cfc63f51a9`.

## Sleeping and waking

`PhysicsWorld.AllowSleeping` defaults to false, matching every game world except the
intro (`IntroScene` keeps Box2D's default of true). With it set, each island ends its
solve with native sleep accounting: a non-static body accumulates the step time while
below 0.01 m/s and 2 degrees/s (and while its `SleepingAllowed` is true), and the
island sleeps once every member has slept 0.5 s and its position solve converged.
Sleeping clears velocities and forces, and applies to every island member, including
static bodies reached by contacts or joints, whose awake flag native toggles too.
`SetAwake`, `IsAwake` and `SleepingAllowed` expose native body state.

Sleeping bodies do not seed islands, integrate, synchronize proxies or take part in
TOI. Contacts whose bodies are all asleep or static keep their manifold without being
updated or overlap-tested, and still feed island building. Native wake sites are
mirrored:

- island propagation through touching contacts and joints
- new contact pairs
- destroying a contact that had manifold points
- non-sensor touching transitions
- joint removal (including during body removal)
- TOI events
- type changes
- changed sensor flags
- velocity, force and impulse setters
- revolute and prismatic runtime setters
- disabling `SleepingAllowed` or `AllowSleeping`

Joint creation and `SetTransform` do not wake, so a sleeping intro chain stays put
while its kinematic anchor is teleported, until a velocity setter wakes it.

`SleepReference.json` compares four independent native scenes with sleeping allowed
through tick 240 (960 snapshots). It is bit-identical on the oracle platform. Fixture
SHA-256: `394f8c90c983e1e5616a290bd6457937dafad1e44312d08a5da012b201e9015f`.

## Contact reports for gameplay callbacks

The game's `ContactListener` forwards native `BeginContact` to its "pre-solve"
delegates, `PostSolve` to post-solve delegates and `EndContact` to finished
delegates. It never overrides native `PreSolve` and never disables contacts, so
callbacks cannot change solving. Native also locks the world during callbacks.
Queued records therefore carry everything the handlers read at callback time:

- With `RecordContactManifolds`, each queued Begin event includes its native world
  manifold (normal, point count and points), computed during narrowphase from the
  bodies' cached transforms. UniversalSoundGenerator reads this point. Sensors and
  End events carry an empty manifold.
- With `RecordPostSolve`, `DrainPostSolve` returns one report per touching solid
  contact, right after each island's position solve and in native island and
  island-contact order. Each report carries fixture identity, creation order, the
  velocity-constraint impulse count (one for a redundant two-point manifold),
  normal and tangent impulses, and the world manifold from the synchronized
  transforms. Omnom and GeneralPolygon read these impulses and points.

Both flags default off, so existing event consumers are unchanged.
`ContactReportReference.json` compares four independent native scenes through
tick 120 (480 snapshots) and is bit-identical on the oracle platform. Fixture
SHA-256: `dab320bd6b2415b61203f88bd7099428e6e85ce3da144e4d27ed379561ee4034`. TOI islands report PostSolve the same way; see the continuous collision section.

## Scene ropes

A rope's initial material points use the `ropeMaterialPoint_menu` design: the
scene's rope constructor builds them before it records its scene, which selects the
menu design. Only tips added by later cuts use `ropeMaterialPoint`. In the shipped
master the two differ only in density (0.8 versus 0.6). The original hint journals
exposed this: with 0.6, every level with ropes diverged by millimetres in the first
step; with the menu design the first steps match the recordings bit for bit.

Each grab rope targets the registered object named by `target_id`, or `candy` when
the attribute is absent; an unregistered name keeps no rope. Candy and circle or
rectangle shapes are supported targets. `target_x`/`target_y` double into a
logical attach shift in the target's local frame, and become the last joint's
local anchor divided by 200. Material points are spaced between the head and tail
origins, not the anchors. A rope with no material points gets one joint with
`collideConnected`. The bone-remainder test compares in double precision.

`rope_instance` objects are arbitrary ropes between two registered names,
`body1_id` (head) and `body2_id` (tail). They are built first in the scene's
post-pass, in document order, ahead of pins, grab ropes and balloon ropes, so they
take the lowest rope and journal indices. The native constructor doubles the
authored `length` twice (four times the editor value in logical units) and each
`x_shift_n`/`y_shift_n` once as that end's attach shift. Either end may be candy,
which then slows down on the rope; an unregistered name keeps no rope. Fireball
ends are supported without candy's slowdown, and Booka ends slow down like candy.

Destroying a body runs the native `cutWithBodyDestroyed` on every rope before the
body leaves the world: an intact rope is cut at the joint next to it, with no
impulse, and the body is dropped from the rope. Eaten or lost candy follows this
path at either rope end.

A live swipe cut gives each new tip the impulse `normalize(toB2Vector(end - start))
* 0.001`, from the touch segment that crossed the rope.

Every update after the step, candy (the bodies that slow down on ropes) gets
linear damping 0.3 while stretched and moving faster than 2 m/s on average, 0.1
while stretched, and 0 otherwise; a cut or released rope stops updating it.
Stretch measures between the head and tail attach points rotated by the body
angles. The stretch springs and the restored rope joint carry the attach shifts;
the head shift also applies to the last joint when the rope has no material
points. For such a rope, the native force lookup returns the newest joint between
the head and tail, which is the whole-rope spring while stretched. The rest length
is recomputed from the rope length and bone count.

Cuts, swipe hit tests and drawing use the native rope body position: the center
plus the attach shift rotated with the framework's 1024-entry sine/cosine table,
which quantizes the angle to 1/1024 of a turn. A cut removes the stretch spring
and the cut joint, adds two tips with a real linear impulse, and measures the new
joints from those positions to the crossing, keeping the head and tail anchors.
`RopeReference.json` is bit-identical under exact comparison on macOS arm64.
Fixture SHA-256: `9f62f8739057c798b0dc02b3f4856dda615a035adb0b348e2f9cc5c1ee9554e4`.
The older chain oracle now includes candy slowdown; its fixtures were regenerated
(`PhysicsReference.csv` `f6fbc9728ee7efd1dbf2ffe040603a8577265a0988ee00811a538700d021978f`,
`RopeCutReference.csv` `7caa11746038cd2e7265d857ebb301840ae78537f85c3142c3e6985c0b8d1f8d`).

## Scene balloons

A balloon body comes from the master `balloon` design merged with its authored
attributes; the scene loads at most ten. Its rope to the target (no default
target) is built after the grab ropes, with a (0, 45) logical head shift. Every
update after the ropes, a balloon above the `balloon_design` stratosphere line
keeps its initial gravity scale and damping; below the line they blend toward the
stratosphere coefficients with height, and at or above the top of the scene they
use the coefficients outright. A tap within 70 logical units (after frogs) or a
swipe passing within 50 (after every rope) pops an active balloon. Popping, or
any cut of its rope, destroys the body: an intact rope attached to it is first cut
at the neighbouring joint at the anchored body position with no tip impulse, and
the body then leaves the rope. Swipe pops replay as `--pop tick:balloon`. When the
candy is eaten its ropes are released first, so attached balloons fly away
without adding tips. The balloon's pre-solve handler only plays hit animations and
cannot stop dispatch for supported gameplay contacts, so it is not modelled.

`BalloonReference.json` (600 native ticks: tap pop, grab-rope cut, rise past the
stratosphere line and swipe pop; and a balloon flying away when its rope to a wood
circle is cut) is bit-identical under exact comparison on macOS arm64, including
gravity scale and damping. Fixture SHA-256:
`11f8b7a90893deef5c38ba257a2279131fddb6f7e5f9af223e4998024987a771`.

## Scene frogs

A frog's tongue runs from 40 logical units in front of the frog to its doubled
`path` target. A tap within 78 logical units opens an idle tongue or closes an
extended one; frogs are tried newest first and a tap in range is consumed even
mid-motion. The tongue extends at 3400 and retracts at 3000 logical units per
second. Whenever the extension changes it is rebuilt: while moving, one static
sensor box along the extended part; when fully extended, static end circles
(9 logical units wide, without a game object) and `max(3, length/80)` dynamic
segments joined by revolutes, with damping 2, category 2 and a mask that ignores
categories 2 and 14 (static wood terrain). Boxes use the rotated native
`SetAsBox` (`ConvexPolygon.Box(hx, hy, center, angle)`). An opening tongue closes
when a Begin contact pairs it with a solid game object other than a frog;
the port reads queued Begin events right after the step, before the update, which
is when the native handler's state change takes effect. Frogs update after ropes,
following the scene's child order. `tongueOut="true"` opens the tongue and runs a
one-second update during construction, so its bridge is built in document order.

Terrain fixtures now carry the collision filter of their merged material and
authored attributes (wood is category 14), as the scene's polygons do; masks are
unchanged, so existing levels are unaffected.

`FrogReference.json` (720 native ticks: tap open/close with a candy on the bridge,
a dynamic block closing the opening tongue three times, and an authored open
tongue over static wood) is bit-identical under exact comparison on macOS arm64.
Fixture SHA-256: `4aa69e9e96ad1d55fa5093e28d245046a8a08c83bb0c2f8ccdead76914413014`.

## Scene pins

Before any object, the scene creates a fixtureless static body at the bottom center
of its logical screen; the port assumes the 640x960 level canvas, which only
affects static-anchor rounding. `PhysicsWorld.AddStaticBody` creates it without a
proxy, and the native rope oracle confirms it changes no other body.

After all objects exist and before grab ropes, each pin, in authored order, queries
predicted proxy bounds within one logical unit of its doubled position and keeps
dynamic bodies. With exactly two, it hinges them at the pin; with `pin_to_world`
it also hinges every found body to the main static body. Each hinge uses native
world-anchor initialization; `fixed_rotation` enables a zero angular limit, and
`enable_motor`, `motor_speed` and `max_force` set the motor. Booleans follow the
C++ stream with `boolalpha`, so only `true` is true. Only a trigger releases a
locked motor, and `is_spring`/`spring_gain` are never read.
The native search orders bodies by address; consecutive Box2D block allocations
increase, so the port uses creation order, which the oracle (using real
pointers) agrees with. Negative motor torque fails explicitly.

`PinReference.json` (240 native ticks: locked body-to-body hinge, rotating,
motorized and limit-locked world hinges, a lone body, three bodies and an empty
pin) is bit-identical under exact comparison on macOS arm64. Fixture SHA-256:
`d3fcb70b987f2ea43cad3ededd5e4c345afeb08d985a8181e39f36e46afe4832`.

## Scene pumps

A `pump` owns no body. The `pump_design` master supplies every pump's `power`,
`cone_height` and `cone_width`; an authored `power` attribute is ignored. A press
within 90 logical units of the pump (newest first, after frogs and balloons) casts
five world rays from the pump at `position / 200`, rotated by the authored degree
angle converted in double precision: to `(0, -h)`, `(-w/2, -h)`, `(-w/2, -h/2)`,
`(w/2, -h)` and `(w/2, -h/2)`. Every reported fixture, sensors included, is kept
and the ray continues. Its body is collected once, up to nine bodies, unless it has
no game object (frog tongue anchors) or is a rope material point. Each collected
body then receives the impulse `R * (0, -power) * (1 - d / h)` at its center when its
center lies within `h` of the pump; static and kinematic bodies are unaffected but
still take a slot. The press is consumed.

## Scene throwers

A `thrower` is a dynamic compound polygon built like Om Nom: the `thrower` master
design, the authored attributes and a forced `normalizeShape`. Its contact handler
runs from begin-contact events with the two objects ordered by native game body type
(general polygon 3, candy 4, Om Nom 6, thrower 8): candy, Om Nom, circle shapes and
other throwers become candidates. While the thrower holds nothing, the candidate,
or the outermost thrower of a chain holding it, is scheduled; otherwise it joins a
collision list that finished contacts erase. After the step, in scene order after
frogs, each thrower cools down toward zero at 1 per second, retries its scheduled
capture until it succeeds or the bodies stop touching, then tries the first stored
collision. A capture is refused during the cooldown or when the candidate thrower
already holds this one through its chain. It teleports the body to the thrower
origin at the thrower angle (the scene object's height is zero), stops its linear
motion and adds a revolute joint from the thrower origin to the body's
`(0, length)` point with a zero-width angle limit. The length is the circle radius
minus 0.06, 0.1 for candy, 0.23 for Om Nom and 0.26 for a thrower. A press within
90 logical units of a holding thrower (throwers first, newest first) destroys the
joint, applies `R * (0, -mass * power)` (`thrower_design` power) at the body's
center and starts a 0.3 second cooldown. Om Nom shows `WITH_THROWER` while held
and falls, chewing if he was, when thrown. Eating held candy swaps it for Om Nom
without a cooldown, and destroying a held body releases it with one.

## Scene helicopters

A `helicopter` is the `helicopter` master design (a kinematic sensor circle) with the
authored attributes and a forced `normalizeShape`. Its path follows the native
`MovableBodyObject`: `path` is read around the doubled editor position, `cyclePath`
picks a closed `SmoothCyclePath` cycled at constant parameter speed, otherwise an open
`SmoothPath` (the native Akima spline, padded with mirrored points below five tuples)
is ping-ponged with one-second quadratic ease-in and ease-out phases, the ease-out
starting at the precomputed 60-frame deceleration distance. Speed is
`speed * 100 / length` in path fractions per second. Each update sets the body's
velocity from the spline derivative; ping-pong adds the gap to the spline point,
both in logical units per second, before dividing by 200. `startOnContact` holds the
path until the helicopter starts carrying something.

Begin-contact events (helicopter type 10, above every candidate) schedule a capture
unless the helicopter is cooling down or already carrying. Om Nom is preferred and
keeps upright (his hinge uses his anchor and a zero reference angle); otherwise a
circle shape or a candy is taken at its current angle. A candy marks the capture as
replaceable by Om Nom, and the source then releases the candy's slot with Om Nom's
delegate paired with the candy body. After the step, in scene order after Om Nom,
each helicopter runs its path, counts the cooldown down, releases a replaced capture,
starts the scheduled one (angular damping `3d + 2`), deletes hinges queued by
releases, and pulls the carried body toward the hook 0.49 m below its origin at 4 m/s,
turning Om Nom upright at twice `-4` times his wrapped angle. Within `sqrt(0.012)` m
the body is teleported onto the hook at its wrapped angle and hinged from the
helicopter's `(0, 0.13)` point with limits of ±1 radian and a 0.003 torque motor. The
anchor is `(0, -0.2)` for candy, `(0, -0.32)` for Om Nom and `(0, -0.02 - r)` for a
circle. A carried body spinning at 2 rad/s or more keeps 65% of it each update.

A press within 93 logical units of a carrying helicopter or 60 of its load (helicopters
first, newest first) releases the load: its damping is restored, a 0.5 second
cooldown starts and the hinge is queued, so it holds for one more physics step before
the next update deletes it. The hint journals split on that last point: about as
many releases follow an immediate deletion as the queued one. In 4-21 the dropped
candy tracks the recording only with immediate deletion, 1/512 m off (one step of
its gravity), while in 1-12's fruit challenge Om Nom follows the queued deletion
exactly. Recorded `PauseTap` positions match the helicopter after the step of the
tap frame in both, so tap timing is not the cause. The port keeps the source order.
Om Nom shows the `WITH_HELICOPTER_*` scenes while carried, opening his mouth near
candy, and falls (chewing if he was) when dropped; eating carried candy releases it.

## Scene fireballs

Fireballs merge the Windows `fireball` master design with authored fields and can
attach to grab, balloon and free-standing ropes. An idle fireball's begin contact
with a dynamic general shape starts a burn, makes the fireball a sensor and clears
its linear velocity before solving. The burn traverses joints newest first to
connected general shapes, including static shapes. Shape removal follows the
0.1-second command timeline and the scene's next post-step removal phase.

The Windows implementation differs from the Android reference: its first fade
update releases both carriers and destroys its physics body, cutting all attached
ropes. The visual holds its last pose until animation resource 25's `FADE_FIRE`
scene finishes. Android turns the body static instead. The journal retains the
fireball's registration slot but reports no live body during this visual fade.
Destroyed bodies also leave scheduled helicopter captures and every thrower's
pending collision list, preventing later captures of retired bodies.

Windows thrower capture clears angular velocity as well as linear velocity. Its
fireball anchor is 0.15 m, as in the Android reference. The matching package's
7-13 hint journal instead shows 0.26 m spacing: a diagnostic-only 0.26 m experiment
reduced the frame-210 fireball position difference from 0.11019 m to below
0.000001 m. Production keeps the executable's 0.15 m value. Matching a level hash
does not establish that a hint recording used this executable's behavior. A reused
recording is only a hypothesis; object mapping and runtime/playback logic still
need to be ruled out before attributing the discrepancy to a different build.

`FireballTests` covers burns, propagation, rope cuts, visual/body lifetimes,
carrier release (including same-step capture), Windows spin reset, missing journal
bodies and queued-capture cleanup. The seven supported bakery levels add 21 hint
journals to the regression baseline. These checks do not establish original-game
trajectory parity: rolling contacts amplify small numerical differences and some
recordings differ from the inspected executable's mechanics.

The supplied iOS 1.6.4 code independently agrees with Windows on the 0.15 m
fireball anchor, capture spin reset and first-fade body destruction. Its revised
`fire17` level has a replacement journal with measured 0.15000004 m spacing at
frame 186, while the Windows journal has 0.25999996 m at frame 210. A headless
comparison using the matching iOS level, master and timelines reproduces that
iOS fireball position within approximately 0.000002 m without applying snapshots.
This supports the capture implementation; it does not resolve later numerical
drift or identify the build that produced the Windows recording.

## Scene Bookas

A Booka merges the master `booka` design with its authored fields, can hang from
grab, balloon and free-standing ropes, and slows those ropes like candy. Om Nom's
contact delegate runs inside the step: a begin contact between Om Nom (game body
type 6) and an uncaught Booka (11), while Om Nom's boo cooldown is not positive,
boos Om Nom from the Booka's center and catches the Booka. The caught Booka cuts its
ropes in its next update (as `cutWithBodyDestroyed`, keeping its body), becomes a
stopped sensor kicked by 0.11 along a heading of -110 to -70 degrees (Windows scales
float sine and cosine by the double 0.11; Android and iOS use double trigonometry), and becomes a
default game body, so Om Nom, throwers and helicopters ignore it. The heading comes
from the unseeded `arc4random`; the port draws it from a fixed xorshift sequence, so
no recording can match a caught Booka's flight. Booka updates run after the frogs: a
Booka beyond the screen grown by 40 + 150 logical units, or one whose ACTION scene
(animation 13) has finished, is destroyed after the next step. Throwers hold uncaught
Bookas on a 0.1 m hinge and helicopters carry them upright from (0, -0.2).

A booed Om Nom that is chewing jumps at once; otherwise, unless he failed, he shows
WITH_BOOKA and ignores Bookas for 0.15 seconds, and the end of that scene makes him
jump. The jump stops him and applies `vectRotate((0, -0.3 * boo_jump_power),
add)`, where `add` is `90 - boo_jump_angle` degrees from `omnom_design`, negated when
the Booka is to his right (the 2D cross of straight up and the Booka offset is not
negative), in which case he faces right. He then falls (chewing if he was) with
WITH_BOOKA_JUMP and FALLING. The Android build rotates with 1024-entry `fmSin`/`fmCos`
tables, which tilt 20 degrees to 19.69; the Windows build inlines the rotation with
float `sinf`/`cosf` (the iOS build calls `__sincosf_stret`), and with float sine and
cosine Om Nom's jump matches the 5-1 and 5-15 recordings exactly. The iOS build also keeps a thrower-held Om Nom
in his thrower state when booed; the port follows the Android order there.

Some recordings keep reporting a caught Booka far below the screen (5-7's reaches
77 m), although the Windows, iOS and Android builds all remove off-screen Bookas; the port
keeps the executables' removal, which only shows as missing samples because a caught
Booka is a sensor.

## Scene construction order

`CandySimulation` creates bodies the way the scene loader does: one pass over the
authored objects in document order (candy, grabs, Om Nom, stars, frogs, balloons
and circle/rectangle/polygon shapes), then pins, the grab ropes and their material points in a
post-pass. Proxy IDs, body traversal and contact creation order therefore follow
the source. Candy, grab and rope-point bodies come from their master designs through
the design body factory; the master loader's tag becomes `shapeType`, and absent
fields read zero or false. Candy and grab placement angles are ignored, as in the
source. Authored `circle*`/`rectangle*`/`polygon*` shapes are exposed as `Shapes` and drawn
diagnostically until their artwork is ported. As in the game, a shape whose body is
static is land: Om Nom's terrain checks and landing impacts count contacts with it.

Authored numbers and flags go through `NativeText`, which reads like the native
`ZString` accessors: absent or blank numbers read zero, `intValue` keeps the leading
integer digits of a decimal, and `boolValue` accepts only lowercase `true`. The body
factory's own flags compare against exactly `true`. Malformed numbers fail explicitly
instead of reading zero as they would in the game.

## Challenge variants

`CandySimulation` takes the `LevelVariant` the scene is built for, as
`GameScene::loadMap` reads the challenge type. Both challenges leave tutorial fingers
out of the scene (the native loader builds them but adds them only outside the
challenges). A fruit challenge also builds a fruit grid after the grab and balloon
ropes and schedules every star for removal: the stars keep their journal indices, can
still be collected during the first step, and are destroyed after it, before the
scene's children update.

The grid has 11 columns of 12 fruits over the logical screen. With `w = width / 11` and
`h = height / 12` in float, column `c` and row `r` sit at
`x = c * w + w / 2 + c * 1.8` and `y = r * h + 50 + (c even ? h / 2 : 0) - 4.5 * c`, with
the native float roundings, and their type is `(c odd ? r : r + 2) % 3`. A fruit is not
created when its center lies inside any existing fixture, sensors and stars included
(an AABB query of ±1.1921e-7 m and `TestPoint`). Each fruit is the master `clover`
design (a kinematic sensor circle) with `normalizeShape`, Booka's filter category as an
excluded category, and a position that travels through text: half of it is printed like
`ostream << float` with six significant digits and the body factory parses and doubles
it again. Fruits register in the journal after the stars.

An overlapping active candy or Om Nom collects a fruit at contact-begin time; the fruit
turns static in its update. Its scene in Windows animation 30 (types 0, 1 and 2 play
APPLE, STRAWBERRY and GRAPE) first advances in the collecting frame. The frame it
finishes adds a particle burst that first updates in the next frame, since children
added during an update wait for the following one. The burst stops emitting after 0.7
seconds, drops its six 0.7 second particles on the 43rd update and reports on the 44th,
and the scene destroys the fruit after the next frame's physics step. Freed proxies are
reused by bodies created later, such as cut rope tips: with this removal, 1223 of the
1553 recorded fruit catches across 39 fruit-challenge journals land on the recorded
frame (1138 without removal), and removing fruits one or two frames earlier lowers it
to 1209. Fruit challenge scoring and medals are pending.

## Design body factory

`BodyFactory` ports the scene's shared body factory for one-fixture circle and
rectangle designs and compound polygon designs. `MaterializedDesign` merges like the scene loader: the
material's master design for `*_materialized` tags, then `shapeType` from the tag
prefix, then the authored attributes. Absent numbers read zero and absent flags
read false, exactly as the game's string accessors do: an absent `gravityScale`
means no gravity and an absent `body_type` means static. Position doubles the
editor `x`/`y` and divides by 200; the degree angle converts in double precision
before narrowing. Radius, width and height double into logical units before the
meter conversion. Rectangles use `ConvexPolygon.Box`, which assigns native
`SetAsBox` axis normals directly; normalizing the edges instead diverges from the
oracle within 44 ticks. Filters use category `1 << filter.category`; `collideWith`
`all`, `like me` and `not like me` give mask 65535, the category, or its
complement; each `filter.excludeCategories` entry clears its bit.

Polygons follow the native branch: the editor position is the body position scaled
back by 200, `SmoothCyclePath` smooths the authored `polygon` path with
`smoothFactor` (default 1) and `normalizeShape`, and the body origin is the editor
position divided by 200 again in double precision. The body keeps the authored
angle. Each ear-clipped triangle, scaled to meters, has its last two vertices swapped
when `b2Cross(v1 - v0, v2 - v1)` is negative and becomes one fixture through
`b2PolygonShape::Set` semantics, slivers included. `SmoothCyclePath` reproduces the
periodic chord-parameter Akima spline, its float/double mix and the original ear
clipper bit for bit, including the closing-edge lookup that reads the first erased
tail tuple. `SmoothPathReference.txt` stores the smooth and triangle point bits of
twelve invented paths from a private native reconstruction; the same reconstruction
matched every authored level polygon and the Om Nom master.

Out of scope and rejected explicitly: bullets (deliberately unported), dynamic bodies without positive
density, static bodies with an initial velocity (native islands would feed it to
contact solving), and numbers the C++ stream would only partly parse.

`BodyFactoryReference.json` holds two native scenes, each with creation data and
180 ticks: static, rotated static and dynamic wood-like rectangles and circles with
a candy-like circle; and fixed rotation, kinematic and dynamic initial motion,
absent gravity scale, all three collide-with modes, excluded categories, a sensor
and an absent body type. Committed tests use the shared 0.00002 relative tolerance;
under exact equality every trajectory, contact and coefficient is bit-identical on
macOS arm64. Reported rotational inertia alone differs by one ulp, because the
managed body stores the inverse that the solver uses. Fixture SHA-256:
`cea29caf3d57e0f360469e0eaa33982c87fef5c6eed9cc8431d10e56e1a2f8f2`.

## Running numerical verification

From the repository root (after the normal package restore), run:

```sh
dotnet build Ctr2.slnx -c Release --no-restore --disable-build-servers -m:1
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*BalloonTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*FrogTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*PinTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*RopeTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*BodyFactoryTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*SmoothCyclePathTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*SleepTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*PrismaticJointTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*ContinuousWorldTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*TimeOfImpactTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*WorldQueryTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*DistanceJointTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*ContactReportTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*BodyTypeTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*BodyControlTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*RevoluteJointTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*FixtureSensorTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*SensorContactTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*PhysicsDistanceTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*CollisionFilterTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*PolygonBodyContactTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*PolygonCircleTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*CircleContactTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*ContactLifecycleTests'
dotnet tests/Ctr2.Tests/bin/Release/net10.0/Ctr2.Tests.dll -class '*BroadPhaseTests'
```

These headless comparisons need no game assets or native compiler. They use
checked-in synthetic outputs of the independent native oracle; the managed engine
does not generate its own expected results. Failures identify the scenario, tick,
proxy/bound or body-state component. Run the DLL without a class filter for all
joint, terrain, polygon, cutting, sensor and continuous reference comparisons plus
other regressions. Passing fixtures establishes those tested scenarios, rather
than full original-game or cross-platform bitwise parity.

## Physics-first verification priority

Further development prioritizes source-faithful physics before game object or
presentation expansion. The managed engine remains a bounded subset, not a
complete or proven 1:1 replacement for the game's physics. The audited numerical
profile and synthetic comparisons still leave control-flow and runtime differences
to verify.

The engine-level port of the game's Box2D usage is complete, including the intro's
sleep/wake behavior. The game only steps its worlds with dt 0.0167 (its zero `frameStep`
pauses skip the step entirely), so variable dt ratios are not needed. The game never creates its mouse
joint, authors no bullets, keeps gravity at (0, 10) and leaves sub-stepping off, so
those paths are deliberately unported (see "Deliberately unported Box2D features"). Next, game objects must build their bodies,
fixtures, filters, sensors and joints from authored data through these APIs, and then
physics-based gameplay must be verified against authored input traces. Passing
synthetic fixtures is evidence for those scenarios; original-game trace comparisons
are a separate acceptance gate. Presentation expansion follows those gates.

## Opt-in gameplay

`CandySimulation(..., play: true)` adds a bounded dry-level slice with one candy
and one Om Nom. As in the original scene, gameplay does not depend on the authored
location, which only selects presentation. Other mechanics and multiple protagonists/candies fail
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
costume substitutions, particles/audio and native contact ordering remain pending. Physical overlap is a discrete compound fixture test, not the
complete native dynamic-contact graph. The original fade completion queues
destruction; this subset retires after the completed fixed tick reaches 0.2 seconds.

## First-level arrival and tutorial hints

The first production level (desktop pack 1/entry 1) preserves its authored negative character position
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

One authored `tutorial_swipe` or `tutorial_tap` is supported independently of
arrival; both are the source's `TutorialFinger`. Position doubles into logical
units. A swipe keeps its authored degree angle and shifts the finger 90 units along
it; a tap ignores the angle and stays centered. Delay expires on fixed ticks, then
Windows animation 104 repeats `SWIPE_LOOP` or `TAP`. The hint never consumes a touch. Primary pointer down
before the delay removes the hint; afterward it fades over 0.4 seconds. Accepted
scheduled replay cuts apply the same dismissal notification. This replays accepted
cuts, not all original raw touch events. Pause freezes hint/particle timing, and
restart restores the delay. Tutorial sensors and multiple hints fail explicitly.
Delay accumulation and subtracting the native remaining delay may differ by one
floating-point tick; exact original timing/visual parity still needs runtime comparison.

## Remaining acceptance work (broader engine)

There are no moving stars, hazards or broader intro logic yet. Kinematic and
static polygon bodies, noncenter anchors and every game joint type exist in the
engine, but most game objects are not yet built on them.
Broader consumption and completion mechanics beyond the opt-in gameplay slice,
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
Authored inter-scene transitions and original runtime visual parity remain pending.

## Character states and scene completion

Given Om Nom's Windows animation (resource 85), gameplay completes authored scenes
itself: on the first fixed tick at or beyond the scene's duration, once per scene
revision, after procedural completion and before the motion history samples the
tick, as the source's timeline callbacks run before its speed calculator. Without
durations only a presentation callback completes scenes, so journal replays and
the desktop share one clock. The motion history scales each window's newest-first
sum by the source's rounded reciprocals (0.2, 0.071429, 0.034483, 0.016949) and adds
the 250 ms windows: 14 velocity samples and angular samples 5 through 13 over 14.
Integer body degrees use the source's `(int)((float)(angle * 180.0) / 3.14159265)`.

The character starts facing right when its origin lies left of the screen center.
The artwork faces left and is mirrored through its scale, with the fixed art offset
inside the mirror. Like the source, which ends each update by mirroring a
right-facing character, the mirror turns on as soon as the character faces right
but turns off only when a left turn finishes or while a procedural scale timeline
rewrites the scale, so the artwork can keep facing right after the character's
attention turns left. Previews without gameplay apply the initial rule.
Opening the mouth faces the candy; a finished `MOUTH_CLOSE` faces the center when
more than 50 units away and enters `Puzzled` until `PUZZLED` finishes. An idle
character playing `IDLE`, slower than 0.2 m/s and 0.1 rad/s over 250 ms and within
20 degrees of upright, turns (`TURN`) toward the center when it faces away and lies
more than 50 units off center. An idle character whose 100 ms velocity exceeds
0.5 m/s, heads between 10 and 170 degrees, stays within one degree of the body angle
modulo 180 and differs by more than 15 degrees from the previous slide's heading
starts `Sliding` (`SLIDE_LEFT` when moving the way it faces, otherwise `SLIDE_RIGHT`);
any finished scene ends the slide without a new scene. Static terrain impacts above
0.2 bump a sliding character like a falling one, so a bump that ends while spinning
rolls. In level 1-16 this reproduces the recording's pump-driven slide, roll and
delayed stand-up bit for bit.

## Scene triggers

Windows `Trigger::initWith` (0x4AB190) merges the master `trigger` design with
the placement, then forces `normalizeShape=true`. Its sensor body participates in
the broadphase in authored order, but is not registered in the hint journal.
`handlePreSolveCollision` (0x4AB420) actually runs from BeginContact, including
sensor contacts, before the current solve. The first dynamic game body to touch it
pushes it permanently; rope material points, frog tongues and fixtureless tongue
anchors cannot push it. Static and kinematic bodies cannot push it either.

The trigger calls every pin's `startMotor` (0x4A08B0), which releases the angular
limit only when the motor is already enabled. A world pin releases its world
hinges only, retaining the separate body-to-body hinge; a pin without world
attachments releases its body-to-body hinge. Destroyed hinges are ignored.
Invented-fixture tests exercise contact-step motor motion, one-shot activation,
normalization, body exclusions and both hinge groups. The desktop draws a diagnostic
marker; artwork, sound, achievements and challenge scoring remain pending.

Triggers bring the Windows desktop construction audit to 67 of 168 entries,
adding 4-12, 4-13, 4-17, 5-8, 5-10, 5-13, 5-18, 5-19, 5-21 and 7-8.

## Windows clover placements

The Windows `GameScene::loadMap` (0x4ECF00) compares the `clover` tag but discards
the result and proceeds directly to `rope_instance`; it never constructs a Clover.
The mobile source instead instantiates clovers behind `ZBuildConfig::premium`.
Shipping resource 18 and the master `clover` definition does not establish that
the Windows loader enables that object. The port therefore accepts authored
clovers but gives them no body, journal entry, collection callback or drawing.
Fruit challenges still use the clover master design to build their fruit sensors,
as the Windows fruit constructor does; this does not instantiate map clovers.

Invented-fixture tests compare all bodies bit for bit for 180 ticks with and
without clover placements in all three variants, including fruit-grid occupancy,
and verify that an ordinary level needs no clover design. This raises the desktop
construction audit to 78 of 168 entries, adding 1-14, 1-18, 2-6, 4-2, 4-4, 4-14,
5-9, 7-5, 7-9, 7-16 and 7-20 to the trigger checkpoint's 67.

## Scene gremlins

Windows initializes authored gremlins from the master `gremlin` body design with
placement overrides (0x492740), accepting at most ten. They update after Bookas
and before throwers. The first update enables cloning; each successful press
starts a 0.1-second cooldown, reduced by fixed dt on each child update. The
162x115 logical-unit touch rectangle is unrotated around the last child-update
pose and excludes its right and bottom edges (0x492DD0).

A clone uses the master design with an explicit origin at the parent's live
center plus its rotated negative half master height, and the parent's live angle
(0x4928C0). It does not inherit placement overrides, velocity or attachments.
The new child updates after all initial children and receives taps first. Its
journal slot appends after every existing slot, whereas authored gremlins register
between Bookas and throwers. Removed clones leave empty slots. Gremlins do not
register authored names for rope lookup. Throwers recognize body type 12 and
hold it on a 0.17 m hinge; helicopters do not capture this type.

The scene's cap enforcement (0x4F3910) schedules the oldest active unheld clone
for disappearance until its local counter reaches ten or no eligible clone
remains. Authored gremlins and the newest clone are protected, so ten authored
gremlins can retain an eleventh body. Pending removals still enter the counter
for another press in the same frame. Removal destroys the body after the next
physics step. Tests cover this edge case and protect clones held by throwers.

`GremlinReference.json` is an invented solid clone stack with gravity, a rotated
parent, floor contacts and cap evictions, generated independently with the local
native Box2D 2.2.1 profile. All six pose/velocity components of every remaining
body match bit for bit over 240 ticks. Fixture SHA-256:
`dc9db714265799046ec61b63894d0895b8a70c4f3487e6e0a35fae2e54b3199a`.
The desktop draws blue fixtures; animation, random visual scene choices, sounds,
particles and achievements remain pending and do not change these bodies.

Gremlins raise the construction audit to 94 of 168 entries, adding 3-1 through
3-7, 3-13, 3-17, 3-19, 3-24, 4-5, 4-16, 5-3, 5-23 and 7-12. Their 48 journals
are supported, but some later open-loop trajectories still diverge, especially
3-19, 3-3 and 7-12; native fixture agreement is not full journal parity.

## Scene snails

Windows `Snail::initWith` (0x4A69F0) merges its body design with placement fields,
mirrors the first four authored polygon points through six-significant-digit
text for negative speed, reads `activeOnStart` with boolalpha, and clamps
`percent * 0.01f` to [0, 1]. The loader attaches it to the named polygon after
arbitrary ropes and before pins (0x4A6FF0). Attachment adds the unrotated local
spline point to the target body's origin and uses the tangent angle; the target's
own angle does not rotate the snail path. The current attachment targets are
polygon shapes, covering every snail target in the supplied Windows maps.
Circle and rectangle track factories remain unsupported explicitly.

Snails update after balloons and before fireballs (0x4A7570). An active snail sets
the next step's velocity to `(derivative * speed * MaxRealT + positionError) *
0.005f`, with `speed = (float)(editorSpeed * 0.06)`. The position error is the
fixed logical path position plus its local point minus the live body origin in
logical units. The cyclic parameter advances by fixed dt times speed and wraps
with inclusive endpoints. Angular velocity comes from successive tangent angles;
the Windows wrap first adds `2 * pi` while below `pi`, then subtracts it while
above `pi`, preserving the rounding this unusual order introduces.

The strict 80-unit press radius uses the last child-update position plus (0,-57)
rotated by the Windows 1024-entry tables at the live body angle (0x4A7460).
Its inlined index uses the full reciprocal of pi; at angle bits `0x3f317fff` it
chooses entry 112, where the generic helper's rounded pi chooses 113. Both this
boundary and the difference from direct sine/cosine have regressions. Both activation
and deactivation clear linear and angular velocity; deactivation also resets the
body's angle to the current spline tangent (0x4A7140/0x4A7220). Snails receive taps
after frogs and before balloons and pumps. Their journal entries follow candies
and precede fireballs; they do not register names or become capture candidates.

`SnailReference.json` contains 720 independent native ticks: forward and reverse
walking across parameter wraps, four pause/resume presses per scenario, candy and
track contacts, and a normalized track body rotated 25 degrees while the snail
path remains unrotated. Every body component and parameter matches bit for bit.
Fixture SHA-256:
`65fba452a028c8df3dd78a3d8656fca92a31fb93483e05010e9471fd2a4f69e6`.
The desktop uses diagnostic markers; visual timelines, sounds and achievements
remain pending and do not change these bodies.

Snails raise the construction audit to 113 of 168 entries, adding 6-1 through 6-7,
6-10, 6-11, 6-13, 6-15 through 6-18, 6-21 through 6-24 and 7-18. All 57 added
journals run. In the 19 newly supported main journals the largest snail-origin
drift is below 0.000002 m, but other bodies still diverge later; this is not a
claim of full original-runtime parity.

## Scene balloon generators

Windows `BalloonGenerator::initWith` (0x485420) reads `balloons` with `intValue`
and doubles `rope_length`, then builds the master `balloonGenerator` design (a
static sensor circle) at `(2 * intValue(x)) * 0.005f`, not through the factory's
division by 200. Generators are created in document order, are added as children
after balloons and before candies, and never enter the hint journal. Their
begin-contact delegate (0x4859B0) accepts only game-body type 4, the candy, and
stores its body; native `BeginContact` dispatches these "pre-solve" delegates for
sensors as well. Every earlier delegate returns false for this pair.

The generator updates after balloons and before snails (0x4857C0). A stored candy
schedules the generator for removal (destroyed first in the next step's removal
phase) and activates its balloons at the generator's origin times 200. The inlined
`activate` and `createBalloonWithRope` (0x484F20) start at `(count - 1) * -0.25f`
radians and add 0.5 per balloon. Each rotates the point one rope length above the
generator using float `sinf`/`cosf`, as `x + (c * dx - s * dy)`, `y + (c * dy + s * dx)`,
with `dx = x - x` and `dy = (y - length) - y`. It then stores `(int)(v * 0.5f)` as
the balloon's editor coordinates in the merged master `balloon` design.

Before the body exists, `checkIsBornDead` (0x483920, 0x483B00, 0x483C40) queries the
AABB of the balloon circle. The circle is built from the design values times
`2.0 * 0.005f`, with fourteen rim points stepped by `0.44879895f` plus the center.
Every static non-sensor fixture removes the points it contains (exact `TestPoint`);
ten or fewer remaining points make the balloon born dead. A born-dead balloon
flies away at once: it is inactive and its body is destroyed, with no rope. Any
other balloon gets a rope from its (0, 45) shift to the candy's origin, slowing
the candy like grab ropes. As with authored ropes, the rope's points use the menu
material design.

`GameScene::addRope` appends the rope's journal slot and child, then `addBalloon`
(0x4F3A00) appends the balloon's, born dead or not. Late children (gremlin clones
and generated ropes and balloons) share one list in this order. They first update
in the step after their creation, since the scene caches its child count, after
every initial child. They take presses before every other child, newest first,
and journaled cuts address generated ropes by their appended slots. Above ten
balloons, `addBalloon` sends the oldest active generated balloon flying. Native
code drops finished balloons from that list after their animations, while the
port keeps every balloon for its index, so above the limit the port counts more
balloons than native. No supplied level comes near the limit: each level has at
most five generators, and each fires once. Grab-generated balloons
(`GrabGeneric::generateBalloons`, late in the Windows post-pass) remain rejected,
because every supplied grab has `balloons="0"`.

`BalloonGeneratorReference.json` was generated independently with the local native
Box2D 2.2.1 profile. All eight body components, including gravity scale and damping,
match bit for bit over 600 ticks, along with balloon activity, generator removal and
rope counts. Fixture SHA-256:
`e4d5268de92a3fe5c5c92038d21b01a5a919bb880f6d1b92ac89c99de383fb0e`.
Particles, sounds, the challenge element flag and the generator animation remain
pending; none of them changes these bodies.

Generators raise the construction audit to 124 of 168 entries, adding 2-8, 2-9,
2-10, 2-12, 2-18, 2-21, 2-24, 4-10, 5-11, 6-9 and 7-22. All 33 added journals run;
the 11 main journals report no unsupported events. Generated balloons stay within
0.09 m of the recordings with no missing samples. In 2-12 the drifting candy reaches
two generators a frame or two after the recording did, so two balloon slots are
briefly unknown. Because burned shapes leave the scene's shape list, the initial
journal size is recorded at construction; without that, 7-22's journaled cut of
its generated rope failed after a fireball burned a shape.

## Scene magnets

Windows `Magnet::initWith` (0x494420) merges the master `magnet` design (a static
solid circle) with the placement and reads `force` from `magnet_design`. Magnets
are created in document order and never enter the hint journal. They are added
as children after stars and fruits, so they update after them and before late
children, and take presses right after late children.

Each update (0x494D80) first caches the logical position and degree rotation, then
looks up the object registered as `candy`. If there is one, the magnet turns to face
it with `SetTransform` at the current origin. The angle is the x87 `fpatan` of the
center difference (candy minus magnet), stored to float, plus `1.5707964f`. An active
magnet normalizes that difference like `b2Vec2::Normalize` and applies `-force` times
it to the candy's center. It then sets the candy's linear damping to 3. An inactive
magnet restores the candy's design `linearDamping` while the candy's magnet counter
is zero. Ropes update earlier in the same step, so in magnet levels this
replaces the rope's candy slowdown whenever no magnet is on.

A press (0x494AA0) toggles the newest magnet within 95 logical units (strictly) of
the point 25 units along `(rotation + 90f) * pi / 180`, computed in double precision.
The rotation and position come from the start of the last update, before its
`SetTransform`, so the press direction lags one update. While a candy named `candy`
exists, the toggle also moves the candy's magnet counter. Without that name, presses
still toggle, but nothing turns or is pulled. Sounds, the levitation achievement and
the challenge element flag do not affect bodies.

`MagnetReference.json` was generated independently with the local native Box2D
2.2.1 profile. All eight body components and magnet states match bit for bit over
600 ticks. Fixture SHA-256:
`7d9f8f1883cc0119adee1a3db3ee4c1686b31aa40c4a2d6de0bbbced3ef6af4d`.

Magnets raise the construction audit to 141 of 168 entries, adding 3-8, 3-9, 3-10,
3-12, 3-15, 3-16, 3-22, 4-8, 4-15, 4-18, 5-6, 5-14, 5-16, 5-17, 5-20, 6-20 and 7-14.
All 51 added journals run, and the 17 main journals report no unsupported events.
Several stay bit-exact through active pulls and several presses: 3-12 through frame
195, 5-6 through 368, 5-20 through 401 and 4-18 through 510. The earliest breaks
elsewhere are ulp-level angle differences on unrelated bodies. 3-12 later drifts
2.1 m after the candy, rotating against wood, leaves the exact path at frame 197.

## Hint journal regression baseline

The original developer hint journals double as playtests. `replay-check` replays one
open-loop and reports per-object drift; `replay-baseline <cache> [--write file]` replays
all of them (about four seconds) and records, per desktop pack, entry and variant, the
first snapshot frame that is not bit-identical and the count of exact samples before it.
`tests/Ctr2.Tests/ReplayBaseline.tsv` stores only those identities and counts, and
`ReplayBaselineTests` fails when any supported journal's exact prefix shrinks or a
supported journal becomes unsupported. It is skipped without a local cache
(`CTR2_CACHE`, or `cache/windows-alpha` at the repository root). Regenerate the
baseline after an improvement.

Each journal replays in its own variant. Recorded fruit catches are compared with the
port's collections like star catches, never applied.

Journaled cuts store only the crossing and bone, but the recording cut live, so the
replay rebuilds the swipe impulse: the recorder logs a cutting move after its cuts,
so the segment runs from the previous touch to the next move in the same frame.

Replaying the port's own world mutations against native Box2D 2.2.1 matches bit for
bit on every supported level for the whole journal, so remaining differences come from
game logic or from the recording platform's float library. Most prefixes now end at
ulp-level differences that first appear on rotating bodies, consistent with the
recording platform's `sinf`/`cosf` rounding a few results differently from the
host's: correctly rounded or FreeBSD-style sine and cosine fix some early divergences
and introduce others, so the port keeps the host's float trigonometry. Level 1-9 is a
known exception: in all three variants the candy jumps by millimetres between two
exact samples while swinging on its ropes, with no input or contact in between, and a
one-ulp nudge does not reproduce it, so an unported logic detail is still involved.

### Snapshot-corrected replay and gameplay outcomes

Open-loop replay amplifies those ulp-level differences until a candy misses a star,
sensor or Om Nom, so it stops verifying gameplay after a few hundred frames.
`replay-check --corrected` and `JournalReplay.Run(..., corrected: true)` do what the
original hint playback does. After step N, each object in snapshot N is first
compared, which measures only the drift since that object's previous recorded state,
and then applied as `SingleBodyObject::applyObjectState` does. `SetTransform` runs
when position and angle are all recorded; stars and helicopters omit the angle, so
they are never moved. Then the linear and angular velocities are set when recorded.
`Booka::applyObjectState` skips a Booka once Om Nom has caught it. The recorder
writes a state when an object moved more than 0.5 m or 0.17453 rad, or at least every
60 frames. Velocities appear only in full syncs: recording start, a gremlin clone and
a thrower capture. Rope points have no state. `--follow <journal index>` prints the
port's per-frame pose of one journal object to standard error.

`replay-outcomes <cache> [--corrected]` plays every supported journal to its end, plus
600 input-free ticks, and grades the result against the recording. Main levels must
win with the recorded star set, challenges must win with at least `desiredStars`, and
fruit challenges must win. Their fruit counts are compared with the recorded catches,
because 17 recordings fall short of their own `minFruits` by catch count. Rope limits
and forbidden elements constrain input, which the recording already obeys.

With 141 startable levels (415 journals), open-loop replay passes 131/141 main,
118/137 challenge and 98/137 fruit-challenge journals (within two fruits). Every
failure follows a trajectory divergence that starts at an ulp-level difference.
Corrected replay passes all 141 main and all 137 challenge journals with the
recorded stars, and 135/137 fruit challenges, all within two fruits (128 exact).
Remaining corrected errors above 5 cm within one snapshot interval are mostly random
Booka flights. Others are bodies near a tipping point whose uncorrected velocities
bifurcate: Om Nom in 7-14 and 6-20, and gremlin stacks.

The two corrected fruit-challenge losses differ:
- 6-20 is such a bifurcation (Om Nom on a rotating shape).
- In 3-12 the candy is pulled back up by a magnet from about 5.76 m, past the
  canvas-based loss line at 960 + 190 logical units. The original measures
  `isOutOfScreen` against `ScreenSizeMgr::FULL_SCREEN` and `SCREEN_OFFSET`, the
  device's whole visible area. The recording device evidently showed more below the
  canvas than the port's canvas-only rule assumes. Matching that rule depends on the
  desktop viewport, which still letterboxes the canvas.

### Water physics checkpoint

Windows water is a late scene child, after magnets and before appended children,
with no body or journal slot. Its update (0x4AF1C0) walks dynamic bodies newest
first, caches each object's above/below design on first sight, and invokes the
above/underwater callback every tick before assigning gravity and damping. Candy,
fireball, Booka, gremlin, thrower and Om Nom use master designs; general polygons
use their merged design. Rope points, balloons and other base-class objects have
no water parameters. New gremlin clones enter the cache on the next water update.

The surface is twice the authored y. World-center y times 200 decides immersion:
equality is above water. Below gravity ramps over 80 logical units; Windows rounds
`depth * 0.0125` to float before multiplying the cached underwater gravity. The
underwater design multiplies gravity by `gravity_multiplier` and replaces linear
and angular damping. Windows formats these through a default C++ output stream,
then reads the strings back as floats (0x4A6140 → 0x48D2B0 → 0x48D6B0): six
significant digits. For example `0.7f * -0.6f` is stored back as `-0.42f`.
Above-water assignments restore design values even after ropes or magnets changed
them. Flow advances the surface after the body walk; supplied levels all use zero.

Fireball water entry (0x489710) starts scene 2, makes the fixture a sensor and
zeros its velocity. Its next fireball child update destroys the body, releasing
carriers and cutting ropes; its visual removal follows the water fade timeline.
Om Nom records the latest 60 immersion callbacks. The newest 59, divided by the
fixed denominator 59 with double arithmetic rounded to float, gate the righting
impulse above 0.5. The impulse runs after character state updates and motion
sampling, before the next water callback. Submerged characters do not enter the
ordinary falling state. The swim/dive artwork and WATER_* presentation states are described in the
water presentation checkpoint below.

Candy, Om Nom and Bookas share the Windows offscreen test (0x4A68E0): outside the
expanded rectangle, or beyond the horizontal margin while submerged with speed
squared strictly below 0.0001. Expanded upper bounds are exclusive. Bounds still
use the logical canvas; the viewport limitation described above remains.

`WaterReference.json` contains 960 invented native Box2D snapshots: a candy on a
rope dipping through the surface, floating dynamic wood, a submerged rotating
Om Nom (including resurfacing and history decay), and an extinguishing fireball.
All nine body components compare exactly at every tick: position, velocity,
angle, spin, both damping values and gravity scale. Om Nom's comparison runs with
gameplay enabled. Fixture SHA-256:
`0caa4a88a99c404d8244bd992d4cf6ceddb1f47fdba4e8c6dbcb13ed056f22ff`.
Focused tests additionally cover clone discovery, flow ordering, partial depth,
master versus merged designs, rope-point exclusion, water fade lifetime,
underwater side loss, journal exclusion and sampling before the righting impulse.
Release validation: 691 tests passed, zero skipped.

The current construction audit is **166/168**; water adds 25 entries. The remaining
entries are **1-8** (tutorial sensor) and **2-14** (negative Om Nom spawn, an existing
admission restriction). The replay baseline is **490 supported journals / 18027
exact-prefix samples**, with no previously committed prefix shrinking.

The corrected outcome sweep covers all 78 water journals. Three variants of 2-14
remain unsupported. All 75 admitted water variants win with zero unsupported
journal events: main 25/25, challenge 25/25, fruit challenge 25/25. Full collectible
verdicts pass 25/25 main, 24/25 challenge and 24/25 fruit challenge. Two limitations
remain after trace/follow inspection:

- 6-8 challenge misses star 1 (recorded tick 511); the candy's largest discrepancy
  between snapshots is 0.039977632 m, with early differences below a micrometre.
- 2-16 fruit challenge collects 41/42 recorded fruits. Fruit 91 (recorded tick 661)
  is missed, and fruit 90 is collected at 708 instead of 653. The candy begins with
  ulp-scale differences; the largest discrepancy reaches 0.251062 m at tick 708.
  Om Nom's position stays exact. The remaining trajectory divergence is unresolved.

Across all supported content, corrected replay wins 166/166 main, 162/162 challenge
and 160/162 fruit challenges. Full collectible verdicts pass 166/166, 161/162 and
155/162 respectively; five won fruit challenges differ by one recorded fruit. The
two fruit losses remain 3-12 and 6-20, described above. These results establish the
physics checkpoint and its limits, not full water visual or trajectory parity.

### Water presentation checkpoint

Om Nom now selects the Windows water scenes: `WATER_DIVE` (37), `WATER_SWIM`
(38), `WATER_MOUTH_OPEN` (39), `WATER_MOUTH_CLOSE` (40), `WATER_CHEWING` (41)
and `WATER_FAIL` (42). The Windows scene name at 38 is `WATER_SWIM`; its gameplay
state is WaterSwimming. Windows init explicitly loops this scene. Presentation
wraps its animation time by the loaded scene duration using the fixed simulation
clock; drawing, pause and restart cannot advance or retain an old scene clock.

Windows update 0x4989F0 enters dive from idle, falling or rolling when the newest
five immersion callbacks, divided by five, exceed 0.5 and the Booka cooldown is
negative. It consumes the previous water update's history, like the righting
impulse. Dive completion selects swimming. Swimming opens its mouth strictly
inside 150 logical units, closes it beyond 170, and returns to idle when the
newest-59 immersion average is below 0.4. Mouth logic runs before the water exit
check, matching Windows. Opening a new water mouth scene also resets artwork
mirroring before applying the chosen facing.

Physical feeding in water-mouth-open selects water chewing and scene 40. On its
completion, scene 41 starts; it can finish the level, with the existing 2.8-second
chewing fallback retained. Closing without food instead returns to scene 38.
Candy loss from a water state selects scene 42. The existing revision guard rejects
stale animation callbacks. Water chewing participates in the same capture and
Booka chewing checks as the other chewing states.

Windows connection timelines, constructed in Om Nom init and configured at
0x497D40, deform the root for 0.125 seconds before switching to dive, resurfaced
idle or water failure. Scale changes from (1, 1) to (1.1, 0.9), with a six-unit
downward offset. The selected child scene starts at that connection's end while
another 0.125-second timeline restores the root. Mouth-close completion starts
only this recovery alongside swimming or chewing (0x498960 / 0x499FD0). Child
scene completion remains active during recovery. A new scene cancels an
interrupted connection, so a Booka response cannot later be replaced by its old
pending dive. These transitions reuse the procedural root renderer without
changing the physics bodies.

Ten invented regressions cover short/long history timing, connection deformation
and loaded dive timing, mouth hysteresis and facing, stale callbacks, water
feeding and failure, Booka cooldown/interruption, and a generated-cache rendering
readback. The readback test distinguishes the repeated swim cycle from a frozen
terminal pose and verifies pause and restart. All 960 native water physics
snapshots still match across all nine checked components; the fixture and its
SHA-256 above are unchanged.

Local Windows-cache render inspection covered all six water animation scenes and
playable frames from 5-2 (tick 18) and 7-24 (tick 60). This verifies decoded artwork
and scene rendering, not visual parity with an original-game recording. The water
surface and tint are covered by the next checkpoint; particles and audio remain
separate presentation work.

The construction audit remains 166/168 (1-8 tutorial sensor and 2-14 negative
spawn remain blocked). The replay baseline remains 490 journals / 18027 exact
prefix samples, with no prefix shrinking. The 3-20 main replay compares 10996
late body samples instead of 11008 as its water chewing changes completion timing;
its exact prefix remains six samples. Corrected replay outcomes are unchanged:
all 75 admitted water variants win, with the same missing challenge star in 6-8
and missing fruit in 2-16 documented above. Full collectible verdicts remain
25/25 main, 24/25 challenge and 24/25 fruit challenge for water, and
166/166, 161/162 and 155/162 over all supported content. Release validation:
701 tests passed, zero skipped; build, format verification and diff checks pass.


### Water surface and tint checkpoint

The desktop renderer now loads Windows resource **250**, preserving the selected
resolution's pixel quads, density and trim metadata. Rear wave 7 draws before
terrain and gameplay objects; upper tint 6, lower tint 5 and front wave 8 draw after
them, before tutorial/UI. The water placement no longer draws a diagnostic dot.
Windows Water::initWith (0x4AE510), drawLayer (0x4AEED0) and the GameScene draw
passes (~313360 / 314080 in the exported C) establish this order.

The surface starts at twice the authored y. Upper tint starts 11 logical units
below it and keeps its trimmed logical height. Lower tint stretches from there
to the logical canvas bottom; both tones stretch across the canvas width and
retain atlas alpha. The native center-based scaling and compensating positions
reduce to these rectangles. Viewport letterboxing still bounds the logical canvas;
this does not resolve native FULL_SCREEN / SCREEN_OFFSET coverage.

Wave strips repeat their trimmed logical width. Windows XShiftTiledImage::update
(0x4AD8D0) accumulates float speed times the fixed delta, wraps with one tile-width
correction, and truncates soft x to an integer for drawing. Front speed is positive;
rear speed is negative. The rear's initial x is offset8.x minus offset7.x, then the
first update replaces it with the scrolling position. Its y remains offset7.y
minus offset8.y. Windows generateTimeline (0x4ADD20) alternates ease-out and ease-in
between the graphics design's vertical scale extrema, scaling about strip center.
The supplied design has speed 180, scale minimum/maximum both 1, and 0.6 seconds
per leg. Rendering advances only visual phase from completed simulation ticks;
repeated draws and pause cannot advance bodies, and restart restores phase.
Static previews use the supplied preview time.

Six generated-atlas rendering cases cover portrait and ultrawide fitting, two
texture densities, layer occlusion, relative trim offsets, full-width tiling,
upper/lower tint geometry and alpha, opposite scrolling with wrap, pause/restart,
render-side physics preservation and static-preview ease-out scaling. The fixtures
include extruded edge texels, as verified in the Windows water atlas, so stretched
linear sampling does not introduce invented transparent borders.

Real cached frames were inspected at 5-2 tick 18 (640x960, resolution 2) and 7-24
tick 60 (1280x480, resolution 4 with density 1.56). These establish decoded atlas
rendering and viewport placement, not pixel parity with original-game captures.
At this checkpoint, light traces, bubble/splash particles, audio and the optional
full-scene WaterEffect distortion path (~314083..314118) remained separate. The two-pass atlas
rendering is also the native path when that optional render-target effect is off.

Construction remains 166/168; replay prefixes remain 490 supported journals /
18027 exact samples, with no shrunk prefix. Corrected outcomes remain unchanged,
including all 75 admitted water variants winning and the previously documented
6-8 challenge star and 2-16 fruit discrepancies. The native water physics fixture
and its SHA-256 remain unchanged.

Release verification for this checkpoint: 707 tests passed, zero skipped; build
completed without warnings or errors, format plus verify passed, and diff checks
were clean. Review found no important correctness issues; rear motion and wrap
coverage was added after review.


### Water light and particle checkpoint

`WaterEffects` advances on the fixed simulation clock, before the late water body
walk, and exposes immutable snapshots. Its private xorshift stream never reads or
writes physics randomness. Repeatable visual trajectories intentionally do not
reproduce the original global random stream. Static previews advance a separate
model using preview ticks; drawing a live session cannot advance effects.

Seven traces use atlas 250 quads 3/4, left/top at the initial water origin. Each
chooses one delay in 0..2 seconds at creation, reuses that delay between cycles,
and reselects X from `-width/4` through `screenWidth+width/4`. Their linear opacity
keys are 0, .3, .4, 0 at 0, .7, 1.3, 2 seconds (Windows 0x4ADEA0). Native node RGB
premultiplication followed by SRC_ALPHA/ONE blending means straight source RGB
contributes `RGB * textureAlpha² * opacity²`. The renderer composes a sampled-color shader with its sampled alpha after linear
filtering, then applies opacity squared; imported pixels and metadata stay intact.

Ambient systems start every 4..7 seconds at logical screen bottom+10. Underwater
touches create short bubble systems and return false, allowing earlier touchable
children to handle input. Water receives touches after late spawned children and
before magnets. Bubble quads 0..2 have capacity 3, initial count 1, emission rate
10, speed 120±80 at -90±12 degrees, position variance (30,30), start scale .75±.25,
end .3, initial alpha .8±.4, fading to zero. Life and duration are 1.2 seconds for
touches and **10.2 seconds** for ambient systems (Windows 0x600950, unlike Android).
Emission occurs before integration, with a strict `counter > 1/rate` condition.
Existing children update before emission. Life is checked before decrement;
removal swaps in the last live particle and visits that slot again.

Bubble images have a .2-second-per-leg 1→1.2→1 quadratic ease-in-out Y bounce
and harmonic X `40*sin(previousElapsed*5)` with initial elapsed in [0,2).
They use left/top anchors inside zero-sized wrappers. If P is particle position,
S its scale, H harmonic X, B bounce, and w/h trimmed logical dimensions, the
rendered center is `P + S*(H+w/2,h/2)` and size `S*(w,h*B)`. Thus wrapper scale
also scales H. Surface visibility fades through y=initialLevel+17..+5; the base
particle color is restored each tick, so that extra fade does not compound.

Fast body entries use ten splash quads 9..11 at the logical body center, after
water callbacks. Fireballs extinguish and clear velocity before this test. The
strict speed threshold is 1.7 m/s; first-frame entries are suppressed. Recently
immersed bodies remain remembered for .06 seconds above water. DeltaCollision
also suppresses the same body within ten logical units, recording entries for
.2 seconds even when too slow to splash. Its Windows expired-prefix behavior is
preserved: if every record expired, it retains the records until an unexpired
record follows them (0x5D7CC0/0x5D7D20).

Drops live 1.3 seconds, with gravity (0,600), position variance (30,10), angle
-90±90 degrees, speed 280±60, radial acceleration 160±30 and tangential 50±15.
Scale is .9±.4→.5±.15 with ratio preserved. The initial quad is transparent;
callbacks prepare opacity `life/1.3` (zero below spawn Y) and velocity-aligned
rotation for the next quad update (0x600C40/0x600E60). Newly spawned splashes
first integrate next tick. Foreground traces and bubbles follow the front wave;
splashes draw afterward in the scene foreground pass.

Verification covers emission timing, Windows ambient duration, surface fade,
first-frame/speed/re-entry suppression, fireball callback order, immutable
snapshots, independent randomness, generated-atlas blend/anchor readback at two
densities and translucent alpha, repeated draws and restart. Audio and optional
framebuffer distortion remain pending. Actual-cache frames establish asset
rendering and placement; original-game pixel/particle trajectory parity remains
unverified.

Release verification: 718 tests passed, zero skipped; build had zero warnings or
errors, format plus verify passed, and diff checks were clean. All 960 native
water body snapshots remain exact. Replay baseline remains 490 supported
journals / 18027 exact-prefix samples, with no prefix shrink. Construction remains
166/168. Corrected outcomes remain 166/166 main, 161/162 challenge and 155/162 fruit
verdicts; all 75 admitted water variants win with the same known collectible
mismatches. Actual-cache inspection includes 5-2 tick60 portrait, 7-24 tick360
ultrawide at resolution4/density1.56, and a released-body 5-2 splash at tick60.


### Water audio checkpoint

Windows water initialization calls LoopSoundHelper::play(323), looping at volume 1
(0x4AE510/0x4AC780). The existing fast-entry gate emits sound 324 or 325 at volume 1
before creating its splash (0x4AF1C0). Touch and ambient bubbles have no sound
request. Fireball::onUnderWater emits 255 once when starting its water fade,
including initial immersion (0x489710); it does not pass the splash-speed gate
because its callback already cleared velocity.

The PE resource registry at 0x8EE9E0 (eight-byte entries) maps 323 to
`water_splash_idle.ogg`, 324/325 to `water_splash01.ogg`/`water_splash02.ogg`, and 255
to `fire_drown01.ogg`. SoundMgr pause/resume are 0x6CA910/0x6CA920. Source choices
and full volume are preserved; random splash selection uses an independent audio
stream, leaving physics and existing particle trajectories unchanged.

Simulation emits device-independent `SoundPlayed` events. Interactive play owns
an NVorbis decoder and SDL stereo float stream. The mixer duplicates mono,
linearly converts decoded rates to 48 kHz, loops ambience, sums overlapping voices
and clips output. Pause retains phase/queued PCM; restart clears old voices and
subscriptions and restarts ambience. Cleanup occurs before SDL shutdown. Dry
levels do not open water audio; snapshots/replay/audit commands never open devices.
Missing files or an unavailable output device report once and keep play available.
This ports these water sounds, not the rest of the original sound manager.

Seven focused regressions cover source cue gates, extinguishing, RNG independence,
PCM overlap/looping/rate conversion and pause/restart/disposal. A local probe
successfully decoded all four actual imported clips (mono, 22050 Hz), verified
finite nonzero PCM and queued/paused/resumed it through SDL's dummy device. This
verifies decoding and device plumbing; physical speaker listening remains unverified.

Release verification: 725 tests passed, zero skipped; release build and formatting
verification clean. Replay baseline regenerated unchanged: 490 supported journals /
18027 exact-prefix samples; no prefix shrink. All 960 native water body snapshots
remain exact. Construction 166/168; corrected verdicts 166/166 main, 161/162 challenge,
155/162 fruit, with all 75 admitted water variants winning. Fixture SHA-256 remains
`0caa4a88a99c404d8244bd992d4cf6ceddb1f47fdba4e8c6dbcb13ed056f22ff`.
Independent review confirmed resource IDs and source pause/resume, with no
outstanding important issues. WaterDrawer distortion follows in the next checkpoint.

### Water distortion checkpoint

Windows WaterDrawer always creates its framebuffer and drawer for water scenes;
the full pass runs while the scene is updateable and the target is ready. This
port captures at physical output dimensions into a reused Skia raster surface,
then draws a textured mesh before foreground splash particles and tutorial/UI.
Rear water, objects, front water, light traces and bubbles are inside capture.
The background-only path is used when paused or a bounded target cannot be
allocated. Existing fitted-canvas clipping and letterboxing remain unchanged;
FULL_SCREEN is reconstructed from physical dimensions divided by viewport scale,
with SCREEN_OFFSET=(FULL_SCREEN-logicalCanvas)/2.

The embedded shader is at 0x601920; grid setup is at 0x600EE0/0x601040. It emits
20×20 cells, 2400 vertices, in X-major order, with triangles TL/TR/BR and TL/BL/BR.
Only outer positions expand by two logical units; UV bounds remain nominal.
Displacement runs at vertices and interpolates across each triangle. Evaluating
sine per pixel would produce different motion. For framebuffer UVs, Y is inverted;
after native vertex displacement the renderer converts it to Skia's top-down
sampling.

With node position (x,y), phase t, and bounds (maxU,maxV), the shader condition is
`y-50 > water.level+15`. Its envelope is `sin(y*3.1415926/192)`; U displacement is
`maxU*sin(y/120+t)*.004*.0112*envelope`; V displacement is
`maxV*sin(x/60+t)*.0112*envelope`. Each coordinate clamps to its texture bounds.
All quantities use float operations. Texture filtering is linear; presentation
uses source replacement, matching disabled native framebuffer blending.

Drawer time is a separate float initially zero, advanced by `+=delta` at the end
of scene updates (~312525). The renderer reconstructs each fixed increment,
including skipped draws; it does not substitute tick*delta. Repeated renders
cannot advance phase. Rewind or simulation replacement resets the drawer.

The paused fallback (0x601450) uses the selected background quad's width/height
normalized by texture dimensions, with UV origin zero regardless of atlas origin,
over FULL_SCREEN without the ordinary background height-fit. It uses positive Y
UVs. Rebuilding this mode adds 3.1415926 to phase once. Resuming framebuffer mode
preserves that phase. The native comparison checks inversion, origin X and max Y,
not max X. The renderer follows those checks. Only backgrounds already supported
by this port participate in this fallback; other location backgrounds remain
outside this checkpoint.

Tests include independent C++ float shader outputs and triangle-interpolated
readback references at 640×960, 1280×480 and 1280×1920, native grid/overscan/order,
strict threshold and UV direction, phase transitions, background mapping, scene
capture, foreground splash exclusion, resize, repeated draw and restart. Earlier
sprite/wave geometry tests use the frozen direct path to isolate those contracts
from postprocess resampling. Native shader expression checks are CPU references,
not original GPU execution or pixel-parity evidence.

Actual imported-cache frames were inspected at 5-2 tick 60 portrait and 7-24 tick
360 ultrawide (resolution 4, density 1.56), including tap bubbles, plus 5-2 tick 60
after release-at 0 for visible splashes. Source/lifetime review found no important
outstanding issue. Physical speaker listening and original-game GPU/pixel
comparison remain unverified; the existing letterboxing and unsupported-level
gaps remain separate.

Release verification: 740 tests passed, zero skipped; build had zero warnings or
errors, format plus verify passed, and diff checks were clean. All 960 native
water body snapshots remain exact with the same fixture SHA-256. Replay baseline
remains 490 supported journals / 18027 exact-prefix samples, regenerated with no
prefix shrink. Construction remains 166/168. Corrected verdicts remain 166/166
main, 161/162 challenge and 155/162 fruit; all 75 admitted water variants win,
with the same known collectible gaps. No decompiled code or game assets are tracked.
