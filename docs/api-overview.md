# API reference

Everything a plugin reads hangs off the static `OriathHub.Core` object. The host runs the memory-reading pipeline and exposes parsed results as managed objects — you read those; you do not write to game memory.

---

## Core entry points

| Member | Type | Description |
|---|---|---|
| `Core.Process` | `GameProcess` | Process/window state and the raw memory-read facade. |
| `Core.States` | `GameStates` | The state machine — `GameCurrentState`, `InGameStateObject`, `AreaLoading`. |
| `Core.Overlay` | `OriathOverlay` | The ImGui overlay — texture loading and window area. |
| `Core.OHSettings` | `State` | The host's own settings (treat as read-only from a plugin). |
| `Core.CurrentAreaLoadedFiles` | `LoadedFiles` | All game files preloaded for the current area. |
| `Core.Prices` | `PriceService` | Shared, cache-backed item prices. Price against the global `Core.Prices.League`. |
| `Core.CoroutinesRegistrar` | `List<ActiveCoroutine>` | Host-owned diagnostics list for long-lived coroutines. Plugins may add coroutines here when they should appear in host coroutine diagnostics, but they must still keep their own handle and cancel it in `OnDisable`. |
| `Core.GetVersion()` | `string` | OriathHub version string. |

---

## Process and window state

`Core.Process` (`GameProcess`) exposes the current game process state and the raw memory-read facade.

| Member | Type | Description |
|---|---|---|
| `Pid` | `uint` | Game process ID, or `0` when the game is detached. |
| `Foreground` | `bool` | `true` when the game window is the foreground window. Use this for gameplay/input/automation gates. |
| `WindowArea` | `Rectangle` | Game client rectangle in monitor/screen coordinates. |

