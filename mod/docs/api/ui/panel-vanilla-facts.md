# Vanilla entity-panel visual recipe (for the MWP pump panel rewrite)

Game build: Timberborn **1.1.2.8-ffc2de0-sw**; no game launch (facts read from the install).
`UIREF` = `.tmp/ui-ref/Views` (game UXML/USS, authoritative); `IL` = `mod/docs/api/ui/*.txt`
(Mono.Cecil dumps of the installed assemblies); `[HDBF]` = `C:\Users\user\hdbf-rel-2.5\src\HDBF.UI`
(read-only sibling panel, same build). The mod ships no UXML/USS (`mod/docs/api/ui/README.md:25`).

## (a) Panel element -> exact vanilla class/control -> source

| Panel element | Vanilla class(es) / control | Source (file:line) |
|---|---|---|
| panel root | `cui:NineSliceVisualElement class="entity-panel bg-sub-box--frame"`; width **325px**, padding 1px, absolute right, top 110px | `UIREF/Common/EntityPanel/EntityPanel.uxml:4`; `.../EntityPanelCommonStyle.uss:1-7` |
| card frame + padding | `cui:NineSliceVisualElement` + `.entity-sub-panel` + `.bg-sub-box--green` (`bg-3`), `--blue` (`bg-7`), `--red-striped` (`bg-6-striped`) or `--frame` (`bg-4`); inner padding `8px 12px` | `EntityPanelCommonStyle.uss:9-11`; `UIREF/Common/CommonStyle.uss:93-115` |
| normal info row | `.entity-panel__row` (row, centre); `.entity-panel__text` (**13px, rgb(204,204,204)**); `.text--bold` (bold only); `Image.inventory-row__image` (**fixed 24x24**, margin 0 4px) | `EntityPanelCommonStyle.uss:311-314, 13-17`; `UIREF/Core/CoreStyle.uss:31-33`; `UIREF/Game/EntityPanel/EntityPanelGameStyle.uss:121-127` |
| canonical amount row | `.inventory-row .inventory-row--informational` > `Image.inventory-row__image` + `Label.entity-panel__text` + `.inventory-row-informational__name/amount/limit` + `.game-text-separator` | `EntityPanelGameStyle.uss:100-166`; `UIREF/Game/EntityPanel/InventoryInformationalRow.uxml:6-12` |
| secondary / heading text | `.entity-panel__sub-text` 12px grey; `.entity-panel__heading-text` 14px grey | `EntityPanelCommonStyle.uss:30-40` |
| status row | `.status-list-element` > `.status-list-element__icon` (**34x34**, margin 2px 8px 2px 0) + `Label.entity-panel__text .status-list-element__text` (max-width 270px) | `EntityPanelGameStyle.uss:241-261`; `UIREF/Game/EntityPanel/StatusListElement.uxml:6-8` |
| status card + construction | `cui:NineSliceVisualElement class="entity-sub-panel bg-sub-box--red-striped status-list-fragment"`; `StatusListFragment` loads `Game/EntityPanel/StatusListFragment` + one `Game/EntityPanel/StatusListElement` per status; `Show()` sets only `Icon.style.backgroundImage` (= `StatusInstance.IconSmall`) and `Text.text`; card auto-hides, footer order 0 | `UIREF/Game/EntityPanel/StatusListFragment.uxml:6`; `EntityPanelGameStyle.uss:237-239`; `.tmp/panel-facts/il-statuslist.txt:13-26, 98-118`; `IL/il-mechprov3.txt:1164-1170` |
| progress bar | `Timberborn.CoreUI.ProgressBar`; ctor loads `UI/Views/Core/ProgressBar` (frame/background/fill/content); class `.progress-bar` (min-height 28px); value `Label` added as a child lands in `progress-bar__content-container` | `IL/il-progressbar.txt:7-26`; `UIREF/Core/CoreStyle.uss:658-706`; `UIREF/Core/ProgressBar.uxml:3-11` |
| bar tone + label | `.progress-bar--green` / `--red` / `--teal` / `--blue` on the bar (rules target `.progress-bar__progress`); child `Label.entity-panel__text` | `UIREF/Game/GameStyle.uss:100-114`; `DeteriorableFragment.uxml:8-10`; `TimerFragment.uxml:22-24` |
| fragment button | `cui:NineSliceButton class="entity-panel__text entity-fragment__button [--narrow] [--red or --green]"`; base = min-width 50%, padding 4px, click sound. Green instances are chips/add buttons (`WorkerView.uxml:6`, `RelayFragment.uxml:16`, `ZiplineConnectionButton.uxml:6`); vanilla text actions are red (`DemolishableFragment.uxml:8`) | `EntityPanelCommonStyle.uss:138-178` |
| button row | `.entity-panel__button-wrapper` (row, centred, margin 6px 0); 2-up needs `--narrow` on both | `EntityPanelCommonStyle.uss:126-150`; `DecalSupplierFragment.uxml:12-15`; `ResettableEvaluatorFragment.uxml:7-9` |
| toggle | `cui:LocalizableToggle` / `Toggle` class `game-toggle entity-panel__text entity-panel__toggle`; 20x20 checkmark, grey 13px text | `UIREF/Common/CommonStyle.uss:381-419`; `EntityPanelCommonStyle.uss:284-287`; `FillValveFragment.uxml:18` |
| section title | `.game-text-heading` 15px grey exists but **no entity-panel fragment uses it** (other panels only, e.g. `Game/EntityDescription/DescriptionHeader.uxml:7`); in-card bold precedent = `text--bold` on `entity-panel__text` | `CommonStyle.uss:19-23`; `ProductivityFragment.uxml:10` |
| explanation box | `cui:NineSliceVisualElement class="entity-fragment__explanation bg-sub-box--blue"` (padding 12px 16px, margin 4px 0) | `EntityPanelCommonStyle.uss:180-186`; `RelayFragment.uxml:19-21`; `TimerFragment.uxml:19-21`; `MemoryFragment.uxml:11-13` |

