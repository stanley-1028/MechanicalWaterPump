# Vanilla "Water" build-menu group — integrations facts (0.2.5)

Game **Timberborn 1.1.2.8-ffc2de0-sw** (`StreamingAssets/Version.txt`), managed dir
`…\Timberborn_Data\Managed`. Dumps under `.tmp/toolgroup-probe/`; `L` = line in those dumps.

Since **0.2.5** the four MWP tiers are no longer a mod-defined bottom-bar group. They are plain
entries of the vanilla **`Water`** group: `PlaceableBlockObjectSpec.ToolGroupId = "Water"` and
`ToolOrder = 53/54/55/56`. The vanilla Water group is itself the game's collapsible water-source
group (one water-drop icon in the bottom bar that expands to its buildings), so the mod inherits
the vanilla expand/collapse behaviour and ships no group blueprint, icon or group-name key.

## a) What the mod ships

Per building (8 files): `"ToolGroupId": "Water"`, `"ToolOrder": 53/54/55/56`
(`MWP.Pump.Basic.*`, `Intermediate.*`, `Advanced.*`, `Top.*`); Core constants:
`MwpDefaults.ToolGroupId = "Water"`, `TierDefinition.ToolOrder => 53 + (int)Tier`.

## b) Why this is enough (facts with evidence)

**1. Tool→group matching / order.** `PlaceableBlockObjectSpecService.GetBlockObjects(spec)` =
`TemplateService.GetAll<PlaceableBlockObjectSpec>().Where(s => s.ToolGroupId == spec.Id).OrderBy(s => s.ToolOrder).ThenBy(s => s.GetSpec<LabeledEntitySpec>().DisplayNameLocKey)`
(`il-placeable-service.txt`; selectors IL_0048-006c; ordinal string equality); buttons append in that
order (`BlockObjectToolGroupButtonFactory.Create` loop `il-groupbuttonfactory.txt` L2-56;
`ToolButtonFactory.Create`; `il-toolbtnfactory.txt`). ⇒ Matching is a plain string comparison
against the vanilla group Id, so pointing our blueprints at `"Water"` is the whole integration.

**2. The vanilla Water group is collapsible like any group.** `BlockObjectToolGroupButtonFactory.Create`
synthesises one `ToolGroupSpec` per `BlockObjectToolGroupSpec`, builds the group button and returns
`BottomBarElement.CreateMultiLevel(button.Root, button.ToolButtonsElement)`; expand/collapse is
`OnToolGroupEntered` showing `ToolButtonsElement` / `OnToolGroupExited` hiding it, with the buttons
initially hidden (`il-groupbuttonfactory.txt` L2-91, L150-205; `il-toolgroupbutton2.txt`). Nothing
about it is mod-specific.

**3. Vanilla Water group content and order** (`Blueprints.zip` in `StreamingAssets/Modding/`):
`WaterPump` 10, `LargeWaterPump`/`DeepWaterPump` 11, `DeepBadwaterPump`/`BadwaterPump` 12,
`Discharge` 20, `MechanicalPump` 50, `DeepMechanicalPump` 51, `CompactMechanicalPump` 52,
`Centrifuge` 60, `BadwaterPressurizer`/`BadwaterDome` 80, `BadwaterRig` 90, `AquiferDrill` 100,
`DevWaterSource` 9000. ⇒ `53–56` puts the four MWP tiers **immediately after the vanilla mechanical
pumps** and before the centrifuge; ties are impossible because 53–56 are unused.

**4. Group definition source.** The game's own group list comes from vanilla
`BlockObjectToolGroupSpec` files in `Blueprints.zip` (`BlockObjectToolGroup.Water.blueprint.json`,
`Order 45`); there is no registration step for a mod to reuse a vanilla group's Id. A missing/
misspelled `ToolGroupId` falls back to the vanilla `Other` group with a warning
(`il-placeable-service.txt`, `il-gamebuttons.txt`), which the validator prevents by comparing the
blueprint value with `MwpDefaults.ToolGroupId`.

**5. No extra assets/keys.** No `data/BlockObjectToolGroups/`, no group `Icon` AssetRef and no
`ToolGroups.MWP` localization key are needed: the group button, icon, tooltip and name all belong to
vanilla Water. Removing them keeps the localization key set at 42 and the blueprint count at 10.

## c) Risks

- `ToolGroupId` typo ⇒ pumps silently fall back to the vanilla **Other** group + warning; validator
  rejects it (`GetBlockObjectsWithoutValidGroup` warning, `il-placeable-service.txt`).
- `ToolOrder` collision with a vanilla/other-mod entry does not crash; the tie is broken by display
  name loc key. 53–56 are unused in vanilla Water, and the validator pins them.
- Restart required: tool groups are built from specs at startup (no hot reload).
- Saves unaffected: `ToolGroupId` has no save/serializer consumer; group membership is derived from
  blueprints at load, so old saves keep working and existing pumps appear in the Water group.

## d) Verify in game (actual 0.2.5 round)

2026-11-15, save copy `MWPDiag/1.timber` loaded automatically with the 0.2.5 build
(`-skipModManager -saveName 1 -settlementName MWPDiag`); the vanilla water-drop group button was
clicked (synthetic mouse input works when the game runs with normal user integrity) and each MWP
tier was hovered to read its native tooltip. Results:

- `docs/evidence/ui-0.2.5-water-group-expanded.png` — vanilla Water group expanded, with the
  "机械饮水泵 Mk I" tooltip shown (100 马力); the bottom bar has no extra MWP group icon.
- `docs/evidence/ui-0.2.5-water-group-zoom.png` — bottom-bar crop of the expanded group + tooltip.
- `docs/evidence/ui-0.2.5-water-group-row-annotated.png` — the expanded Water row with the four MWP
  icons boxed and labelled Mk I–Mk IV.
- `docs/evidence/ui-0.2.5-water-tiers-tooltips.png` — the four native tooltips:
  Mk I 100 马力 / Mk II 200 马力 / Mk III 500 马力 / Mk IV 700 马力, order Mk I→IV.
- `docs/evidence/ui-in-game-2026-11-15-checks.txt` / `ui-in-game-2026-11-15-Player.log` — log is
  clean (0 Exception / 0 MWP error, no `unknown BlockObjectToolGroupSpec`, no asset/loc failures).

Ordering caveat: on a heavily modded install other water mods also use ToolOrder 53–56, so their
buildings interleave between the four tiers; the tier order is still Mk I→IV and all four are in
the Water group. On a vanilla-only install they are consecutive after the vanilla mechanical
pumps (50–52).
