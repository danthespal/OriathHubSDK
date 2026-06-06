# Gotchas

Common mistakes that bite plugin authors.

---

## Shared assemblies

The host loads each plugin into a collectible `PluginAssemblyLoadContext` that forwards `OriathHub` and `GameOffsets` to the host's already-loaded copies. A `Core.States` reference in your plugin is the same object the host uses.

**Consequences:**

- Never ship `OriathHub.dll` or `GameOffsets.dll` next to your plugin DLL. The SDK references them as compile-time-only (`ref/`) assemblies, so a default build will not copy them — keep it that way.
- The shared third-party libraries (`ImGuiNET`, `ClickableTransparentOverlay`, `Coroutine`, `Newtonsoft.Json`, `SixLabors.ImageSharp`) are already loaded by the host. Do not deploy them either.

---

## Adding a new third-party dependency

If your plugin references a NuGet package the host does not already have, the host cannot resolve it at runtime. You must copy that DLL into your `Plugins/<Name>/` folder alongside your plugin DLL. The default `CopyToHostPluginsDir` target only copies your own DLL — extend it for any extra dependencies.

Avoid native dependencies (unmanaged DLLs) when possible.

---

## Discovery rules

- The loader looks for `Plugins/<FolderName>/<FolderName>*.dll`. Your assembly name must **start with** the folder name, or the plugin will not be found.
- There must be **exactly one** `sealed PluginBase` subclass per DLL. Zero or more than one causes the plugin to be skipped silently.

---

## Release has no console — use `Log`

OriathHub Release is a `WinExe` with no console window. Any `Console.WriteLine` diagnostics vanish. Use `OriathHub.Utils.Log` instead — it writes to `oriathhub.log` next to the executable and is visible in Release.

```csharp
Log.Info("loaded", this.Name);    // not Console.WriteLine
```

---

## `DrawUI` runs every rendered frame

`DrawUI()` is called once per frame. Keep it cheap, bail out early when there is nothing to draw, and cache anything expensive outside the method. The same rule applies to coroutines — they run on the render thread and must never block or spin.

---

## Releasing resources on disable / reload

A plugin is torn down whenever it is disabled or reloaded. **Nothing is cleaned up automatically.** Release everything you allocated in `OnEnable`:

- **Coroutines:** keep the `ActiveCoroutine` returned by `CoroutineHandler.Start(...)` and call `.Cancel()` on it in `OnDisable`.
- **Textures:** call `Core.Overlay.RemoveImage(path)` for each one you loaded.
- **Anything else:** stop timers, close files, clear static caches.

A lingering coroutine or texture keeps your assembly referenced. The collectible load context cannot unload and **Reload from disk leaks the old copy** of your plugin in memory. Clean up everything and reloads stay clean.

---

## Reloading during development

Expand a plugin in the Plugins tab and click **Reload from disk** to unload it and read the updated DLL from disk — no app restart required. The host loads plugin DLLs from an in-memory byte copy, so the file is never locked and you can rebuild while OriathHub is running.

Tip: if you gate `DrawUI` entirely on `GameCurrentState == InGameState`, you will see nothing until you are in a loaded area. Draw at least a minimal unconditional window during development so you can confirm the plugin loaded successfully.

---

## Re-packing the SDK during development

NuGet caches packages by version. If you re-`pack` the SDK without bumping `<Version>`, clear the cache so consumers pick up the new build:

```sh
rm -rf %USERPROFILE%\.nuget\packages\oriathhub.sdk
```

Or bump the SDK `<Version>` in `Directory.Build.props` and update your plugin's `PackageReference`.

The `Sdk/build-sdk.ps1` script does this automatically — it evicts the cached copy before packing.

---

## Entity delta lists are empty on zone-change frames

`EntitiesAddedThisFrame` and `EntitiesRemovedThisFrame` are both empty on the frame a zone transition is detected, because the area dictionary is bulk-cleared rather than drained per-entity. Use `RemoteEvents.AreaChanged` to react to transitions.

---

## `*Required` reads on hot paths

`ReadMemoryRequired<T>` and `ReadMemoryArrayRequired<T>` throw `MemoryReadException` on failure. A torn frame during a parallel entity loop will cause an `AggregateException` that crashes the host. Reserve these methods for top-level sequential startup reads where failure genuinely means a stale offset — never call them inside a `Parallel.ForEach` or inside `DrawUI`.

---

## Account risk

OriathHub reads another process's memory, which carries account-risk under the game's Terms of Service. Plugins are read-only by design — never add anything that writes to or injects into the game process.
