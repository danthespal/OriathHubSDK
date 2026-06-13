# Plugin examples

These examples are intentionally small. Copy the shape, then add only the state your plugin actually needs.

All examples assume your plugin class derives from `OriathHub.Plugin.PluginBase` and that you have these common imports:

```csharp
namespace MyCompany.MyPlugin
{
    using ImGuiNET;
    using OriathHub;
    using OriathHub.Plugin;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteEnums.Entity;
    using OriathHub.RemoteObjects.Components;
    using OriathHub.Utils;
    using System;
    using System.IO;
    using System.Numerics;
}
```

## Minimal plugin shell

Use this to confirm discovery, enable/disable, and rendering before adding game reads.

```csharp
public sealed class MyPlugin : PluginBase
{
    public override string Name => "My Plugin";
    public override string Description => "Minimal plugin shell.";
    public override string Author => "You";
    public override string Version => "1.0.0";

    public override void OnEnable(bool isGameOpened)
    {
        Log.Info($"enabled; game attached: {isGameOpened}", Name);
    }

    public override void OnDisable()
    {
    }

    public override void DrawSettings()
    {
    }

    public override void DrawUI()
    {
        if (ImGui.Begin("My Plugin"))
        {
            ImGui.Text($"OriathHub {Core.GetVersion()}");
            ImGui.Text($"Game attached: {Core.Process.Pid != 0}");
        }

        ImGui.End();
    }

    public override void SaveSettings()
    {
    }
}
```

## Settings stored next to the plugin

`DllDirectory` is set by the loader before `OnEnable`. Build all config and asset paths from it so reloads and copied plugin folders keep working.

```csharp
public sealed class MySettings
{
    public bool ShowWindow = true;
    public Vector4 TextColor = new(1f, 1f, 1f, 1f);
}

private MySettings settings = new();
private FileInfo SettingsFile => new(Path.Combine(DllDirectory, "config", "settings.json"));

public override void OnEnable(bool isGameOpened)
{
    settings = JsonHelper.CreateOrLoadJsonFile<MySettings>(SettingsFile);
}

public override void DrawSettings()
{
    ImGui.Checkbox("Show window", ref settings.ShowWindow);
    ImGui.ColorEdit4("Text color", ref settings.TextColor);
}

public override void SaveSettings()
{
    JsonHelper.SaveToFile(settings, SettingsFile);
}
```

## Guard game-state reads

Most game data only exists while the current state is `InGameState`. Bail early before reading the player, entities, world data, or UI panels.

```csharp
public override void DrawUI()
{
    if (Core.States.GameCurrentState != GameStateTypes.InGameState)
    {
        return;
    }

    var area = Core.States.InGameStateObject.CurrentAreaInstance;

    if (ImGui.Begin("Area summary"))
    {
        ImGui.Text($"Area level: {area.CurrentAreaLevel}");
        ImGui.Text($"Awake entities: {area.AwakeEntities.Count}");

        if (area.Player.TryGetComponent<Life>(out var life))
        {
            ImGui.Text($"HP: {life.Health.Current} / {life.Health.Total}");
        }
    }

    ImGui.End();
}
```

## Draw labels over rare monsters

Use `Render` for position, `ObjectMagicProperties` for rarity, and `WorldToScreen` for screen projection.

```csharp
public override void DrawUI()
{
    if (Core.States.GameCurrentState != GameStateTypes.InGameState)
    {
        return;
    }

    var inGame = Core.States.InGameStateObject;
    if (inGame.GameUi.IsAnyLargePanelOpen)
    {
        return;
    }

    var area = inGame.CurrentAreaInstance;
    var world = inGame.CurrentWorldInstance;
    var draw = ImGui.GetBackgroundDrawList();

    foreach (var entity in area.AwakeEntities.Values)
    {
        if (!entity.IsValid || entity.EntityType != EntityTypes.Monster)
        {
            continue;
        }

        if (!entity.TryGetComponent<ObjectMagicProperties>(out var magic) ||
            magic.Rarity != Rarity.Rare)
        {
            continue;
        }

        if (!entity.TryGetComponent<Render>(out var render))
        {
            continue;
        }

        var screen = world.WorldToScreen(render.WorldPosition);
        draw.AddText(screen, ImGuiHelper.Color(255, 220, 80, 255), "Rare");
    }
}
```

## Keep visual overlays visible while settings are focused

Use `FocusHelper.IsGameOrOverlayForeground()` for visual overlays that users may tune live from OriathHub settings. This keeps the overlay visible when the game is behind the settings window. Use `Core.Process.Foreground` instead for hotkeys and automation logic that should only run while the game itself is focused.

```csharp
public override void DrawUI()
{
    if (!FocusHelper.IsGameOrOverlayForeground())
    {
        return;
    }

    if (Core.States.GameCurrentState != GameStateTypes.InGameState)
    {
        return;
    }

    var draw = ImGui.GetBackgroundDrawList();
    draw.AddCircleFilled(new Vector2(200, 200), settings.MarkerRadius, ImGuiHelper.Color(0, 255, 0, 180));
}
```

## React to area changes

Coroutines run on the render thread. Keep the returned `ActiveCoroutine`, cancel it in `OnDisable`, and avoid blocking work inside the coroutine.

