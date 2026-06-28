# Plugin lifecycle

Every plugin derives from `OriathHub.Plugin.PluginBase`. The host (`PluginManager`) owns discovery, enable/disable state, rendering, saving, and reloads. Your plugin implements the members below and cleans up everything it starts.

## PluginBase members

| Member | When it runs | Notes |
|---|---|---|
| `Name` | Read on demand | Display name in the Plugins tab. Required. |
| `Description` | Read on demand | Short text next to the enable toggle. Required. |
| `Author` | Read on demand | Optional (`virtual`, default empty string). Shown in the Plugins tab when set. |
| `Version` | Read on demand | Optional (`virtual`, default empty string). Shown in the Plugins tab when set. |
| `DllDirectory` | Available during construction and later lifecycle calls | Absolute path to the folder containing your plugin DLL. Build config and asset paths from this value. Do not set it yourself. |
| `OnEnable(bool isGameOpened)` | When the plugin is switched on, and at startup if it was enabled last session | Load settings, start coroutines, load textures, and initialize plugin state. |
| `OnDisable()` | When the plugin is switched off or reloaded | Cancel coroutines, free textures, stop timers, close files, and clear static references. |
| `DrawDashboard()` | Each frame while the Dashboard tab is active | Optional (`virtual`, default empty). Override to add a Dashboard tab, shown **first** in the plugin's detail view. The tab is hidden automatically when not overridden. |
| `DrawSettings()` | Each frame while the Settings tab is active | Render the everyday ImGui controls for your plugin — the settings users interact with most. |
| `DrawAdvancedSettings()` | Each frame while the Advanced tab is active | Optional (`virtual`, default empty). Override to expose power-user or technical controls in a separate tab. The Advanced tab is hidden automatically when not overridden. |
| `GetSearchableSettings()` | Each frame while the settings search box has text | Optional (`virtual`, default none). Override to make your options findable from the settings window's search box. See [Making settings searchable](#making-settings-searchable). |
| `DrawUI()` | Every rendered frame while enabled | Draw overlays and plugin windows. Keep it cheap and bail out early when there is nothing to draw. |
| `SaveSettings()` | When the host raises its save event, only while enabled | Persist settings to disk. This happens when the settings window is closed, **Save Settings Now** is clicked, or the overlay shuts down cleanly. |

For visual overlays, prefer `FocusHelper.IsGameOrOverlayForeground()` over `Core.Process.Foreground` when deciding whether to draw. That lets users focus the OriathHub settings window and still preview changes such as colors, sizes, and border thickness live over the game. Use `FocusHelper.IsGameForeground()` or `Core.Process.Foreground` for hotkeys, automation logic, and true "hide when game is in the background" settings.

## Settings tabs

The host's plugin detail pane shows **Settings** and **About** always, plus two optional tabs that only appear when you override their draw method: **Dashboard** (shown first) and **Advanced**. So a plugin overriding everything shows **Dashboard · Settings · Advanced · About**; a plugin overriding nothing optional shows just **Settings · About**.

- **Dashboard** — calls `DrawDashboard()` every frame, but **the tab is hidden when you do not override the method**. When present it is the first tab. Use it for an at-a-glance status/overview view (live readouts, summaries, primary actions).
- **Settings** — calls `DrawSettings()` every frame. Put the controls users touch regularly here.
- **Advanced** — calls `DrawAdvancedSettings()` every frame, but **the tab is hidden when you do not override the method**. Put technical knobs, calibration sliders, and power-user options here so the everyday settings view stays clean.
- **About** — filled automatically by the host from `Author`, `Version`, `Description`, `DllDirectory`, and the SDK stamp. No code needed.

```csharp
public override void DrawDashboard()
{
    // Dashboard tab only appears because this method is overridden; it renders first.
    ImGui.Text($"Tracked entities: {trackedCount}");
}

public override void DrawSettings()
{
    // Common toggles — visible to everyone
    ImGui.Checkbox("Show overlay", ref settings.ShowOverlay);
}

public override void DrawAdvancedSettings()
{
    // Advanced tab only appears because this method is overridden
    ImGui.DragFloat("Scale multiplier", ref settings.ScaleMultiplier, 0.01f, 0.1f, 5f);
    ImGui.Checkbox("Verbose logging", ref settings.VerboseLogging);
}
```

## Making settings searchable

The settings window has a single search box above the content panel. When the user types in it,
the host stops drawing your normal Settings/Advanced tabs and instead shows a flattened, filtered
list of just the matching options. That list comes from `GetSearchableSettings()` — override it to
opt your plugin in. A plugin that doesn't override it shows *"This plugin's settings cannot be
searched."* while a query is active; its normal tabs are unaffected when the box is empty.

Each `SettingSearchEntry` carries a **section** breadcrumb (where the option normally lives, also
shown as the result heading), a searchable **label**, a **draw** delegate, and optional extra
**keywords**. The draw delegate should render *the same widget your normal view uses* — it owns the
side effects, so reuse a shared method when the logic is non-trivial. Declare each flat option as
its own entry; represent a large dynamic cluster (a list, per-item tabs) as a single entry whose
delegate draws the whole cluster.

