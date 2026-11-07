# Changelog — Mechanical Water Pump (MWP)

All notable changes are recorded here. Version numbers follow `manifest.json`;
save/config/spec schema versions are separate (see `docs/CONTRACT.md` §13).

## 0.2.0 — 2026-11-04

Mod version `0.2.0`, save schema **2**, config schema **2**.

### Added
- Maintenance system for all four tiers: `condition` 0–100, wear only while producing,
  intervals 8/10/12/15 days, materials 10 Log / 15 Plank / 10 MetalBlock+10 Plank / 15 MetalBlock,
  2 game-hour downtime, automatic maintenance (on by default, can be disabled per pump), manual
  "maintain now", missing-material and no-path states, efficiency multipliers 1.00/0.90/0.75/0.50/0.
- Save schema v2 with `condition`, `autoMaintenance`, `maintenanceState`,
  `maintenanceRemainingHours`; idempotent v1→v2 migration and per-pump corrupt reset.
- Entity panel (`MWP.UI`): highest-priority state with next-step hint, actual Water/h, power %,
  water quality, internal stock, intake depth, pipe state, condition bar, next-maintenance
  countdown, required materials, auto-maintenance toggle and "maintain now" button (≤4 Hz).
- Runtime diagnostics equivalent of `/mwp.diag` (`features.uiDiagnostics=true` logs version,
  pump count, adapter state and average tick time).
- Blueprint data for 4 tiers × 2 factions (8 blueprints), 2 template collections, 3 localizations
  (enUS/zhCN/zhTW), `MWP_Config.json` schema 2, 8 generated placeholder icons.
- Offline tooling: `tools/ApiIndex` (game metadata/IL dumper) and `tools/validate` (validates data
  and shipped-assembly contracts against the real 1.1.2.8 game metadata).
- Documentation: `docs/CONTRACT.md`, `docs/API_FACTS_MWP.md`, `docs/ARCHITECTURE.md`,
  `docs/REQ_TRACEABILITY.md`, `docs/VERIFICATION.md`, `docs/ART_PLACEHOLDER.md`.

### Added (visual upgrade, 2026-11-06)
- **Node animations** on all eight finished pump models: flywheel, head pulley, drive shaft and
  piston child nodes are animated (120 frames @24 fps, seamless loop, 46 animated channels total,
  5-7 per model). They are driven by the game's own `MechanicalNodeAnimator`
  (`AddDecorator<MechanicalNodeAnimatorSpec, MechanicalNodeAnimator>`, IL verified): speed scales
  with power efficiency and the animation stops at 0 hp. Construction-stage models stay static.
  This intentionally overrides requirements §13.3 ("no animation") per the user's request (DV-07).
- **World-scale tiled UVs** (HDBF ART-07 policy) replace the old flat-patch UVs: wood 117-145 px/m,
  metal 121-163 px/m, planks 255-340 px/m, glass 82 px/m, plaster 340 px/m (all above the 80 px/m
  target, no more stretched 3 m faces). UVs stay inside each material's verified canvas so the
  runtime auto-atlas cannot sample a neighbouring material.
- Art validator extended to 274 checks (C1-C14): animation tracks, FPS/frame count, seamless loop,
  rest pose == frame 0, UV canvas containment and px/m density. `16/16 PASS`, determinism re-run.

### Changed
- Rated output raised to **3 / 6 / 15 / 20 Water per hour** (was the 1/3/6/9 baseline) and power
  demand to **100 / 200 / 500 / 700 hp** so no tier is weaker than its vanilla comparison.
- Internal Water capacity raised to 15 / 30 / 75 / 100 (about 5 hours of output).
- Contamination curve defaults to the vanilla-equivalent `q = 1 − c`
  (`badwaterPenalty = 1.0`, `minWaterQuality = 0.0`) to satisfy "never better than vanilla under
  pollution"; the requirements' 0.75/0.25 pair remains configurable (DV-01).
- Engine `BlockObjectSpec.Size` axis convention resolved from game IL: X = width, Y = depth,
  Z = height; shipped sizes are {2,3,3}, {2,3,3}, {3,3,4}, {3,4,4} (DV-03).
- Entity ids in the UI contracts use `System.Guid` (`EntityComponent.EntityId`), not int (DV-05).
- `manifest.json` keeps `OptionalMods: []`; Timber Pipes is detected at runtime instead of being
  declared as a manifest dependency (DV-02).

### Changed
- **DV-06 (2026-11-05, placement feel)**: Mk III / Mk IV no longer copy the vanilla
  MechanicalPump/LargeWaterPump support layout (whose `GroundOrStackable` strip sits in the middle
  of the footprint and needs a large flat bank). Their `Blocks` are now generated with the only
  solid support cells on the **front row** (3 cells), so a 3×1 flat strip is enough and the rest may
  overhang water - the same placement feel as Mk I/II. Envelopes, entrance/water side, colliders and
  every balance spec are unchanged; `gen_blueprints.py` reproduces the layout.

