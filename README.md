# OriathHub.Sdk

SDK for building OriathHub plugins. Reference this single package to compile a plugin without referencing the OriathHub source tree.

The package provides compile-time reference assemblies for:

- `OriathHub.dll`: host API, including `Core`, `RemoteObjects`, `Components`, `States`, `RemoteEnums`, `Utils`, and `PluginBase`.
- `GameOffsets.dll`: public native container layouts under `GameOffsets.Natives.*`, such as `StdVector`, `StdMap`, and `StdWString`.

The game-specific offset structs under `GameOffsets.Objects.*` are not part of the SDK package. Read game data through the host wrappers first; define your own `[StructLayout]` types only when a plugin needs a raw memory read that the host does not wrap yet.

The package also declares the third-party libraries plugins commonly compile against: `ClickableTransparentOverlay`/ImGui.NET, `Coroutine`, `Newtonsoft.Json`, and `SixLabors.ImageSharp`.

At runtime the host already has the SDK assemblies loaded. Ship your plugin DLL, plugin assets, and any extra non-SDK dependencies only.

## Account risk

Reading another process's memory for an online game can carry account risk under the game's Terms of Service. OriathHub is read-only by design: plugins must not write to game memory, inject code, or otherwise modify the game process.

## Quick start

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <!-- net10.0-windows matches the host and avoids CA1416 on host API calls. -->
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OriathHub.Sdk" Version="0.4.5" />
  </ItemGroup>
</Project>
```

```csharp
namespace MyCompany.MyPlugin
{
    using ImGuiNET;
    using OriathHub;
    using OriathHub.Plugin;

    public sealed class MyPlugin : PluginBase
    {
        public override string Name => "My Plugin";
        public override string Description => "Shows a small status window.";

        public override void OnEnable(bool isGameOpened)
        {
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
                ImGui.Text($"Game attached: {Core.Process.Pid != 0}");
            }

            ImGui.End();
        }

        public override void SaveSettings()
        {
        }
    }
}
```

Build the plugin and copy the resulting DLL to the host's runtime plugin folder (the folder containing `OriathHub.exe`):

```text
<OriathHubDir>/Plugins/MyPlugin/MyPlugin.dll
```

The loader scans `Plugins/<FolderName>/<FolderName>*.dll` next to the running `OriathHub.exe`.

See `Sdk/docs/` for the full guide and `Sdk/docs/examples.md` for small examples covering settings, drawing, events, textures, hotkeys, and raw memory reads.
