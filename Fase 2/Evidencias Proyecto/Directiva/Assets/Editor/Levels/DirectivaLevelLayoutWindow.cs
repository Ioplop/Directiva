using System;
using System.Collections.Generic;
using Directiva.Levels;
using UnityEditor;
using UnityEngine;

namespace Directiva.Editor.Levels
{
    /// <summary>
    /// Orthographic 2D authoring surface for the authoritative initial world.
    /// This is an EditorWindow, NOT a second simulation renderer/scene controller.
    /// </summary>
    public sealed class DirectivaLevelLayoutWindow : EditorWindow
    {
        private enum DragTarget
        {
            None, Pan, Nexus1, Nexus2, Resource, MirrorCenter, MirrorAxis,
            BorderLeft, BorderRight, BorderBottom, BorderTop
        }

        private DirectivaLevel level;
        private Vector2 viewCenter = new Vector2(25f, 25f);
        private float pixelsPerUnit = 12f;
        private Vector2 scroll;
        private Rect canvas;
        private DragTarget dragging;
        private Vector2 dragOffset;
        private int selectedResource = -1;
        private int undoGroup = -1;
        private bool didMove;

        private static readonly Color Background = new Color(0.10f, 0.12f, 0.16f);
        private static readonly Color GridColor = new Color(0.21f, 0.25f, 0.29f);
        private static readonly Color GridMajor = new Color(0.31f, 0.35f, 0.40f);
        private static readonly Color BorderColor = new Color(0.79f, 0.85f, 0.91f);
        private static readonly Color Side1Color = new Color(0.23f, 0.72f, 1f);
        private static readonly Color Side2Color = new Color(1f, 0.38f, 0.40f);
        private static readonly Color ResourceColor = new Color(1f, 0.81f, 0.25f);
        private static readonly Color MirrorColor = new Color(0.95f, 0.38f, 0.95f);

        [MenuItem("Tools/Directiva/Editor de niveles")]
        public static void OpenFromSelection()
        {
            var selected = Selection.activeObject as DirectivaLevel;
            Open(selected);
        }

        public static void Open(DirectivaLevel selected)
        {
            var window = GetWindow<DirectivaLevelLayoutWindow>("Directiva · Layout");
            window.minSize = new Vector2(640f, 380f);
            if (selected != null) window.SetLevel(selected);
            window.Show();
        }

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;
        private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;
        private void OnUndoRedo() { ValidateSelection(); Repaint(); }

        public void SetLevel(DirectivaLevel selected)
        {
            level = selected;
            selectedResource = -1;
            CenterView();
            Repaint();
        }