### Fixed
- **BUG-03 (2026-11-07, in-game)**: models looked inside-out/crumpled in game (hollow frames,
  fragmented gears) although Blender previews were fine. Root cause: the T9 refactor moved mesh
  construction into `mwp_scene._fill_mesh()` and dropped the single `bmesh.ops.reverse_faces` call
  that compensates the plugin's mirrored export; every mesh had a negative signed volume and
  inward normals, which single-sided in-game materials render as "inside". Fix: restore the flip,
  rendered a backface-culling reproduction (`art/reports/t10/backface_broken_advanced.png` vs
  `backface_fixed_advanced.png`), and the art validator now enforces **signed volume > 0** per mesh
  (the old normals-vs-winding check passed even when everything was inverted).
- A static diagnostic build (animation curves temporarily removed) was shipped first; the final
  build restores the animations after the winding fix, with the new validator checks C15 (per-frame
  transform vs source), C16 (rotation axis vs part geometry), C17 (nodes not merged) and signed
  volume C9. Final numbers: 16 files / 653,318 B / 30,320 tris / 46 animated nodes.
- **Mk I/II are now all-wood constructions** (user request): FT uses BaseWood_Brown/LightBrown +
  IrregularPlanks_Brown, IT uses BaseWood_DarkBrown/Grey + IrregularPlanks_MetalForged; no metal,
  glass or plaster on these two tiers. Mk III/IV keep the wood/metal look.

- **BUG-02 (2026-11-05, in-game screenshots)**: the panel showed the internal stock as
  `15/2147483647` (Mk II `30/2147483647`) because `SimpleOutputInventory` is created with unlimited
  capacity and the snapshot used `Inventory.Capacity`. The panel now shows the tier design capacity
  (`TierCatalog.InternalCapacity`); production was already correctly capped by the tier capacity.
- **BUG-01 (2026-11-05, in-game)**: a beaver's drink-need evaluation threw
  `NullReferenceException` in `InventoryNeedBehavior.ActionPosition` and aborted the simulation
  whenever a pump existed. Root cause: vanilla `SimpleOutputInventoryInitializer` unconditionally
  adds `InventoryNeedBehavior`, which dereferences `Enterable.CanReserveSlot`, and the MWP
  blueprints had no `EnterableSpec`. Fix: every blueprint now ships a zero-capacity `EnterableSpec`
  (safe `null` action position, no beaver can enter the pump). Validator + build stay green.
- Thermal/power consistency: pausing or maintaining a pump disables its power draw through
  `MechanicalBuilding.SetConsumptionDisabled` without touching the shared mechanical graph.
- Water-body accounting cannot create water: production is granted only for the volume actually
  withdrawn through the vanilla `WaterInput` chain (0.2 m³ per Water item, verified in IL).

### 2026-11-04 art pass (same 0.2.0)
- Four tiers × two factions now ship **self-made `.timbermesh` models** (8 finished + 8 construction
  stage, `data/Buildings/Water/MWP/`), replacing the placeholder vanilla-model references.
  Envelopes (block-corner export space): Mk I/II `(2,3,3)`, Mk III `(3,4,3)`, Mk IV `(3,4,4)`;
  LOD0 triangles 2,112 / 2,812 / 3,952 / 3,704 per tier (budget 12k, 30,328 total for all 16 files).
- Custom 128×128 icons replace the generated placeholders.
- Art pipeline lives in `art/` (Blender 4.2.1 headless generator → official Timbermesh plugin v1.2.0
  exporter → pure-Python protobuf validator; 16/16 files PASS) with `art/export-manifest.json`
  and 24 preview renders.
- Materials are vanilla `MaterialCollection` names only (13 checked present per faction); no custom
  PBR/AssetBundle and no vanilla assets are packaged.
- Blueprints' `TimbermeshSpec.Model` / `ConstructionStage0.Model` now point at the mod-local models
  and the colliders were re-authored as footprint boxes.

### Known limitations
- Art is placeholder: blueprints reference same-faction vanilla models read-only (see
  `docs/ART_PLACEHOLDER.md`); REQ-ART-01/02/03/05/06/07 remain open.
- In-game acceptance T01–T27 has not been executed on a live game; see `docs/VERIFICATION.md`.
- The optional direct pipe output port (REQ-COMPAT-03, SHOULD) is detection-only in v0.2; the
  valve extraction path (MUST) relies on the standard Water inventory.

## 0.1.0 — 2026-11-03

- Requirements/design baseline only (v0.1 of `機械水泵模組需求文檔.md`): four tiers at
  1/3/6/9 Water per hour, 100/200/350/500 hp, no maintenance system.
