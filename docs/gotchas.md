# Gotchas

Common mistakes that bite plugin authors.

## Shared assemblies

The host loads each plugin into a collectible `PluginAssemblyLoadContext`. The loader forwards `OriathHub` and `GameOffsets` to the host's already-loaded copies, so a `Core.States` reference in your plugin is the same object the host uses.

Consequences:

- Do not ship `OriathHub.dll` or `GameOffsets.dll` next to your plugin DLL. The SDK provides them as compile-time reference assemblies.
- Do not copy the shared SDK dependencies (`ImGuiNET`, `ClickableTransparentOverlay`, `Coroutine`, `Newtonsoft.Json`, `SixLabors.ImageSharp`) unless you have a specific versioning problem and know how it will resolve at runtime.
- Treat `Core.OHSettings` as host-owned. Read it when needed, but store plugin settings in your own file under `DllDirectory`.

## Trust model — plugins run with full access

There is no sandbox. A plugin loads into the host process as ordinary full-trust .NET code, so it can do anything the process can: read and write any file, open network connections, start other processes, call native APIs, and open the game process with **write** access. The SDK reference assemblies only narrow what you can reference *at compile time*; at runtime your plugin binds to the host's real, full `OriathHub.dll`, and nothing — including the host's `internal` types and the licensing code — is actually unreachable (reflection ignores accessibility in full trust).

What this means in practice:

- The host's "external, read-only" guarantee covers the host's own behavior. It does **not** extend to plugins. If a plugin writes to or injects into the game, that is on the plugin and carries the same account-ban risk the host avoids.
- Installing a plugin is equivalent to running an unsigned program from its author. Users should only install plugins from authors they trust, from source they can inspect.
- Do not treat the compile-time reference surface as a security boundary in your own design.

Author responsibly: keep your plugin to reads through the documented `Core` facade, and never open the game with write access.

## SDK version stamps

The SDK package stamps consuming plugin assemblies with `AssemblyMetadata("OriathHubSdkVersion", "...")`. `PluginManager` logs the plugin SDK version and the host SDK version when the plugin loads.

The loader warns when the major version differs or when a plugin was built against a newer SDK than the host. The plugin may still load, but it can reference APIs the running host does not provide. Rebuild against the SDK version that matches the host before investigating runtime bugs.

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

## Multi-project plugins (a shared library alongside the plugin)

A plugin can be more than one project — e.g. a thin `MyPlugin.csproj` plus a `MyPlugin.Common.csproj` shared library. This is fully supported:

- **At runtime**, the loader picks the main `Plugins/<FolderName>/<FolderName>*.dll` (the one with your `sealed PluginBase`) and resolves any other DLLs in that same folder as dependencies. So shipping a sidecar like `MyPlugin.Common.dll` beside your plugin DLL just works.
- **In the Marketplace**, a source repo with several `.csproj` files builds fine. The Marketplace selects the project whose file name matches the repo/folder name as the one to build (and shows a picker so a user can override it); that project's `ProjectReference`s are compiled in as usual. There is no "single project" requirement.

The only thing you must get right is **deployment** — your build has to land every DLL the plugin needs in the host folder, and **only** those. The default copy target ships just your own `<TargetName>.dll`, so a separate sidecar would be left behind and the plugin would fail to load at runtime. Pick one of two patterns:

**A. Merge into a single DLL (simplest).** Use [ILRepack](https://github.com/gluck/il-repack) (`ILRepack.Lib.MSBuild.Task`) to fold your library project(s) into the plugin DLL at build time. There is then exactly one DLL to deploy and nothing else to manage. This is what the `AutoLoot` plugin does. Add the package reference and a merge target that runs after build but **before** your deploy target, so the copy step ships the already-merged DLL:

```xml
<ItemGroup>
  <PackageReference Include="ILRepack.Lib.MSBuild.Task" Version="2.0.34" />
</ItemGroup>

<!-- Fold MyPlugin.Common.dll into the plugin DLL, then drop the now-redundant standalone copy. -->
<Target Name="ILRepackPlugin" AfterTargets="Build" BeforeTargets="CopyToHostPluginsDir">
  <PropertyGroup>
    <_CommonDll>$(OutputPath)MyPlugin.Common.dll</_CommonDll>
  </PropertyGroup>
  <ItemGroup Condition="Exists('$(_CommonDll)')">
    <_RepackInput Include="$(TargetPath)" />   <!-- your plugin DLL -->
    <_RepackInput Include="$(_CommonDll)" />   <!-- the library to fold in -->
    <_RepackLibDirs Include="$(OutputPath)" />
    <!-- Let ILRepack *resolve* (not merge) any host-shared package the library references.
         Add one line per such package — here, Newtonsoft.Json: -->
    <_RepackLibDirs Include="@(ReferencePathWithRefAssemblies)" Condition="'%(Filename)' == 'Newtonsoft.Json'" />
  </ItemGroup>
  <ILRepack Condition="Exists('$(_CommonDll)')"
            Parallel="true"
            Internalize="true"
            InputAssemblies="@(_RepackInput)"
            OutputFile="$(TargetPath)"
            TargetKind="Dll"
            LibraryPath="@(_RepackLibDirs->'%(RootDir)%(Directory)')" />
  <Delete Condition="Exists('$(_CommonDll)')" Files="$(_CommonDll)" />
  <Delete Condition="Exists('$(_CommonDll).pdb')" Files="$(_CommonDll).pdb" />
</Target>
```

`Internalize="true"` keeps the merged library's types private to your plugin so they can't collide with another plugin's. With this in place there is a single `MyPlugin.dll` to deploy, so the default copy target needs no changes.

**B. Copy your private DLLs explicitly.** Keep them separate and list each one in your deploy target, exactly as for a [third-party dependency](#adding-a-third-party-dependency):

```xml
<Target Name="CopyToHostPluginsDir" AfterTargets="Build" Condition="Exists('$(OriathHubDir)')">
  <ItemGroup>
    <PluginFiles Include="$(OutDir)$(TargetName)$(TargetExt)" />
    <PluginFiles Include="$(OutDir)MyPlugin.Common.dll" />
  </ItemGroup>
  <Copy SourceFiles="@(PluginFiles)"
        DestinationFolder="$(OriathHubDir)\Plugins\$(AssemblyName)"
        SkipUnchangedFiles="true" />
</Target>
```

**Never deploy a host-shared assembly this way.** List only your *own* private DLLs. Copying `OriathHub.dll`, `GameOffsets.dll`, `ImGuiNET`, `Coroutine`, `Newtonsoft.Json`, or `SixLabors.ImageSharp` into your plugin folder makes the loader bind a *second* copy from that folder, and your plugin's types stop being identical to the host's — see [Shared assemblies](#shared-assemblies). Pattern A sidesteps this because ILRepack merges only the assemblies you point it at.

For a release-ZIP plugin, the same rule applies to the zip: include your main DLL plus your private sidecars, and no host-shared assemblies.

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

They are also pure per-frame deltas: on their own they never report entities that were already alive before your plugin started observing — present in the spawn bubble at zone-in, or already awake when the plugin is enabled or reloaded mid-area. Seed that initial set from `AreaInstance.GetAwakeEntitiesSnapshot()` once (in `OnEnable` and on `RemoteEvents.AreaChanged`), then follow later arrivals through `EntitiesAddedThisFrame`.

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
