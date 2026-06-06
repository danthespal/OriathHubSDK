# OriathHub.Sdk

SDK for building [OriathHub](https://github.com/) plugins. Reference this single package and you
get everything needed to compile a plugin — no OriathHub source tree required.

The package bundles, as **compile-time reference assemblies**:

- `OriathHub.dll` (+ XML docs) — the host API: `Core`, `RemoteObjects`, `Components`, `States`,
  `RemoteEnums`, `Utils`, and the `PluginBase` you derive from.
- `GameOffsets.dll` (+ XML docs) — the public **native container layouts** (`GameOffsets.Natives.*`:
  `StdVector`, `StdMap`, `StdWString`, …) that the `Core.Process.ReadStd*` reader methods take. The
  game-specific offset structs (`GameOffsets.Objects.*`) are **internal** and not visible to plugins;
  read game data through the host `RemoteObjects`/`Components` wrappers (or your own struct layouts).

and pulls in the third-party libraries a plugin compiles against (`ImGuiNET` via
`ClickableTransparentOverlay`, `Coroutine`, `Newtonsoft.Json`, `SixLabors.ImageSharp`).

At runtime the host already has these assemblies loaded and forces every plugin to share its
copies, so none of them are copied into your plugin's output — you ship only your own DLL.

## Account Risk

Reading another process's memory for an online game can carry account risk under
the game's Terms of Service. OriathHub is read-only by design: plugins must not
write to game memory, inject code, or otherwise modify the game process.

## Quick start

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OriathHub.Sdk" Version="0.3.0" />
  </ItemGroup>
</Project>
```

```csharp
namespace MyCompany.MyPlugin
{
    using ImGuiNET;
    using OriathHub.Plugin;

    public sealed class MyPlugin : PluginBase
    {
        public override string Name => "My Plugin";
        public override string Description => "Does something useful.";
        public override void OnEnable(bool isGameOpened) { }
        public override void OnDisable() { }
        public override void DrawSettings() { }
        public override void DrawUI() => ImGui.Begin("My Plugin"); // ... ImGui.End();
        public override void SaveSettings() { }
    }
}
```

Build, then drop the resulting `MyPlugin.dll` into `OriathHub/Plugins/MyPlugin/` and enable it from
the Plugins tab.

See the `Sdk/docs/` folder in the OriathHub repo for the full guide.