        private void CenterView()
        {
            if (level == null) return;
            var world = level.World;
            viewCenter = world.Origin + new Vector2(world.Width, world.Height) * 0.5f;
            float fitWidth = Mathf.Max(250f, position.width - 315f);
            float fitHeight = Mathf.Max(250f, position.height - 90f);
            pixelsPerUnit = Mathf.Clamp(Mathf.Min(fitWidth / (world.Width + 8f),
                fitHeight / (world.Height + 8f)), 2f, 80f);
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (level == null)
            {
                EditorGUILayout.HelpBox("Selecciona un DirectivaLevel en el proyecto y abre su layout.",
                    MessageType.Info);
                var selected = (DirectivaLevel)EditorGUILayout.ObjectField("Nivel", null,
                    typeof(DirectivaLevel), false);
                if (selected != null) SetLevel(selected);
                return;
            }

            float width = Mathf.Clamp(position.width * 0.31f, 245f, 320f);
            canvas = new Rect(0f, 24f, Mathf.Max(100f, position.width - width),
                Mathf.Max(100f, position.height - 46f));
            var right = new Rect(canvas.xMax + 2f, 24f, position.width - canvas.xMax - 2f,
                canvas.height);
            DrawCanvas();
            DrawProperties(right);
            GUI.Label(new Rect(7f, position.height - 20f, position.width - 14f, 19f),
                "Arrastra objetos / bordes · arrastra el fondo para mover · rueda para zoom · Ctrl = sin snap",
                EditorStyles.miniLabel);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(23f)))
            {
                if (GUILayout.Button("Centrar", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    CenterView();
                GUILayout.Space(8f);
                GUILayout.Label("Espejo:", GUILayout.Width(52f));
                if (level != null)
                {
                    LevelWorldLayout world = level.World;
                    int mode = GUILayout.Toolbar((int)world.MirrorMode,
                        new[] { "Apagado", "Recta", "Punto" }, EditorStyles.toolbarButton,
                        GUILayout.Width(210f));
                    if (mode != (int)world.MirrorMode)
                    {
                        Change("Cambiar modo espejo", () =>
                        {
                            world.MirrorMode = (LevelMirrorMode)mode;
                            CoupleExistingPairs(world);
                        });
                    }
                }
                GUILayout.FlexibleSpace();
                var selected = (DirectivaLevel)EditorGUILayout.ObjectField(level,
                    typeof(DirectivaLevel), false, GUILayout.Width(170f));
                if (selected != level) SetLevel(selected);
            }
        }

        private Vector2 WorldToCanvas(Vector2 world) =>
            new Vector2(canvas.width * 0.5f + (world.x - viewCenter.x) * pixelsPerUnit,
                canvas.height * 0.5f - (world.y - viewCenter.y) * pixelsPerUnit);
        private Vector2 CanvasToWorld(Vector2 local) =>
            viewCenter + new Vector2((local.x - canvas.width * 0.5f) / pixelsPerUnit,
                -(local.y - canvas.height * 0.5f) / pixelsPerUnit);

        private void DrawCanvas()
        {
            GUI.BeginGroup(canvas);
            EditorGUI.DrawRect(new Rect(0f, 0f, canvas.width, canvas.height), Background);
            DrawMap();
            ProcessCanvasInput(Event.current);
            GUI.EndGroup();
        }

        private void DrawMap()
        {
            LevelWorldLayout w = level.World;
            var bottomLeft = WorldToCanvas(w.Origin);
            var topRight = WorldToCanvas(w.Origin + new Vector2(w.Width, w.Height));
            Handles.BeginGUI();
            if (pixelsPerUnit >= 7f)
            {
                Vector2 visibleMin = CanvasToWorld(new Vector2(0f, canvas.height));
                Vector2 visibleMax = CanvasToWorld(new Vector2(canvas.width, 0f));
                int firstX = Mathf.CeilToInt(Mathf.Max(w.Origin.x, visibleMin.x));
                int lastX = Mathf.FloorToInt(Mathf.Min(w.Origin.x + w.Width, visibleMax.x));
                int firstY = Mathf.CeilToInt(Mathf.Max(w.Origin.y, visibleMin.y));
                int lastY = Mathf.FloorToInt(Mathf.Min(w.Origin.y + w.Height, visibleMax.y));
                for (int x = firstX; x <= lastX; x++)
                {
                    Handles.color = x % 5 == 0 ? GridMajor : GridColor;
                    Line(WorldToCanvas(new Vector2(x, w.Origin.y)),
                        WorldToCanvas(new Vector2(x, w.Origin.y + w.Height)), 1f);
                }
                for (int y = firstY; y <= lastY; y++)
                {
                    Handles.color = y % 5 == 0 ? GridMajor : GridColor;
                    Line(WorldToCanvas(new Vector2(w.Origin.x, y)),
                        WorldToCanvas(new Vector2(w.Origin.x + w.Width, y)), 1f);
                }
            }

            Handles.color = BorderColor;
            var tl = new Vector2(bottomLeft.x, topRight.y);
            var br = new Vector2(topRight.x, bottomLeft.y);
            Line(tl, topRight, 2.5f);
            Line(topRight, br, 2.5f);
            Line(br, bottomLeft, 2.5f);
            Line(bottomLeft, tl, 2.5f);
            DrawHandle(new Vector2((tl.x + topRight.x) * 0.5f, tl.y));
            DrawHandle(new Vector2((br.x + bottomLeft.x) * 0.5f, br.y));
            DrawHandle(new Vector2(tl.x, (tl.y + bottomLeft.y) * 0.5f));
            DrawHandle(new Vector2(topRight.x, (topRight.y + br.y) * 0.5f));

            if (w.MirrorMode != LevelMirrorMode.Off)
            {
                var center = WorldToCanvas(w.MirrorCenter);
                if (w.MirrorMode == LevelMirrorMode.Axis)
                {
                    float radians = w.MirrorAxisDegrees * Mathf.Deg2Rad;
                    var direction = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
                    Handles.color = MirrorColor;
                    Line(center - direction * 4000f, center + direction * 4000f, 2.2f);
                }
                Handles.color = Color.black;
                Handles.DrawSolidDisc(center, Vector3.forward, 10.5f);
                Handles.color = MirrorColor;
                Handles.DrawSolidDisc(center, Vector3.forward, 8f);
                Handles.color = Color.white;
                Line(center + Vector2.left * 12f, center + Vector2.right * 12f, 2f);
                Line(center + Vector2.up * 12f, center + Vector2.down * 12f, 2f);
            }

            DrawDisc(w.NexusSide1, Mathf.Max(6f, level.Settings.nexusRadius * pixelsPerUnit),
                Side1Color, true);
            DrawDisc(w.NexusSide2, Mathf.Max(6f, level.Settings.nexusRadius * pixelsPerUnit),
                Side2Color, true);
            if (w.Deposits != null)
                for (int i = 0; i < w.Deposits.Count; i++)
                {
                    if (w.Deposits[i] == null) continue;
                    DrawDisc(w.Deposits[i].Position,
                        Mathf.Max(5f, level.Settings.resourceRadius * pixelsPerUnit),
                        ResourceColor, i == selectedResource);
                }
            Handles.EndGUI();
            GUI.Label(new Rect(7f, 5f, canvas.width - 10f, 18f),
                $"{w.Width:0.##} × {w.Height:0.##}  ·  {pixelsPerUnit:0.#} px/u  ·  " +
                $"{(w.Deposits != null ? w.Deposits.Count : 0)} depósitos", EditorStyles.whiteMiniLabel);
        }

        private static void Line(Vector2 a, Vector2 b, float thickness) =>
            Handles.DrawAAPolyLine(thickness, new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));

