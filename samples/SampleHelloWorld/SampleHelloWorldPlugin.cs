namespace OriathHub.Plugins.SampleHelloWorld
{
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using ImGuiNET;
    using OriathHub.CoroutineEvents;
    using OriathHub.Plugin;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteObjects;
    using OriathHub.RemoteObjects.Components;
    using OriathHub.Utils;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using System.Runtime.InteropServices;

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
        private ActiveCoroutine? playerProbeCoroutine;
        private PlayerProbe? playerProbe;
        private bool toggleHotkeyDown;

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
            // StartCoroutine (on PluginBase) ties the coroutine to this plugin's lifetime: the host
            // force-cancels it on disable/reload/unload even if OnDisable is skipped or throws.
            areaChangeCoroutine = StartCoroutine(LogAreaChanges(), "SampleHelloWorld.AreaChange");

            // Reference pattern: a custom memory object the host does not track, refreshed from the
            // PerFrameDataUpdate event so the read happens during the data phase, not while drawing.
            playerProbe = new PlayerProbe();
            playerProbeCoroutine = StartCoroutine(RefreshPlayerProbe(), "SampleHelloWorld.PlayerProbe");
        }

        /// <inheritdoc/>
        public override void OnDisable()
        {
            areaChangeCoroutine?.Cancel();
            areaChangeCoroutine = null;
            playerProbeCoroutine?.Cancel();
            playerProbeCoroutine = null;
            playerProbe = null;
        }

        private IEnumerator<Wait> LogAreaChanges()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                Log.Info("area changed", this.Name);
            }
        }

        /// <summary>
        ///     Drives <see cref="playerProbe"/> from the per-frame data phase. This is the correct
        ///     place to assign an address and trigger a memory read: it runs before drawing, on the
        ///     same schedule the host uses for its own objects, so it never races a render-thread read.
        /// </summary>
        private IEnumerator<Wait> RefreshPlayerProbe()
        {
            while (true)
            {
                yield return new Wait(OriathEvents.PerFrameDataUpdate);

                // Assigning Address drives the object lifecycle: non-zero -> UpdateData, zero ->
                // CleanUpData. The player address is stable across frames, so PlayerProbe is built
                // with forceUpdate so the same address still re-reads the (changing) data each frame.
                playerProbe!.Address = Core.States.GameCurrentState == GameStateTypes.InGameState
                    ? Core.States.InGameStateObject.CurrentAreaInstance.Player.Address
                    : IntPtr.Zero;
            }
        }

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            ImGui.Checkbox("Show info window", ref settings.Show);
            ImGui.ColorEdit4("Text colour", ref settings.TextColor);
        }

        /// <summary>
        ///     Makes this plugin's options findable from the settings window's search box.
        ///     Each entry declares where the option lives (section breadcrumb), its searchable
        ///     label, and a delegate that draws the very same widget the normal settings view uses.
        /// </summary>
        /// <inheritdoc/>
        public override IEnumerable<SettingSearchEntry> GetSearchableSettings() => new[]
        {
            new SettingSearchEntry("Settings", "Show info window",
                () => ImGui.Checkbox("Show info window", ref settings.Show), "visible overlay toggle"),
            new SettingSearchEntry("Settings", "Text colour",
                () => ImGui.ColorEdit4("Text colour", ref settings.TextColor), "color font"),
        };

        /// <inheritdoc/>
        public override void DrawUI()
        {
            if (HotkeyHelper.IsPressedOnce(VK.F5, ref this.toggleHotkeyDown))
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

                    // Already refreshed off the render thread by PlayerProbe (see RefreshPlayerProbe);
                    // here we only read the cached value, so no memory read happens while drawing.
                    ImGui.Text($"Player base[0]: 0x{(playerProbe?.BasePointer ?? 0):X}");
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

        /// <summary>
        ///     The native layout the probe reads. A real plugin maps the exact fields it needs at the
        ///     offsets discovered for its target struct; here we read only the object's first pointer
        ///     field as a stable, offset-free example value.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private struct PlayerProbeNative
        {
            [FieldOffset(0x00)] public long BasePointer;
        }

        /// <summary>
        ///     Minimal example of tracking a memory object the host does not wrap. Derive from
        ///     <see cref="RemoteObjectBase"/>, read in <see cref="UpdateData"/>, reset in
        ///     <see cref="CleanUpData"/>, and let a PerFrameDataUpdate coroutine assign
        ///     <see cref="RemoteObjectBase.Address"/> (see <see cref="RefreshPlayerProbe"/>).
        /// </summary>
        private sealed class PlayerProbe : RemoteObjectBase
        {
            // skipFirstUpdate: nothing to read at construction (no address yet); the coroutine drives it.
            // forceUpdate: re-read every frame even though the player address does not change.
            public PlayerProbe()
                : base(IntPtr.Zero, forceUpdate: true, skipFirstUpdate: true)
            {
            }

            /// <summary>Gets the player object's first pointer field, refreshed once per frame.</summary>
            public long BasePointer { get; private set; }

            /// <inheritdoc/>
            protected override void UpdateData(bool hasAddressChanged)
            {
                if (Core.Process.ReadMemory<PlayerProbeNative>(Address, out var raw))
                {
                    BasePointer = raw.BasePointer;
                }
            }

            /// <inheritdoc/>
            protected override void CleanUpData()
            {
                BasePointer = 0;
            }
        }
    }
}
