# Plugin lifecycle

Every plugin derives from `OriathHub.Plugin.PluginBase`. The host (`PluginManager`) owns the lifecycle — you only implement the members below.

---

## PluginBase members

| Member | When it runs | Notes |
|---|---|---|
| `Name` | Read on demand | Display name in the Plugins tab. Required. |
| `Description` | Read on demand | Short text next to the enable toggle. Required. |
| `Author` | Read on demand | Optional (`virtual`, default empty string). Shown in the Plugins tab when set. |
| `Version` | Read on demand | Optional (`virtual`, default empty string). Shown in the Plugins tab when set. |
| `DllDirectory` | Set once before `OnEnable` | Absolute path to the folder containing your plugin DLL. Use it to build config/asset paths. Provided by `PluginBase` — do not set it yourself. |
| `OnEnable(bool isGameOpened)` | When the plugin is switched on, and at startup if it was enabled last session | `isGameOpened` is `true` if the game process is already attached. Load settings, start coroutines, and load textures here. |
| `OnDisable()` | When the plugin is switched off **or reloaded** | Release everything `OnEnable` allocated. Cancel every coroutine and free every texture. Nothing is cleaned up automatically. |
| `DrawSettings()` | Each frame while the Settings window shows your plugin | Render ImGui controls bound to your settings fields. |
| `DrawUI()` | **Every rendered frame** while enabled | Your overlay. Keep it cheap; guard on game state and bail early when there is nothing to draw. |
| `SaveSettings()` | Periodically and on a clean shutdown — only while enabled | Persist your settings to disk. |

---

## Settings

Settings are a plain class of public fields, serialized with Newtonsoft.Json via `OriathHub.Utils.JsonHelper`. Store the file under `DllDirectory` so each plugin instance is self-contained:

```csharp
public class MyPluginSettings
{
    public bool  ShowOverlay = true;
    public float BarWidth    = 80f;
    public System.Numerics.Vector4 HealthColor = new(0.2f, 0.8f, 0.2f, 1f);
}

private MyPluginSettings settings = new();
private FileInfo SettingsFile => new(Path.Combine(DllDirectory, "config", "settings.json"));

public override void OnEnable(bool isGameOpened)
    => settings = JsonHelper.CreateOrLoadJsonFile<MyPluginSettings>(SettingsFile);

public override void SaveSettings()
    => JsonHelper.SaveToFile(settings, SettingsFile);
```

`SaveSettings()` is only called while your plugin is enabled, so a disabled plugin never overwrites a user's config with defaults.

---

## Coroutines and events

OriathHub is driven by the [`Coroutine`](https://www.nuget.org/packages/Coroutine) scheduler ticked on the render thread. You can start your own coroutines that wait on host events — for example, to reset per-area caches when the player changes zones.

Start a coroutine with `CoroutineHandler.Start`, keep the returned `ActiveCoroutine`, and **always cancel it in `OnDisable`**:

```csharp
using Coroutine;
using OriathHub.CoroutineEvents;

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

**Coroutines run on the render thread — never block them.** A coroutine or texture you leave running keeps your assembly referenced, preventing the collectible load context from unloading and causing **Reload from disk to leak the old copy**.

See the [API reference](api-overview.md#coroutine-events) for the full list of events you can wait on.

---

## Textures

Load textures through the overlay:

```csharp
Core.Overlay.AddOrGetImagePointer("path/to/icon.png", false, out var ptr, out var w, out var h);
// use ptr with ImGui.Image(ptr, new Vector2(w, h)) or drawList.AddImage(ptr, ...)
```

Free them in `OnDisable`:

```csharp
Core.Overlay.RemoveImage("path/to/icon.png");
```

---

## Error handling

`PluginManager` wraps every lifecycle call in a try/catch and logs exceptions through `OriathHub.Utils.Log` (written to `oriathhub.log`). A throwing plugin will not crash the host, but it also will not be retried — handle your own recoverable errors internally and use `Log` for diagnostics so they remain visible in Release builds where `Console.WriteLine` is a no-op.
