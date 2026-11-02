# Simulation regression tests (Directiva)

This suite targets the currently implemented simulation foundation only:
`SSimulation.Simulation`, `SSimulation.SBEntity`, and their DMath dependencies.
It does not pretend that Unit, Nexus, Resource, movement orders, attacks, or VM
adapter methods are already implemented.

## Run

1. Open the Unity project and allow the Editor assembly to recompile.
2. Use `Directiva > Tests > Open Test Runner and Reports...` and choose a
   report directory OUTSIDE the Unity project and repository.
3. Use `Directiva > Tests > Simulation > TestEverything` (47 cases) or
   `Directiva > Tests > Math > TestEverything` (15 cases).
4. The existing `Directiva > Tests > TestAll` includes both new categories.
5. Share `SUMMARY.md`, `SUMMARY.json`, or the resulting external report folder
   when investigating a failure. Reports are not stored under Assets.

## Coverage

- `Simulation/Entities` (17): initial state, IDs, ownership, registration,
  duplicate rejection, deferred creation, death/removal, clocks and ordering.
- `Simulation/World` (12): grid sizing, indexing, cell refresh, clamping,
  fractional radii and boundaries.
- `Simulation/Collisions` (13): separation, tangent/overlap cases, disabled
  and dead entities, priority, coincident centers, cross-cell collisions,
  world boundaries and solver passes.
- `Simulation/Determinism` (5): equivalent simulations with matching seeds,
  repeated creation/removal, crowded worlds and exact Fixed tick accumulation.
- `Math` (15): Fixed ceiling/floor/arithmetic, vectors, deterministic random,
  sine/cosine and Atan2 axes.

The Editor test harness uses a small amount of reflection solely to set
`SBEntity.Position`, invoke `SBEntity.Die`, and inspect the private registry,
list, and spatial grid. These are internal implementation details unavailable
from the Editor assembly. No production API was made public for tests.

## Important limitations

- The tests were statically reviewed, but have NOT been executed in Unity in
  this handoff. A PASS count can only be claimed after running them locally.
- The collision tests validate the current three-pass correction algorithm;
  they do not promise that every crowded or wall-constrained scenario ends
  with all overlaps removed.
- Larger CollisionPriority currently means the entity yields/moves more when
  the priorities differ. The tests document this actual behavior; discuss the
  intended meaning before changing it.
- Tests for scripted movement, observations, attacks and Unit/Nexus/Resource
  must be added when those features exist. This suite does not fake them.
- To keep the current production implementation independent of the Editor,
  there is no Unity Test Framework dependency or scene/Play Mode requirement.

## Small runtime dependency correction included in the package

`Scripts/Math/FixedDecimal.cs` received a one-line fix for `CeilToLong`:
when the fractional part is zero or negative, it must return `integerPart`
instead of `decimalPart`. Otherwise, an exact even world dimension such as
20 produces a grid width of zero. Two unused editor-only using directives
were also removed to keep the Math code safe for player builds.

`Scripts/Simulation/**` is unchanged in this delivery.
