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
between contact samples without collecting a star. As in the scene, each star
also owns a kinematic sensor body built by the design body factory and kept after
collection. It cannot push the candy, but its proxy takes broadphase IDs and so
takes part in pair ordering. Collection itself still uses the geometric test above.

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
gameplay adjustments and character star collection remain pending. The opt-in gameplay layer below adds feeding,
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
which then slows down on the rope; an unregistered name keeps no rope. Booka and
fireball ends remain unsupported.

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
locked motor (triggers are pending), and `is_spring`/`spring_gain` are never read.
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
