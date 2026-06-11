# Plugin lifecycle

Every plugin derives from `OriathHub.Plugin.PluginBase`. The host (`PluginManager`) owns discovery, enable/disable state, rendering, saving, and reloads. Your plugin implements the members below and cleans up everything it starts.

## PluginBase members

| Member | When it runs | Notes |
|---|---|---|
| `Name` | Read on demand | Display name in the Plugins tab. Required. |
| `Description` | Read on demand | Short text next to the enable toggle. Required. |
| `Author` | Read on demand | Optional (`virtual`, default empty string). Shown in the Plugins tab when set. |
| `Version` | Read on demand | Optional (`virtual`, default empty string). Shown in the Plugins tab when set. |
| `DllDirectory` | Set once before `OnEnable` | Absolute path to the folder containing your plugin DLL. Build config and asset paths from this value. Do not set it yourself. |
| `OnEnable(bool isGameOpened)` | When the plugin is switched on, and at startup if it was enabled last session | Load settings, start coroutines, load textures, and initialize plugin state. |
| `OnDisable()` | When the plugin is switched off or reloaded | Cancel coroutines, free textures, stop timers, close files, and clear static references. |
| `DrawDashboard()` | Each frame while the Dashboard tab is active | Optional (`virtual`, default empty). Override to add a Dashboard tab, shown **first** in the plugin's detail view. The tab is hidden automatically when not overridden. |
| `DrawSettings()` | Each frame while the Settings tab is active | Render the everyday ImGui controls for your plugin — the settings users interact with most. |
| `DrawAdvancedSettings()` | Each frame while the Advanced tab is active | Optional (`virtual`, default empty). Override to expose power-user or technical controls in a separate tab. The Advanced tab is hidden automatically when not overridden. |
| `DrawUI()` | Every rendered frame while enabled | Draw overlays and plugin windows. Keep it cheap and bail out early when there is nothing to draw. |
| `SaveSettings()` | Periodically and on clean shutdown, only while enabled | Persist settings to disk. |

For visual overlays, prefer `FocusHelper.IsGameOrOverlayForeground()` over `Core.Process.Foreground` when deciding whether to draw. That lets users focus the OriathHub settings window and still preview changes such as colors, sizes, and border thickness live over the game. Keep `Core.Process.Foreground` for hotkeys and automation logic that must only run while the game window itself is focused.

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

Keep the returned `ActiveCoroutine` and cancel it in `OnDisable`. You may add long-lived coroutines to `Core.CoroutinesRegistrar` if you want them visible in host coroutine diagnostics, but registration is not what keeps them alive. The handle and cancellation are the important parts.

```csharp
using Coroutine;
using OriathHub.CoroutineEvents;
using System.Collections.Generic;

private ActiveCoroutine? areaChangeCoroutine;

public override void OnEnable(bool isGameOpened)
{
    settings = JsonHelper.CreateOrLoadJsonFile<MyPluginSettings>(SettingsFile);
    areaChangeCoroutine = CoroutineHandler.Start(OnAreaChange(), "MyPlugin.AreaChange");
}

public override void OnDisable()
{
    areaChangeCoroutine?.Cancel();
    areaChangeCoroutine = null;
    // free textures here too
}

private IEnumerator<Wait> OnAreaChange()
{
    while (true)
    {
        yield return new Wait(RemoteEvents.AreaChanged);
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
