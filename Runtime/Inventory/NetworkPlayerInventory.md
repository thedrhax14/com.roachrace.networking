# NetworkPlayerInventory — Designer-Friendliness Notes

Goal: make inventory behavior adjustable by game designers (via Inspector + ScriptableObjects) with minimal code edits and minimal risk to networking.

## Current pain points (as seen in `NetworkPlayerInventory`)
- Tuning lives on the component (slot count, auto-select, initial items), which encourages per-prefab divergence.
- Item rules are split between runtime code and `ItemDefinition` (e.g., consuming inventory on use is checked here; other rules are implied).
- Setup is fragile: relies on child item GameObjects + `PlayerItemRegistry` hierarchy discipline.
- Debugging iteration is hard (limited visual tooling / inspector-time previews).

## Recommended improvements (high ROI)

### 1) Move “tuning” into ScriptableObjects
Create an `InventoryConfig` ScriptableObject with:
- Slot count, slot layout metadata (hotbar vs backpack, left/right groups)
- Auto-select rules (on pickup; if selected slot empty; select first non-empty on spawn)
- Team/class overrides (Survivor/Ghost/Monster)

Benefits:
- Designers change one asset to affect many prefabs.
- Easier to version/review changes.

### 2) Replace per-prefab initial items with a `Loadout` asset
Create an `InventoryLoadout` ScriptableObject:
- Ordered list of `ItemDefinition` + amount
- Optional per-team/per-class loadouts

Have server apply loadout on spawn.

Benefits:
- Reusable, swappable loadouts.
- Less risk of inconsistent prefab arrays.

### 3) Make item behavior asset-driven
Expand or standardize `ItemDefinition` as the single source of truth:
- `stackable`, `maxStack`
- `consumesInventoryOnUse`
- `useOnSelect` / `equipOnSelect`
- `requiresAim`
- cooldown/charges
- allowed teams / game modes

Then ensure server-side logic reads these flags consistently.

### 4) Add an Inventory Ruleset hook (game mode designer control)
Add a pluggable rules asset interface, e.g. `IInventoryRuleSet` implemented by ScriptableObjects:
- `CanSelect(slotIndex, context)`
- `CanUse(itemId, slotIndex, context)`
- `OnUsed(...)` / `OnUseFailed(...)`

Benefits:
- Designers can swap rule sets per scene/game mode.
- Keeps core network replication stable.

### 5) Improve Inspector UX + validation (fail fast)
- Prefer selecting `ItemDefinition` (avoid manual ids).
- Show derived read-only info (id/icon/maxStack).
- Validate critical deps at runtime:
  - `itemRegistry` must be present
  - `itemDatabase` required when using definitions
  - clear errors when missing

Optional editor tooling:
- Loadout “preview fill” (simulate stacking/slot occupancy)
- Buttons: Grant test item / Clear inventory / Fill test loadout

### 6) Decouple “inventory state” from “world item objects”
Today: items must exist as child objects registered in `PlayerItemRegistry`.

Options:
- Keep registry, but add a safer authoring path (auto-register from definitions).
- Or allow `ItemDefinition` to specify equip/visual prefab and spawn/attach on select.

Benefits:
- Designers add new items without modifying the player prefab hierarchy.

### 7) Designer-facing events & feedback
Provide easy-to-wire events (client-side):
- `OnSelectionChanged`
- `OnItemUsed`
- `OnItemUseFailed(reason, itemId, slotIndex)`

Then UI/VFX/audio can hook in without code changes.

### 8) Debugging / iteration helpers
- In-game debug overlay for owner: slots, selection, last fail reason.
- Optional logging toggles (dev builds only).

## Suggested incremental refactor path (low risk)
1. Introduce `InventoryConfig` + `InventoryLoadout` assets (no behavior change).
2. Wire `NetworkPlayerInventory` to read config/loadout; deprecate per-prefab arrays.
3. Add ruleset hook; move remaining “magic rules” from code into assets.
4. Improve registry authoring pipeline (auto-register or prefab-driven equip).

## Non-goals (for now)
- Replacing FishNet replication model (`SyncList`/`SyncVar`).
- Changing the UI binding model unless needed.