Colour budget: entity-panel UXML uses `bg-sub-box--green` 103 times in 82 files, `--blue` 5 times
(explanation boxes + entity description `EntityPanel.uxml:28`), `--red-striped` once
(`StatusListFragment.uxml:6`), `--purple-striped` only the header (`EntityPanel.uxml:8`). Skin swap:
`--red` and `--green` set the same slice geometry and differ only in `--background-image` (normal
`bg-square-2` vs `bg-square-1`, hover `bg-square-5` vs `bg-square-4`), so removing `--red` and adding
`--green` on a template-extracted button is a complete identical green skin
(`EntityPanelCommonStyle.uss:156-178`). HDBF instead borrows the gold `button-game` skin from
`Game/EntityPanel/DebugButton.uxml:6` (`[HDBF]/Panels/HdbfIndoorPanelStyle.cs:124`).

## (b) Verified vanilla sprite paths (`Resources.Load<Sprite>(path)`)

| Resource path | Depicts | Vanilla `resource()` evidence | HDBF key |
|---|---|---|---|
| `UI/Images/Core/warning_triangle` | warning (status/alert) | `EntityPanelGameStyle.uss:462` | warning |
| `UI/Images/Game/unfiltered-fluid` | water (unfiltered fluid) | `EntityPanelGameStyle.uss:772` | tank |
| `UI/Images/Game/ico-contamination` | contamination / bad water | `PopulationStyle.uss:44` | no-water |
| `UI/Images/Game/ico-water-source-regulator-open` | open regulator (valve) | `GameMiscStyle.uss:1198` | supply |
| `UI/Images/Game/ico-water-source-regulator-closed` | closed regulator (valve/pipe state) | `GameMiscStyle.uss:1202` | — |
| `UI/Images/Game/ico-durability` | durability / maintenance | `BatchControlStyle.uss:719` | maintenance |
| `UI/Images/Game/storage-goods` | storage / materials | `GameMiscStyle.uss:1089` | material, output-full |
| `UI/Images/Game/workplace-goods` | workplace goods / port | `GameMiscStyle.uss:1083` | port |
| `UI/Images/Game/ico-clock` | clock / days | `GameMiscStyle.uss:186` | backlog, due |
| `UI/Images/Game/ico-power` | power | `EntityPanelGameStyle.uss:8` | power |
| `UI/Images/Game/output` | output | `EntityPanelGameStyle.uss:164` | output |
| `UI/Images/Game/ico-workers` | workers | `GameMiscStyle.uss:562` | workers, effective |
| `UI/Images/Game/productivity-high` | high productivity | `BatchControlStyle.uss:685` | efficiency |
| `UI/Images/Game/empty-icon` | empty slot | `GameMiscStyle.uss:1118` | empty |
| `UI/Images/Game/plants-growth` | plant growth | `GameMiscStyle.uss:772` | growing, slots |

Each path appears in a shipped USS `resource('...')` rule at the cited file:line (asset ships in the
game Resources tree); bare file names are under `UIREF/Game/` (`EntityPanel/`, `Population/`,
`BatchControl/`) unless noted. HDBF loads the identical strings via `Resources.Load<Sprite>`, caches
them and omits the `Image` when null (`[HDBF]/Panels/HdbfPanelIcons.cs:47-58`; map
`HdbfIndoorPanelStyle.cs:180-216`). No pipe-specific sprite exists among the USS-referenced
`UI/Images` icons; the regulator/valve pair is the closest. Runtime loading not exercised here.

## (c) Recommended MWP card layout (status -> measurements -> maintenance, contiguous cards)