        private static void DrawHandle(Vector2 p)
        {
            Handles.color = Color.black;
            Handles.DrawSolidDisc(p, Vector3.forward, 5.5f);
            Handles.color = BorderColor;
            Handles.DrawSolidDisc(p, Vector3.forward, 3.5f);
        }

        private void DrawDisc(Vector2 world, float radius, Color color, bool highlighted)
        {
            var p = WorldToCanvas(world);
            Handles.color = Color.black;
            Handles.DrawSolidDisc(p, Vector3.forward, radius + 2.5f);
            Handles.color = color;
            Handles.DrawSolidDisc(p, Vector3.forward, radius);
            if (highlighted)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(p, Vector3.forward, radius + 3.5f);
            }
        }

        private void ProcessCanvasInput(Event e)
        {
            if (e.type == EventType.KeyDown && selectedResource >= 0 &&
                (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) &&
                EditorGUIUtility.editingTextField == false)
            {
                RemoveDeposit();
                e.Use();
                return;
            }
            if (e.type == EventType.ScrollWheel)
            {
                Vector2 anchor = CanvasToWorld(e.mousePosition);
                pixelsPerUnit = Mathf.Clamp(pixelsPerUnit * Mathf.Pow(1.12f, -e.delta.y), 1.2f, 160f);
                Vector2 newAnchor = CanvasToWorld(e.mousePosition);
                viewCenter += anchor - newAnchor;
                e.Use();
                Repaint();
                return;
            }
            if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1 || e.button == 2))
            {
                // Right and middle clicks always pan. Left click on empty space pans too.
                if (e.button != 0) dragging = DragTarget.Pan;
                else dragging = Pick(e.mousePosition);
                if (dragging == DragTarget.None)
                    dragging = DragTarget.Pan;
                if (dragging != DragTarget.Resource)
                    selectedResource = -1;
                if (dragging == DragTarget.Resource || dragging == DragTarget.Nexus1 ||
                    dragging == DragTarget.Nexus2 || dragging == DragTarget.MirrorCenter)
                {
                    Vector2 at = dragging == DragTarget.Resource
                        ? level.World.Deposits[selectedResource].Position
                        : dragging == DragTarget.Nexus1 ? level.World.NexusSide1
                        : dragging == DragTarget.Nexus2 ? level.World.NexusSide2
                        : level.World.MirrorCenter;
                    dragOffset = at - CanvasToWorld(e.mousePosition);
                }
                didMove = false;
                undoGroup = Undo.GetCurrentGroup();
                if (dragging != DragTarget.Pan) Undo.SetCurrentGroupName("Editar layout Directiva");
                e.Use();
                Repaint();
                return;
            }
            if (e.type == EventType.MouseDrag && dragging != DragTarget.None)
            {
                if (dragging == DragTarget.Pan)
                    viewCenter += new Vector2(-e.delta.x, e.delta.y) / pixelsPerUnit;
                else
                {
                    Vector2 at = CanvasToWorld(e.mousePosition);
                    if (dragging == DragTarget.MirrorAxis) RotateMirror(at, e.control);
                    else if (dragging == DragTarget.BorderLeft || dragging == DragTarget.BorderRight ||
                        dragging == DragTarget.BorderBottom || dragging == DragTarget.BorderTop)
                        ResizeWorld(at, e.control);
                    else MoveSelected(at + dragOffset, e.control);
                    didMove = true;
                }
                e.Use();
                Repaint();
                return;
            }
            if ((e.type == EventType.MouseUp || e.type == EventType.MouseLeaveWindow) &&
                dragging != DragTarget.None)
            {
                if (didMove && undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);
                dragging = DragTarget.None;
                undoGroup = -1;
                e.Use();
                Repaint();
            }
        }

        private DragTarget Pick(Vector2 mouse)
        {
            LevelWorldLayout w = level.World;
            if (w.MirrorMode != LevelMirrorMode.Off)
            {
                Vector2 c = WorldToCanvas(w.MirrorCenter);
                if (Vector2.Distance(mouse, c) <= 13f) return DragTarget.MirrorCenter;
            }
            if (Vector2.Distance(mouse, WorldToCanvas(w.NexusSide1)) <=
                Mathf.Max(9f, level.Settings.nexusRadius * pixelsPerUnit + 4f)) return DragTarget.Nexus1;
            if (Vector2.Distance(mouse, WorldToCanvas(w.NexusSide2)) <=
                Mathf.Max(9f, level.Settings.nexusRadius * pixelsPerUnit + 4f)) return DragTarget.Nexus2;
            if (w.Deposits != null)
                for (int i = w.Deposits.Count - 1; i >= 0; i--)
                {
                    if (w.Deposits[i] == null) continue;
                    if (Vector2.Distance(mouse, WorldToCanvas(w.Deposits[i].Position)) <=
                        Mathf.Max(8f, level.Settings.resourceRadius * pixelsPerUnit + 3f))
                    {
                        selectedResource = i;
                        return DragTarget.Resource;
                    }
                }
            if (w.MirrorMode == LevelMirrorMode.Axis)
            {
                Vector2 c = WorldToCanvas(w.MirrorCenter);
                float r = w.MirrorAxisDegrees * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
                Vector2 relative = mouse - c;
                if (Mathf.Abs(relative.x * d.y - relative.y * d.x) <= 7f &&
                    relative.magnitude > 14f) return DragTarget.MirrorAxis;
            }

            // All four sides are draggable. Origin changes only the editor space;
            // entity world coordinates are converted relative to that origin on load.
            Vector2 topLeft = WorldToCanvas(w.Origin + Vector2.up * w.Height);
            Vector2 bottomRight = WorldToCanvas(w.Origin + Vector2.right * w.Width);
            if (mouse.y >= topLeft.y - 7f && mouse.y <= bottomRight.y + 7f)
            {
                if (Mathf.Abs(mouse.x - topLeft.x) <= 8f) return DragTarget.BorderLeft;
                if (Mathf.Abs(mouse.x - bottomRight.x) <= 8f) return DragTarget.BorderRight;
            }
            if (mouse.x >= topLeft.x - 7f && mouse.x <= bottomRight.x + 7f)
            {
                if (Mathf.Abs(mouse.y - topLeft.y) <= 8f) return DragTarget.BorderTop;
                if (Mathf.Abs(mouse.y - bottomRight.y) <= 8f) return DragTarget.BorderBottom;
            }
            return DragTarget.None;
        }

        private static Vector2 Snap(Vector2 position, bool free) => free ? position :
            new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));

        private static Vector2 Clamp(Vector2 pos, LevelWorldLayout layout, float radius) =>
            new Vector2(Mathf.Clamp(pos.x, layout.Origin.x + radius,
                    layout.Origin.x + layout.Width - radius),
                Mathf.Clamp(pos.y, layout.Origin.y + radius,
                    layout.Origin.y + layout.Height - radius));

        private void MoveSelected(Vector2 worldPosition, bool free)
        {
            LevelWorldLayout w = level.World;
            Vector2 pos = Snap(worldPosition, free);
            if (dragging == DragTarget.MirrorCenter)
            {
                Change("Mover centro espejo", () =>
                {
                    w.MirrorCenter = pos;
                    RefreshMirroredPositions(w);
                });
            }
            else if (dragging == DragTarget.Nexus1 || dragging == DragTarget.Nexus2)
            {
                pos = Clamp(pos, w, level.Settings.nexusRadius);
                Vector2 counterpart = LevelMirrorMath.Reflect(pos, w.MirrorMode,
                    w.MirrorCenter, w.MirrorAxisDegrees);
                if (w.MirrorMode != LevelMirrorMode.Off &&
                    !w.Contains(counterpart, level.Settings.nexusRadius)) return;
                Change("Mover nexo", () =>
                {
                    if (dragging == DragTarget.Nexus1) w.NexusSide1 = pos;
                    else w.NexusSide2 = pos;
                    if (w.MirrorMode != LevelMirrorMode.Off)
                    {
                        if (dragging == DragTarget.Nexus1) w.NexusSide2 = counterpart;
                        else w.NexusSide1 = counterpart;
                    }
                });
            }
            else if (dragging == DragTarget.Resource && selectedResource >= 0 &&
                selectedResource < w.Deposits.Count)
            {
                pos = Clamp(pos, w, level.Settings.resourceRadius);
                Vector2 counterpart = LevelMirrorMath.Reflect(pos, w.MirrorMode,
                    w.MirrorCenter, w.MirrorAxisDegrees);
                if (w.MirrorMode != LevelMirrorMode.Off &&
                    !w.Contains(counterpart, level.Settings.resourceRadius)) return;
                Change("Mover depósito", () =>
                {
                    if (w.MirrorMode != LevelMirrorMode.Off)
                        EnsureResourcePair(w, selectedResource);
                    LevelResourcePlacement selected = w.Deposits[selectedResource];
                    selected.Position = pos;
                    UpdateResourcePartner(w, selectedResource);
                });
            }
        }

        private void RotateMirror(Vector2 position, bool free)
        {
            LevelWorldLayout w = level.World;
            Vector2 offset = position - w.MirrorCenter;
            if (offset.sqrMagnitude < 0.01f) return;
            float degrees = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            if (!free) degrees = Mathf.Round(degrees / 15f) * 15f;
            Change("Rotar espejo", () =>
            {
                w.MirrorAxisDegrees = degrees;
                RefreshMirroredPositions(w);
            });
        }

        private void ResizeWorld(Vector2 position, bool free)
        {
            LevelWorldLayout w = level.World;
            float coord = dragging == DragTarget.BorderLeft || dragging == DragTarget.BorderRight
                ? position.x : position.y;
            if (!free) coord = Mathf.Round(coord);
            Vector2 origin = w.Origin;
            float width = w.Width, height = w.Height;
            switch (dragging)
            {
                case DragTarget.BorderLeft:
                    coord = Mathf.Min(coord, origin.x + width - 16f);
                    width += origin.x - coord;
                    origin.x = coord;
                    break;
                case DragTarget.BorderRight:
                    width = Mathf.Max(16f, coord - origin.x);
                    break;
                case DragTarget.BorderBottom:
                    coord = Mathf.Min(coord, origin.y + height - 16f);
                    height += origin.y - coord;
                    origin.y = coord;
                    break;
                case DragTarget.BorderTop:
                    height = Mathf.Max(16f, coord - origin.y);
                    break;
            }
            // Reflect the opposite border when reflection preserves an axis-aligned
            // rectangle. For oblique axes there is no axis-aligned mirrored border.
            bool reflectHorizontal = w.MirrorMode == LevelMirrorMode.Point;
            bool reflectVertical = w.MirrorMode == LevelMirrorMode.Point;
            if (w.MirrorMode == LevelMirrorMode.Axis)
            {
                float radians = w.MirrorAxisDegrees * Mathf.Deg2Rad;
                reflectHorizontal = Mathf.Abs(Mathf.Cos(radians)) < 0.001f; // Vertical mirror axis.
                reflectVertical = Mathf.Abs(Mathf.Sin(radians)) < 0.001f;   // Horizontal mirror axis.
            }
            if (reflectHorizontal && (dragging == DragTarget.BorderLeft ||
                dragging == DragTarget.BorderRight))
            {
                float editedEdge = dragging == DragTarget.BorderLeft ? origin.x : origin.x + width;
                float opposite = 2f * w.MirrorCenter.x - editedEdge;
                origin.x = Mathf.Min(editedEdge, opposite);
                width = Mathf.Abs(editedEdge - opposite);
            }
            if (reflectVertical && (dragging == DragTarget.BorderBottom ||
                dragging == DragTarget.BorderTop))
            {
                float editedEdge = dragging == DragTarget.BorderBottom ? origin.y : origin.y + height;
                float opposite = 2f * w.MirrorCenter.y - editedEdge;
                origin.y = Mathf.Min(editedEdge, opposite);
                height = Mathf.Abs(editedEdge - opposite);
            }
            if (width < 16f || height < 16f) return;
            // Never silently move entities or allow a border to cut through them.
            if (!Fits(w, origin, width, height)) return;
            Change("Mover borde", () =>
            {
                w.Origin = origin;
                w.Width = width;
                w.Height = height;
            });
        }

        private bool Fits(LevelWorldLayout w, Vector2 origin, float width, float height)
        {
            bool Inside(Vector2 pos, float r) => pos.x - r >= origin.x &&
                pos.y - r >= origin.y && pos.x + r <= origin.x + width &&
                pos.y + r <= origin.y + height;
            if (!Inside(w.NexusSide1, level.Settings.nexusRadius) ||
                !Inside(w.NexusSide2, level.Settings.nexusRadius)) return false;
            if (w.Deposits != null)
                foreach (LevelResourcePlacement res in w.Deposits)
                    if (res != null && !Inside(res.Position, level.Settings.resourceRadius))
                        return false;
            return true;
        }

        private static int NewPairId(LevelWorldLayout w)
        {
            int id = 1;
            foreach (LevelResourcePlacement res in w.Deposits)
                if (res != null) id = Mathf.Max(id, res.MirrorPairId + 1);
            return id;
        }

        private static int FindPartner(LevelWorldLayout w, int index)
        {
            if (index < 0 || index >= w.Deposits.Count) return -1;
            int pair = w.Deposits[index].MirrorPairId;
            if (pair == 0) return -1;
            for (int i = 0; i < w.Deposits.Count; i++)
                if (i != index && w.Deposits[i].MirrorPairId == pair) return i;
            return -1;
        }

        // Called as part of an Undo transaction. Never pair by proximity after a
        // pair is established: identical or overlapping deposits may coexist.
        private static void EnsureResourcePair(LevelWorldLayout w, int index)
        {
            if (w.MirrorMode == LevelMirrorMode.Off || FindPartner(w, index) >= 0) return;
            LevelResourcePlacement source = w.Deposits[index];
            Vector2 mirror = LevelMirrorMath.Reflect(source.Position,
                w.MirrorMode, w.MirrorCenter, w.MirrorAxisDegrees);
            if (!w.Contains(mirror)) return;
            if (Vector2.Distance(mirror, source.Position) <= 0.0001f) return;
            for (int i = 0; i < w.Deposits.Count; i++)
            {
                if (i == index || w.Deposits[i].MirrorPairId != 0) continue;
                if (Vector2.Distance(w.Deposits[i].Position, mirror) > 0.001f) continue;
                int existingPair = NewPairId(w);
                source.MirrorPairId = existingPair;
                w.Deposits[i].MirrorPairId = existingPair;
                return;
            }
            int pair = NewPairId(w);
            source.MirrorPairId = pair;
            w.Deposits.Add(new LevelResourcePlacement(mirror, source.Amount,
                source.Richness, pair));
        }

        private static void CoupleExistingPairs(LevelWorldLayout w)
        {
            if (w.MirrorMode == LevelMirrorMode.Off || w.Deposits == null) return;
            for (int i = 0; i < w.Deposits.Count; i++)
            {
                if (w.Deposits[i].MirrorPairId != 0) continue;
                Vector2 mirror = LevelMirrorMath.Reflect(w.Deposits[i].Position,
                    w.MirrorMode, w.MirrorCenter, w.MirrorAxisDegrees);
                for (int j = i + 1; j < w.Deposits.Count; j++)
                    if (w.Deposits[j].MirrorPairId == 0 &&
                        (w.Deposits[j].Position - mirror).sqrMagnitude < 0.000001f &&
                        w.Deposits[j].Amount == w.Deposits[i].Amount &&
                        w.Deposits[j].Richness == w.Deposits[i].Richness)
                    {
                        int pair = NewPairId(w);
                        w.Deposits[i].MirrorPairId = pair;
                        w.Deposits[j].MirrorPairId = pair;
                        break;
                    }
            }
        }

        private static void UpdateResourcePartner(LevelWorldLayout w, int index)
        {
            if (w.MirrorMode == LevelMirrorMode.Off) return;
            int partner = FindPartner(w, index);
            if (partner < 0) return;
            var source = w.Deposits[index];
            var target = w.Deposits[partner];
            target.Position = LevelMirrorMath.Reflect(source.Position,
                w.MirrorMode, w.MirrorCenter, w.MirrorAxisDegrees);
            target.Amount = source.Amount;
            target.Richness = source.Richness;
        }

        private static void RefreshMirroredPositions(LevelWorldLayout w)
        {
            if (w.MirrorMode == LevelMirrorMode.Off) return;
            w.NexusSide2 = LevelMirrorMath.Reflect(w.NexusSide1,
                w.MirrorMode, w.MirrorCenter, w.MirrorAxisDegrees);
            var seen = new HashSet<int>();
            if (w.Deposits == null) return;
            for (int i = 0; i < w.Deposits.Count; i++)
            {
                int pair = w.Deposits[i].MirrorPairId;
                if (pair == 0 || !seen.Add(pair)) continue;
                UpdateResourcePartner(w, i);
            }
        }

        private void AddDeposit()
        {
            LevelWorldLayout w = level.World;
            Vector2 position = Clamp(Snap(viewCenter, false), w, level.Settings.resourceRadius);
            Vector2 reflection = LevelMirrorMath.Reflect(position, w.MirrorMode,
                w.MirrorCenter, w.MirrorAxisDegrees);
            if (w.MirrorMode != LevelMirrorMode.Off &&
                !w.Contains(reflection, level.Settings.resourceRadius))
            {
                ShowNotification(new GUIContent("El depósito reflejado quedaría fuera del mundo."));
                return;
            }
            Change("Crear recurso", () =>
            {
                if (w.Deposits == null) w.Deposits = new List<LevelResourcePlacement>();
                w.Deposits.Add(new LevelResourcePlacement(position,
                    Mathf.Max(1, level.Settings.resourceMaxAmount),
                    Mathf.Max(1, level.Settings.resourceGatherAmount)));
                selectedResource = w.Deposits.Count - 1;
                EnsureResourcePair(w, selectedResource);
            });
        }

        private void RemoveDeposit()
        {
            LevelWorldLayout w = level.World;
            if (w.Deposits == null || selectedResource < 0 || selectedResource >= w.Deposits.Count)
                return;
            Change("Borrar recurso", () =>
            {
                int existingPartner = FindPartner(w, selectedResource);
                int partner = w.MirrorMode == LevelMirrorMode.Off ? -1 : existingPartner;
                if (w.MirrorMode == LevelMirrorMode.Off && existingPartner >= 0)
                    w.Deposits[existingPartner].MirrorPairId = 0;
                if (partner >= 0 && partner > selectedResource)
                    w.Deposits.RemoveAt(partner);
                w.Deposits.RemoveAt(selectedResource);
                if (partner >= 0 && partner < selectedResource)
                    w.Deposits.RemoveAt(partner);
                selectedResource = -1;
            });
        }

        private void ValidateSelection()
        {
            if (level == null || level.World.Deposits == null ||
                selectedResource >= level.World.Deposits.Count) selectedResource = -1;
        }

        private void Change(string label, Action action)
        {
            if (level == null) return;
            Undo.RecordObject(level, label);
            action();
            EditorUtility.SetDirty(level);
            Repaint();
        }

        private void DrawProperties(Rect right)
        {
            // GUI.BeginGroup only clips controls: it does not constrain GUILayout's
            // automatic layout width. Use a layout area so the sidebar fields (in
            // particular both Vector2 coordinates) measure against this panel.
            GUILayout.BeginArea(right, EditorStyles.helpBox);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Layout del nivel", EditorStyles.boldLabel);
            LevelWorldLayout w = level.World;
            DrawVector("Origen", w.Origin, v => w.Origin = v);
            DrawFloat("Ancho", w.Width, v => w.Width = Mathf.Max(16f, v));
            DrawFloat("Alto", w.Height, v => w.Height = Mathf.Max(16f, v));
            DrawInt("Semilla", w.Seed, v => w.Seed = v);
            DrawInt("Recursos iniciales P1", w.InitialResourcesSide1,
                v => w.InitialResourcesSide1 = Mathf.Max(0, v));
            DrawInt("Recursos iniciales P2", w.InitialResourcesSide2,
                v => w.InitialResourcesSide2 = Mathf.Max(0, v));
            GUILayout.Space(6f);
            GUILayout.Label("Nexos", EditorStyles.boldLabel);
            DrawVector("Jugador 1", w.NexusSide1, v =>
            {
                w.NexusSide1 = v;
                if (w.MirrorMode != LevelMirrorMode.Off) RefreshMirroredPositions(w);
            });
            DrawVector("Jugador 2", w.NexusSide2, v =>
            {
                w.NexusSide2 = v;
                if (w.MirrorMode != LevelMirrorMode.Off)
                    w.NexusSide1 = LevelMirrorMath.Reflect(v, w.MirrorMode,
                        w.MirrorCenter, w.MirrorAxisDegrees);
            });
            GUILayout.Space(7f);
            GUILayout.Label("Depósitos de recursos", EditorStyles.boldLabel);
            if (GUILayout.Button("+ Crear depósito")) AddDeposit();
            ValidateSelection();
            if (selectedResource >= 0)
            {
                LevelResourcePlacement res = w.Deposits[selectedResource];
                GUILayout.Label("Seleccionado: #" + (selectedResource + 1), EditorStyles.miniBoldLabel);
                DrawVector("Posición", res.Position, v =>
                {
                    res.Position = v;
                    if (w.MirrorMode != LevelMirrorMode.Off)
                    {
                        EnsureResourcePair(w, selectedResource);
                        UpdateResourcePartner(w, selectedResource);
                    }
                });
                DrawInt("Cantidad", res.Amount, v =>
                {
                    EnsureResourcePair(w, selectedResource);
                    res.Amount = Mathf.Max(1, v);
                    UpdateResourcePartner(w, selectedResource);
                });
                DrawInt("Riqueza", res.Richness, v =>
                {
                    EnsureResourcePair(w, selectedResource);
                    res.Richness = Mathf.Max(1, v);
                    UpdateResourcePartner(w, selectedResource);
                });
                if (GUILayout.Button("Eliminar seleccionado")) RemoveDeposit();
            }
            GUILayout.Space(10f);
            GUILayout.Label("Herramienta espejo", EditorStyles.boldLabel);
            if (w.MirrorMode != LevelMirrorMode.Off)
            {
                DrawVector("Centro", w.MirrorCenter, v =>
                {
                    w.MirrorCenter = v;
                    RefreshMirroredPositions(w);
                });
                if (w.MirrorMode == LevelMirrorMode.Axis)
                    DrawFloat("Ángulo (grados)", w.MirrorAxisDegrees, v =>
                    {
                        w.MirrorAxisDegrees = v;
                        RefreshMirroredPositions(w);
                    });
                if (GUILayout.Button("Simetrizar depósitos sin pareja"))
                {
                    Change("Crear parejas espejo", () =>
                    {
                        int count = w.Deposits.Count;
                        for (int i = 0; i < count; i++)
                            if (w.Deposits[i].MirrorPairId == 0) EnsureResourcePair(w, i);
                    });
                }
            }
            GUILayout.Space(10f);
            if (!Fits(w, w.Origin, w.Width, w.Height))
                EditorGUILayout.HelpBox("Hay entidades fuera del mapa. Ajusta los bordes o las posiciones.",
                    MessageType.Error);
            if (w.MirrorMode == LevelMirrorMode.Axis)
                GUILayout.Label("Los bordes se reflejan cuando el eje es horizontal o vertical. " +
                    "Con ángulos oblicuos, solo se reflejan objetos.",
                    EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("Guarda el asset con Ctrl+S de Unity.\nLos cambios son Undo/Redo.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawVector(string label, Vector2 old, Action<Vector2> apply)
        {
            // Vector2Field relies on the EditorWindow's global wide-mode setting.
            // In this narrow sidebar, that can put the Y component off-screen.
            // Draw both coordinates explicitly inside the available sidebar width.
            EditorGUILayout.LabelField(label);
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            const float gap = 8f;
            float fieldWidth = (row.width - gap) * 0.5f;
            Rect xRect = new Rect(row.x, row.y, fieldWidth, row.height);
            Rect yRect = new Rect(row.x + fieldWidth + gap, row.y, fieldWidth, row.height);

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 18f;
            EditorGUI.BeginChangeCheck();
            float x = EditorGUI.FloatField(xRect, "X", old.x);
            float y = EditorGUI.FloatField(yRect, "Y", old.y);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUIUtility.labelWidth = previousLabelWidth;
            if (changed)
            {
                Vector2 value = new Vector2(x, y);
                Change("Editar " + label, () => apply(value));
            }
        }
        private void DrawFloat(string label, float old, Action<float> apply)
        {
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(label, old);
            if (EditorGUI.EndChangeCheck()) Change("Editar " + label, () => apply(value));
        }
        private void DrawInt(string label, int old, Action<int> apply)
        {
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(label, old);
            if (EditorGUI.EndChangeCheck()) Change("Editar " + label, () => apply(value));
        }
    }
}
