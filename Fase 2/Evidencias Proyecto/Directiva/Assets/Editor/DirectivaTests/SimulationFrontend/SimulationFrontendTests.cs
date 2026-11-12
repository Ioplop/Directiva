using System;
using System.Collections.Generic;
using Directiva.SimulationFrontend;
using Directiva.SimulationUI;
using DSimulation;
using DMath;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.EditorTests.SimulationFrontendTests
{
    /// <summary>Importer/compilation smoke tests for the Unity presentation layer.</summary>
    internal static class SimulationFrontendTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "Frontend", name, description, action));

            Add("PlayerProgramStartsUnassigned",
                "A level starts without any hidden player program or locked controls", t =>
            {
                var gameObject = new GameObject("test-no-implicit-player-script");
                try
                {
                    var synchronizer = gameObject.AddComponent<SimulationSceneSynchronizer>();
                    t.Check(!synchronizer.HasSelectedPlayerProgram,
                        "player Nexus must not have a program assigned at scene creation");
                    t.Check(string.IsNullOrEmpty(synchronizer.SelectedPlayerScript),
                        "no default player file");
                    t.Check(string.IsNullOrEmpty(synchronizer.SelectedPlayerFunction),
                        "no default entry function");
                    t.Check(synchronizer.CurrentMatch == null,
                        "no Orchestrator created by assigning an Inspector demo");
                    t.Check(!synchronizer.IsProgramSelectionLocked,
                        "player can choose a script before pressing Play");
                    synchronizer.Play();
                    t.Check(!synchronizer.IsRunning,
                        "Play without a player program must not start any VM");
                    t.Check(synchronizer.CurrentMatch == null,
                        "Play never replaces the missing program with a demo");
                    t.Check(!synchronizer.IsProgramSelectionLocked,
                        "unsuccessful Play must not lock the empty selector");
                }
                finally { UnityEngine.Object.DestroyImmediate(gameObject); }
            });

            Add("EntityPrefabContracts", "All three world prefabs expose VisualEntity and interpolation", t =>
            {
                foreach (string kind in new[] { "Unit", "Nexus", "Resource" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/Simulation/" + kind + ".prefab");
                    t.Check(prefab != null, kind + " prefab imported");
                    t.Check(prefab.GetComponent<VisualEntity>() != null,
                        kind + " has VisualEntity");
                    var interpolator = prefab.GetComponent<SimulationTransformInterpolator>();
                    t.Check(interpolator != null, kind + " has interpolator");
                    if (kind == "Resource")
                    {
                        t.Check(!interpolator.SynchronizeRotation,
                            "resource rotation is left to Rotator");
                        t.Check(prefab.GetComponent<VisualEntity>().Side == null,
                            "neutral resource does not require side color");
                    }
                }
            });

            Add("StatusFullIsHidden", "All entity types start with hidden health/shield indicators", t =>
            {
                var unit = new SimulationStatusValues(10, 10, 10, 10);
                var nexus = new SimulationStatusValues(100, 100, 50, 50);
                var resource = new SimulationStatusValues(100, 100, 0, 0);
                t.Check(!unit.ShouldDisplay, "full unit hidden");
                t.Check(!nexus.ShouldDisplay, "full nexus hidden");
                t.Check(!resource.ShouldDisplay, "full resource hidden, including zero-capacity shield");
                t.Equal("100 / 100", resource.HealthLabel, "resource health text");
                t.Equal("0 / 0", resource.ShieldLabel, "resource has no shields");
            });

            Add("StatusEitherDamageShowsBoth", "A decrease in either status reveals the paired bars", t =>
            {
                var original = new SimulationStatusValues(100, 100, 50, 50);
                var shieldDamage = original.WithShields(25);
                t.Check(shieldDamage.ShouldDisplay, "shield damage makes both bars visible");
                t.Equal("100 / 100", shieldDamage.HealthLabel, "undamaged health is still shown");
                t.Equal("25 / 50", shieldDamage.ShieldLabel, "damaged shield is shown");
                var healthDamage = original.WithHealth(45);
                t.Check(healthDamage.ShouldDisplay, "health damage makes both bars visible");
                t.Equal("50 / 50", healthDamage.ShieldLabel, "full shields are still shown");
                t.Check(!healthDamage.WithHealth(100).ShouldDisplay, "healing fully hides both bars");
                t.Check(!shieldDamage.WithShields(50).ShouldDisplay, "shield regeneration fully hides both bars");
            });

            Add("StatusResourceWithoutShields", "Resources show damage with zero-capacity shield indicator", t =>
            {
                var resource = new SimulationStatusValues(73, 100, 0, 0);
                t.Check(resource.ShouldDisplay, "damaged resource shows status");
                t.Equal(0f, resource.ShieldFraction, "no shield division by zero");
                t.Equal("0 / 0", resource.ShieldLabel, "honest zero-capacity shield display");
                t.Check(!resource.WithHealth(100).ShouldDisplay, "resource fully repaired is hidden");
            });

            Add("StatusFractionsAndPrecision", "Fills are clamped and fractional shields display readably", t =>
            {
                var partial = new SimulationStatusValues(7, 10, Fixed.Decimal(125, 2), 5);
                t.Check(Mathf.Abs(partial.HealthFraction - 0.7f) < 0.0001f, "health is 70 percent");
                t.Check(Mathf.Abs(partial.ShieldFraction - 0.25f) < 0.0001f, "shields are 25 percent");
                t.Equal("1.25 / 5", partial.ShieldLabel, "fraction is shown with invariant decimal format");
                t.Equal(0f, partial.WithHealth(-1).HealthFraction, "negative fill clamped");
                t.Equal(1f, partial.WithHealth(20).HealthFraction, "oversized fill clamped");
            });

            Add("StatusOverlayBehindUIAndNotPickable", "Status UI uses the same panel, beneath HUD and without pointer picking", t =>
            {
                var root = new VisualElement();
                var hud = new VisualElement { name = "hud" };
                root.Add(hud);
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    t.Equal(overlay.Layer, root[0], "overlay is the first child");
                    t.Equal(hud, root[1], "HUD remains in front");
                    t.Equal(PickingMode.Ignore, overlay.Layer.pickingMode, "overlay does not block mouse");
                }
                finally { overlay.Dispose(); }
                t.Equal(1, root.childCount, "overlay disposed without removing HUD");
            });
            Add("StatusOverlayLifecycleAndEvents", "Indicators retain an entity ID and receive independent updates", t =>
            {
                var root = new VisualElement();
                var entity = new GameObject("test-status-anchor");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.Track(42, entity.transform, new SimulationStatusValues(100, 100, 50, 50));
                    t.Equal(1, overlay.Count, "one indicator registered");
                    var visual = root.Q<VisualElement>("entity-status-42");
                    t.Check(visual != null, "indicator created");
                    t.Equal(PickingMode.Ignore, visual.pickingMode, "indicator not pickable");
                    t.Equal(DisplayStyle.None, visual.style.display.value, "initially hidden");
                    t.Equal(2, visual.Q<VisualElement>("entity-status-42-health-shields").childCount,
                        "both bars exist regardless of damage source");
                    t.Equal(DisplayStyle.None, visual.Q<Label>("status-health-label").style.display.value,
                        "compact bars hide numbers by default");
                    t.Equal(PickingMode.Ignore,
                        visual.Q<Label>("status-health-label").pickingMode,
                        "numeric labels do not block entity selection");

                    overlay.SetShields(42, 20);
                    t.Check(overlay.TryGetValues(42, out var shields), "entity tracked");
                    t.Equal((Fixed)20, shields.Shields, "shield event applied");
                    t.Equal((Fixed)100, shields.Health, "health unaffected by shield event");
                    t.Equal(16f, visual.style.height.value.value,
                        "compact paired bars reserve a small height");
                    overlay.SetHoveredEntity(42);
                    t.Equal(DisplayStyle.Flex, visual.Q<Label>("status-shields-label").style.display.value,
                        "hover reveals numeric text");
                    t.Equal("20 / 50", visual.Q<Label>("status-shields-label").text,
                        "shield bar text updated");
                    t.Equal(45f, visual.style.height.value.value,
                        "hover expands paired bars to the readable size");

                    overlay.SetHealth(42, 80);
                    t.Check(overlay.TryGetValues(42, out var both), "entity still tracked");
                    t.Equal((Fixed)80, both.Health, "health event applied");
                    t.Equal("80 / 100", visual.Q<Label>("status-health-label").text,
                        "health bar text updated");
                    t.Equal((Fixed)20, both.Shields, "shield unaffected by health event");

                    overlay.SetHoveredEntity(0);
                    t.Equal(DisplayStyle.None, visual.Q<Label>("status-health-label").style.display.value,
                        "leaving hover returns to compact bars");
                    overlay.SetHealth(42, 100);
                    overlay.SetShields(42, 50);
                    t.Equal(DisplayStyle.None, visual.style.display.value,
                        "recovered indicator is hidden immediately");
                    overlay.Remove(42);
                    t.Equal(0, overlay.Count, "death removes indicator");
                    t.Check(root.Q<VisualElement>("entity-status-42") == null, "UI removed on death");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(entity);
                }
            });
            Add("ResourceAmountBarLifecycle", "Resource amount hides when full, remains independent of health and uses its own maximum", t =>
            {
                var root = new VisualElement();
                var anchor = new GameObject("resource-amount-test");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.TrackResource(51, anchor.transform,
                        new SimulationStatusValues(100, 100, 0, 0), 100, 100);
                    VisualElement visual = root.Q<VisualElement>("entity-status-51");
                    VisualElement yellow = visual.Q<VisualElement>("status-resources-bar");
                    t.Check(visual != null, "resource indicator was created");
                    t.Equal(4, visual.childCount, "error/selection marks, status group and yellow bar exist");
                    t.Equal(yellow, visual[3], "yellow amount is vertically after health and shields");
                    t.Equal(DisplayStyle.None, yellow.style.display.value,
                        "full resource does not show a yellow bar");
                    t.Equal(DisplayStyle.None, visual.style.display.value,
                        "full healthy resource has no floating indicator");

                    overlay.SetResources(51, 95);
                    t.Equal(DisplayStyle.Flex, yellow.style.display.value,
                        "resource depleted by one gathering shows the yellow bar");
                    t.Equal("95 / 100", visual.Q<Label>("status-resources-label").text,
                        "resource numeric label contains current and maximum");
                    t.Equal(DisplayStyle.None, visual.Q<Label>("status-resources-label").style.display.value,
                        "compact yellow bar hides text until hover");
                    t.Equal(7f, visual.style.height.value.value,
                        "only the compact yellow bar consumes vertical space");
                    t.Equal(PickingMode.Ignore, visual.Q<Label>("status-resources-label").pickingMode,
                        "resource text is click-through");
                    t.Equal(DisplayStyle.None,
                        visual.Q<VisualElement>("entity-status-51-health-shields").style.display.value,
                        "undamaged resource does not show health/shields");

                    overlay.SetResources(51, 35);
                    t.Equal("35 / 100", visual.Q<Label>("status-resources-label").text,
                        "EventResources changes only current amount");
                    t.Check(Mathf.Abs(visual.Q<VisualElement>("status-resources-fill")
                        .style.width.value.value - 35f) < 0.01f,
                        "yellow fill represents fraction of own maximum");
                    overlay.SetHoveredEntity(51);
                    t.Equal(DisplayStyle.Flex, visual.Q<Label>("status-resources-label").style.display.value,
                        "hover reveals the yellow number");
                    // Hover reveals health and shields even when both are full,
                    // so the yellow bar is part of a three-bar expanded stack.
                    t.Equal(DisplayStyle.Flex,
                        visual.Q<VisualElement>("entity-status-51-health-shields").style.display.value,
                        "hover also shows full health and shields");
                    t.Equal(69f, visual.style.height.value.value,
                        "hover expands all three bars, not just the yellow bar");
                    overlay.SetHealth(51, 75);
                    t.Equal(DisplayStyle.Flex,
                        visual.Q<VisualElement>("entity-status-51-health-shields").style.display.value,
                        "damage reveals both health and shields");
                    t.Equal(69f, visual.style.height.value.value,
                        "expanded health and shields plus yellow bar have a reserved gap");
                    overlay.SetHoveredEntity(0);
                    t.Equal(25f, visual.style.height.value.value,
                        "compact health and shields plus yellow bar are much smaller");
                    overlay.SetHealth(51, 100);
                    t.Equal(DisplayStyle.None,
                        visual.Q<VisualElement>("entity-status-51-health-shields").style.display.value,
                        "recovery hides health and shields without hiding depleted amount");

                    overlay.SetResources(51, 200, 200);
                    t.Equal(DisplayStyle.None, yellow.style.display.value,
                        "a new maximum of 200 correctly counts as full");
                    t.Equal(DisplayStyle.None, visual.style.display.value,
                        "restoring all maxima hides the entire indicator");
                    overlay.SetResources(51, 125);
                    t.Equal("125 / 200", visual.Q<Label>("status-resources-label").text,
                        "updated maximum is used by subsequent amount events");
                    t.Check(Mathf.Abs(visual.Q<VisualElement>("status-resources-fill")
                        .style.width.value.value - 62.5f) < 0.01f,
                        "fill is recomputed against the updated maximum");
                    overlay.Remove(51);
                    t.Equal(0, overlay.Count, "resource death removes all bars");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(anchor);
                }
            });
            Add("UnitCargoBarLifecycle", "Units show a yellow cargo indicator when carrying, beside paired status without overlap", t =>
            {
                var root = new VisualElement();
                var anchor = new GameObject("unit-cargo-test");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.TrackUnit(61, anchor.transform,
                        new SimulationStatusValues(100, 100, 50, 50), 0, 20);
                    VisualElement visual = root.Q<VisualElement>("entity-status-61");
                    VisualElement yellow = visual.Q<VisualElement>("status-cargo-bar");
                    t.Equal(4, visual.childCount, "unit has selection/error marks, status group and cargo bar");
                    t.Equal(yellow, visual[3], "cargo is below health and shields");
                    t.Equal(DisplayStyle.None, visual.style.display.value,
                        "empty undamaged unit is hidden");
                    t.Equal(DisplayStyle.None, yellow.style.display.value,
                        "empty cargo is hidden");

                    overlay.SetCargo(61, 5);
                    t.Equal(DisplayStyle.Flex, yellow.style.display.value,
                        "EventCargo reveals yellow bar");
                    t.Equal("5 / 20", visual.Q<Label>("status-cargo-label").text,
                        "cargo shows unit-specific capacity");
                    t.Check(Mathf.Abs(visual.Q<VisualElement>("status-cargo-fill")
                        .style.width.value.value - 25f) < 0.01f,
                        "cargo fill uses capacity, not an assumed constant");
                    t.Equal(7f, visual.style.height.value.value,
                        "empty status group takes no vertical space");

                    overlay.SetShields(61, 15);
                    t.Equal(DisplayStyle.Flex,
                        visual.Q<VisualElement>("entity-status-61-health-shields").style.display.value,
                        "damaged shields reveal paired bars");
                    t.Equal(25f, visual.style.height.value.value,
                        "compact paired bars and cargo occupy distinct vertical intervals");
                    overlay.SetHoveredEntity(61);
                    t.Equal(DisplayStyle.Flex, visual.Q<Label>("status-cargo-label").style.display.value,
                        "hover reveals numeric text");
                    t.Equal(69f, visual.style.height.value.value,
                        "hover expands all visible bars");
                    overlay.SetCargo(61, 0);
                    t.Equal(DisplayStyle.Flex, yellow.style.display.value,
                        "hover keeps the empty cargo bar visible");
                    t.Equal("0 / 20", visual.Q<Label>("status-cargo-label").text,
                        "empty cargo reports zero against the unit's own capacity");
                    t.Equal(69f, visual.style.height.value.value,
                        "hover retains all three expanded bars even with no cargo");
                    overlay.SetHoveredEntity(0);
                    t.Equal(DisplayStyle.None, yellow.style.display.value,
                        "empty cargo disappears when hover ends");
                    t.Equal(16f, visual.style.height.value.value,
                        "only compact damaged health and shields remain");
                    overlay.SetShields(61, 50);
                    t.Equal(DisplayStyle.None, visual.style.display.value,
                        "restoring shields after unloading hides entire indicator");

                    overlay.SetCargo(61, 20);
                    t.Equal("20 / 20", visual.Q<Label>("status-cargo-label").text,
                        "full cargo remains visible (unlike full resource deposits)");
                    overlay.Remove(61);
                    t.Equal(0, overlay.Count, "death clears the cargo indicator");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(anchor);
                }
            });
            Add("StatusTextContrast", "Hover numbers are legible without a dark backing plate", t =>
            {
                var root = new VisualElement();
                var anchor = new GameObject("status-contrast-test");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.TrackUnit(71, anchor.transform,
                        new SimulationStatusValues(90, 100, 30, 50), 5, 15);
                    overlay.SetHoveredEntity(71);
                    foreach (string key in new[] { "health", "shields", "cargo" })
                    {
                        Label value = root.Q<Label>("status-" + key + "-label");
                        t.Check(value != null, key + " numeric text exists");
                        t.Check(value.resolvedStyle.fontSize >= 13f,
                            key + " uses a readable font size");
                        t.Equal(0f, value.style.backgroundColor.value.a,
                            key + " text has no dark backing plate");
                        t.Equal(0f, value.style.unityTextOutlineWidth.value,
                            key + " does not use an overly heavy outline");
                        t.Equal(PickingMode.Ignore, value.pickingMode,
                            key + " text remains non-interactive");
                    }
                    t.Equal(Color.black, root.Q<Label>("status-cargo-label").style.color.value,
                        "yellow bar uses black text for readability");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(anchor);
                }
            });

            Add("StatusHoverAlwaysShowsRelevantBars",
                "Hover reveals full health/shields and yellow capacity, independent of damage or cargo", t =>
            {
                var root = new VisualElement();
                var anchor = new GameObject("status-hover-full-test");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.TrackUnit(81, anchor.transform,
                        new SimulationStatusValues(10, 10, 20, 20), 0, 30);
                    overlay.TrackResource(82, anchor.transform,
                        new SimulationStatusValues(150, 150, 0, 0), 250, 250);
                    var unit = root.Q<VisualElement>("entity-status-81");
                    var resource = root.Q<VisualElement>("entity-status-82");
                    t.Equal(DisplayStyle.None, unit.style.display.value,
                        "fully healthy and unloaded unit has no compact bars");
                    t.Equal(DisplayStyle.None, resource.style.display.value,
                        "full undamaged resource has no compact bars");

                    overlay.SetHoveredEntity(81);
                    t.Equal(DisplayStyle.Flex,
                        unit.Q<VisualElement>("entity-status-81-health-shields").style.display.value,
                        "hover reveals full unit health and shields");
                    t.Equal(DisplayStyle.Flex, unit.Q<VisualElement>("status-cargo-bar").style.display.value,
                        "hover reveals the empty cargo capacity");
                    t.Equal("0 / 30", unit.Q<Label>("status-cargo-label").text,
                        "maximum cargo comes from the unit instance");
                    t.Equal(69f, unit.style.height.value.value,
                        "hovered unit reserves enough room for three expanded bars");
                    t.Equal(DisplayStyle.None,
                        resource.Q<Label>("status-resources-label").style.display.value,
                        "other entities remain unexpanded");

                    overlay.SetHoveredEntity(82);
                    t.Equal(DisplayStyle.None,
                        unit.Q<Label>("status-cargo-label").style.display.value,
                        "previous hover returns to compact presentation");
                    t.Equal(DisplayStyle.Flex,
                        resource.Q<VisualElement>("status-resources-bar").style.display.value,
                        "hover reveals full deposit stock");
                    t.Equal("250 / 250", resource.Q<Label>("status-resources-label").text,
                        "resource maximum is dynamic");
                    t.Equal(69f, resource.style.height.value.value,
                        "hovered resource shows all three bars without overlap");

                    overlay.SetHoveredEntity(0);
                    t.Equal(DisplayStyle.None, unit.style.display.value,
                        "unit returns to hidden state after mouse leaves");
                    t.Equal(DisplayStyle.None, resource.style.display.value,
                        "resource returns to hidden state after mouse leaves");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(anchor);
                }
            });

            Add("NexusConstructionProgress",
                "A Nexus displays actual build progress, even before the first visible unit appears", t =>
            {
                var root = new VisualElement();
                var anchor = new GameObject("nexus-construction-test");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.TrackNexus(91, anchor.transform,
                        new SimulationStatusValues(100, 100, 50, 50));
                    var nexus = root.Q<VisualElement>("entity-status-91");
                    var build = nexus.Q<VisualElement>("status-construction-bar");
                    t.Check(build != null, "Nexus owns a construction bar");
                    t.Equal(DisplayStyle.None, build.style.display.value,
                        "idle Nexus does not display a misleading empty build");

                    overlay.SetConstruction(91, (Fixed)3, (Fixed)12);
                    t.Equal(DisplayStyle.Flex, build.style.display.value,
                        "active build is visible while not hovering");
                    t.Equal(7f, nexus.style.height.value.value,
                        "compact construction is only one thin bar");
                    t.Check(Mathf.Abs(nexus.Q<VisualElement>("status-construction-fill")
                        .style.width.value.value - 25f) < 0.01f,
                        "progress uses the configured duration, not a constant");
                    overlay.SetHoveredEntity(91);
                    t.Equal(69f, nexus.style.height.value.value,
                        "three expanded bars do not overlap");
                    t.Equal("3 / 12", nexus.Q<Label>("status-construction-label").text,
                        "construction numerator and maximum are shown on hover");
                    t.Equal(DisplayStyle.Flex,
                        nexus.Q<VisualElement>("entity-status-91-health-shields").style.display.value,
                        "healthy Nexus status remains visible during hover");
                    overlay.SetConstruction(91, null, (Fixed)12);
                    t.Equal(DisplayStyle.None, build.style.display.value,
                        "completed construction removes the grey bar");
                    overlay.SetHoveredEntity(0);
                    t.Equal(DisplayStyle.None, nexus.style.display.value,
                        "finished fully healthy Nexus has no indicators");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(anchor);
                }
            });

            Add("EntityRuntimeErrorMarker", "Recovering VMs show a red exclamation above their compact status", t =>
            {
                var root = new VisualElement();
                var anchor = new GameObject("error-mark-test");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.TrackUnit(99, anchor.transform,
                        new SimulationStatusValues(100, 100, 50, 50), 0, 15);
                    var visual = root.Q<VisualElement>("entity-status-99");
                    var marker = root.Q<Label>("entity-status-99-error");
                    t.Check(marker != null, "error marker exists for each entity");
                    t.Equal(DisplayStyle.None, marker.style.display.value,
                        "error marker hidden when healthy VM runs");
                    overlay.SetFaulted(99, true);
                    t.Equal(DisplayStyle.Flex, marker.style.display.value,
                        "fault reveals error marker even with full status and empty cargo");
                    t.Equal("!", marker.text, "marker is a red exclamation, not an emoji");
                    t.Equal(PickingMode.Ignore, marker.pickingMode,
                        "marker does not block mouse selection");
                    t.Equal(19f, visual.style.height.value.value,
                        "standalone fault marker reserves exactly one row");
                    overlay.SetFaulted(99, false);
                    t.Equal(DisplayStyle.None, visual.style.display.value,
                        "recovered VM hides marker and whole indicator");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(anchor);
                }
            });

            Add("SelectedUnitMarker", "A selected entity is identified without making the HUD block clicks", t =>
            {
                var root = new VisualElement();
                var anchor = new GameObject("selected-unit-test");
                var overlay = new SimulationStatusOverlay(root);
                try
                {
                    overlay.TrackUnit(121, anchor.transform,
                        new SimulationStatusValues(100, 100, 50, 50), 0, 10);
                    var indicator = root.Q<VisualElement>("entity-status-121");
                    var marker = root.Q<Label>("entity-status-121-selected");
                    t.Equal(DisplayStyle.None, marker.style.display.value, "not selected initially");
                    overlay.SetSelectedEntity(121);
                    t.Equal(DisplayStyle.Flex, marker.style.display.value,
                        "selection marker appears even when all bars are hidden");
                    t.Equal(PickingMode.Ignore, marker.pickingMode, "selection remains click-through");
                    t.Equal(14f, indicator.style.height.value.value,
                        "selection alone reserves a compact marker row");
                    overlay.SetSelectedEntity(0);
                    t.Equal(DisplayStyle.None, indicator.style.display.value,
                        "deselection restores original visibility");
                }
                finally
                {
                    overlay.Dispose();
                    UnityEngine.Object.DestroyImmediate(anchor);
                }
            });

            Add("ResourceInitialAmountPersists", "A deposit retains its original amount for attaching views mid-match", t =>
            {
                var world = new Simulation(100UL, new FixedVector2(50, 50));
                var resource = new SBResource(world.GetSide(0), new FixedVector2(10, 10), 0, 100);
                t.Equal(100, resource.InitialAmount, "capacity stays independent of current amount");
                t.Equal(100, resource.Amount, "initial stock preserved");
            });

            Add("CreateUnitCarriesCargoMaximum", "Unit creation events expose their own capacity to the HUD", t =>
            {
                var world = new Simulation(102UL, new FixedVector2(50, 50));
                var unit = new SBUnit(world.GetSide(1), new FixedVector2(12, 12), 0);
                world.EnqueueCreation(unit);
                world.Start();
                int? capacityFromEvent = null;
                while (world.TryConsumeEvent(out DSEvents.Event message))
                {
                    if (message is DSEvents.EventCreateUnit created && created.id == unit.id)
                        capacityFromEvent = created.maxCargo;
                }
                t.Check(capacityFromEvent.HasValue, "unit creation event emitted");
                t.Equal(unit.MaxCargo, capacityFromEvent.GetValueOrDefault(),
                    "HUD capacity agrees with the unit's own maximum");
            });

            Add("MatchStatsPanelIsReadOnly", "Replay-style HUD has three side metrics and a localized countdown; ignores input", t =>
            {
                var uiRoot = new VisualElement();
                var otherUi = new VisualElement { name = "editor" };
                uiRoot.Add(otherUi);
                var stats = new SimulationMatchStatsBar(uiRoot);
                t.Equal(stats.Root, uiRoot[0], "scoreboard paints under editor");
                t.Equal(otherUi, uiRoot[1], "editor remains in front");
                t.Equal(PickingMode.Ignore, stats.Root.pickingMode, "stat bar is not pickable");
                t.Equal(4, stats.Root.Q<VisualElement>("sim-match-stats").childCount,
                    "three side metrics plus one match countdown");
                t.Check(stats.Root.Q<VisualElement>("match-stat-memory") == null,
                    "lazy-GC heap usage is not displayed");
                foreach (string key in new[] { "resources", "units", "errors" })
                {
                    t.Check(stats.Root.Q<Label>("match-stat-" + key + "-caption") != null,
                        key + " has a localized caption");
                    t.Equal(PickingMode.Ignore,
                        stats.Root.Q<Label>("match-stat-" + key + "-blue").pickingMode,
                        key + " blue number is non-interactive");
                    t.Equal(PickingMode.Ignore,
                        stats.Root.Q<Label>("match-stat-" + key + "-red").pickingMode,
                        key + " red number is non-interactive");
                }
                var world = new Simulation(101UL, new FixedVector2(50, 50));
                world.Start();
                stats.UpdateValues(world, null, 330);
                t.Equal("5:30", stats.Root.Q<Label>("match-stat-time-value").text,
                    "countdown begins at the configured maximum");
                t.Check(stats.Root.Q<Label>("match-stat-time-caption") != null,
                    "localized time caption is present");
                t.Equal(PickingMode.Ignore,
                    stats.Root.Q<Label>("match-stat-time-value").pickingMode,
                    "countdown does not capture world clicks");
                t.Equal("4:59", SimulationMatchStatsBar.FormatRemainingTime(300, 1.0),
                    "countdown uses elapsed simulated seconds");
                t.Equal("5:00", SimulationMatchStatsBar.FormatRemainingTime(300, 0.02),
                    "one tick does not hide a whole second");
                t.Equal("0:00", SimulationMatchStatsBar.FormatRemainingTime(300, 301),
                    "time remaining never becomes negative");
                t.Equal(DisplayStyle.Flex, stats.Root.style.display.value,
                    "stopped-world preview still displays statistics");
                t.Equal("0", stats.Root.Q<Label>("match-stat-units-blue").text,
                    "empty blue army");
                t.Equal("0", stats.Root.Q<Label>("match-stat-errors-red").text,
                    "zero errors without active VMs");
                stats.UpdateValues(null, null);
                t.Equal(DisplayStyle.None, stats.Root.style.display.value,
                    "no world hides obsolete statistics");
            });

            Add("MatchTimerUsesSimulatedTicks",
                "The countdown advances with simulation ticks, not with UI refreshes or real time", t =>
            {
                var root = new VisualElement();
                var stats = new SimulationMatchStatsBar(root);
                var world = new Simulation(109UL, new FixedVector2(50, 50));
                world.Start();
                stats.UpdateValues(world, null, 123);
                Label time = root.Q<Label>("match-stat-time-value");
                t.Equal("2:03", time.text, "time limit comes from match configuration");
                stats.UpdateValues(world, null, 123);
                t.Equal("2:03", time.text, "UI refresh alone does not tick the clock");
                for (int i = 0; i < 50; i++) world.Update();
                stats.UpdateValues(world, null, 123);
                t.Equal("2:02", time.text, "50 backend ticks consume one simulated second");
                stats.UpdateValues(world, null, 123);
                t.Equal("2:02", time.text, "pause without ticks holds remaining time");
            });

            Add("EffectPrefabContracts", "Attack is transient; gather survives until its controller calls End", t =>
            {
                var attack = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Simulation/Attack.prefab");
                var gather = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Simulation/Gather.prefab");
                t.Check(attack != null && gather != null, "both effect prefabs imported");
                t.Check(attack.GetComponent<AttackAligner>() != null, "attack uses AttackAligner");
                t.Check(attack.GetComponent<LifeTimeLimiter>() != null,
                    "attack has its own lifespan");
                t.Check(gather.GetComponent<GatherAligner>() != null, "gather uses GatherAligner");
                t.Check(gather.GetComponent<AttackAligner>() == null,
                    "gather no longer carries an AttackAligner");
                t.Check(gather.GetComponent<LifeTimeLimiter>() == null,
                    "gather has no autonomous lifespan limiter");
            });
        }
    }
}
