# Getting started: building an OriathHub plugin

An OriathHub plugin is a .NET class library loaded from the `Plugins` folder next to the running `OriathHub.exe`. Build it against the `OriathHub.Sdk` NuGet package; do not reference the OriathHub source projects from external plugins.

## Prerequisites

- .NET 10 SDK.
- x64 build output.
- The distributed `OriathHub.Sdk.<version>.nupkg` file.
- An OriathHub host build or install for runtime testing.

Put the `.nupkg` file you were given in a local folder and register that folder as a NuGet source:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="oriathhub" value="C:\oriathhub-sdk" />
  </packageSources>
</configuration>
```

NuGet resolves `OriathHub.Sdk` from that folder when your plugin project references the package. Use the package version distributed with the OriathHub build you are targeting.

## 1. Create the project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <!-- net10.0-windows matches the host; host APIs are [SupportedOSPlatform("windows")], so a
         plain net10.0 plugin raises CA1416 on every host call. -->
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>1701;1702;1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OriathHub.Sdk" Version="0.7.2" />
  </ItemGroup>

  <PropertyGroup>
    <!-- Folder that holds the running OriathHub.exe. Set it to your install so the build
         auto-deploys the plugin. Left unset/non-existent, the deploy step is skipped and the
         built DLL just stays in bin\ for you to copy manually. -->
    <OriathHubDir Condition="'$(OriathHubDir)' == ''">C:\Games\OriathHub</OriathHubDir>
  </PropertyGroup>

  <!-- Deploy only your plugin DLL to the host output folder. The folder is named after the
       assembly because the loader looks for Plugins/<FolderName>/<FolderName>*.dll. Skipped when
       OriathHubDir does not exist, so a build without an install still succeeds. -->
  <Target Name="CopyToHostPluginsDir" AfterTargets="Build" Condition="Exists('$(OriathHubDir)')">
    <Copy SourceFiles="$(OutDir)$(TargetName)$(TargetExt)"
          DestinationFolder="$(OriathHubDir)\Plugins\$(AssemblyName)"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

Set `OriathHubDir` to the folder that contains the `OriathHub.exe` you run — either in the project as above, or per build: `dotnet build MyPlugin.csproj -p:OriathHubDir="C:\Games\OriathHub"`. The `Exists` condition means a build without a matching install simply skips the copy instead of failing.

The loader looks for `Plugins/<FolderName>/<FolderName>*.dll`. Your folder name and assembly name do not have to be identical, but the DLL file name must start with the folder name.

## 2. Write the plugin class

Derive exactly one `sealed` class from `PluginBase`:

```csharp
namespace MyCompany.MyPlugin
{
    using ImGuiNET;
    using OriathHub;
    using OriathHub.Plugin;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteObjects.Components;
    using OriathHub.Utils;

    public sealed class MyPlugin : PluginBase
    {
        public override string Name => "My Plugin";
        public override string Description => "Shows a small live status window.";
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
                ImGui.Text($"State: {Core.States.GameCurrentState}");

                if (Core.States.GameCurrentState == GameStateTypes.InGameState)
                {
                    var area = Core.States.InGameStateObject.CurrentAreaInstance;
                    ImGui.Text($"Awake entities: {area.AwakeEntities.Count}");

                    if (area.Player.TryGetComponent<Life>(out var life))
                    {
                        ImGui.Text($"Player HP: {life.Health.Current} / {life.Health.Total}");
                    }
                }
                else
                {
                    ImGui.TextDisabled("Enter a game area to see live entity data.");
                }
            }

            ImGui.End();
        }

        public override void SaveSettings()
        {
        }
    }
}
```

If the DLL contains zero or more than one sealed `PluginBase` subclass, the loader logs a warning and skips that DLL.

## 3. Build and install

1. Build your plugin:

   ```powershell
   dotnet build PATH\TO\MyPlugin.csproj -c Debug
   ```

2. Confirm the DLL landed under your install's `Plugins` folder:

   ```text
   <OriathHubDir>/Plugins/MyPlugin/MyPlugin.dll
   ```

3. Launch OriathHub, open the Plugins tab, and enable your plugin.

During development, rebuild the plugin, expand it in the Plugins tab, and click **Reload from disk**. OriathHub loads plugin DLLs from an in-memory byte copy, so the file on disk is not locked while the host is running.

## 4. Add assets or extra dependencies only when needed

The SDK references `OriathHub`, `GameOffsets`, ImGui, Coroutine, Newtonsoft.Json, and ImageSharp for compilation. Do not copy those assemblies beside your plugin.

If your plugin has assets, copy them into the same plugin folder and load them through `DllDirectory`:

```xml
<Target Name="CopyAssetsToHost" AfterTargets="Build" Condition="Exists('$(OriathHubDir)')">
  <ItemGroup>
    <PluginAssets Include="textures\*.*" />
  </ItemGroup>
  <Copy SourceFiles="@(PluginAssets)"
        DestinationFolder="$(OriathHubDir)\Plugins\$(AssemblyName)\textures"
        SkipUnchangedFiles="true" />
</Target>
```

If your plugin references a NuGet package that OriathHub does not already ship, set `<EnableDynamicLoading>true</EnableDynamicLoading>` and copy that package's runtime DLLs beside your plugin DLL.

## Next steps

- [Plugin lifecycle](plugin-lifecycle.md): when each method is called, how settings are saved, and how to clean up for reloads.
- [Plugin examples](examples.md): small examples for drawing, settings, game-state reads, events, textures, and raw memory reads.
- [API reference](api-overview.md): the public data surface exposed by the host.
- [Gotchas](gotchas.md): discovery rules, shared assemblies, extra dependencies, and reload caveats.
