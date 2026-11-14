using System;
using System.Globalization;
using Directiva.Localization;
using DOrchestration;
using DSimulation;
using UnityEngine.UIElements;

namespace Directiva.SimulationUI
{
    /// <summary>
    /// Compact, read-only match statistics, displayed underneath the editor and
    /// other interactive UI. All children ignore pointer input.
    /// </summary>
    public sealed class SimulationMatchStatsBar
    {
        private sealed class Metric
        {
            internal string LocalizationKey;
            internal Label Caption;
            internal Label Blue;
            internal Label Red;
        }

        private readonly VisualElement root;
        private readonly VisualElement bar;
        private readonly Metric[] metrics = new Metric[3];
        private readonly Label remainingTimeCaption;
        private readonly Label remainingTimeValue;
        private static readonly StyleColor NormalTimeColor = new StyleColor(new UnityEngine.Color32(231, 235, 242, 255));
        private static readonly StyleColor WarningTimeColor = new StyleColor(new UnityEngine.Color32(255, 211, 92, 255));
        private static readonly StyleColor CriticalTimeColor = new StyleColor(new UnityEngine.Color32(255, 107, 111, 255));

        public VisualElement Root => root;

        public SimulationMatchStatsBar(VisualElement parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            root = new VisualElement { name = "sim-match-stats-layer", pickingMode = PickingMode.Ignore };
            root.AddToClassList("sim-match-stats-layer");
            bar = new VisualElement { name = "sim-match-stats", pickingMode = PickingMode.Ignore };
            bar.AddToClassList("sim-match-stats");
            root.Add(bar);
            parent.Add(root);

            AddMetric(0, "hud_resources", "resources");
            AddMetric(1, "hud_units", "units");
            AddMetric(2, "hud_errors", "errors");

            var timeGroup = new VisualElement { name = "match-stat-time", pickingMode = PickingMode.Ignore };
            timeGroup.AddToClassList("sim-match-stat");
            timeGroup.AddToClassList("sim-match-stat-time");
            remainingTimeCaption = MakeLabel("match-stat-time-caption", "sim-match-stat-caption");
            remainingTimeValue = MakeLabel("match-stat-time-value", "sim-match-stat-time-value");
            remainingTimeValue.text = "--:--";
            timeGroup.Add(remainingTimeCaption);
            timeGroup.Add(remainingTimeValue);
            bar.Add(timeGroup);
            Relocalize();
            root.style.display = DisplayStyle.None;
        }

        private void AddMetric(int index, string key, string id)
        {
            var metric = new Metric { LocalizationKey = key };
            var group = new VisualElement { name = "match-stat-" + id, pickingMode = PickingMode.Ignore };
            group.AddToClassList("sim-match-stat");

            metric.Caption = MakeLabel("match-stat-" + id + "-caption", "sim-match-stat-caption");
            metric.Blue = MakeLabel("match-stat-" + id + "-blue", "sim-match-stat-blue");
            metric.Red = MakeLabel("match-stat-" + id + "-red", "sim-match-stat-red");
            Label separator = MakeLabel("match-stat-" + id + "-separator", "sim-match-stat-separator");
            separator.text = "–";

            group.Add(metric.Caption);
            group.Add(metric.Blue);
            group.Add(separator);
            group.Add(metric.Red);
            bar.Add(group);
            metrics[index] = metric;
        }

        private static Label MakeLabel(string name, string className)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        public void Relocalize()
        {
            foreach (Metric metric in metrics)
                metric.Caption.text = DirectivaLocalization.Get("Simulation", metric.LocalizationKey) + ":";
            remainingTimeCaption.text = DirectivaLocalization.Get("Simulation", "hud_time_remaining") + ":";
        }

        /// <summary>
        /// Error totals include messages that have since left the bounded output
        /// log; heap usage is intentionally omitted because GC runs lazily.
        /// </summary>
        public void UpdateValues(Simulation simulation, Orchestrator match,
            int matchDurationSeconds = 0)
        {
            if (simulation == null)
            {
                root.style.display = DisplayStyle.None;
                return;
            }
            root.style.display = DisplayStyle.Flex;
            SBSide blue = simulation.GetSide(1);
            SBSide red = simulation.GetSide(2);
            Set(0, Format(blue.Resources), Format(red.Resources));
            Set(1, Format(AliveUnits(blue)), Format(AliveUnits(red)));
            Set(2,
                Format(match != null ? match.GetErrorCount(1) : 0L),
                Format(match != null ? match.GetErrorCount(2) : 0L));

            double elapsedSeconds = (double)simulation.Time.RawValue / DMath.Constants.FixedScale;
            string remaining = FormatRemainingTime(matchDurationSeconds, elapsedSeconds);
            if (remainingTimeValue.text != remaining)
                remainingTimeValue.text = remaining;
            ApplyRemainingTimeColor(matchDurationSeconds, elapsedSeconds);
        }

        /// <summary>
        /// Displays simulation time left, never wall-clock time. Rounding up
        /// avoids showing one second less immediately after the first tick.
        /// </summary>
        public static string FormatRemainingTime(int durationSeconds, double elapsedSeconds)
        {
            if (durationSeconds <= 0) return "--:--";
            double remaining = Math.Max(0.0, durationSeconds - Math.Max(0.0, elapsedSeconds));
            long seconds = (long)Math.Ceiling(remaining - 0.0000001);
            return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }


        private void ApplyRemainingTimeColor(int durationSeconds, double elapsedSeconds)
        {
            if (durationSeconds <= 0)
            {
                remainingTimeValue.style.color = NormalTimeColor;
                return;
            }

            double remainingSeconds = Math.Max(0.0, durationSeconds - Math.Max(0.0, elapsedSeconds));
            double ratio = remainingSeconds / durationSeconds;
            remainingTimeValue.style.color = ratio <= 0.10
                ? CriticalTimeColor
                : ratio <= 0.25 ? WarningTimeColor : NormalTimeColor;
        }

        private static int AliveUnits(SBSide side)
        {
            int count = 0;
            foreach (SBUnit unit in side.Units)
                if (unit.alive) count++;
            return count;
        }

        private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

        private void Set(int index, string blue, string red)
        {
            Metric metric = metrics[index];
            if (metric.Blue.text != blue) metric.Blue.text = blue;
            if (metric.Red.text != red) metric.Red.text = red;
        }
    }
}