```csharp
using Coroutine;
using OriathHub.CoroutineEvents;
using System.Collections.Generic;

private readonly HashSet<uint> seenEntities = new();
private ActiveCoroutine? areaCoroutine;

public override void OnEnable(bool isGameOpened)
{
    areaCoroutine = CoroutineHandler.Start(OnAreaChanged(), "MyPlugin.AreaChanged");
}

public override void OnDisable()
{
    areaCoroutine?.Cancel();
    areaCoroutine = null;
    seenEntities.Clear();
}

private IEnumerator<Wait> OnAreaChanged()
{
    while (true)
    {
        yield return new Wait(RemoteEvents.AreaChanged);
        seenEntities.Clear();
        Log.Info("cleared area cache", Name);
    }
}
```

## Read preloaded area files

Use `HybridEvents.PreloadsUpdated` when you need `Core.CurrentAreaLoadedFiles` to be current for the new area.

```csharp
using Coroutine;
using OriathHub.CoroutineEvents;
using System;
using System.Linq;

private ActiveCoroutine? preloadsCoroutine;
private bool hasBreachPreload;

public override void OnEnable(bool isGameOpened)
{
    preloadsCoroutine = CoroutineHandler.Start(OnPreloadsUpdated(), "MyPlugin.Preloads");
}

public override void OnDisable()
{
    preloadsCoroutine?.Cancel();
    preloadsCoroutine = null;
}

private IEnumerator<Wait> OnPreloadsUpdated()
{
    while (true)
    {
        yield return new Wait(HybridEvents.PreloadsUpdated);

        hasBreachPreload = Core.CurrentAreaLoadedFiles.PathNames.Keys
            .Any(path => path.Contains("Breach", StringComparison.OrdinalIgnoreCase));
    }
}
```

## Add a foreground hotkey

Global hotkeys are useful for plugin toggles. Avoid sending keys unless your plugin is explicitly an automation plugin.

```csharp
using ClickableTransparentOverlay.Win32;
using OriathHub.Utils;

private bool f5WasDown;

public override void DrawUI()
{
    if (Core.Process.Foreground && HotkeyHelper.IsPressedOnce(VK.F5, ref f5WasDown))
    {
        settings.ShowWindow = !settings.ShowWindow;
    }

    if (!settings.ShowWindow)
    {
        return;
    }

    // draw your UI
}
```

## Load and free a texture

Put image files under your plugin folder, for example `Plugins/MyPlugin/textures/icon.png`, and load them through the overlay.

```csharp
private string IconPath => Path.Combine(DllDirectory, "textures", "icon.png");
private IntPtr icon;
private uint iconWidth;
private uint iconHeight;

public override void OnEnable(bool isGameOpened)
{
    Core.Overlay.AddOrGetImagePointer(IconPath, false, out icon, out iconWidth, out iconHeight);
}

public override void OnDisable()
{
    Core.Overlay.RemoveImage(IconPath);
    icon = IntPtr.Zero;
}

public override void DrawUI()
{
    if (icon == IntPtr.Zero)
    {
        return;
    }

    if (ImGui.Begin("Icon"))
    {
        ImGui.Image(icon, new Vector2(iconWidth, iconHeight));
    }

    ImGui.End();
}
```

## Read raw memory for unsupported data

Prefer host wrappers first. When a wrapper does not expose the field you need, read through `Core.Process` and keep failure paths cheap.

```csharp
using GameOffsets.Natives;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
private struct CustomData
{
    [FieldOffset(0x10)] public StdVector Values;
    [FieldOffset(0x28)] public StdWString Name;
}

private static void TryReadCustomData(IntPtr address)
{
    if (!Core.Process.ReadMemory<CustomData>(address, out var raw))
    {
        return;
    }

    var values = Core.Process.ReadStdVector<int>(raw.Values);
    var name = Core.Process.ReadStdWString(raw.Name);

    Log.Info($"{name}: {values.Length} values", "MyPlugin");
}
```

Use `ReadMemoryRequired<T>` only for one-shot startup reads where failure means the offset is wrong. Do not use required reads in `DrawUI`, entity loops, or parallel code.

## Copy plugin assets and dependencies

The loader finds `Plugins/<FolderName>/<FolderName>*.dll` next to the running `OriathHub.exe`. If your plugin has assets or non-SDK dependencies, copy them beside your plugin DLL. This reuses the `OriathHubDir` property from [getting-started](getting-started.md) — set it to your install folder.

```xml
<Target Name="CopyToHostPluginsDir" AfterTargets="Build" Condition="Exists('$(OriathHubDir)')">
  <ItemGroup>
    <PluginFiles Include="$(OutDir)$(TargetName)$(TargetExt)" />
    <PluginFiles Include="$(OutDir)MyExtraDependency.dll" />
    <PluginFiles Include="textures\icon.png" />
  </ItemGroup>
  <Copy SourceFiles="@(PluginFiles)"
        DestinationFolder="$(OriathHubDir)\Plugins\$(AssemblyName)"
        SkipUnchangedFiles="true" />
</Target>
```

If you add a NuGet dependency that is not already part of OriathHub, set `<EnableDynamicLoading>true</EnableDynamicLoading>` in your plugin project so runtime assemblies are copied to your output, then include those DLLs in the copy target.
