namespace OriathHub.Plugins.SampleStashPricing
{
    using Coroutine;
    using ImGuiNET;
    using OriathHub.CoroutineEvents;
    using OriathHub.Plugin;
    using OriathHub.Pricing;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteObjects.States.InGameStateObjects;
    using OriathHub.RemoteObjects.UiElement;
    using System;
    using System.Collections.Generic;
    using System.Numerics;

    /// <summary>
    ///     Demonstrates semantic inventory entries, visible-stash occupancy, and shared item prices.
    /// </summary>
    public sealed class SampleStashPricingPlugin : PluginBase
    {
        private const string League = "Standard";
        private IDisposable? priceLease;
        private IDisposable? visibleStashLease;
        private ActiveCoroutine? refreshCoroutine;
        private IReadOnlyList<PricedCell> pricedCells = Array.Empty<PricedCell>();

        /// <inheritdoc />
        public override string Name => "Sample: Stash Pricing";

        /// <inheritdoc />
        public override string Description => "Shows shared prices over occupied cells in the visible stash page.";

        /// <inheritdoc />
        public override string Author => "OriathHub";

        /// <inheritdoc />
        public override string Version => "1.0.0";

        /// <inheritdoc />
        public override void OnEnable(bool isGameOpened)
        {
            this.priceLease = Core.Prices.Acquire(League);
            this.visibleStashLease = ImportantUiElements.RequestVisibleStashItems();
            this.refreshCoroutine = CoroutineHandler.Start(this.RefreshPrices(), "SampleStashPricing.Refresh");
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
            this.refreshCoroutine?.Cancel();
            this.refreshCoroutine = null;
            this.visibleStashLease?.Dispose();
            this.visibleStashLease = null;
            this.priceLease?.Dispose();
            this.priceLease = null;
            this.pricedCells = Array.Empty<PricedCell>();
        }

        /// <inheritdoc />
        public override void DrawSettings()
        {
            if (ImGui.Button("Refresh shared prices"))
            {
                Core.Prices.RequestRefresh(League);
            }
        }

        /// <inheritdoc />
        public override void DrawUI()
        {
            var status = Core.Prices.GetStatus(League);
            if (ImGui.Begin("Sample — Stash Pricing"))
            {
                ImGui.Text(status.HasData
                    ? $"Catalogue updated: {status.UpdatedUtc:u}"
                    : status.IsLoading ? "Catalogue loading…" : "No catalogue loaded");
                if (!string.IsNullOrEmpty(status.Error))
                {
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), status.Error);
                }

                ImGui.Text($"Priced visible items: {this.pricedCells.Count}");
            }

            ImGui.End();

            var draw = ImGui.GetBackgroundDrawList();
            foreach (var cell in this.pricedCells)
            {
                // Label sits just ABOVE the cell, on a black box, so it doesn't cover the item icon
                // and stays readable over bright art.
                var p0 = cell.Element.Position;
                var label = $"{cell.Price.ExaltedValue:0.##} ex";
                var textSize = ImGui.CalcTextSize(label);
                var labelTop = p0.Y - textSize.Y - 2f;
                draw.AddRectFilled(new Vector2(p0.X, labelTop), new Vector2(p0.X + textSize.X + 4f, p0.Y), 0x99000000);
                draw.AddText(new Vector2(p0.X + 2f, labelTop + 1f), 0xFFFFFFFF, label);
            }
        }

        /// <inheritdoc />
        public override void SaveSettings()
        {
        }

        private IEnumerator<Wait> RefreshPrices()
        {
            while (true)
            {
                yield return new Wait(OriathEvents.PostPerFrameDataUpdate);
                if (Core.States.GameCurrentState != GameStateTypes.InGameState)
                {
                    this.pricedCells = Array.Empty<PricedCell>();
                    continue;
                }

                var cells = Core.States.InGameStateObject.GameUi.VisibleStashItems;
                var priced = new List<PricedCell>(cells.Count);
                foreach (var cell in cells)
                {
                    if (Core.Prices.TryGetPrice(cell.Entry.Item, League, out var price))
                    {
                        priced.Add(new PricedCell(cell.Element, price));
                    }
                }

                this.pricedCells = priced;
            }
        }

        private readonly record struct PricedCell(UiElementBase Element, ItemPrice Price);
    }
}
