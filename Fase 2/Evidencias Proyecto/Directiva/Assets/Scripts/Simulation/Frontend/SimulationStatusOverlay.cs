using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Non-interactive, world-anchored status bars. By default they are compact
    /// and numberless to avoid clutter. The entity under the mouse expands to the
    /// readable full-size view and shows numeric values.
    /// </summary>
    public sealed class SimulationStatusOverlay : IDisposable
    {
        private const float ExpandedBarHeight = 21f;
        private const float ExpandedBarGap = 3f;
        private const float CompactBarHeight = 7f;
        private const float CompactBarGap = 2f;

        private static readonly Color HealthColor = new Color32(74, 195, 102, 255);
        private static readonly Color ShieldColor = new Color32(63, 180, 230, 255);
        private static readonly Color ResourceColor = new Color32(240, 187, 54, 255);
        private static readonly Color HealthBackground = new Color32(34, 49, 40, 225);
        private static readonly Color ShieldBackground = new Color32(31, 52, 66, 225);
        private static readonly Color ResourceBackground = new Color32(63, 54, 27, 225);
        private static readonly Color ConstructionColor = new Color32(207, 214, 223, 255);
        private static readonly Color ConstructionBackground = new Color32(57, 63, 71, 225);
        private static readonly Color BarBorder = new Color32(15, 25, 32, 230);

        private enum YellowBarKind
        {
            None,
            Resource,
            Cargo
        }

        private sealed class Entry
        {
            internal int EntityId;
            internal Transform Anchor;
            internal SimulationStatusValues Values;
            internal VisualElement Root;
            internal Label ErrorMark;
            internal bool Faulted;
            internal Label SelectionMark;
            internal bool Selected;
            internal VisualElement HealthShieldGroup;
            internal VisualElement HealthBar;
            internal VisualElement ShieldBar;
            internal VisualElement HealthFill;
            internal VisualElement ShieldFill;
            internal Label HealthText;
            internal Label ShieldText;
            internal YellowBarKind YellowKind;
            internal VisualElement YellowBar;
            internal VisualElement YellowFill;
            internal Label YellowText;
            internal int Amount;
            internal int MaxAmount;
            internal bool HasConstructionBar;
            internal VisualElement ConstructionBar;
            internal VisualElement ConstructionFill;
            internal Label ConstructionText;
            internal DMath.Fixed? ConstructionProgress;
            internal DMath.Fixed ConstructionDuration;
            internal float VisibleHeight;
            internal float VisibleWidth;
        }

        private readonly VisualElement layer;
        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private readonly float expandedWidth;
        private readonly float compactWidth;
        private readonly float verticalOffset;
        private int hoveredEntityId;
        private int selectedEntityId;

        public int Count => entries.Count;
        public VisualElement Layer => layer;

        public SimulationStatusOverlay(VisualElement uiRoot, float barWidth = 124f, float offsetY = 1f)
        {
            if (uiRoot == null) throw new ArgumentNullException(nameof(uiRoot));
            expandedWidth = Mathf.Max(80f, barWidth);
            compactWidth = Mathf.Max(40f, Mathf.Round(expandedWidth * 0.5f));
            verticalOffset = offsetY;

            layer = new VisualElement { name = "simulation-status-overlay", pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.right = 0;
            layer.style.top = 0;
            layer.style.bottom = 0;

            // The main HUD and editor are inserted later, therefore paint over this layer.
            uiRoot.Insert(0, layer);
        }

        public void Track(int entityId, Transform anchor, SimulationStatusValues values)
        {
            TrackCore(entityId, anchor, values, YellowBarKind.None, 0, 0);
        }

        /// <summary>Nexuses have a construction indicator in addition to health and shields.</summary>
        public void TrackNexus(int entityId, Transform anchor, SimulationStatusValues values)
        {
            TrackCore(entityId, anchor, values, YellowBarKind.None, 0, 0, hasConstructionBar: true);
        }

        public void TrackResource(int entityId, Transform anchor, SimulationStatusValues values,
            int remaining, int maximum)
        {
            TrackCore(entityId, anchor, values, YellowBarKind.Resource, remaining, maximum);
        }

        public void TrackUnit(int entityId, Transform anchor, SimulationStatusValues values,
            int cargo, int maxCargo)
        {
            TrackCore(entityId, anchor, values, YellowBarKind.Cargo, cargo, maxCargo);
        }

        public void SetHoveredEntity(int entityId)
        {
            if (hoveredEntityId == entityId) return;
            hoveredEntityId = entityId;
            foreach (Entry entry in entries.Values)
                Refresh(entry);
        }

        /// <summary>The selected unit receives a discreet noninteractive cyan marker.</summary>
        public void SetSelectedEntity(int entityId)
        {
            if (selectedEntityId == entityId) return;
            int old = selectedEntityId;
            selectedEntityId = entityId;
            if (entries.TryGetValue(old, out Entry previous))
            {
                previous.Selected = false;
                Refresh(previous);
            }
            if (entries.TryGetValue(entityId, out Entry current))
            {
                current.Selected = true;
                Refresh(current);
            }
        }

        private void TrackCore(int entityId, Transform anchor, SimulationStatusValues values,
            YellowBarKind yellowKind, int amount, int maximum, bool hasConstructionBar = false)
        {
            if (anchor == null)
                throw new ArgumentNullException(nameof(anchor));

            if (entries.TryGetValue(entityId, out Entry prior))
            {
                if (prior.YellowKind != yellowKind || prior.HasConstructionBar != hasConstructionBar)
                    throw new InvalidOperationException("Entity status kind changed for entity " + entityId);
                prior.Anchor = anchor;
                prior.Values = values;
                prior.Amount = amount;
                prior.MaxAmount = maximum;
                Refresh(prior);
                return;
            }

            var entry = new Entry
            {
                EntityId = entityId,
                Anchor = anchor,
                Values = values,
                YellowKind = yellowKind,
                Amount = amount,
                MaxAmount = maximum,
                HasConstructionBar = hasConstructionBar,
                Root = new VisualElement { name = "entity-status-" + entityId, pickingMode = PickingMode.Ignore }
            };
            entry.Root.style.position = Position.Absolute;
            entry.Root.style.width = compactWidth;
            entry.Root.style.flexDirection = FlexDirection.Column;
            entry.Root.style.justifyContent = Justify.SpaceBetween;
            entry.Root.style.alignItems = Align.Stretch;
            entry.Root.style.display = DisplayStyle.None;

            entry.ErrorMark = new Label("!")
            {
                name = "entity-status-" + entityId + "-error",
                pickingMode = PickingMode.Ignore
            };
            entry.ErrorMark.style.height = 19f;
            entry.ErrorMark.style.flexShrink = 0f;
            entry.ErrorMark.style.unityTextAlign = TextAnchor.MiddleCenter;
            entry.ErrorMark.style.color = (Color)new Color32(255, 69, 75, 255);
            entry.ErrorMark.style.fontSize = 20f;
            entry.ErrorMark.style.unityFontStyleAndWeight = FontStyle.Bold;
            entry.ErrorMark.style.unityTextOutlineColor = Color.black;
            entry.ErrorMark.style.unityTextOutlineWidth = 1f;
            entry.ErrorMark.style.display = DisplayStyle.None;
            entry.Root.Add(entry.ErrorMark);

            entry.SelectionMark = new Label("▼")
            {
                name = "entity-status-" + entityId + "-selected",
                pickingMode = PickingMode.Ignore
            };
            entry.SelectionMark.style.height = 14f;
            entry.SelectionMark.style.flexShrink = 0f;
            entry.SelectionMark.style.unityTextAlign = TextAnchor.MiddleCenter;
            entry.SelectionMark.style.color = (Color)new Color32(89, 208, 255, 255);
            entry.SelectionMark.style.fontSize = 14f;
            entry.SelectionMark.style.unityFontStyleAndWeight = FontStyle.Bold;
            entry.SelectionMark.style.unityTextOutlineColor = Color.black;
            entry.SelectionMark.style.unityTextOutlineWidth = 1f;
            entry.SelectionMark.style.display = DisplayStyle.None;
            entry.Root.Add(entry.SelectionMark);
            entry.Selected = selectedEntityId == entityId;

            entry.HealthShieldGroup = new VisualElement
            {
                name = "entity-status-" + entityId + "-health-shields",
                pickingMode = PickingMode.Ignore
            };
            entry.HealthShieldGroup.style.flexShrink = 0;
            entry.HealthShieldGroup.style.flexDirection = FlexDirection.Column;
            entry.HealthShieldGroup.style.justifyContent = Justify.SpaceBetween;
            entry.HealthShieldGroup.Add(CreateBar("health", HealthColor, HealthBackground, Color.white,
                out entry.HealthBar, out entry.HealthFill, out entry.HealthText));
            entry.HealthShieldGroup.Add(CreateBar("shields", ShieldColor, ShieldBackground, Color.white,
                out entry.ShieldBar, out entry.ShieldFill, out entry.ShieldText));
            entry.Root.Add(entry.HealthShieldGroup);

            if (yellowKind != YellowBarKind.None)
            {
                string id = yellowKind == YellowBarKind.Resource ? "resources" : "cargo";
                entry.Root.Add(CreateBar(id, ResourceColor, ResourceBackground, Color.black,
                    out entry.YellowBar, out entry.YellowFill, out entry.YellowText));
            }

            if (hasConstructionBar)
            {
                entry.Root.Add(CreateBar("construction", ConstructionColor, ConstructionBackground,
                    Color.black, out entry.ConstructionBar, out entry.ConstructionFill,
                    out entry.ConstructionText));
            }

            layer.Add(entry.Root);
            entries.Add(entityId, entry);
            Refresh(entry);
        }

        public bool TryGetValues(int entityId, out SimulationStatusValues values)
        {
            if (entries.TryGetValue(entityId, out Entry entry))
            {
                values = entry.Values;
                return true;
            }
            values = default;
            return false;
        }

        public void SetValues(int entityId, SimulationStatusValues values)
        {
            if (!entries.TryGetValue(entityId, out Entry entry)) return;
            entry.Values = values;
            Refresh(entry);
        }

        public void SetHealth(int entityId, DMath.Fixed health)
        {
            if (entries.TryGetValue(entityId, out Entry entry))
                SetValues(entityId, entry.Values.WithHealth(health));
        }

        public void SetShields(int entityId, DMath.Fixed shields)
        {
            if (entries.TryGetValue(entityId, out Entry entry))
                SetValues(entityId, entry.Values.WithShields(shields));
        }

        public void SetResources(int entityId, int remaining)
        {
            SetAmount(entityId, YellowBarKind.Resource, remaining, null);
        }

        public void SetResources(int entityId, int remaining, int maximum)
        {
            SetAmount(entityId, YellowBarKind.Resource, remaining, maximum);
        }

        public void SetCargo(int entityId, int cargo)
        {
            SetAmount(entityId, YellowBarKind.Cargo, cargo, null);
        }

        public void SetCargo(int entityId, int cargo, int maximum)
        {
            SetAmount(entityId, YellowBarKind.Cargo, cargo, maximum);
        }

        private void SetAmount(int entityId, YellowBarKind kind, int amount, int? maximum)
        {
            if (!entries.TryGetValue(entityId, out Entry entry) || entry.YellowKind != kind) return;
            entry.Amount = amount;
            if (maximum.HasValue)
                entry.MaxAmount = maximum.Value;
            Refresh(entry);
        }

        /// <summary>
        /// Updates Nexus construction from authoritative elapsed and maximum time.
        /// A null progress means no unit is currently under construction.
        /// </summary>
        public void SetConstruction(int entityId, DMath.Fixed? progress, DMath.Fixed duration)
        {
            if (!entries.TryGetValue(entityId, out Entry entry) || !entry.HasConstructionBar)
                return;
            if (Nullable.Equals(entry.ConstructionProgress, progress) &&
                entry.ConstructionDuration == duration)
                return;
            entry.ConstructionProgress = progress;
            entry.ConstructionDuration = duration;
            Refresh(entry);
        }

        /// <summary>Fault markers ignore pointer input and survive while the VM recovers.</summary>
        public void SetFaulted(int entityId, bool faulted)
        {
            if (!entries.TryGetValue(entityId, out Entry entry) || entry.Faulted == faulted)
                return;
            entry.Faulted = faulted;
            Refresh(entry);
        }

        public void Remove(int entityId)
        {
            if (!entries.TryGetValue(entityId, out Entry entry)) return;
            entries.Remove(entityId);
            entry.Root.RemoveFromHierarchy();
            if (hoveredEntityId == entityId)
                hoveredEntityId = 0;
            if (selectedEntityId == entityId)
                selectedEntityId = 0;
        }

        /// <summary>Follow the interpolated entity center, even while the world is paused.</summary>
        public void UpdatePositions(Camera worldCamera)
        {
            if (worldCamera == null || layer.panel == null)
            {
                foreach (Entry entry in entries.Values)
                    entry.Root.style.display = DisplayStyle.None;
                return;
            }

            foreach (Entry entry in entries.Values)
            {
                if (entry.VisibleHeight <= 0f || entry.Anchor == null)
                {
                    entry.Root.style.display = DisplayStyle.None;
                    continue;
                }

                Vector3 screen = worldCamera.WorldToScreenPoint(
                    entry.Anchor.position + Vector3.up * verticalOffset);
                if (screen.z <= worldCamera.nearClipPlane ||
                    screen.x < 0f || screen.x > Screen.width ||
                    screen.y < 0f || screen.y > Screen.height)
                {
                    entry.Root.style.display = DisplayStyle.None;
                    continue;
                }

                Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(layer.panel,
                    new Vector2(screen.x, Screen.height - screen.y));
                Vector2 local = layer.WorldToLocal(panelPoint);
                entry.Root.style.left = local.x - entry.VisibleWidth * 0.5f;
                entry.Root.style.top = local.y - entry.VisibleHeight;
                entry.Root.style.display = DisplayStyle.Flex;
            }
        }

        private static VisualElement CreateBar(string id, Color fillColor, Color background, Color labelColor,
            out VisualElement bar, out VisualElement fill, out Label label)
        {
            bar = new VisualElement { name = "status-" + id + "-bar", pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Relative;
            bar.style.height = CompactBarHeight;
            bar.style.flexShrink = 0;
            bar.style.width = Length.Percent(100f);
            bar.style.alignItems = Align.Center;
            bar.style.justifyContent = Justify.Center;
            bar.style.backgroundColor = background;
            bar.style.overflow = Overflow.Hidden;
            bar.style.borderTopLeftRadius = 2f;
            bar.style.borderTopRightRadius = 2f;
            bar.style.borderBottomLeftRadius = 2f;
            bar.style.borderBottomRightRadius = 2f;
            bar.style.borderTopWidth = 1f;
            bar.style.borderBottomWidth = 1f;
            bar.style.borderLeftWidth = 1f;
            bar.style.borderRightWidth = 1f;
            bar.style.borderTopColor = BarBorder;
            bar.style.borderBottomColor = BarBorder;
            bar.style.borderLeftColor = BarBorder;
            bar.style.borderRightColor = BarBorder;

            fill = new VisualElement { name = "status-" + id + "-fill", pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            fill.style.left = 0f;
            fill.style.top = 0f;
            fill.style.bottom = 0f;
            fill.style.width = Length.Percent(100f);
            fill.style.backgroundColor = fillColor;
            bar.Add(fill);

            label = new Label { name = "status-" + id + "-label", pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = 0f;
            label.style.right = 0f;
            label.style.top = 0f;
            label.style.bottom = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            label.style.paddingLeft = 2f;
            label.style.paddingRight = 2f;
            label.style.backgroundColor = Color.clear;
            label.style.color = labelColor;
            label.style.fontSize = 13f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityTextOutlineWidth = 0f;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.display = DisplayStyle.None;
            bar.Add(label);
            return bar;
        }

        private void Refresh(Entry entry)
        {
            bool expanded = entry.EntityId == hoveredEntityId;
            float barHeight = expanded ? ExpandedBarHeight : CompactBarHeight;
            float barGap = expanded ? ExpandedBarGap : CompactBarGap;
            float width = expanded ? expandedWidth : compactWidth;

            entry.HealthFill.style.width = Length.Percent(entry.Values.HealthFraction * 100f);
            entry.ShieldFill.style.width = Length.Percent(entry.Values.ShieldFraction * 100f);
            entry.HealthText.text = entry.Values.HealthLabel;
            entry.ShieldText.text = entry.Values.ShieldLabel;
            // Without a backing plate, choose foreground contrast from how much
            // of the center text falls over the colored fill versus dark track.
            entry.HealthText.style.color = entry.Values.HealthFraction > 0.65f
                ? Color.black : Color.white;
            entry.ShieldText.style.color = entry.Values.ShieldFraction > 0.65f
                ? Color.black : Color.white;

            // Hover always reveals the full health/shield pair, even at maximum.
            bool showHealth = expanded || entry.Values.ShouldDisplay;
            entry.HealthShieldGroup.style.display = showHealth ? DisplayStyle.Flex : DisplayStyle.None;

            bool showYellow = false;
            if (entry.YellowKind != YellowBarKind.None)
            {
                // Hover reveals depleted-resource and carried-cargo capacity even
                // when the resource is full or the unit currently carries nothing.
                showYellow = expanded || (entry.YellowKind == YellowBarKind.Resource
                    ? entry.MaxAmount > 0 && entry.Amount < entry.MaxAmount
                    : entry.Amount > 0);
                entry.YellowBar.style.display = showYellow ? DisplayStyle.Flex : DisplayStyle.None;
                float fraction = entry.MaxAmount > 0
                    ? Mathf.Clamp01((float)entry.Amount / entry.MaxAmount) : 0f;
                entry.YellowFill.style.width = Length.Percent(fraction * 100f);
                entry.YellowText.text = entry.Amount.ToString(CultureInfo.InvariantCulture) + " / " +
                    entry.MaxAmount.ToString(CultureInfo.InvariantCulture);
                entry.YellowText.style.display = expanded && showYellow ? DisplayStyle.Flex : DisplayStyle.None;
                entry.YellowBar.style.height = barHeight;
            }

            entry.HealthBar.style.height = barHeight;
            entry.ShieldBar.style.height = barHeight;
            entry.HealthText.style.display = expanded && showHealth ? DisplayStyle.Flex : DisplayStyle.None;
            entry.ShieldText.style.display = expanded && showHealth ? DisplayStyle.Flex : DisplayStyle.None;
            entry.HealthShieldGroup.style.height = showHealth ? (barHeight * 2f + barGap) : 0f;

            bool showConstruction = entry.HasConstructionBar &&
                entry.ConstructionProgress.HasValue && entry.ConstructionDuration.RawValue > 0;
            if (entry.HasConstructionBar)
            {
                entry.ConstructionBar.style.display = showConstruction
                    ? DisplayStyle.Flex : DisplayStyle.None;
                entry.ConstructionBar.style.height = barHeight;
                float fraction = showConstruction
                    ? Mathf.Clamp01((float)((double)entry.ConstructionProgress.Value.RawValue /
                        entry.ConstructionDuration.RawValue)) : 0f;
                entry.ConstructionFill.style.width = Length.Percent(fraction * 100f);
                entry.ConstructionText.style.color = fraction > 0.65f
                    ? Color.black : Color.white;
                entry.ConstructionText.text = showConstruction
                    ? FormatConstructionTime(entry.ConstructionProgress.Value) + " / " +
                      FormatConstructionTime(entry.ConstructionDuration)
                    : string.Empty;
                entry.ConstructionText.style.display = expanded && showConstruction
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }

            entry.Root.style.width = width;
            entry.VisibleWidth = width;

            float visibleHeight = 0f;
            if (showHealth)
                visibleHeight += barHeight * 2f + barGap;
            if (showYellow)
            {
                if (showHealth) visibleHeight += barGap;
                visibleHeight += barHeight;
            }

            if (showConstruction)
            {
                if (showHealth || showYellow) visibleHeight += barGap;
                visibleHeight += barHeight;
            }

            entry.SelectionMark.style.display = entry.Selected ? DisplayStyle.Flex : DisplayStyle.None;
            if (entry.Selected)
            {
                if (visibleHeight > 0f) visibleHeight += barGap;
                visibleHeight += 14f;
            }
            entry.ErrorMark.style.display = entry.Faulted ? DisplayStyle.Flex : DisplayStyle.None;
            if (entry.Faulted)
            {
                if (visibleHeight > 0f) visibleHeight += barGap;
                visibleHeight += 19f;
            }

            entry.VisibleHeight = visibleHeight;
            entry.Root.style.height = visibleHeight;
            if (visibleHeight <= 0f)
                entry.Root.style.display = DisplayStyle.None;
            // UpdatePositions restores visibility for nonempty, on-screen entries.
        }

        private static string FormatConstructionTime(DMath.Fixed time) =>
            ((double)time.RawValue / DMath.Constants.FixedScale)
                .ToString("0.#", CultureInfo.InvariantCulture);

        public void Dispose()
        {
            entries.Clear();
            layer.RemoveFromHierarchy();
        }
    }
}
