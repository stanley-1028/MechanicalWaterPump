# BlockObjectToolGroup facts — mod-shipped collapsible build-menu group

Game **Timberborn 1.1.2.8-ffc2de0-sw** (`StreamingAssets/Version.txt`), managed dir `…\Timberborn_Data\Managed`. Dumps under `.tmp/toolgroup-probe/`; `L` = line in those dumps.

## a) Exact template we ship (already staged)

`mod/data/BlockObjectToolGroups/BlockObjectToolGroup.MWP.blueprint.json` → shipped at mod root (`mod/build.ps1:118-121` copies `data/*` verbatim: "the game discovers blueprint JSON by walking the mod directory"):

```json
{ "BlockObjectToolGroupSpec": {
    "Id": "MWP", "Order": 46, "NameLocKey": "ToolGroups.MWP",
    "Icon": "Icons/MWPBasicFolktails", "FallbackGroup": false } }
```

Per building (8 files): `"ToolGroupId": "MWP"`, `"ToolOrder": 10/20/30/40` (`MWP.Pump.Basic.*:199-200`, `Intermediate.*:203-204`, `Advanced.*:329-330`, `Top.*:409-410`).

## b) Facts with evidence

**1. Fields.** `[Timberborn.BlockObjectTools] BlockObjectToolGroupSpec` (`: ComponentSpec`): `Id` string, `Order` int, `NameLocKey` string, `Icon` `AssetRef<UnityEngine.Sprite>`, `FallbackGroup` bool (`dump-toolgroup-types.txt` L3-38). Top-level JSON key is the bare type name; vanilla files have exactly this shape and no other spec (`vanilla-toolgroups/`, from `Blueprints.zip`: Water `Order 45`, Other `Order 300`, `FallbackGroup true`).

**2. `Load()` sorts only.** `GetSpecs<BlockObjectToolGroupSpec>()` → `OrderBy(s => s.Order)` → `ToImmutableArray()` (`il-specservice.txt` L2-19). No duplicate-Order check; stable sort, so equal Orders keep spec enumeration order (built-in list, then mods by mod priority, then per-mod file order) — unpinned, keep `Order` unique.

**3. `GetFallbackSpec()`** = `AllSpecs.Single(s => s.FallbackGroup)` (`il-specservice.txt` L21-34); throws if 0 or >1 fallbacks. Vanilla ships exactly one (`Other`).

**4. `GetSpec(id)`** = `AllSpecs.Single(s => s.Id == id)` (`il-specservice.txt` L36-48); throws on missing/duplicate Id. `referrers` finds **no caller** in shipped assemblies (`refs-botgspec.txt`), so a duplicate Id is not fatal at boot — just two buttons.

**5. Regular groups iterate every spec.** `GameBlockObjectButtons.GetElements` = `Concat(regular, fallback)` (`il-gamebuttons.txt` L2-8); regular branch = `AllSpecs.Where(s => !s.FallbackGroup)`, per spec build group and **return null (no icon) if the tool list is empty** (L18-38, L152-232). `AllSpecs` comes from `ISpecService`, so vanilla **and mod** group-spec blueprints participate; no registration step, and no "group must exist before blueprints are read" — buttons are built on demand from specs, tools matched by string.

**6. Tool→group match / order.** `PlaceableBlockObjectSpecService.GetBlockObjects(spec)` = `TemplateService.GetAll<PlaceableBlockObjectSpec>().Where(s => s.ToolGroupId == spec.Id).OrderBy(s => s.ToolOrder).ThenBy(s => s.GetSpec<LabeledEntitySpec>().DisplayNameLocKey)` (`il-placeable-service.txt`; selectors IL_0048-006c; ordinal string equality). Buttons append in that order (`BlockObjectToolGroupButtonFactory.Create` loop, `il-groupbuttonfactory.txt` L2-56; `ToolButtonFactory.Create` ends with `content.Add(root)`, `il-toolbtnfactory.txt`) ⇒ visible order = ascending `ToolOrder`, ties by display-name loc key.

**7. Unknown `ToolGroupId`.** `GetBlockObjectsWithoutValidGroup()` = specs whose `ToolGroupId` is not in `AllSpecs.Select(Id).ToHashSet()`; rendered under `GetFallbackSpec()` with `Debug.LogWarning("Block object '<TemplateName>' is associated with an unknown BlockObjectToolGroupSpec with ID '<ToolGroupId>'")` (`il-placeable-service.txt`; `il-gamebuttons.txt` L48-94, L103-150). Typo ⇒ no MWP icon (empty group skipped), pumps in vanilla **Other**, warning.

