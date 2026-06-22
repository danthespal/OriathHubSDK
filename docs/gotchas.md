# Gotchas

Common mistakes that bite plugin authors.

## Shared assemblies

The host loads each plugin into a collectible `PluginAssemblyLoadContext`. The loader forwards `OriathHub` and `GameOffsets` to the host's already-loaded copies, so a `Core.States` reference in your plugin is the same object the host uses.

Consequences:

- Do not ship `OriathHub.dll` or `GameOffsets.dll` next to your plugin DLL. The SDK provides them as compile-time reference assemblies.
- Do not copy the shared SDK dependencies (`ImGuiNET`, `ClickableTransparentOverlay`, `Coroutine`, `Newtonsoft.Json`, `SixLabors.ImageSharp`) unless you have a specific versioning problem and know how it will resolve at runtime.
- Treat `Core.OHSettings` as host-owned. Read it when needed, but store plugin settings in your own file under `DllDirectory`.

## SDK version stamps

The SDK package stamps consuming plugin assemblies with `AssemblyMetadata("OriathHubSdkVersion", "...")`. `PluginManager` logs the plugin SDK version and the host SDK version when the plugin loads.

If the major version differs, the loader logs a warning. The plugin may still load, but a major mismatch means the plugin was compiled against a potentially incompatible API surface. Rebuild against the SDK version that matches the host before investigating runtime bugs.

## Adding a third-party dependency

If your plugin references a NuGet package the host does not already ship, copy that dependency's runtime DLLs into your `Plugins/<Name>/` folder beside your plugin DLL. The default copy target usually copies only your own DLL.

For package references, set this property so runtime assemblies are copied into your plugin output:

```xml
<EnableDynamicLoading>true</EnableDynamicLoading>
```

Then include those DLLs in your deployment target:

```xml
<ItemGroup>
  <PluginFiles Include="$(OutDir)$(TargetName)$(TargetExt)" />
  <PluginFiles Include="$(OutDir)System.Linq.Dynamic.Core.dll" />
</ItemGroup>
```

Avoid unmanaged/native dependencies when possible. If you need one, test reload and startup carefully because native load/unload behavior is less forgiving.

## Discovery rules

- The loader scans the `Plugins` folder next to the running `OriathHub.exe`.
- It searches each non-hidden subfolder for `Plugins/<FolderName>/<FolderName>*.dll`.
- Your assembly name must start with the folder name, or the plugin will not be found.
- A plugin DLL must contain exactly one `sealed PluginBase` subclass.
- Discovery and instantiation failures are logged through `Log` and the DLL is skipped.

## Component wrappers are discovered by name

If your plugin ships a `ComponentBase` subclass (see [examples](examples.md)), the host registers it by **type name** — which must match the game's component name — when the plugin loads.

- The host registers its own components first, so a plugin can never replace a host component (e.g. your own `Life`). The collision is logged and the host's type is kept.
- Two plugins that ship a component of the same name collide the same way: first loaded wins. Plugin folders load in alphabetical order.
- Registrations are dropped when the plugin is reloaded from disk, so the wrapper type stops pinning the old assembly. They are re-registered from the rebuilt DLL.
- A custom component still only resolves when the entity actually has that component; discovery only maps the name to a type, it does not invent the component on entities that lack it.

## Release has no console

OriathHub Release is a `WinExe` with no console window. `Console.WriteLine` diagnostics are not useful for plugin users. Use `OriathHub.Utils.Log`; it writes to `oriathhub.log` next to the executable.

```csharp
Log.Info("loaded", Name);
Log.Warning("config not found, using defaults", Name);
Log.Error($"failed to load texture: {ex}", Name);
```

## `DrawUI` runs every rendered frame

`DrawUI()` is called once per rendered frame while the plugin is enabled. Keep it cheap:

- Check `GameCurrentState` before reading in-game objects.
- Return early when neither the game nor OriathHub overlay is focused if your visual overlay does not need to draw; use `FocusHelper.IsGameOrOverlayForeground()` for this.
- Use `FocusHelper.IsGameForeground()` or `Core.Process.Foreground` instead for foreground-only hotkeys, automation gates, and true "hide when game is in the background" settings.
- Return early when `GameUi.IsAnyLargePanelOpen` if your world overlay would cover menus.
- Cache expensive work outside `DrawUI` and update caches on events such as `RemoteEvents.AreaChanged`.

## Releasing resources on disable and reload

A plugin is torn down whenever it is disabled or reloaded. Nothing is cleaned up automatically.

Release everything you allocated in `OnEnable`:

- Coroutines: keep the `ActiveCoroutine` returned by `CoroutineHandler.Start(...)` and call `.Cancel()` in `OnDisable`.
- Textures: call `Core.Overlay.RemoveImage(key)` for each texture you loaded.
- Timers/events: unsubscribe, stop timers, and clear static references.
- Files/resources: close or dispose file handles and disposable objects.

A lingering coroutine, texture, timer, or static reference can keep your assembly alive. The new DLL can still load, but the old copy stays in memory until the process exits.

## Reloading during development

Expand a plugin in the Plugins tab and click **Reload from disk** to unload it and read the updated DLL without restarting OriathHub. The host loads plugin DLLs from an in-memory byte copy, so the file is not locked and you can rebuild while OriathHub is running.

During early development, draw a small unconditional debug window. If you gate all output on `GameCurrentState == InGameState`, the plugin may appear to do nothing until you are in a loaded area.

## Updating to a newer SDK package

Use the `.nupkg` file distributed with the OriathHub version you are targeting. If you receive a newer SDK package with the same package version during private testing, clear NuGet's cached copy before rebuilding your plugin:

```powershell
Remove-Item -Recurse -Force "$env:USERPROFILE\.nuget\packages\oriathhub.sdk"
```

For normal releases, the SDK package version should change when the plugin-facing API changes, so clearing the cache should not be necessary.

## Entity delta lists are empty on zone-change frames

`EntitiesAddedThisFrame` and `EntitiesRemovedThisFrame` are both empty on the frame a zone transition is detected because the area dictionary is bulk-cleared rather than drained one entity at a time.

Use `RemoteEvents.AreaChanged` to reset per-area caches.

## `*Required` reads on hot paths

`ReadMemoryRequired<T>` and `ReadMemoryArrayRequired<T>` throw `MemoryReadException` on failure. A torn frame during a parallel entity loop can become an `AggregateException` and destabilize the host.

Use `ReadMemory<T>` and `ReadMemoryArray<T>` in `DrawUI`, entity loops, coroutines, and other hot paths. Reserve required reads for one-shot startup checks where failure genuinely means an offset or layout is wrong.

## Atlas map nodes require an opt-in lease

`GameUi.AtlasMapsNodesUiElements` and `GameUi.AtlasMapConnections` are empty by default. The host skips the per-frame atlas node enumeration unless at least one plugin holds a lease.

Call `ImportantUiElements.RequestAtlasMapNodes()` in `OnEnable` and dispose the returned `IDisposable` in `OnDisable`. Forgetting to dispose leaves the reference count permanently incremented, so nodes are computed every frame even after the plugin is disabled.

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