| Card | Frame | Content | Rationale |
|---|---|---|---|
| Status | `bg-sub-box--red-striped` | `.status-list-element`: 34px icon (`warning_triangle`, or `ico-contamination`/`unfiltered-fluid`/`ico-power` by state) + `entity-panel__text status-list-element__text`; hint below as `entity-panel__sub-text`; **pause/resume** in `.entity-panel__button-wrapper`: `entity-fragment__button` + `--red` running / `--green` paused | mirrors the only vanilla red-striped card/row and keeps severity in the icon, text grey (`StatusListFragment.uxml:6`, `il-statuslist.txt:98-118`); vanilla pause is a header toggle (`AddMiddleHeaderFragment`, `IL/il-mechprov3.txt:317-326`; `EntityPanelGameStyle.uss:177-209`) unavailable to a middle fragment, so red=stop/green=go in the card body is the closest pattern |
| Measurements | `bg-sub-box--green` | one `.entity-panel__row` per value: `Image.inventory-row__image` (24px) + `entity-panel__text` label + `entity-panel__text text--bold` value; rows rate (`unfiltered-fluid`), water quality (`ico-contamination`), fallback inventory (`storage-goods`), fallback depth (`ico-water-source-regulator-open`), pipe adapter (`ico-water-source-regulator-closed`); hide with `ToggleDisplayStyle` | green is the vanilla info-card tone (103 uses); blue is only explanation boxes; 24px is the fixed row icon size |
| Maintenance | `bg-sub-box--green` | condition bar `progress-bar progress-bar--green` (swap `--red` below the critical threshold) + inner `Label.entity-panel__text`; rows `ico-clock` (next due), `storage-goods` (materials); in-progress bar `progress-bar--green`; auto toggle `game-toggle entity-panel__text entity-panel__toggle`; **maintain-now** in `.entity-panel__button-wrapper`: `entity-fragment__button entity-fragment__button--green` (`--narrow` only if 2-up) | `DeteriorableFragment.uxml:7-10` = green card + bar + grey label; `TimerFragment.uxml:22-24` = green bar; `FillValveFragment.uxml:18` toggle; green is a complete skin swap; green for the positive action is the one deliberate deviation (no vanilla text-action green) |

No card titles: no entity-panel template uses `.game-text-heading` (bold only for values/tooltips,
`ProductivityFragment.uxml:10`); fragment root stays a plain transparent `VisualElement` (see trap 7).
The extracted `DemolishableFragment` button carries `--red` from the template
(`DemolishableFragment.uxml:8`), so both MWP buttons stay red until the class is swapped (trap 4).

## (d) Traps

1. `AddToClassList("a b")` adds **one** class named `"a b"`; apply one name per call (this caused the old dark fallback text) — `[HDBF]/Panels/README.md:50-53`, `HdbfIndoorPanelStyle.cs:20-24`.
2. No inline colours/fonts: the only inline style vanilla uses here is the status icon's `backgroundImage` (`il-statuslist.txt:98-118`). `text--yellow` is gold `rgb(188,162,108)` (`CommonStyle.uss:54-56`) and `game-text--red` is pure red (`CommonStyle.uss:31-33`); vanilla status rows stay grey.
3. `.entity-fragment__button` has `min-width: 50%`; two buttons on one row need `--narrow` on both (`EntityPanelCommonStyle.uss:138-150`; `DecalSupplierFragment.uxml:13-14`).
4. A button taken from `DemolishableFragment` keeps `entity-fragment__button--red`: remove it before adding `--green` (`EntityPanelCommonStyle.uss:156-178`).
5. A bar tone must be its own class (rule is `.progress-bar--green .progress-bar__progress`, `GameStyle.uss:108-110`); `.progress-bar` min-height 28px; child labels go to `content-container` automatically (`IL/il-progressbar.txt:7-26`).
6. A card needs `NineSliceVisualElement` **plus** a `bg-sub-box--*` class: an unstyled `NineSliceVisualElement` draws nothing and a plain `VisualElement` cannot render a nine-slice (`[HDBF]/Panels/README.md:54-55`).
7. Fragment root must stay transparent: `CoreStyle.uss:1-9` resets background/border/margin and the tan `.entity-panel` frame (`bg-sub-box--frame`, `EntityPanel.uxml:4`) shows through any gap left by non-contiguous cards (`[HDBF]/Panels/README.md:54-57`).
8. Width is fixed at 325px incl. 1px padding (`EntityPanelCommonStyle.uss:1-7`); never set widths on the fragment or rows (`[HDBF]/Panels/README.md:32-33`).
9. Style inheritance (verified): `EntityPanel.uxml:2-6` declares Core/Common at document root and EntityPanelCommon/EntityPanelGame inside the `EntityPanel` root; `GameStyle`/`GameMiscStyle` come from `GameUI.uxml:2-11` because the panel is added with `UILayout.AddAbsoluteItem` (`IL/il-entitypanel.txt:41-60`); middle fragments go into the `Fragments` element (`IL/il-entitypanel.txt:362-367`) — a code-only fragment inherits all of it, but only inside this tree.
10. Vanilla status card is always `bg-sub-box--red-striped` (no class change in `Show()`) and is footer order 0; the MWP status card is middle order 1 — keep red for problems only (`il-statuslist.txt:98-118`; `IL/il-mechprov3.txt:1164-1170`).
11. A plain `Toggle` needs `game-toggle` **and** `entity-panel__text entity-panel__toggle`; the selectors depend on the built-in `unity-toggle__input > unity-toggle__checkmark/text` hierarchy (`CommonStyle.uss:381-419`).