Use these for window-relative UI decisions, foreground-only hotkeys, and diagnostics. For visual overlays that should stay visible while the OriathHub settings window is focused, use `FocusHelper.IsGameOrOverlayForeground()` instead of `Core.Process.Foreground`. Use the raw read methods in the [Raw memory reads](#raw-memory-reads) section for memory access.

---

## Game state guard

Most data only exists while in-game. Always check before reading:

```csharp
using OriathHub.RemoteEnums;

if (Core.States.GameCurrentState != GameStateTypes.InGameState)
    return;

var inGame = Core.States.InGameStateObject;
var area   = inGame.CurrentAreaInstance;   // entities, player, terrain
var world  = inGame.CurrentWorldInstance;  // world-to-screen matrix, area metadata
var ui     = inGame.GameUi;               // in-game UI panels
```

The current area name is always available, even on the loading screen:

```csharp
string areaName = Core.States.AreaLoading.CurrentAreaName;
```

**`GameStates` members:**

| Member | Type | Description |
|---|---|---|
| `GameCurrentState` | `GameStateTypes` | Current game state, such as `InGameState`, `EscapeState`, or `GameNotLoaded`. |
| `InGameStateObject` | `InGameState` | In-game data root. Valid only while the in-game state exists. |
| `AreaLoading` | `AreaLoadingState` | Loading-screen state, including `CurrentAreaName`. |
| `AllStates` | `Dictionary<IntPtr, GameStateTypes>` | Known game-state addresses resolved by the host. Mostly useful for diagnostics. |

---

## Area instance

`inGame.CurrentAreaInstance` (`AreaInstance`) is the top-level source of in-game data.

| Member | Type | Description |
|---|---|---|
| `Player` | `Entity` | The local player entity. |
| `AwakeEntities` | `ConcurrentDictionary<EntityKey, Entity>` | All awake (interactive) entities — monsters, chests, players, NPCs, shrines. |
| `EntitiesAddedThisFrame` | `IReadOnlyList<Entity>` | Entities that appeared this frame. Empty on zone-change frames. |
| `EntitiesRemovedThisFrame` | `IReadOnlyList<Entity>` | Entities removed this frame (last-known state). Empty on zone-change frames. |
| `GetAwakeEntitiesSnapshot()` | `IReadOnlyList<Entity>` | Point-in-time copy of `AwakeEntities`. Use it to discover entities that already exist when your plugin starts observing — present at zone-in, or already awake when the plugin is enabled/reloaded mid-area — which the per-frame delta lists never report. |
| `ServerDataObject` | `ServerData` | Player progression and inventory pointers. |
| `CurrentAreaLevel` | `int` | Monster level of the current area. |
| `AreaHash` | `string` | Unique hex hash for this area instance. |
| `NetworkBubbleEntityCount` | `int` | Total entity count inside the network bubble. |
| `UselessAwakeEntities` | `int` | Count of awake entities currently classified as no longer useful. |
| `EntityCaches` | `List<DisappearingEntity>` | Last-known disappearing-entity caches used by the host. Treat as diagnostic data unless you have a specific need. |
| `TerrainMetadata` | `TerrainInfo` | Raw terrain metadata wrapper for the current area. |
| `GridHeightData` | `float[][]` | Per-grid-cell terrain height, indexed `[y][x]`. |
| `GridWalkableData` | `byte[]` | Raw walkability bitfield for the current area. |
| `TgtTilesLocations` | `Dictionary<string, List<Vector2>>` | Grid positions of named map tiles (useful for detecting league-mechanic spawn tiles). |
| `WorldToGridConvertor` | `float` | Divide a world-space coordinate by this to obtain a grid coordinate. |

```csharp
// Iterate every awake entity
foreach (var (key, entity) in area.AwakeEntities)
{
    if (!entity.IsValid) continue;
    // use entity
}

// React to entities appearing this frame
foreach (var entity in area.EntitiesAddedThisFrame)
    Log.Info($"appeared: {entity.Path}", Name);

// React to entities leaving this frame
foreach (var entity in area.EntitiesRemovedThisFrame)
    Log.Info($"removed: {entity.Path}", Name);
```

> Both delta lists are empty on the zone-change frame — a transition is a bulk reset, not per-entity churn, so use `RemoteEvents.AreaChanged` to react to zone changes. They are also pure per-frame deltas: on their own they never report entities that were already alive before your plugin started observing (present at zone-in, or already awake when the plugin is enabled or reloaded mid-area). Seed that initial set once from `GetAwakeEntitiesSnapshot()` — in `OnEnable` and on `RemoteEvents.AreaChanged` — then follow later arrivals through `EntitiesAddedThisFrame`.

**`TerrainInfo` properties:**

| Member | Type | Description |
|---|---|---|
| `TotalTiles` | `StdTuple2D<long>` | Total tile dimensions. |
| `TileDetailsPtr` | `StdVector` | Native vector pointer for tile details. |
| `GridWalkableData` | `StdVector` | Native vector for walkability data. The parsed byte array is available on `AreaInstance.GridWalkableData`. |
| `GridLandscapeData` | `StdVector` | Native vector for landscape data. |
| `BytesPerRow` | `int` | Number of bytes per terrain-data row. |
| `TileHeightMultiplier` | `short` | Height multiplier used by terrain data. |

---

## World data

`inGame.CurrentWorldInstance` (`WorldData`).

| Member | Type | Description |
|---|---|---|
| `AreaDetails` | `WorldAreaDat` | Metadata row from WorldArea.dat for the current area. |
| `WorldToScreen(worldPos)` | `Vector2` | Converts a `StdTuple3D<float>` world position to 2D screen coordinates. |
| `WorldToScreen(worldPos, height)` | `Vector2` | Same, but with an explicit height override (e.g. `render.TerrainHeight`). |
| `WorldToScreen(Vector2, height)` | `Vector2` | 2D world position overload. |

**`WorldAreaDat` properties:**

| Member | Type | Description |
|---|---|---|
| `Id` | `string` | Area identifier string (e.g. `"1_1_1"`). |
| `Name` | `string` | Display name (e.g. `"The Twilight Strand"`). |
| `Act` | `int` | Act number. |
| `IsTown` | `bool` | True if the area is a town. |
| `IsHideout` | `bool` | True if the area is a hideout. |
| `HasWaypoint` | `bool` | True if the area has a waypoint. |
| `IsBattleRoyale` | `bool` | True if the area is an Exile Royale area. |

```csharp
var details = world.AreaDetails;
if (details.IsTown || details.IsHideout)
    return; // skip overlay in safe zones

// Convert world position to screen
if (entity.TryGetComponent<Render>(out var render))
{
    var screen = world.WorldToScreen(render.WorldPosition);
    ImGui.GetBackgroundDrawList().AddCircleFilled(screen, 5f, 0xFF00FF00);
}
```

---

## Entity

### Properties

| Member | Type | Description |
|---|---|---|
| `Path` | `string` | Asset path (e.g. `"Metadata/Monsters/Mercenaries/..."`). |
| `Id` | `uint` | Unique ID within the current area instance. |
| `IsValid` | `bool` | Whether the entity is present in game memory this frame. |
| `EntityType` | `EntityTypes` | Broad classification. |
| `EntitySubtype` | `EntitySubtypes` | Narrower classification. |
| `EntityState` | `EntityStates` | Current lifecycle state. |
| `Zones` | `NearbyZones` | Whether the entity is inside the player's inner/outer radius. |
| `EntityCustomGroup` | `int` | User-defined group number for POI monsters and special objects. |
| `CanExplodeOrRemovedFromGame` | `bool` | Host cleanup hint: `true` for entities that can disappear from memory while inside the network bubble. |
| `ConsecutiveInvalidFrames` | `int` | How many frames in a row this entity has been invalid. |
| `ComponentNames` | `IReadOnlyCollection<string>` | Snapshot of every game component currently present on the entity, including components with no registered wrapper. |

### EntityTypes

| Value | Meaning |
|---|---|
| `Monster` | Hostile or neutral creature. |
| `Player` | A player character (self or party member). |
| `Chest` | Chest, strongbox, or league chest. |
| `Shrine` | A shrine entity. |
| `NPC` | Non-player character. |
| `Renderable` | Positioned object with no other classification (opt-in, disabled by default). |
| `DeliriumSpawner` | Delirium ShardPack spawner. |
| `DeliriumBomb` | Delirium volatile bomb. |
| `OtherImportantObjects` | Objects matching the `SpecialMiscObjPaths` host setting. |
| `Item` | Item entity — ground drop (`WorldItem`) or inventory item (`InventoryItem`). |
| `Unidentified` | Not yet classified. |

### EntitySubtypes (selection)

**Players:** `PlayerSelf`, `PlayerOther`  
**Chests:** `Strongbox`, `ExpeditionChest`, `BreachChest`, `ChestWithMagicRarity`, `ChestWithRareRarity`  
**Monsters:** `POIMonster`, `PinnacleBoss`  
**NPCs:** `SpecialNPC`  
**Items:** `WorldItem`, `InventoryItem`

### EntityStates

| Value | Meaning |
|---|---|
| `None` | Normal, active. |
| `Useless` | Dead, opened, or otherwise no longer relevant. |
| `MonsterFriendly` | Monster is friendly to the player. |
| `PinnacleBossHidden` | Pinnacle boss is in its hidden/invulnerable phase. |
| `PlayerLeader` | This player entity is the designated party leader. |

### Methods

```csharp
// Get a component — always use TryGetComponent; presence is not guaranteed
if (entity.TryGetComponent<Life>(out var life)) { /* ... */ }

// Pass shouldCache: false for one-off checks where you do not want to share
// the component instance with other readers.
if (entity.TryGetComponent<Life>(out var uncachedLife, shouldCache: false)) { /* ... */ }

// Grid-space distance between two entities
int dist = entity.DistanceFrom(area.Player);  // in grid units

// Check whether a monster was (or currently is) a specific subtype
// Returns true even if the entity was later re-classified to POIMonster
if (entity.IsOrWasMonsterSubType(EntitySubtypes.PinnacleBoss)) { /* ... */ }

// Discover unsupported components before writing a custom wrapper.
foreach (var componentName in entity.ComponentNames) { /* inspect name */ }
if (entity.HasComponent("Life")) { /* present, whether or not it has been materialized */ }
```

---

## Server data and inventories

`area.ServerDataObject` (`ServerData`) exposes player server-side data wrappers, including access to every loaded inventory — equipment, the main inventory, and any open stash tabs.

| Member | Type | Description |
|---|---|---|
| `FlaskInventory` | `Inventory` | Inventory wrapper for the flask slots (`InventoryName.Flask1`). |
| `AvailableInventories` | `IReadOnlyCollection<InventoryName>` | Inventory names currently present on the player's server data. Stash tabs carry dynamic ids beyond the named `InventoryName` values and appear here as unnamed `InventoryName` casts. |
| `GetInventory(name)` | `Inventory` | Returns a cached, self-updating wrapper for the given inventory. Safe to call every frame — the host keeps its address and items current (~5×/sec). When the inventory is not present the wrapper has a zero address and empty `Items`, filling in automatically once it appears. Pass any value from `AvailableInventories` (including stash-tab casts). |

```csharp
// Price/inspect every loaded inventory, including open stash tabs.
var serverData = area.ServerDataObject;
foreach (var name in serverData.AvailableInventories)
{
    var inv = serverData.GetInventory(name);
    foreach (var item in inv.Items.Values)
    {
        if (!item.IsValid) continue;
        var count = item.TryGetComponent<Stack>(out var stack) ? stack.Count : 1;
        Log.Info($"{name}: {item.Path} x{count}", Name);
    }
}
```

**`Inventory` members:**

| Member | Type | Description |
|---|---|---|
| `TotalBoxes` | `StdTuple2D<int>` | Inventory dimensions in slots (`X` columns, `Y` rows). |
| `ServerRequestCounter` | `int` | Server request counter for this inventory. Useful for detecting inventory refreshes. |
| `Items` | `ConcurrentDictionary<IntPtr, Item>` | Items keyed by inventory item pointer. |
| `Entries` | `IReadOnlyList<InventoryEntry>` | Distinct items with authoritative `X`, `Y`, `Width`, and `Height`. |
| `TryGetEntryAtSlot(x, y, out entry)` | `bool` | Resolves the occupied entry at a logical inventory slot. |
| `this[y, x]` | `Item` | Item at a zero-based row/column slot. Returns an invalid zero-address item when the slot is empty or out of range. |

`Item` derives from `Entity`. Inventory items use `EntityType.Item` and `EntitySubtype.InventoryItem`, so component access works the same way:

```csharp
var flasks = area.ServerDataObject.FlaskInventory;
for (var y = 0; y < flasks.TotalBoxes.Y; y++)
{
    for (var x = 0; x < flasks.TotalBoxes.X; x++)
    {
        var item = flasks[y, x];
        if (!item.IsValid)
            continue;

        if (item.TryGetComponent<Mods>(out var mods))
            Log.Info($"{item.Path}: {mods.Rarity}", Name);
    }
}
```

---

## Shared item prices

`Core.Prices` owns provider downloads and the per-league disk cache. The economy league is a **global
host setting** chosen in *App Settings → Basic → poe.ninja Prices*; the host keeps that league's
catalogue warm. Plugins should price against `Core.Prices.League` and do **not** need to acquire a lease
for it:

```csharp
var league = Core.Prices.League;   // the global league the user selected
if (Core.Prices.TryGetPrice(item, league, out var quote))
    ImGui.Text($"{quote.DisplayName}: {quote.ExaltedValue:0.##} ex ({quote.Source})");
```

Use `GetStatus(league)`, `GetDivineToExaltedRate(league)`, and `RequestRefresh(league)` for status,
conversion, and manual refresh. The initial provider is poe.ninja; callers depend on the host contract,
not provider JSON.

The provider, refresh interval, auto-refresh toggle, and on-disk cache location are also global host
settings under *App Settings → Basic → poe.ninja Prices* (with a *Clear cache* action). Plugins don't
manage these — they only read prices for `Core.Prices.League`.

### Pricing without an `Item`

When you don't have a live `Item` entity — items you describe yourself, custom windows, non-inventory
mechanics — price from a `PriceQuery` instead. Supply the base-item `Path`, `Rarity`, the unique's art
path (`RenderItem.ResourcePath`, empty if none), and optional stack sizes:

```csharp
var query = new PriceQuery(
    Path: "Metadata/Items/Currency/CurrencyRerollRare",
    Rarity: Rarity.Normal,
    ArtPath: "",
    StackCount: 7);

if (Core.Prices.TryGetPrice(in query, Core.Prices.League, out var quote))
    ImGui.Text($"{quote.DisplayName}: {quote.ExaltedValue:0.##} ex");
```

`TryGetPrice(Item, …)` is just a convenience wrapper that builds a `PriceQuery` from the item's
components.

### Pricing a different league (advanced)

To price against a league other than the global one, acquire a lease for it (so its catalogue loads and
stays cached) and release it in `OnDisable`:

```csharp
private IDisposable? priceLease;

public override void OnEnable(bool isGameOpened) => priceLease = Core.Prices.Acquire("Hardcore");
public override void OnDisable() { priceLease?.Dispose(); priceLease = null; }
```

**`ItemPrice` result:**

| Member | Type | Description |
|---|---|---|
| `DisplayName` | `string` | Provider-resolved display name, including a matched unique name when available. |
| `ExaltedValue` | `double` | Total stack value normalized to Exalted Orbs. |
| `Source` | `string` | Provider that produced the quote. |
| `UpdatedUtc` | `DateTime` | Timestamp of the immutable catalogue snapshot used for the quote. |

**`PriceServiceStatus` result:**

| Member | Type | Description |
|---|---|---|
| `IsLoading` | `bool` | A cache/provider refresh is currently running. |
| `HasData` | `bool` | At least one usable catalogue snapshot is available. |
| `UpdatedUtc` | `DateTime` | Timestamp of the available snapshot, or `default` before first load. |
| `Error` | `string` | Last refresh error; empty when the latest refresh succeeded. Cached data can remain usable after an error. |

See the complete [`SampleStashPricing`](../samples/SampleStashPricing) package consumer for lease cleanup,
data-phase pricing, status display, and drawing from visible cell geometry.

---

## Components

Retrieve any component with `TryGetComponent<T>`. Components live in `OriathHub.RemoteObjects.Components`. The call is cheap — the entity caches components after the first access.

```csharp
using OriathHub.RemoteObjects.Components;

if (entity.TryGetComponent<Life>(out var life))
{
    // life is valid here
}
```

All component classes inherit from `ComponentBase`, which exposes the remote `Address` inherited from `RemoteObjectBase`.

| Member | Type | Description |
|---|---|---|
| `Address` | `IntPtr` | Remote address of the component wrapper's backing memory. |
| `IsParentValid(parentEntityAddress)` | `bool` | Returns `true` if the component still belongs to the supplied entity address. Mostly useful for defensive raw-memory reads; `TryGetComponent<T>` handles normal plugin use. |

```csharp
if (entity.TryGetComponent<Life>(out var life) && !life.IsParentValid(entity.Address))
    return; // stale component from a torn frame; skip this entity
```

### Plugin-provided components and the registry

A component wrapper is just a `ComponentBase` subclass whose **type name matches the game's component name**. You can ship your own for a component the host does not model — give it a `public (IntPtr)` constructor and override `UpdateData`:

```csharp
using OriathHub.RemoteObjects.Components;

// Rename "ExampleComponent" to the real component name as the game reports it (visible in the
// entity inspector), and read your own offsets struct for its layout.
public sealed class ExampleComponent : ComponentBase
{
    public ExampleComponent(IntPtr address) : base(address) { }
    public int Value { get; private set; }

    protected override void UpdateData(bool hasAddressChanged)
    {
        if (Core.Process.ReadMemory<ExampleLayout>(this.Address, out var data))
            this.Value = data.Value;
    }
}

// Read it from any entity — the generic overload works across plugins with no host change:
if (entity.TryGetComponent<ExampleComponent>(out var c)) { /* c.Value */ }
```

See [examples — Provide a custom component](examples.md) for the full offsets-struct version.

When a plugin loads, the host reflects over its assembly and registers every such type in **`ComponentRegistry`**, a `name -> Type` map seeded with the host components. This lets a component be resolved by name when the concrete type is not known at compile time — for the entity inspector, or for one plugin to reach a component another plugin defined.

| Member | Description |
|---|---|
| `ComponentRegistry.TryResolve(name, out Type)` | Resolve a component name to its registered wrapper type. |
| `ComponentRegistry.TryCreate(name, address, out ComponentBase)` | Raw factory: resolve and construct a wrapper at an address (does not consult an entity's component list). |
| `ComponentRegistry.RegisteredNames` | Names that currently have a managed wrapper registered by the host or a plugin. This is not an entity's component list. |
| `entity.TryGetComponent(string name, out ComponentBase, shouldCache = true)` | Entity-scoped, name-based component access (the string counterpart of `TryGetComponent<T>`). |
| `entity.ComponentNames` | Names actually present on this entity, including unsupported components. Use this for discovery. |

```csharp
// Resolve and inspect a component by name (e.g. supplied at runtime):
if (entity.TryGetComponent("ExampleComponent", out var comp))
{
    // comp is ComponentBase; cast to the concrete type if you reference it, or read raw memory
}
```

Names already registered by the host (or an earlier plugin) win — a plugin cannot shadow a host component; the collision is logged and ignored. Registrations are dropped when a plugin is reloaded from disk, so the wrapper type does not pin the old assembly in memory.

---

### `Life`

Vitals for any living entity — monsters, players, some NPCs.

| Member | Type | Description |
|---|---|---|
| `IsAlive` | `bool` | `true` if current health > 0. |
| `Health` | `VitalInfo` | Health resource. |
| `Mana` | `VitalInfo` | Mana resource. |
| `EnergyShield` | `VitalInfo` | Energy shield resource. |
| `Ward` | `VitalInfo` | Ward resource. |

**`VitalInfo` properties:**

| Member | Type | Description |
|---|---|---|
| `Current` | `int` | Current amount. |
| `Total` | `int` | Maximum amount. |
| `PtrToLifeComponent` | `IntPtr` | Pointer to the underlying Life component vital block. Mostly useful for raw-memory diagnostics. |
| `Unreserved` | `int` | Maximum minus reserved. |
| `ReservedFlat` | `int` | Flat-reserved amount (e.g. from aura skills). |
| `ReservedPercent` | `int` | Percent reservation × 100 (2023 → 20.23 %). |
| `ReservedTotal` | `int` | Calculated total reservation. |
| `Regeneration` | `float` | Regen rate (positive while regenerating). |
| `CurrentInPercent()` | `int` | `Current` as a percentage of `Unreserved`. |
| `ReservedInPercent()` | `int` | `ReservedTotal` as a percentage of `Total`. |

```csharp
if (entity.TryGetComponent<Life>(out var life) && life.IsAlive)
{
    int hpPct = life.Health.CurrentInPercent();   // 0–100
    int maPct = life.Mana.CurrentInPercent();
    int esPct = life.EnergyShield.CurrentInPercent();

    bool lowLife = hpPct < 35;
}
```

---

### `Actor`

Animation state and active skill data for monsters and players.

| Member | Type | Description |
|---|---|---|
| `AnimationId` | `int` | Current `Animation.dat` row id. This is the stable value to compare in plugins. |
| `AnimationName` | `string` | Current animation name resolved from the game's loaded `Animation.dat`, with enum fallback when unavailable. |
| `Animation` | `Animation` (enum) | `[Obsolete]` (emits a build warning) compatibility fallback for older plugins. Prefer `AnimationId` or `AnimationName`; the enum can become stale when `Animation.dat` rows shift. |
| `ActiveSkills` | `Dictionary<string, ActiveSkillInfo>` | All known active skills, keyed by skill name. |
| `IsSkillUsable` | `HashSet<string>` | Names of skills currently off cooldown and usable. For most use-cases this is all you need; for per-skill cooldown details see `ActiveSkillCooldowns`. |
| `ActiveSkillCooldowns` | `Dictionary<uint, ActiveSkillCooldownInfo>` | Per-skill cooldown records, keyed by the packed skill/equipment id (`ActiveSkillCooldownInfo.SkillKey`). Matches the `UnknownIdAndEquipmentInfo` key in `ActiveSkillInfo`. |
| `DeployedEntities` | `int[256]` | Per-type count of deployed objects (totems, mines, minions …). Index is the deployed-object type ID. |

**`ActiveSkillInfo` properties:**

| Member | Type | Description |
|---|---|---|
| `UseStage` | `int` | Current use/cast stage reported by the skill data. |
| `CastType` | `int` | Raw cast-type value. |
| `ActiveSkillsDatId` | `uint` | Row ID in ActiveSkills.dat. |
| `TotalCooldownTimeInMs` | `int` | Cooldown duration in milliseconds. |
| `TotalUses` | `int` | Total number of times the skill has been used this session. |
| `UnknownIdAndEquipmentInfo` | `uint` | Packed gem socket / equipment info. Use as the key into `Actor.ActiveSkillCooldowns`. |
| `GrantedEffectsDatRow` | `IntPtr` | Pointer to the GrantedEffects.dat row. |
| `GrantedEffectsPerLevelDatRow` | `IntPtr` | Pointer to the GrantedEffectsPerLevel.dat row. |
| `ActiveSkillsDatPtr` | `IntPtr` | Pointer to the ActiveSkills.dat row/data block. |
| `GrantedEffectStatSetsPerLevelDatRow` | `IntPtr` | Pointer to the granted-effect stat set row. |

**`ActiveSkillCooldownInfo` properties:**

| Member | Type | Description |
|---|---|---|
| `SkillKey` | `uint` | Packed skill/equipment id — matches `ActiveSkillInfo.UnknownIdAndEquipmentInfo`. |
| `ActiveSkillsDatId` | `int` | Row ID in ActiveSkills.dat for this cooldown record. |
| `MaxUses` | `int` | Maximum simultaneous uses before the skill enters cooldown. |
| `TotalCooldownTimeInMs` | `int` | Full cooldown duration in milliseconds. |
| `TotalActiveCooldowns` | `int` | Number of currently active (unexpired) cooldown instances. |
| `CannotBeUsed` | `bool` | `true` when all uses are on cooldown (`TotalActiveCooldowns >= MaxUses`). Equivalent to the logic behind `IsSkillUsable`. |

```csharp
if (entity.TryGetComponent<Actor>(out var actor))
{
    bool isRunning = actor.AnimationName == "Run";
    int animationId = actor.AnimationId;

    if (actor.ActiveSkills.TryGetValue("Ice Strike", out var skill))
        Log.Info($"cooldown: {skill.TotalCooldownTimeInMs} ms", Name);

    bool canUse = actor.IsSkillUsable.Contains("Ice Strike");

    // Per-skill cooldown detail
    if (actor.ActiveSkills.TryGetValue("Ice Strike", out var iceStrike) &&
        actor.ActiveSkillCooldowns.TryGetValue(iceStrike.UnknownIdAndEquipmentInfo, out var cd))
    {
        Log.Info($"active cooldowns: {cd.TotalActiveCooldowns}/{cd.MaxUses}", Name);
    }
}
```

---

### `Buffs`

Active buffs and debuffs on an entity.

| Member | Type | Description |
|---|---|---|
| `StatusEffects` | `ConcurrentDictionary<string, StatusEffectInfo>` | All active effects, keyed by buff name. |
| `FlaskActive` | `bool[5]` | Whether each flask slot (0–4) is currently active. Only meaningful on the local player entity. |

**`StatusEffectInfo` properties:**

| Member | Type | Description |
|---|---|---|
| `BuffDefinationPtr` | `IntPtr` | Pointer to the buff definition row. The spelling matches the public API. |
| `TotalTime` | `float` | Full duration in seconds (`float.MaxValue` for permanent effects). |
| `TimeLeft` | `float` | Remaining time in seconds. |
| `Charges` | `short` | Stack count. |
| `SourceEntityId` | `uint` | ID of the entity that applied the effect. |
| `FlaskSlot` | `short` | Source flask slot (0–4); -1 if not from a flask. |
| `Effectiveness` | `short` | Raw effectiveness modifier (display value = 100 + this). |
| `UnknownIdAndEquipmentInfo` | `uint` | Packed source/equipment information when present. |

```csharp
if (entity.TryGetComponent<Buffs>(out var buffs))
{
    // Check for a specific debuff
    if (buffs.StatusEffects.TryGetValue("ignited", out var burn))
        Log.Info($"burning, {burn.TimeLeft:0.0}s left", Name);

    // Check the player's flask slots (local player only)
    bool flask1Active = buffs.FlaskActive[0];
    bool flask2Active = buffs.FlaskActive[1];
}
```

---

### `Render`

World-space and grid-space position.

| Member | Type | Description |
|---|---|---|
| `WorldPosition` | `StdTuple3D<float>` | 3D world position (X, Y, Z). Z points toward the entity's healthbar. Pass directly to `WorldToScreen`. |
| `GridPosition` | `StdTuple3D<float>` | 2D grid (map) position derived from world position. Z is always 0. |
| `TerrainHeight` | `float` | Height of the terrain tile the entity stands on. |
| `ModelBounds` | `StdTuple3D<float>` | Model bounding-box extents. |

```csharp
if (entity.TryGetComponent<Render>(out var render))
{
    // Draw text at the entity's screen position
    var screen = world.WorldToScreen(render.WorldPosition);
    ImGui.GetBackgroundDrawList().AddText(screen, 0xFFFFFFFF, entity.Path);

    // Grid position — useful for minimap overlays
    float gx = render.GridPosition.X;
    float gy = render.GridPosition.Y;
}
```

---

### `Positioned`

Faction and alignment.

| Member | Type | Description |
|---|---|---|
| `IsFriendly` | `bool` | `true` if the entity is on the player's side. |
| `Flags` | `byte` | Raw reaction flags byte. |

```csharp
if (entity.TryGetComponent<Positioned>(out var pos) && !pos.IsFriendly)
{
    // hostile
}
```

---

### `ObjectMagicProperties`

Rarity and mod data for monsters and chests.

| Member | Type | Description |
|---|---|---|
| `Rarity` | `Rarity` (enum) | `Normal`, `Magic`, `Rare`, `Unique`. |
| `Mods` | `List<(string name, (float value0, float value1) values)>` | All mods with numeric ranges. Value is `float.NaN` if the mod has no numeric value. |
| `ModNames` | `HashSet<string>` | All mod names for fast lookup. |
| `ModStats` | `Dictionary<GameStats, int>` | Stats granted by the entity's mods. |

```csharp
if (entity.TryGetComponent<ObjectMagicProperties>(out var omp))
{
    bool isRare    = omp.Rarity == Rarity.Rare;
    bool isBeyond  = omp.ModNames.Contains("MonsterConvertsOnDeath");
    bool isEnraged = omp.ModNames.Contains("MonsterEnraged");
}
```

---

### `Mods`

Like `ObjectMagicProperties` but for items (ground drops, inventory items). Mods are only read once when the address first changes, so this is safe to call on every frame.

| Member | Type | Description |
|---|---|---|
| `Rarity` | `Rarity` | `Normal`, `Magic`, `Rare`, `Unique`. |
| `ImplicitMods` | `List<(string, (float, float))>` | Implicit mods. |
| `ExplicitMods` | `List<(string, (float, float))>` | Explicit (rolled) mods. |
| `EnchantMods` | `List<(string, (float, float))>` | Enchantment mods. |
| `HellscapeMods` | `List<(string, (float, float))>` | Crucible / hellscape mods. |
| `ModStats` | `Dictionary<GameStats, int>` | Stats granted by the item's mods (same shape as `ObjectMagicProperties.ModStats`). Only populated when the component address first changes (i.e. when the item is first seen). |

```csharp
if (entity.TryGetComponent<Mods>(out var mods))
{
    foreach (var (name, (v0, v1)) in mods.ExplicitMods)
        Log.Info($"{name}: {v0}–{v1}", Name);
}
```

---

### `Stats`

Numeric stat values, split by source.

| Member | Type | Description |
|---|---|---|
| `StatsChangedByItems` | `Dictionary<GameStats, int>` | Stats from equipped items. |
| `StatsChangedByBuffAndActions` | `Dictionary<GameStats, int>` | Stats from buffs and skills. |
| `CurrentWeaponIndex` | `int` | Active weapon set (0 = weapon I, 1 = weapon II). |
| `IsInShapeshiftedForm` | `bool` | `true` if the entity is currently in a shapeshifted form. |
| `ShapeshiftFormsDatRow` | `IntPtr` | Pointer to the active `ShapeshiftForms.dat` row, or `IntPtr.Zero` when not shapeshifted. Use this to identify which form is active via a raw read. |

`GameStats` is a large enum in `OriathHub.RemoteEnums`. Browse it in your IDE for the full list of stat IDs.

```csharp
if (entity.TryGetComponent<Stats>(out var stats))
{
    // Check whether the entity is dead (stat-driven classification)
    if (stats.StatsChangedByBuffAndActions.TryGetValue(GameStats.is_dead, out var dead) && dead > 0)
        return;

    // Check active weapon set
    bool dualWield = stats.CurrentWeaponIndex == 1;
}
```

---

### `Player`

Player-specific data. Present only on player entities.

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | Character name. Updated only when the address first changes. |
| `Level` | `int` | Character level. |
| `Xp` | `uint` | Current experience points. |

```csharp
if (area.Player.TryGetComponent<Player>(out var playerComp))
{
    ImGui.Text($"{playerComp.Name}  Lv.{playerComp.Level}");
}
```

---

### `Chest`

State of a chest entity.

| Member | Type | Description |
|---|---|---|
| `IsOpened` | `bool` | `true` if the chest has been opened. |
| `IsStrongbox` | `bool` | `true` if this is a strongbox. |
| `StrongboxDatRow` | `IntPtr` | Pointer to the `StrongboxTypes.dat` row for this chest, or `IntPtr.Zero` when it is not a strongbox. Use this for a raw read of strongbox-type metadata. |
| `IsLabelVisible` | `bool` | `true` if the chest label is visible (breach/legion/normal chests). |

```csharp
if (entity.TryGetComponent<Chest>(out var chest) && !chest.IsOpened)
{
    // highlight unopened chest
}
```

---

### `Shrine`

| Member | Type | Description |
|---|---|---|
| `IsUsed` | `bool` | `true` if the shrine has been activated. |

```csharp
if (entity.TryGetComponent<Shrine>(out var shrine) && !shrine.IsUsed)
{
    // draw shrine indicator
}
```

---

### `MinimapIcon`

| Member | Type | Description |
|---|---|---|
| `IconName` | `string?` | Icon key from MinimapIcons.dat (e.g. `"RewardChestExpedition"`). `null` if not set. |

```csharp
if (entity.TryGetComponent<MinimapIcon>(out var icon) && icon.IconName != null)
    Log.Info($"minimap icon: {icon.IconName}", Name);
```

---

### `Targetable`

| Member | Type | Description |
|---|---|---|
| `IsTargetable` | `bool` | `true` if the entity can currently be targeted by the player. Combines all flags below into one predicate. |
| `IsHighlightable` | `bool` | Raw highlight flag. Note: non-highlightable entities can still be targetable. |
| `IsTargettedByPlayer` | `bool` | `true` while the player's cursor is actively targeting this entity. |
| `HiddenfromPlayer` | `bool` | `true` when the entity is hidden from the player (contributes `false` to the combined `IsTargetable`). |
| `NeedsTrue` | `bool` | Internal flag that must be `true` for the entity to be targetable. |
| `MeetsQuestState` | `bool` | `true` when the entity's quest-state requirement is satisfied. |
| `NeedsFalse` | `bool` | Internal flag that must be `false` for the entity to be targetable. |

`IsTargetable` is equivalent to `IsTargetable && !HiddenfromPlayer && NeedsTrue && MeetsQuestState && !NeedsFalse`. The individual flags are useful for debugging why a specific entity is not selectable.

```csharp
if (entity.TryGetComponent<Targetable>(out var targetable) && targetable.IsTargetable)
{
    // entity is interactable
}

// Debug why an entity is not targetable
if (entity.TryGetComponent<Targetable>(out var t) && !t.IsTargetable)
{
    Log.Info($"hidden={t.HiddenfromPlayer} needsTrue={t.NeedsTrue} " +
             $"questOk={t.MeetsQuestState} needsFalse={t.NeedsFalse}", Name);
}
```

---

### `Transitionable`

For doors, levers, and similar interactive objects.

| Member | Type | Description |
|---|---|---|
| `CurrentState` | `int` | Transition state index (0 = closed/default, 1 = open; varies by object type). |

```csharp
if (entity.TryGetComponent<Transitionable>(out var door))
    Log.Info($"door state: {door.CurrentState}", Name);
```

---

### `TriggerableBlockage`

| Member | Type | Description |
|---|---|---|
| `IsBlocked` | `bool` | `true` if the blockage is currently closed/active. |

```csharp
if (entity.TryGetComponent<TriggerableBlockage>(out var blockage) && blockage.IsBlocked)
    Log.Info($"blocked: {entity.Path}", Name);
```

---

### `Charges`

Flask or skill charges.

| Member | Type | Description |
|---|---|---|
| `Current` | `int` | Current number of charges. |
| `PerUseCharge` | `int` | Charges consumed per use. |

```csharp
if (entity.TryGetComponent<Charges>(out var charges))
{
    int uses = charges.Current / charges.PerUseCharge;
    ImGui.Text($"uses available: {uses}");
}
```

---

### `Stack`

Item stack size.

| Member | Type | Description |
|---|---|---|
| `Count` | `int` | Current stack size. |
| `MaxCount` | `int` | Maximum stack size for this item's base type; `0` when unavailable. |

```csharp
if (item.TryGetComponent<Stack>(out var stack))
    ImGui.Text($"stack: {stack.Count}/{stack.MaxCount}");
```

---

### `Quality`

Item quality. Present on items that can have quality.

| Member | Type | Description |
|---|---|---|
| `ItemQuality` | `int` | Quality in percent (e.g. `20` for 20%). |

```csharp
if (item.TryGetComponent<Quality>(out var quality))
    ImGui.Text($"quality: {quality.ItemQuality}%");
```

---

### `SkillGem`

Present on skill/support gem items.

| Member | Type | Description |
|---|---|---|
| `Level` | `int` | Gem level. |

```csharp
if (item.TryGetComponent<SkillGem>(out var gem))
    ImGui.Text($"gem level: {gem.Level}");
```

---

### `RenderItem`

Inventory-icon art path. Uniquely identifies an item's base/unique visual.

| Member | Type | Description |
|---|---|---|
| `ResourcePath` | `string` | Art path of the item's inventory icon, e.g. `Art/2DItems/.../AmorMandragora.dds`. Empty when unavailable. |

```csharp
if (item.TryGetComponent<RenderItem>(out var render))
    Log.Info($"art: {render.ResourcePath}", Name);
```

---

### `Animated`

Animated entity (projectile, summoned object). Only updated when the address first changes.

| Member | Type | Description |
|---|---|---|
| `Path` | `string` | Asset path of the animated entity. |
| `Id` | `uint` | ID of the animated entity. |

```csharp
if (entity.TryGetComponent<Animated>(out var animated))
    Log.Info($"animated {animated.Id}: {animated.Path}", Name);
```

---

### `StateMachine`

Multi-state objects such as bosses and interactive objects.

| Member | Type | Description |
|---|---|---|
| `States` | `IReadOnlyList<StateMachineState>` | All states in the machine. |

**`StateMachineState` properties:**

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | State name. |
| `Value` | `long` | Current state value (0 often means inactive). |

```csharp
if (entity.TryGetComponent<StateMachine>(out var sm))
{
    foreach (var state in sm.States)
    {
        if (state.Value != 0)
            Log.Info($"{state.Name} = {state.Value}", Name);
    }
}
```

---

### `NPC` / `DiesAfterTime`

Marker components with no public properties. Their presence drives entity classification:

- `NPC` — the entity is an NPC.
- `DiesAfterTime` — the entity is a temporary hostile (timed death).

```csharp
if (entity.TryGetComponent<NPC>(out _))
    Log.Info($"npc: {entity.Path}", Name);

if (entity.TryGetComponent<DiesAfterTime>(out _))
    Log.Info($"temporary entity: {entity.Path}", Name);
```

---

## UI panels

`inGame.GameUi` (`ImportantUiElements`) exposes the in-game UI elements tracked by the host.

| Member | Type | Description |
|---|---|---|
| `IsAnyLargePanelOpen` | `bool` | `true` if any blocking panel is open: left/right side panel, skill tree, or world-travel map. Use this to hide world-space overlays while the player is in a menu. |
| `IsSkillTreeOpen` | `bool` | `true` if a passive or atlas skill-tree graph view is visible. |
| `IsPassiveSkillTreeOpen` | `bool` | `true` if the passive skill tree is visible. |
| `IsAtlasSkillTreeOpen` | `bool` | `true` if the atlas skill tree is visible. |
| `IsAtlasMapOpen` | `bool` | `true` if the endgame atlas map screen is visible. |
| `LeftPanel` | `UiElementBase` | The currently open left panel. Visible only while open. |
| `RightPanel` | `UiElementBase` | The currently open right panel. Visible only while open. |
| `WorldMapPanel` | `UiElementBase` | The world-travel map screen. Visible only while open. |
| `LargeMap` | `LargeMapUiElement` | The in-area large map. |
| `MiniMap` | `MapUiElement` | The minimap. |
| `ChatParent` | `ChatParentUiElement` | The chat UI element. |
| `AtlasMapsNodesUiElements` | `List<AtlasMapsNodeUiElement>` | Atlas map node controls currently present on the endgame atlas map screen. **Only populated while a lease from `ImportantUiElements.RequestAtlasMapNodes()` is held** — see below. Each node exposes UI-element basics plus map name/id, description, biome id, raw status flags, derived status state, completion, and current runnable state. |
| `AtlasMapConnections` | `IReadOnlyList<AtlasMapNodeConnection>` | Connections (edges) between revealed atlas map nodes on the endgame atlas map. Each entry exposes `From` and `To` (`AtlasMapsNodeUiElement`); draw a routing line between `From.Position` and `To.Position`. Edges are deduplicated (one per undirected pair) and only include endpoints that have an on-screen control — connections involving fogged/unrevealed nodes are omitted. **Only populated while a lease from `ImportantUiElements.RequestAtlasMapNodes()` is held.** Enumerate during `DrawUI`, do not mutate or cache across frames. |

**`UiElementBase` common members:**

| Member | Type | Description |
|---|---|---|
| `IsVisible` | `bool` | Whether the element is currently shown. |
| `IsValidElement` | `bool` | `false` after a `Refresh` (or address change) where the self-pointer sanity check failed — meaning the address no longer points to a UiElement (e.g. a vendor inventory panel sharing address space with the stash tree, or a torn-frame transient). Check this after calling `Refresh` if you intend to iterate children or read the element's data. The child indexer already returns `null` for cached elements where this is `false`, so most traversal code is naturally protected without an explicit check. |
| `Position` | `Vector2` | Screen position (top-left corner). |
| `Size` | `Vector2` | Element size in pixels. |
| `Scale` | `float` | Cached UI scale-like value exposed for compatibility. Position/size calculations use `Position` and `Size`. |
| `Flags` | `uint` | Raw UI element flags read from the game. Useful for traversal predicates and diagnostics. |
| `TotalChildrens` | `int` | Number of child UI elements. The spelling matches the public API. |
| `TryGetParent(out parent)` | `bool` | Returns the cached parent element if one is available. During transitions this can return `false`. |
| `this[index]` | `UiElementBase?` | Lazily materializes and caches a child element by index, or returns `null` when the index is out of range or when a previously-cached child has `IsValidElement == false`. Always returns the base type. |
| `GetChildAddress(index)` | `IntPtr` | Address of the child at `index` without materializing it, or `IntPtr.Zero` if the index is invalid. Uses the already-cached child addresses, so no extra memory read. |
| `Refresh(reloadChildren = false)` | `void` | Re-reads the element. Pass `true` when a dynamic UI reuses the same child-vector allocation but replaces its pointers in place, such as folder stash subtabs. After a failed Refresh `IsValidElement` will be `false` and `TotalChildrens` will be `0`. |

To wrap a UI element the host does **not** already expose, create a small derived type and pass the raw UI-element address to the protected base constructor:

```csharp
public sealed class CustomPanelElement : UiElementBase
{
    public CustomPanelElement(IntPtr address) : base(address) { }
}

var panel = new CustomPanelElement(address);
if (panel.IsVisible)
    ImGui.GetForegroundDrawList().AddRect(panel.Position, panel.Position + panel.Size, 0xFF00FF00);
```

The protected constructor parses the element immediately and resolves its parent chain via a shared internal cache, so `Position`/`Size` are correct. Reassign `.Address` to refresh it on a later frame.

Use `UiElementTraversal` when you need to find descendants by a structural signature rather than fixed child indexes:

```csharp
var visibleLargeChild = UiElementTraversal.FindFirst(
    panel,
    element => element.IsVisible && element.Size.X > 500f,
    maxDepth: 4);
```

Traversal uses the currently materialized child collection. For stable trees, the host refresh is enough.
For a plugin-owned dynamic tree, call `element.Refresh(reloadChildren: true)` before traversing when the UI
can replace child pointers without reallocating the vector. Do not force-reload large trees unless needed.

When iterating with explicit `Refresh` calls, guard against addresses that are no longer valid UiElements
(e.g. vendor panels that momentarily share an address with stash UI):

```csharp
element.Refresh(reloadChildren: true);
if (!element.IsValidElement)
    continue; // skip element and its subtree; children are already cleared
```

The `this[index]` indexer automatically returns `null` for any cached child whose `IsValidElement` is
`false`, so `UiElementTraversal.BreadthFirst` / `FindFirst` / `FindAll` are safe without an extra check.

When you need child elements as your own derived type, use `GetChildAddress` instead of `this[index]`:

```csharp
for (var i = 0; i < panel.TotalChildrens; i++)
{
    var child = new CustomPanelElement(panel.GetChildAddress(i));
    // ... use child.Position, child.Size, etc.
}
```

**`MapUiElement` members (`LargeMap` and `MiniMap`):**

| Member | Type | Description |
|---|---|---|
| `Shift` | `Vector2` | Current map pan offset. |
| `DefaultShift` | `Vector2` | Map pan offset at rest. |
| `Zoom` | `float` | Current map zoom; normal values are usually around `0.5f` to `1.5f`. |

**Specialized UI element members:**

| Member | Type | Description |
|---|---|---|
| `LargeMap.Center` | `Vector2` | Center point of the large map before shift/default-shift adjustments. |
| `ChatParent.IsChatActive` | `bool` | `true` when the chat parent background alpha indicates the chat input is active. |
| `AtlasMapsNodeUiElement.MapAreaId` | `string` | `WorldAreas.dat` id for the map node, such as `MapVaalFactory`. |
| `AtlasMapsNodeUiElement.MapName` | `string` | Display map name, such as `The Assembly`. |
| `AtlasMapsNodeUiElement.Description` | `string` | Atlas node flavour/description text. |
| `AtlasMapsNodeUiElement.AtlasNodeId` | `int` | Raw atlas node id/coordinate value from the UI node. |
| `AtlasMapsNodeUiElement.EndgameMapBiomeId` | `byte` | `EndgameMapBiomes.dat` row id. |
| `AtlasMapsNodeUiElement.StatusFlags` | `byte` | Raw packed node status flags. The low two bits encode `StatusState`; keep the full byte available when validating higher-bit semantics. |
| `AtlasMapsNodeUiElement.StatusState` | `byte` | Derived low two-bit node state. Known live states: `0` unavailable, `1` runnable, `3` completed. State `2` exists in memory but its gameplay meaning is not confirmed yet. |
| `AtlasMapsNodeUiElement.NodeIndex` | `int` | Index inside the current atlas map node collection. |
| `AtlasMapsNodeUiElement.IsCompleted` | `bool` | `true` when `StatusState == 3`. |
| `AtlasMapsNodeUiElement.CanRun` | `bool` | `true` when `StatusState == 1`. |
| `AtlasMapsNodeUiElement.Content` | `IReadOnlyList<string>` | Canonical display names of the content rolled on the node (e.g. `Powerful Map Boss`, `Breach`, `Expedition`), read from the node's nested content elements. These match the names in `EndgameMapContent` verbatim. Empty when the node has no rolled content. |

`EndgameMapBiomeId` maps to the `_rid` field in `EndgameMapBiomes.dat`:

| `_rid` | `Id` | `Name` |
|---:|---|---|
| `0` | `Water` | `Water` |
| `1` | `Mountain` | `Mountain` |
| `2` | `Grass` | `Grass` |
| `3` | `Forest` | `Forest` |
| `4` | `Swamp` | `Swamp` |
| `5` | `Desert` | `Desert` |
| `6` | `EzomyteCity` | `Ezomyte City` |
| `7` | `FaridunCity` | `Faridun City` |
| `8` | `VaalCity` | `Vaal City` |
| `9` | `BreachCity` | `Breach Stronghold` |
| `10` | `Ocean` | `Ocean` |
| `11` | `Island` | `Island` |
| `12` | `OriathCity` | `Oriath` |

Use `IsSkillTreeOpen` when a plugin only needs to know that either tree is open. Use
`IsPassiveSkillTreeOpen` or `IsAtlasSkillTreeOpen` when passive and atlas behavior should differ.

`AtlasMapsNodesUiElements` and `AtlasMapConnections` are **opt-in**: the host skips atlas node enumeration
by default and only computes it while at least one lease is active. Call
`ImportantUiElements.RequestAtlasMapNodes()` in `OnEnable` and dispose the returned lease in `OnDisable`.
Multiple plugins can each hold their own lease — the host uses a reference count internally.

```csharp
private IDisposable? atlasLease;

public override void OnEnable(bool isGameOpened)
{
    this.atlasLease = ImportantUiElements.RequestAtlasMapNodes();
}

public override void OnDisable()
{
    this.atlasLease?.Dispose();
    this.atlasLease = null;
}
```

```csharp
public override void DrawUI()
{
    if (Core.States.GameCurrentState != GameStateTypes.InGameState)
        return;

    // Hide world overlay while player is in any menu
    if (Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen)
        return;

    // ... draw overlay ...
}
```

**Common UI element traversal:**

```csharp
var ui = Core.States.InGameStateObject.GameUi;
var panel = ui.RightPanel;
if (panel.IsVisible)
{
    ImGui.Text($"panel at {panel.Position}, size {panel.Size}");

    var firstChild = panel[0];
    if (firstChild != null && firstChild.TryGetParent(out var parent))
        ImGui.Text($"child count: {parent.TotalChildrens}");
}
```

**Map UI elements:**

```csharp
var largeMap = Core.States.InGameStateObject.GameUi.LargeMap;
if (largeMap.IsVisible)
{
    var center = largeMap.Center + largeMap.Shift;
    ImGui.GetBackgroundDrawList().AddCircle(center, 6f, 0xFFFFFFFF);
}

var minimap = Core.States.InGameStateObject.GameUi.MiniMap;
if (minimap.IsVisible)
    ImGui.Text($"minimap zoom: {minimap.Zoom:0.00}");
```

**Chat element:**

```csharp
var gameUi = Core.States.InGameStateObject.GameUi;
if (gameUi.ChatParent.IsChatActive)
    return; // avoid drawing or handling hotkeys while typing
```

**Atlas map nodes** (requires a lease — see above):

```csharp
public override void DrawUI()
{
    var gameUi = Core.States.InGameStateObject.GameUi;
    if (!gameUi.IsAtlasMapOpen)
        return;

    foreach (var node in gameUi.AtlasMapsNodesUiElements)
    {
        ImGui.GetForegroundDrawList().AddText(node.Position, 0xFFFFFFFF, node.MapName);
    }
}
```

Visible stash occupancy is opt-in. `RequestVisibleStashItems()` populates `VisibleStashItems` with the
cell element, inventory identity, and authoritative `InventoryEntry`. Normal grids and special/folder
subtabs are resolved by the host. Draw using each cell's own `Position` and `Size`.

```csharp
private IDisposable? stashLease;

public override void OnEnable(bool isGameOpened)
{
    stashLease = ImportantUiElements.RequestVisibleStashItems();
}

public override void OnDisable()
{
    stashLease?.Dispose();
    stashLease = null;
}

foreach (var cell in Core.States.InGameStateObject.GameUi.VisibleStashItems)
    ImGui.GetBackgroundDrawList().AddRect(cell.Element.Position,
        cell.Element.Position + cell.Element.Size, 0xFFFFFFFF);
```

The player's main inventory panel (right side) works the same way and shares the `VisibleStashItem`
shape. `RequestVisibleInventoryItems()` populates `VisibleInventoryItems` with the occupied cells in
the main inventory grid; equipment slots are filtered out. Both leases are reference-counted, so request
each one your plugin needs in `OnEnable` and dispose them in `OnDisable`.

```csharp
private IDisposable? inventoryLease;

public override void OnEnable(bool isGameOpened)
{
    inventoryLease = ImportantUiElements.RequestVisibleInventoryItems();
}

public override void OnDisable()
{
    inventoryLease?.Dispose();
    inventoryLease = null;
}

foreach (var cell in Core.States.InGameStateObject.GameUi.VisibleInventoryItems)
    ImGui.GetBackgroundDrawList().AddRect(cell.Element.Position,
        cell.Element.Position + cell.Element.Size, 0xFFFFFFFF);
```

> **Controller mode:** Side panels, visible stash cells, and the world-travel map are not resolved yet.

---

## Coroutine events

Subscribe in a coroutine started from `OnEnable`. Cancel it in `OnDisable`. These are the events a plugin can wait on (all in `OriathHub.CoroutineEvents`):

| Event | Class | When it fires |
|---|---|---|
| `RemoteEvents.AreaChanged` | `RemoteEvents` | ~50 ms after zone-transition detection. Preloads may not be ready yet. |
| `HybridEvents.PreloadsUpdated` | `HybridEvents` | After `Core.CurrentAreaLoadedFiles` finishes scanning the new area's files. |
| `OriathEvents.OnMoved` | `OriathEvents` | Game window moved or resized. |
| `OriathEvents.OnForegroundChanged` | `OriathEvents` | Game window gained or lost foreground. |
| `OriathEvents.OnClose` | `OriathEvents` | Game process is closing. |
| `OriathEvents.PerFrameDataUpdate` | `OriathEvents` | Once per frame, before rendering. Wait on this to refresh your own data ahead of `DrawUI`. |
| `OriathEvents.PostPerFrameDataUpdate` | `OriathEvents` | After `PerFrameDataUpdate`, as close to render as possible — for the freshest reads. |

> For most per-frame work you don't need these: `DrawUI` runs every rendered frame and the host's parsed data (entities, player, deltas) is already refreshed by the time it's called. Use `PerFrameDataUpdate`/`PostPerFrameDataUpdate` only when you must do data work in a coroutine *before* drawing (e.g. to feed another coroutine).

```csharp
using Coroutine;
using OriathHub.CoroutineEvents;

private ActiveCoroutine? areaCoroutine;

public override void OnEnable(bool isGameOpened)
{
    areaCoroutine = CoroutineHandler.Start(OnAreaChange(), "MyPlugin.AreaChange");
}

public override void OnDisable()
{
    areaCoroutine?.Cancel();
    areaCoroutine = null;
}

private IEnumerator<Wait> OnAreaChange()
{
    while (true)
    {
        yield return new Wait(RemoteEvents.AreaChanged);
        // If the plugin is enabled after the current area has already loaded,
        // this event will not fire once just for plugin startup.
        // clear per-area caches here
    }
}
```

---

## Preloaded files

`Core.CurrentAreaLoadedFiles.PathNames` is a `ConcurrentDictionary<string, int>` of all game files loaded for the current area. Populated after `HybridEvents.PreloadsUpdated` fires.

```csharp
bool isDelirium = Core.CurrentAreaLoadedFiles.PathNames
    .ContainsKey("Metadata/Monsters/LeagueDelirium/DoodadDaemons/DoodadDaemon");
```

---

## Drawing

`DrawUI()` runs inside an ImGui frame — use `ImGuiNET` directly.

Visual overlays should usually keep drawing while either the game window or the OriathHub overlay/settings window is focused, so users can preview setting changes live. Gate those overlays with `OriathHub.Utils.FocusHelper.IsGameOrOverlayForeground()`. Use `OriathHub.Utils.FocusHelper.IsGameForeground()` or `Core.Process.Foreground` for gameplay hotkeys, automation logic, and settings that must only consider the game window itself.

```csharp
if (!FocusHelper.IsGameOrOverlayForeground())
    return;

// ImGui window
ImGui.SetNextWindowBgAlpha(0.7f);
if (ImGui.Begin("My Plugin"))
{
    ImGui.Text("Hello!");
    ImGui.SliderFloat("value", ref myValue, 0f, 100f);
}
ImGui.End();

// World-space overlay (background draw list)
var draw = ImGui.GetBackgroundDrawList();
draw.AddCircleFilled(screenPos, 6f, 0xFF00FF00);
draw.AddText(screenPos, 0xFFFFFFFF, "Label");

// Colors are packed ABGR: 0xAABBGGRR
uint red   = 0xFF0000FF;
uint green = 0xFF00FF00;
uint white = 0xFFFFFFFF;
```

### Textures

Texture loading is provided by the overlay itself (inherited from `ClickableTransparentOverlay`). `Core.Overlay` exposes three methods — each call after the first with the same key just returns the cached handle:

| Method | Loads from | Out params |
|---|---|---|
| `AddOrGetImagePointer(string filePath, bool srgb, out IntPtr handle, out uint width, out uint height)` | a file on disk (the path doubles as the cache key) | handle + pixel dimensions |
| `AddOrGetImagePointer(string name, Image<Rgba32> image, bool srgb, out IntPtr handle)` | an in-memory `SixLabors.ImageSharp` image (you supply the key) | handle only |
| `RemoveImage(string key)` → `bool` | — | returns `true` if a cached texture was removed |

Call these on the render thread (from `DrawUI`, `OnEnable`, or a coroutine). Free every texture you loaded when your plugin is disabled.

```csharp
// From disk — the path is both the source and the cache key.
Core.Overlay.AddOrGetImagePointer("path/to/icon.png", false, out var ptr, out var w, out var h);
ImGui.Image(ptr, new Vector2(w, h));

// From memory — supply your own key, e.g. a procedurally generated image.
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
Image<Rgba32> image = /* build or decode an image */;
Core.Overlay.AddOrGetImagePointer("my-generated-texture", image, false, out var genPtr);

// Free in OnDisable, using the same key you loaded with:
Core.Overlay.RemoveImage("path/to/icon.png");
Core.Overlay.RemoveImage("my-generated-texture");
```

---

## Logging

Use `OriathHub.Utils.Log` — the console is absent in Release builds, but `Log` writes to `oriathhub.log` next to the executable.

```csharp
using OriathHub.Utils;

Log.Info("plugin started", this.Name);
Log.Warning("config not found, using defaults", this.Name);
Log.Error($"exception: {ex}", this.Name);
```

---

## Math helpers

```csharp
using OriathHub.Utils;

float   v = MathHelper.Lerp(0f, 1f, 0.5f);           // 0.5
Vector2 p = MathHelper.Lerp(vecA, vecB, t);           // interpolated Vector2
```

## Focus helpers

```csharp
using OriathHub.Utils;

// True while either the game window or OriathHub overlay/settings window is focused.
bool canPreviewVisualOverlay = FocusHelper.IsGameOrOverlayForeground();
```

Use `FocusHelper.IsGameOrOverlayForeground()` for visual drawing that should remain visible while users edit plugin settings. Use `Core.Process.Foreground` for foreground-only hotkeys, gameplay actions, and automation safety gates.

---

## Input

**Global hotkeys** — fire even while the game window is focused:

```csharp
using ClickableTransparentOverlay.Win32;
using OriathHub.Utils;

private bool f5WasDown;

if (HotkeyHelper.IsPressedOnce(VK.F5, ref f5WasDown))
    settings.Show = !settings.Show;
```

Use `HotkeyHelper.IsPressedOnce` for toggles and other one-shot actions. Use `HotkeyHelper.IsPressed` only for behavior that should intentionally continue while the key is held.

**Sending key-up messages** — available through `OriathHub.Utils.MiscHelper`, but use it only for plugins whose explicit purpose is automation:

```csharp
using ClickableTransparentOverlay.Win32;
using OriathHub.Utils;

bool sent = MiscHelper.KeyUp(VK.F1);
```

`MiscHelper.KeyUp` is rate-limited by the host settings and returns `false` in controller mode.

**Overlay-focused input** — only when your ImGui window has focus:

```csharp
ImGui.IsKeyPressed(ImGuiKey.F5)
ImGui.GetIO().MousePos
```

**Configurable hotkey** — store a `VK` in settings and render a picker in `DrawSettings`:

```csharp
ImGuiHelper.NonContinuousEnumComboBox("Hotkey", ref settings.Hotkey);
```

---

## Settings helpers

`OriathHub.Utils.ImGuiHelper` also provides:

```csharp
// Packed ABGR color helpers
uint green = ImGuiHelper.Color(0, 255, 0, 255);
Vector4 color = ImGuiHelper.Color(green);

// Foreground rectangle and world-space text helpers
ImGuiHelper.DrawRect(position, size, 255, 255, 0);
ImGuiHelper.DrawText(worldPosition, "Label");

// Enum combo-box (continuous stepping — every frame while held)
ImGuiHelper.EnumComboBox("Mode", ref settings.Mode);

// Enum combo-box (non-continuous — fires once per key press)
ImGuiHelper.NonContinuousEnumComboBox("Hotkey", ref settings.Hotkey);

// Tooltip on the previous widget
ImGuiHelper.ToolTip("This is what the setting does.");

// Small text helpers
ImGuiHelper.HelpInline("Shown when hovering the (?) marker.");
ImGuiHelper.Info("Disabled informational text.");
```

`OriathHub.Utils.JsonHelper`:

```csharp
// Load or create settings (creates the file with defaults if absent)
settings = JsonHelper.CreateOrLoadJsonFile<MySettings>(new FileInfo(path));

// Save settings
JsonHelper.SaveToFile(settings, new FileInfo(path));
```

---

## Raw memory reads

When the host wrappers do not expose what you need, read raw game memory through `Core.Process`. Define your own `[StructLayout]` struct with the relevant offsets — the host's internal layout structs are not visible to plugins.

```csharp
using System.Runtime.InteropServices;
using GameOffsets.Natives;   // StdVector, StdWString, StdMap, StdList, etc.

[StructLayout(LayoutKind.Explicit, Pack = 1)]
struct MyCustomStruct
{
    [FieldOffset(0x10)] public StdVector Items;
    [FieldOffset(0x28)] public StdWString Name;
}

// Every RemoteObject exposes a public Address property:
IntPtr addr = someComponent.Address;

// Error-safe read (prefer this on hot paths):
if (Core.Process.ReadMemory<MyCustomStruct>(addr, out var raw))
{
    int[]  items = Core.Process.ReadStdVector<int>(raw.Items);
    string name  = Core.Process.ReadStdWString(raw.Name);
}
// else: read failed (torn frame, bad address) — handle or skip
```

### Custom remote objects

For data the host does not track, wrap the address in your own `RemoteObjectBase` subclass. Use `forceUpdate: true` when the same address should be refreshed every frame, and use `skipFirstUpdate: true` so the base constructor does not call your override before your derived fields are initialized.

`RemoteObjectBase`, `UiElementBase`, and `ComponentBase` expose their constructor/update/cleanup/diagnostic hooks to derived plugin types. Override `UpdateData` and `CleanUpData` for the memory lifecycle, and override `ToImGui` only if you want host-style diagnostics for your custom wrapper.

```csharp
using OriathHub.RemoteObjects;
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
private struct CustomTrackedStruct
{
    [FieldOffset(0x18)] public int Value;
}

private sealed class CustomTrackedObject : RemoteObjectBase
{
    public CustomTrackedObject(IntPtr address)
        : base(IntPtr.Zero, forceUpdate: true, skipFirstUpdate: true)
    {
        this.Address = address;
    }

    public int Value { get; private set; }

    protected override void UpdateData(bool hasAddressChanged)
    {
        if (Core.Process.ReadMemory<CustomTrackedStruct>(this.Address, out var raw))
        {
            this.Value = raw.Value;
        }
    }

    protected override void CleanUpData()
    {
        this.Value = 0;
    }
}
```

Refresh plugin-owned objects from a coroutine that waits on `OriathEvents.PerFrameDataUpdate`; this runs before the plugin render pass.

```csharp
using Coroutine;
using OriathHub.CoroutineEvents;
using System.Collections.Generic;

private ActiveCoroutine? refreshCoroutine;
private CustomTrackedObject? tracked;

public override void OnEnable(bool isGameOpened)
{
    tracked = new CustomTrackedObject(IntPtr.Zero);
    refreshCoroutine = CoroutineHandler.Start(RefreshCustomData(), "MyPlugin.CustomData");
}

public override void OnDisable()
{
    refreshCoroutine?.Cancel();
    refreshCoroutine = null;
    tracked = null;
}

private IEnumerator<Wait> RefreshCustomData()
{
    while (true)
    {
        yield return new Wait(OriathEvents.PerFrameDataUpdate);

        IntPtr address = FindCurrentCustomAddress();
        if (tracked != null)
        {
            tracked.Address = address;
        }
    }
}
```

All read methods on `Core.Process`:

| Method | Returns | Reads |
|---|---|---|
| `ReadMemory<T>(addr, out T value)` | `bool` | One unmanaged struct. `false` on failure. |
| `ReadMemoryArray<T>(addr, count, out T[] value)` | `bool` | `count` contiguous structs. `false` on failure. |
| `ReadMemoryRequired<T>(addr)` | `T` | One struct; throws `MemoryReadException` on failure. |
| `ReadMemoryArrayRequired<T>(addr, count)` | `T[]` | `count` contiguous structs; throws on failure. |
| `ReadStdVector<T>(stdVector)` | `T[]` | A `std::vector<T>`. |
| `ReadStdList<T>(stdList)` | `List<T>` | A `std::list<T>`. |
| `ReadStdMap<TKey,TValue>(stdMap, maxSizeAllowed, enableCounting, onEach)` | `int` | Walks a `std::map`; calls `onEach(key, value)` per node (return `false` to skip a node). Returns total node count. |
| `ReadStdWString(stdWString)` | `string` | A `std::wstring` (UTF-16). |
| `ReadUnicodeString(addr)` | `string` | Null-terminated UTF-16 string at a raw address. |

**Error-safe vs fast-fail:**

- `ReadMemory`/`ReadMemoryArray` return `false` and write `default`/empty on failure — they never log or throw. Use them everywhere except top-level startup reads.
- `*Required` variants throw `MemoryReadException`. Reserve them for one-shot sequential reads where failure indicates a real bug (e.g. a stale offset after a game patch). **Never use them on a hot path or inside a parallel loop** — a torn frame will throw, and in `Parallel.*` it becomes an `AggregateException` that crashes the host.
- `ReadStd*` container helpers always return empty on a bad address rather than throwing.

### Reading `.dat`/`.datc64` tables by name

`DatFileReader.TryGetDatTable(path, out DatTable)` (namespace `OriathHub.RemoteObjects.FilesStructures`) resolves a loaded data file from the game's File Root by its in-game path and returns its row block.

```csharp
if (DatFileReader.TryGetDatTable("Data/Balance/EndgameMapBiomes.dat", out var table))
{
    int rowSize = 0xA0;                       // per-table; you supply it
    for (int i = 0; i < table.RowCount(rowSize); i++)
    {
        IntPtr row = table.Row(i, rowSize);   // row i base address
        Core.Process.ReadMemory<IntPtr>(row + 0x28, out var namePtr);
        string name = Core.Process.ReadUnicodeString(namePtr);
    }
}
```

`DatTable` exposes `RowsBegin`/`RowsEnd`, `IsValid`, `ByteLength`, `RowCount(rowSize)`, `Row(index, rowSize)`. Row size and column offsets are table-specific — find them in `GameOffsets`. Returns `false` until the game is attached and the file is loaded.

Convenience readers:

- `BaseItemTypes.TryGet(metadataPath, out BaseItemType itemType)` resolves an entity/item metadata path to `MetadataPath`, localized `Name`, and normalized `ClassName`. The table is cached by the host and cleared when the game closes.
- `EndgameMapBiomes.TryGetNames(out IReadOnlyList<string> names)` returns biome display names indexed by biome id (the `AtlasMapsNodeUiElement.EndgameMapBiomeId`), cached.
- `EndgameMapContent.TryGetNames(out IReadOnlyList<string> names)` returns map-content display names (Breach, Expedition, Powerful Map Boss, …) indexed by row id from `EndgameMapContent.dat`, cached. Use it to present a content picker or canonical names; per-node content is on `AtlasMapsNodeUiElement.Content`.
- `AnimationDat.TryGetName(int animationId, out string name)` returns an animation name from the loaded `Animation.dat` table. Prefer `Actor.AnimationName` unless you already have a raw animation id.

```csharp
if (BaseItemTypes.TryGet(item.Path, out var baseType))
    Log.Info($"{baseType.Name} ({baseType.ClassName})", Name);
```

---

## Plugin metadata

```csharp
public override string Name        => "My Plugin";        // required
public override string Description => "Does X.";          // required
public override string Author      => "YourName";         // optional
public override string Version     => "1.2.3";            // optional

// Path to the plugin's deployment folder — build config/asset paths from this:
var configPath  = Path.Combine(DllDirectory, "config",   "settings.json");
var texturePath = Path.Combine(DllDirectory, "textures", "icon.png");
```

---

## Discovering the full surface

Because the SDK ships `OriathHub.xml` and `GameOffsets.xml`, your IDE shows XML docs and full IntelliSense over all `OriathHub.*` namespaces and the public `GameOffsets.Natives.*` types. Browsing `OriathHub.RemoteObjects.*` and `OriathHub.RemoteObjects.Components.*` in your editor is the fastest way to explore what data is available.

The SDK ships a stripped, `Natives`-only `GameOffsets`, so the `GameOffsets.Objects.*` layout structs are not present in the package at all — access game data through the host wrappers instead, and define your own `[StructLayout]` structs for any raw reads.