```csharp
public override IEnumerable<SettingSearchEntry> GetSearchableSettings() => new[]
{
    // A flat option restated inline — cheap, one widget.
    new SettingSearchEntry("Settings", "Show overlay",
        () => ImGui.Checkbox("Show overlay", ref settings.ShowOverlay), "visible hud"),

    // An option with side effects: extract a shared method and call it from both
    // DrawSettings() and here, so there is a single source of truth.
    new SettingSearchEntry("Settings", "Bar width", DrawBarWidthOption),

    // A whole cluster behind one entry (the delegate draws the entire block).
    new SettingSearchEntry("Advanced", "Color profiles", DrawColorProfiles,
        "palette theme colours"),
};
```

Matching is case-insensitive and tests the label, the section, and the keywords. Returning a fresh
list each call is fine — keep the delegates light since they run per frame while searching.

## Settings

Settings are plain classes with public fields, serialized with Newtonsoft.Json through `OriathHub.Utils.JsonHelper`. Store the file under `DllDirectory` so each plugin folder is self-contained.

```csharp
public sealed class MyPluginSettings
{
    public bool ShowOverlay = true;
    public float BarWidth = 80f;
    public System.Numerics.Vector4 HealthColor = new(0.2f, 0.8f, 0.2f, 1f);
}

private MyPluginSettings settings = new();
private FileInfo SettingsFile => new(Path.Combine(DllDirectory, "config", "settings.json"));

public override void OnEnable(bool isGameOpened)
{
    settings = JsonHelper.CreateOrLoadJsonFile<MyPluginSettings>(SettingsFile);
}

public override void SaveSettings()
{
    JsonHelper.SaveToFile(settings, SettingsFile);
}
```

`SaveSettings()` is called only while your plugin is enabled, so a disabled plugin will not overwrite a user's config with defaults.

## Coroutines and events

OriathHub uses the [`Coroutine`](https://www.nuget.org/packages/Coroutine) scheduler on the render thread. A plugin can start coroutines that wait on public host events, such as an area change.

Prefer **`StartCoroutine(...)`** (defined on `PluginBase`) over calling `CoroutineHandler.Start(...)` directly. A coroutine started through `StartCoroutine` is tied to your plugin's lifetime: the host force-cancels it when your plugin is disabled, reloaded, or unloaded — even if your `OnDisable` forgets to cancel it or throws partway through. A coroutine left running against an unloaded plugin throws every frame and pins your collectible load context, defeating hot-reload. If you started a coroutine another way (e.g. an event-based overload), pass its handle to **`TrackCoroutine(...)`** to get the same managed cancellation.

You can still cancel your own handle in `OnDisable` for immediate teardown; doing both is harmless. You may also add long-lived coroutines to `Core.CoroutinesRegistrar` if you want them visible in host coroutine diagnostics, but registration there is not what keeps them alive or cancels them.

```csharp
using Coroutine;
using OriathHub.CoroutineEvents;
using System.Collections.Generic;

private ActiveCoroutine? areaChangeCoroutine;

public override void OnEnable(bool isGameOpened)
{
    settings = JsonHelper.CreateOrLoadJsonFile<MyPluginSettings>(SettingsFile);
    // Host-managed: cancelled automatically on disable/reload/unload.
    areaChangeCoroutine = StartCoroutine(OnAreaChange(), "MyPlugin.AreaChange");
}

public override void OnDisable()
{
    // Optional — the host also cancels tracked coroutines for you.
    areaChangeCoroutine?.Cancel();
    areaChangeCoroutine = null;
    // free textures here too
}

private IEnumerator<Wait> OnAreaChange()
{
    while (true)
    {
        yield return new Wait(RemoteEvents.AreaChanged);
        // If the plugin is enabled after OriathHub has already started,
        // this event will not fire once just for plugin startup.
        // reset per-area caches
    }
}
```

Coroutines run on the render thread. Do not block, sleep, spin, or perform expensive synchronous I/O inside them. A coroutine, static event subscription, timer, file handle, or texture left alive can keep your assembly referenced and prevent clean reloads.

See the [API reference](api-overview.md#coroutine-events) for the full list of public events.

## Textures

Load textures through the overlay:

```csharp
Core.Overlay.AddOrGetImagePointer("path/to/icon.png", false, out var ptr, out var w, out var h);
// use ptr with ImGui.Image(ptr, new Vector2(w, h)) or drawList.AddImage(ptr, ...)
```

Free them in `OnDisable` using the same key:

```csharp
Core.Overlay.RemoveImage("path/to/icon.png");
```

## Error handling

`PluginManager` wraps lifecycle calls in try/catch and logs exceptions through `OriathHub.Utils.Log`. A throwing plugin should not crash the host. For repeated per-frame failures, the manager suppresses duplicate exception spam until the plugin recovers or throws a different exception.

Handle recoverable errors inside your plugin and log useful diagnostics with `Log`. `Console.WriteLine` is not visible in Release builds because the host is a `WinExe`.
