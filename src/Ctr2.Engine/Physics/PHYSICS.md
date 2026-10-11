# Candy and rope physics slice

This is an altered C# subset of Erin Catto's Box2D 2.2.1. See
[the original license](BOX2D_LICENSE.txt). It implements contact-free, always-awake
circle bodies and center-anchored rope/soft-distance joints. It is not a complete
Box2D replacement and does not establish original-game runtime parity.

## Behavior contract

Implementation research remains private and outside the tracked repository.
Do not include decompiled game code or reconstructed reference implementations
in commits; verification artifacts use synthetic inputs and numerical fixtures.
The Windows master design document supplies body radius, density, gravity scale,
damping and rope parameters. Editor coordinates are doubled into logical units;
200 logical units equal one meter. Positive Y points down. Each tick is `0.0167f`
seconds, with gravity `(0, 10)` and 15 velocity / 30 position iterations.

Rope construction preserves generated material points, the shorter final segment,
and coincident initial candy/final material-point centers. Native rope updates run
*after* the world step: relative endpoint speeds feed a 60-sample damping average;
stretching replaces the last rope constraint with a 10 Hz / 0.3 spring and adds a
whole-rope spring. Restoring the rope follows the native reaction-force sign check.
Independent islands preserve depth-first joint order and stop position iterations
independently. Linear slop is CTR2's `0.001f` rather than stock `0.005f`.

`CandySimulation` requires zero initial velocities, centered grab anchors and
unrotated placements. Unsupported definitions fail explicitly. The preview uses
Windows atlas 133 quads 0/1, restored trim geometry, and the native combined
`0.92 * 0.92` artwork scale, independently of fixture radius.

## Numerical fixture

`tests/Ctr2.Tests/PhysicsReference.csv` contains expected numerical states from
synthetic sensor-only scenes evaluated with upstream C++ Box2D 2.2.1. The local
reference used the CTR2 linear slop of `0.001f`; no other CTR2 Box2D patches were
applied. The reference harness and regeneration tools remain in ignored local
storage and are not part of the repository. No original game assets, level data,
or decompiled game code are included in this fixture.

Each headerless CSV row has seven columns:

| Column | Meaning |
| --- | --- |
| scenario | Synthetic scene identifier |
| tick | Completed fixed-step count |
| body | Zero-based body creation index |
| x, y | Position in meters, positive Y down |
| vx, vy | Velocity in meters per second |

Samples cover tick 1 and every tenth tick through 180. Tests compare sampled
components within `0.00002` meters or meters/second. Cases cover free fall/damping,
slack/taut ropes, chains, soft springs, multiple anchors, independent islands,
rope updates and diagnostic release. The `game` and `game_release` names identify
synthetic rope-update scenarios, not captures from the original game. The latter
releases before zero-based tick 60. Synthetic scene definitions live in
`PhysicsTests.cs` and `CandySimulationTests.cs`. A separate regression ensures
adding an unrelated chain cannot change a chain's trajectory.

Tests consume the checked-in fixture without a native reference harness, Clang,
downloads, or game assets. These fixtures validate the supported contact-free
subset; contact/broadphase behavior and original-runtime parity remain unverified.

## Remaining work

There are no contacts, collision polygons, sleeping, angular dynamics, off-center
anchors, candy consumption, stars, hazards or completion logic. The rope draw is a
material-point polyline, not the final native textured rope. Candy flare, skins,
masks and angular rendering remain pending. `--release-at` removes the final candy
link and stretch spring before a zero-based tick; it is a diagnostic release, not
the original swipe-cut algorithm, which creates cut fragments and changes masses.
Live time accumulates fixed ticks; a bounded catch-up retains unprocessed backlog.
Original recorded-input replay, interactive cutting and one playable original level
remain acceptance milestones. Web work follows the PC slice.
