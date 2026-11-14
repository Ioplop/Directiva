namespace Directiva.UI
{
    public enum SimulationHudLayout
    {
        Standard,
        Stacked,
        Narrow,
        Large
    }

    public enum OptionsLayout
    {
        Standard,
        Compact,
        Small,
        Large
    }

    /// <summary>
    /// Choose layouts using the available UI Toolkit panel dimensions, not
    /// Screen.width: PanelSettings may already scale pixels to panel units.
    /// Reflow narrow layouts instead of shrinking important labels to illegibility.
    /// </summary>
    public static class ResponsiveUILayout
    {
        public static SimulationHudLayout ForSimulation(float width, float height)
        {
            if (width < 710f) return SimulationHudLayout.Narrow;
            if (width < 1190f) return SimulationHudLayout.Stacked;
            if (width >= 2600f && height >= 1400f) return SimulationHudLayout.Large;
            return SimulationHudLayout.Standard;
        }

        public static OptionsLayout ForOptions(float width, float height)
        {
            if (width < 650f || height < 480f) return OptionsLayout.Small;
            if (width < 1160f || height < 720f) return OptionsLayout.Compact;
            if (width >= 2600f && height >= 1400f) return OptionsLayout.Large;
            return OptionsLayout.Standard;
        }

        public static float EditorTopInset(SimulationHudLayout layout)
        {
            switch (layout)
            {
                case SimulationHudLayout.Narrow: return 160f;
                case SimulationHudLayout.Stacked: return 124f;
                case SimulationHudLayout.Large: return 106f;
                default: return 76f;
            }
        }

        public static float EditorBottomInset(SimulationHudLayout layout)
        {
            return layout == SimulationHudLayout.Large ? 86f : 64f;
        }
    }
}
