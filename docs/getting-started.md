# Getting started — building an OriathHub plugin

An OriathHub plugin is a single .NET class library. The host discovers it at startup, loads it, and calls into it every frame. You build it against the **`OriathHub.Sdk`** NuGet package — no source tree required.

## Prerequisites

- **.NET 10 SDK, x64.**
- The **`OriathHub.Sdk`** package, supplied to you as `OriathHub.Sdk.0.3.0.nupkg`. You don't build it — just install the file you were given:

  1. Save `OriathHub.Sdk.0.3.0.nupkg` into a folder, e.g. `C:\oriathhub-sdk\`.
  2. Point NuGet at that folder by adding a `nuget.config` next to your plugin project (or solution):

     ```xml
     <?xml version="1.0" encoding="utf-8"?>
     <configuration>
       <packageSources>
         <add key="oriathhub" value="C:\oriathhub-sdk" />
       </packageSources>
     </configuration>
     ```

     (Or, equivalently, run `dotnet nuget add source C:\oriathhub-sdk --name oriathhub`.)

  Then reference the package from your csproj — see [Create the project](#1-create-the-project) below. NuGet resolves `OriathHub.Sdk` 0.3.0 from the folder you registered.

---

## 1. Create the project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
    <NoWarn>1701;1702;1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OriathHub.Sdk" Version="0.3.0" />
  </ItemGroup>

  <!-- Deploy your plugin DLL to the host after every build. -->
  <Target Name="CopyToHostPluginsDir" AfterTargets="Build">
    <Copy SourceFiles="$(OutDir)$(TargetName)$(TargetExt)"
          DestinationFolder="PATH\TO\OriathHub\Plugins\$(ProjectName)"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

> **Naming rule:** The loader looks for `Plugins/<FolderName>/<FolderName>*.dll`. Your assembly name must start with the folder name, or the plugin will not be found.

---

## 2. Write the plugin class

Derive exactly one `sealed` class from `PluginBase`:

```csharp
namespace MyCompany.MyPlugin
{
    using ImGuiNET;
    using OriathHub.Plugin;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteObjects.Components;
    using OriathHub.Utils;
    using System.IO;

    public sealed class MyPlugin : PluginBase
    {
        public override string Name        => "My Plugin";
        public override string Description => "Does something useful.";
        public override string Author      => "You";
        public override string Version     => "1.0.0";

        public override void OnEnable(bool isGameOpened)
        {
            Log.Info("enabled", this.Name);
        }

        public override void OnDisable()
        {
            // Cancel coroutines and release textures here.
        }

        public override void DrawSettings()
        {
            // Render ImGui controls for your settings.
        }

        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
                return;

            var area = Core.States.InGameStateObject.CurrentAreaInstance;

            if (ImGui.Begin("My Plugin"))
            {
                ImGui.Text($"Entities: {area.AwakeEntities.Count}");

                if (area.Player.TryGetComponent<Life>(out var life))
                    ImGui.Text($"HP: {life.Health.Current} / {life.Health.Total}");
            }
            ImGui.End();
        }

        public override void SaveSettings() { }
    }
}
```

The loader requires **exactly one** `sealed PluginBase` subclass per DLL. Zero or more than one and the plugin is skipped.

---

## 3. Build and install

1. `dotnet build -c Debug` — the `CopyToHostPluginsDir` target deploys the DLL automatically.
2. Launch OriathHub, open the **Plugins** tab, and enable your plugin. The enabled state is remembered in `configs/PluginsMetadata.json`.
3. After a rebuild, expand the plugin in the Plugins tab and click **Reload from disk** to hot-reload the new code without restarting OriathHub.

---

## Next steps

- [Plugin lifecycle](plugin-lifecycle.md) — when each method is called, settings, and coroutines.
- [API reference](api-overview.md) — every game-data property and method available to a plugin.
- [Gotchas](gotchas.md) — shared assembly rules, resource cleanup, and reload caveats.
