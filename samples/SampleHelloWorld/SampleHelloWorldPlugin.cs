namespace OriathHub.Plugins.SampleHelloWorld
{
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using ImGuiNET;
    using OriathHub.CoroutineEvents;
    using OriathHub.Plugin;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteObjects.Components;
    using OriathHub.Utils;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;

    /// <summary>
    ///     The smallest useful OriathHub plugin: a draggable info window that reports the current
    ///     game state, the number of awake entities, and the local player's health — read live from
    ///     game memory through the host API.
    ///
    ///     Use it as a starting point: copy this folder, rename the namespace/class/csproj, and
    ///     replace <see cref="DrawUI"/> with your own logic.
    /// </summary>
    public sealed class SampleHelloWorldPlugin : PluginBase
    {
        private SampleHelloWorldSettings settings = new();
        private ActiveCoroutine? areaChangeCoroutine;

        // Settings live next to the plugin DLL. DllDirectory is set by the loader before OnEnable.
        private FileInfo SettingsFile => new(Path.Combine(DllDirectory, "config", "settings.json"));

        /// <inheritdoc/>
        public override string Name => "Sample: Hello World";

        /// <inheritdoc/>
        public override string Description => "Reference plugin — shows game state, entity count, and player health.";

        /// <inheritdoc/>
        public override string Author => "OriathHub";

        /// <inheritdoc/>
        public override string Version => "1.0.0";

        /// <inheritdoc/>
        public override void OnEnable(bool isGameOpened)
        {
            settings = JsonHelper.CreateOrLoadJsonFile<SampleHelloWorldSettings>(SettingsFile);
            Log.Info("enabled", this.Name);
            areaChangeCoroutine = CoroutineHandler.Start(LogAreaChanges(), "SampleHelloWorld.AreaChange");
        }

        /// <inheritdoc/>
        public override void OnDisable()
        {
            areaChangeCoroutine?.Cancel();
            areaChangeCoroutine = null;
        }

        private IEnumerator<Wait> LogAreaChanges()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                Log.Info("area changed", this.Name);
            }
        }

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            ImGui.Checkbox("Show info window", ref settings.Show);
            ImGui.ColorEdit4("Text colour", ref settings.TextColor);
        }

        /// <inheritdoc/>
        public override void DrawUI()
        {
            if (Utils.IsKeyPressedAndNotTimeout(VK.F5))
            {
                settings.Show = !settings.Show;
            }

            if (!settings.Show)
            {
                return;
            }

            ImGui.SetNextWindowBgAlpha(0.6f);
            if (ImGui.Begin("Hello World"))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, settings.TextColor);
                ImGui.Text($"State: {Core.States.GameCurrentState}");
                ImGui.TextDisabled($"Game attached: {Core.Process.Pid != 0}   (F5 toggles this window)");

                if (Core.States.GameCurrentState == GameStateTypes.InGameState)
                {
                    var area = Core.States.InGameStateObject.CurrentAreaInstance;
                    ImGui.Separator();
                    ImGui.Text($"Awake entities: {area.AwakeEntities.Count}");

                    if (area.Player.TryGetComponent<Life>(out var life))
                    {
                        ImGui.Text($"Player HP: {life.Health.Current} ({life.Health.CurrentInPercent():0}%)");
                    }

                    Core.Process.ReadMemory<long>(area.Player.Address, out var firstQword);
                    ImGui.Text($"Player base[0]: 0x{firstQword:X}");
                }
                else
                {
                    ImGui.TextDisabled("Enter a game area to see entity and player data.");
                }

                ImGui.PopStyleColor();
            }

            ImGui.End();
        }

        /// <inheritdoc/>
        public override void SaveSettings()
        {
            JsonHelper.SaveToFile(settings, SettingsFile);
        }
    }
}
