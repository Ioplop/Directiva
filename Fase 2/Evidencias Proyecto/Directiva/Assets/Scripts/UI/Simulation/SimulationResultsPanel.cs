using System;
using System.Globalization;
using DOrchestration;
using Directiva.CodeUI;
using Directiva.Localization;
using Directiva.Progress;
using Directiva.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.SimulationUI
{
    /// <summary>Read-only, localized match summary over the finished 3D arena.</summary>
    public sealed class SimulationResultsPanel
    {
        private static readonly LevelStarFlags[] Objectives =
        {
            LevelStarFlags.Victory, LevelStarFlags.UntouchedNexus, LevelStarFlags.FastVictory
        };

        private static readonly string[] MetricKeys =
        {
            "results_resources", "results_errors", "results_built",
            "results_destroyed", "results_damage", "results_orders_per_second"
        };

        private readonly VisualElement backdrop;
        private readonly Label outcomeLabel;
        private readonly Label levelLabel;
        private readonly Label elapsedName;
        private readonly Label elapsedValue;
        private readonly Label[] names = new Label[6];
        private readonly Label[,] values = new Label[6, 2];
        private readonly Label blueHeading;
        private readonly Label redHeading;
        private readonly Image[] objectiveImages = new Image[3];
        private readonly VisualElement[] objectiveSlots = new VisualElement[3];
        private readonly Button returnButton;
        private readonly Button levelsButton;
        private readonly DirectivaUIIcons icons;
        private readonly Action returnToArena;
        private readonly Action showLevels;
        private SimulationMatchResult result;
        private string levelName;
        private bool hasResult;

        private static string T(string key, params object[] args) =>
            DirectivaLocalization.Get("Simulation", key, args);

        public bool IsVisible => backdrop.style.display.value == DisplayStyle.Flex;

        public SimulationResultsPanel(VisualElement overlay, DirectivaUITheme theme,
            Action onReturnToArena, Action onShowLevels)
        {
            if (overlay == null) throw new ArgumentNullException(nameof(overlay));
            icons = theme?.icons;
            returnToArena = onReturnToArena;
            showLevels = onShowLevels;

            backdrop = new VisualElement { name = "simulation-results-screen" };
            backdrop.AddToClassList("sim-results-backdrop");
            backdrop.style.display = DisplayStyle.None;
            backdrop.pickingMode = PickingMode.Position;
            overlay.Add(backdrop);

            var card = new VisualElement();
            card.AddToClassList("sim-results-card");
            backdrop.Add(card);

            outcomeLabel = new Label();
            outcomeLabel.AddToClassList("sim-results-outcome");
            card.Add(outcomeLabel);

            levelLabel = new Label();
            levelLabel.AddToClassList("sim-results-level");
            levelLabel.style.display = DisplayStyle.None;
            card.Add(levelLabel);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            // The content can fit naturally at common resolutions. Show a scrollbar
            // only if the available height actually forces this area to shrink.
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.AddToClassList("sim-results-scroll");
            card.Add(scroll);

            var metrics = new VisualElement();
            metrics.AddToClassList("sim-results-metrics");
            scroll.Add(metrics);

            // One metric label and two numeric columns, in the same color and
            // order as the blue/red sides shown by the match HUD.
            var headings = new VisualElement();
            headings.AddToClassList("sim-results-metric-row");
            headings.AddToClassList("sim-results-metric-headings");
            var headingSpacer = new Label();
            headingSpacer.AddToClassList("sim-results-metric-name");
            headings.Add(headingSpacer);
            blueHeading = CreateSideValueLabel(0);
            blueHeading.AddToClassList("sim-results-metric-heading");
            redHeading = CreateSideValueLabel(1);
            redHeading.AddToClassList("sim-results-metric-heading");
            headings.Add(blueHeading);
            headings.Add(redHeading);
            metrics.Add(headings);

            for (int i = 0; i < MetricKeys.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("sim-results-metric-row");
                names[i] = new Label();
                names[i].AddToClassList("sim-results-metric-name");
                names[i].pickingMode = PickingMode.Ignore;
                row.Add(names[i]);
                for (int column = 0; column < 2; column++)
                {
                    values[i, column] = CreateSideValueLabel(column);
                    row.Add(values[i, column]);
                }
                metrics.Add(row);
            }

            // Elapsed time belongs to the match, not either side. Show it once
            // after the side-by-side statistics, using a neutral text color.
            var elapsedRow = new VisualElement();
            elapsedRow.AddToClassList("sim-results-elapsed-row");
            elapsedName = new Label();
            elapsedName.AddToClassList("sim-results-elapsed-name");
            elapsedValue = new Label();
            elapsedValue.AddToClassList("sim-results-time-value");
            elapsedRow.Add(elapsedName);
            elapsedRow.Add(elapsedValue);
            scroll.Add(elapsedRow);

            var stars = new VisualElement();
            stars.AddToClassList("sim-results-stars");
            scroll.Add(stars);
            for (int i = 0; i < Objectives.Length; i++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("sim-results-star-slot");
                slot.pickingMode = PickingMode.Position;
                objectiveSlots[i] = slot;
                var star = UIIconView.Create(null, "sim-results-star-icon");
                objectiveImages[i] = star;
                slot.Add(star);
                stars.Add(slot);
            }

            var actions = new VisualElement();
            actions.AddToClassList("sim-results-actions");
            returnButton = new Button(() => returnToArena?.Invoke());
            returnButton.AddToClassList("sim-results-action");
            levelsButton = new Button(() => showLevels?.Invoke());
            levelsButton.AddToClassList("sim-results-action");
            levelsButton.AddToClassList("sim-results-action-primary");
            actions.Add(returnButton);
            actions.Add(levelsButton);
            card.Add(actions);
            Relocalize();
        }

        private static Label CreateSideValueLabel(int column)
        {
            var label = new Label();
            label.AddToClassList("sim-results-metric-value");
            label.AddToClassList(column == 0
                ? "sim-results-value-blue" : "sim-results-value-red");
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        public void RegisterTooltips(HoverTooltipController tooltips)
        {
            if (tooltips == null) return;
            for (int index = 0; index < Objectives.Length; index++)
            {
                int objective = index;
                tooltips.Register(objectiveSlots[index], () => GetStarTooltip(Objectives[objective]));
            }
        }

        public void Show(SimulationMatchResult matchResult, string levelDisplayName = null)
        {
            result = matchResult;
            levelName = levelDisplayName;
            hasResult = true;
            Relocalize();
            backdrop.style.display = DisplayStyle.Flex;
            // Take focus away from the code editor so editor shortcuts cannot
            // accidentally run while the modal results screen is open.
            if (backdrop.panel != null)
                returnButton.Focus();
        }

        public void Hide()
        {
            if (returnButton.focusController?.focusedElement == returnButton)
                returnButton.Blur();
            if (levelsButton.focusController?.focusedElement == levelsButton)
                levelsButton.Blur();
            backdrop.style.display = DisplayStyle.None;
        }

        private string GetStarTooltip(LevelStarFlags star)
        {
            if (star == LevelStarFlags.Victory) return T("results_star_victory");
            if (star == LevelStarFlags.UntouchedNexus) return T("results_star_nexus");
            return T("results_star_fast", FormatTime(result.FastVictorySeconds));
        }

        public void Relocalize()
        {
            returnButton.text = T("results_return");
            levelsButton.text = T("results_levels");
            blueHeading.text = T("results_side_blue");
            redHeading.text = T("results_side_red");
            elapsedName.text = T("results_time");
            for (int i = 0; i < MetricKeys.Length; i++)
                names[i].text = T(MetricKeys[i]);
            if (!hasResult) return;

            levelLabel.text = levelName ?? string.Empty;
            levelLabel.style.display = string.IsNullOrWhiteSpace(levelName)
                ? DisplayStyle.None : DisplayStyle.Flex;
            elapsedValue.text = FormatTime(result.ElapsedSeconds);

            outcomeLabel.text = result.Outcome == MatchOutcome.Side1Won ? T("results_victory") :
                result.Outcome == MatchOutcome.Side2Won ? T("results_defeat") : T("results_draw");
            outcomeLabel.RemoveFromClassList("sim-results-victory");
            outcomeLabel.RemoveFromClassList("sim-results-defeat");
            outcomeLabel.RemoveFromClassList("sim-results-draw");
            outcomeLabel.AddToClassList(result.Outcome == MatchOutcome.Side1Won
                ? "sim-results-victory" : result.Outcome == MatchOutcome.Side2Won
                    ? "sim-results-defeat" : "sim-results-draw");

            CultureInfo culture = DirectivaLocalization.CurrentLocale == "es"
                ? CultureInfo.GetCultureInfo("es-CL") : CultureInfo.GetCultureInfo("en-US");
            ShowSideMetrics(0, result.Blue, culture);
            ShowSideMetrics(1, result.Red, culture);

            for (int i = 0; i < Objectives.Length; i++)
            {
                bool earned = (result.EarnedStars & Objectives[i]) != 0;
                // Results can use larger, separate objective art without changing level icons.
                // Existing themes keep working until the optional results sprites are assigned.
                objectiveImages[i].sprite = earned
                    ? (icons?.resultObjectiveEarned ?? icons?.earnedStar)
                    : (icons?.resultObjectiveUnearned ?? icons?.unearnedStar);
                objectiveSlots[i].EnableInClassList("sim-results-star-earned", earned);
            }
        }

        private void ShowSideMetrics(int column, SimulationSideMatchResult side,
            CultureInfo culture)
        {
            values[0, column].text = side.ResourcesGathered.ToString("N0", culture);
            values[1, column].text = side.Errors.ToString("N0", culture);
            values[2, column].text = side.UnitsBuilt.ToString("N0", culture);
            values[3, column].text = side.EntitiesDestroyed.ToString("N0", culture);
            values[4, column].text = side.EffectiveDamage.ToString("0.##", culture);
            values[5, column].text = side.OrdersPerSecond(result.ElapsedSeconds)
                .ToString("0.00", culture);
        }

        // Simulated time (not wall-clock or playback speed).
        public static string FormatTime(double seconds)
        {
            long total = Math.Max(0L, (long)Math.Floor(seconds + 0.000001d));
            return total >= 3600
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                    total / 3600, total / 60 % 60, total % 60)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}",
                    total / 60, total % 60);
        }
    }
}