**8. One collapsible icon.** `BlockObjectToolGroupButtonFactory.Create` synthesises `ToolGroupSpec { Id = "BlockObjectToolGroupSpec." + spec.Id, DisplayNameLocKey = spec.NameLocKey, Icon = spec.Icon }` (+ `ConstructionModeToolGroupSpec`), `CreateGreen`, adds a `ToolButton` per `UsableWithCurrentFeatureToggles` block object (`AssignToGroup` + `AddTool`), `RegisterGroup`, returns `BottomBarElement.CreateMultiLevel(button.Root, button.ToolButtonsElement)` (`il-groupbuttonfactory.txt` L2-91). Expand/collapse = vanilla (`OnToolGroupEntered` shows `ToolButtonsElement`, `OnToolGroupExited` hides, `il-toolgroupbutton2.txt`; initially hidden, `il-groupbuttonfactory.txt` L150-205).

**9. Icon (mod PNG works).** `InitializeElement`: `root.style.backgroundImage = new StyleBackground(toolGroup.Icon.Asset)` (`il-groupbuttonfactory.txt` L150-205). `AssetRef` loads lazily via `IAssetLoader.Load<Sprite>(path)` (`il-assetref.txt`; missing ⇒ `InvalidOperationException("Failed to load asset at …")`, `il-bundleloader.txt` L2-18). Paths are normalised to **mod-root-relative, final extension stripped, `\`→`/`, lowercased** (`AssetPathHelper`, `il-assetpathhelper.txt` L2-145). Mod sprites: `ModSystemFileProvider<Sprite>` + `ModSpriteConverter` (`.png`/`.jpg`) + `<file>.meta.json` sidecar (`fullName + ".meta.json"`, `il-modfileprovider.txt` L226-260); `"isSprite": true` → `SetSpritePreset()` (`il-spriteconverter.txt`). So `"Icons/MWPBasicFolktails"` ↔ `<mod root>/Icons/MWPBasicFolktails.png` + `.png.meta.json` — same `AssetRef<Sprite>` mechanism as our working `LabeledEntitySpec.Icon` (`MWP.Pump.Basic.Folktails.blueprint.json:218`; `GetToolImage` = `LabeledEntitySpec.Icon.Asset`, `il-blocktoolbtn.txt`). Caveats: no `.png` in the JSON, file must exist, `data/` is not in the shipped path.

**10. Name key.** `NameLocKey` → `ToolGroupSpec.DisplayNameLocKey` (`il-groupbuttonfactory.txt` L58-91) → tooltip label = `_loc.T(DisplayNameLocKey)` (`ToolGroupButton.PostLoad`, `il-toolgroupbutton.txt` L17-41). `Loc.T` is a dictionary lookup; missing key logs `"No localization for <key>"` and returns the key (`il-loc.txt` L2-27) ⇒ arbitrary keys fine. Mod CSVs load through `LocalizationLoader.GetLocalizationFiles()` = `IAssetLoader.LoadAll<TextAsset>("Localizations")` (`LocalizationsDirectory`, `il-locloader2.txt`; method `il-locloader.txt` L214-221); the mod file provider walks recursively and `ModTextAssetConverter` accepts `.csv` (`il-textasset.txt`); language = stem before `_`; codes include `enUS`/`zhCN`/`zhTW` (`LocalizationCodes::.cctor`). Rows exist: `enUS.csv:44`, `zhCN.csv:44`, `zhTW.csv:44`, header `ID,Text,Comment` (LINQtoCSV → `LocalizationRecord`).

**11. Spec-only blueprints load from the mod folder.** `ModBlueprintConverter.CanConvert` = `file.Name.EndsWith(BlueprintAsset.FullExtension)`; `FullExtension = ".blueprint"+".json"` (`il-modblueprintconverter.txt` L2-7; `BlueprintAsset::.cctor`, `il-assetpathhelper.txt` L261-275). `ModSystemFileProvider.CacheFilesFromMod` = `GetFiles("*", SearchOption.AllDirectories)` filtered by the converter (`il-modfileprovider.txt` L109-168) ⇒ **any** `*.blueprint.json` under the mod root; `BlockObjectToolGroups/` is convention only. No `TemplateSpec`/`TemplateCollection` needed (vanilla group files contain only this spec) and `SpecService.GetSpecs<T>` iterates `_cachedBlueprintsBySpecs[typeof(T)]`, calling `Blueprint.GetSpec<T>()` on each (`il-getspecs-movenext.txt`), independent of `TemplateService`.

**12. Mod blueprints join regardless of the built-in list.** `SpecService.Load` reads built-in `TextAsset "Blueprints"` (vanilla path list) then `BlueprintFileBundleLoader.GetBundles(String.Empty, paths)` (`il-specservice2.txt` L2-87; `SpecService::.cctor` → `"Blueprints"`, `il-spec-cctor.txt`); `GetBundles` calls `IAssetLoader.LoadAll<BlueprintAsset>(root, paths)` (`il-bundleloader.txt` L153-192), and `ModSystemFileProvider<BlueprintAsset>.LoadAll` ignores `resourceAssets`, yielding every cached mod blueprint whose path `StartsWith("")` (`il-modfileprovider.txt` L302-414). Constraints: lowercase `.blueprint.json` suffix (case-sensitive `EndsWith`); unique mod-relative path (bundles group by path; same path from two mods merges); `.optional.blueprint.json` reserved for overlays.

**13. Placement.** Vanilla Orders: Paths 1, Landscaping 5, Storage 20, DistrictManagement 30, Housing 40, **Water 45**, **Food 50**, Wood 60 … Other 300 (fallback). `Order 46` inserts MWP right after Water and before Food within the building-group sequence.

## c) Risks

- Second `FallbackGroup: true` spec (or losing `Other`) ⇒ `GetFallbackSpec().Single(...)` throws when an unknown-group tool exists. Ours is `false`.
- Duplicate group `Id` ⇒ two buttons with the same buildings; `GetSpec` would throw (currently unused).
- `ToolGroupId` typo ⇒ pumps in vanilla **Other** + warning, MWP icon absent (empty group skipped); matching is case-sensitive.
- Bad Icon path ⇒ `IAssetLoader.Load<Sprite>` throws during bottom-bar build (`EnableSafeMode` unused, `refs-safemode.txt`); PNG must exist with `isSprite` meta.
- Equal `Order` values do not crash, but tie order is unpinned; keep `Order` unique.
- Restart required: `SpecService`/`ModRepository` are `ILoadableSingleton`s loaded at startup; no hot reload.
- Saves unaffected: `ToolGroupId` has no save/serializer consumer (`refs-toolgroupid.txt`: only the group-matching lambdas, spec accessors, UI warning); group membership is derived from blueprints at load, so old saves keep working and pumps simply appear under the new group.
- Missing loc key ⇒ raw key as label + error log (no crash).

## d) Verify in game

1. `mod/build.ps1`; install/enable mod; restart Timberborn.
2. Load a save or new game; open the bottom build bar.
3. Expect **one new icon** (128×128 MWP sprite) after the Water group and before Food, vanilla style.
4. Click: expands to **4 buttons in order Mk I → Mk IV** (`ToolOrder 10/20/30/40`); click to collapse; hover shows the localized group name.
5. Selecting a tool places the pump normally; zhCN/zhTW tooltip = 机械水泵/機械水泵.
6. `Player.log`: no `unknown BlockObjectToolGroupSpec` warning, no `Failed to load asset at Icons/MWPBasicFolktails`.

### d.1 Actual round (2026-11-14, v0.2.4)

Steps 1–3 and 6 were executed automatically on the save copy `MWPDiag/1.timber`
(`-skipModManager -saveName 1 -settlementName MWPDiag`): `Load time 36,512 ms`,
`[MWP/Info] MWP 0.2.4 ready`, no exceptions and none of the warnings listed above; the
bottom-bar screenshot shows the one collapsed MWP group icon **between the vanilla Water (water
drop) and Food (carrot) icons**, with the vanilla Wood/Metal groups unchanged after it — i.e. the
`Order 46` placement and the vanilla one-icon collapsed presentation are confirmed in game.
Evidence: `docs/evidence/ui-in-game-2026-11-14-checks.txt`, `ui-0.2.4-bottombar.png`,
`ui-0.2.4-mwp-group-zoom.png`.

Steps 4–5 (clicking to expand, picking a tier) could not be automated on this machine: synthetic
mouse/keyboard input is unavailable (`OpenInputDesktop` access denied; probe
`form_clicks=0/form_keys=0`, same environment fact as the sibling HDBF project §16.2). They remain
manual checks; the expand/collapse code path itself is the vanilla `BlockObjectToolGroupButton`
behaviour documented in §b.8.
