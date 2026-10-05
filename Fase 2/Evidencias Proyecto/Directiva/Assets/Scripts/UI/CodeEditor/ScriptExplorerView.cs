using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    public sealed class ScriptExplorerView : VisualElement
    {
        private sealed class NodeInfo
        {
            public string Path;
            public bool IsFolder;
            public bool IsRoot;
        }

        private readonly IScriptStorage _storage;
        private readonly ScriptWorkspace _workspace;
        private readonly LocalizationService _locale;

        private readonly VisualElement _toolbar;
        private readonly Button _newScript;
        private readonly Button _newFolder;
        private readonly ScrollView _tree;
        private readonly VisualElement _popupLayer;
        private readonly HashSet<string> _expanded =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const float MinimumFontSize = 6f;
        private const float MaximumFontSize = 48f;
        private const float ZoomStep = 1f;

        private float _fontSize;
        private HoverTooltipController _tooltips;

        private string _selectedPath;
        private string _creationTargetFolder = string.Empty;
        private VisualElement _contextMenu;

        private NodeInfo _dragSource;
        private Vector2 _dragStart;
        private bool _dragging;
        private VisualElement _dragOverRow;

        public event Action<string> ScriptSelected;
        public event Action<string> SaveQuickRequested;
        public event Action<string> RevertQuickRequested;
        public event Action<string, string> RenameRequested;
        public event Action<string> DuplicateRequested;
        public event Action<string> DeleteRequested;
        public event Action<string, string> MoveRequested;
        public event Action NewScriptRequested;
        public event Action NewFolderRequested;
        public event Action<float> FontSizeChanged;

        public ScriptExplorerView(
            IScriptStorage storage,
            ScriptWorkspace workspace,
            LocalizationService locale,
            VisualElement popupLayer,
            float fontSize)
        {
            _storage = storage;
            _workspace = workspace;
            _locale = locale;
            _popupLayer = popupLayer;
            _fontSize = Mathf.Clamp(fontSize, MinimumFontSize, MaximumFontSize);

            AddToClassList("script-explorer");
            style.fontSize = _fontSize;

            RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);

            _toolbar = new VisualElement();
            _toolbar.AddToClassList("explorer-toolbar");

            _newScript = new Button(() => NewScriptRequested?.Invoke());
            _newFolder = new Button(() => NewFolderRequested?.Invoke());

            _newScript.AddToClassList("explorer-symbol-button");
            _newFolder.AddToClassList("explorer-symbol-button");

            _toolbar.Add(_newScript);
            _toolbar.Add(_newFolder);
            ApplyToolbarFontSize();

            _tree = new ScrollView(ScrollViewMode.Vertical);
            _tree.AddToClassList("script-tree");
            _tree.AddToClassList("directiva-scroll-view");

            // Clicking unused explorer space selects "/" as the creation/drop target
            // without closing or changing the currently active script.
            _tree.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                var element = evt.target as VisualElement;
                if (FindAncestorRow(element) != null)
                    return;

                _creationTargetFolder = string.Empty;
                HideContextMenu();
                Rebuild();
            });

            Add(_toolbar);
            Add(_tree);

            _workspace.TreeChanged += Rebuild;
            _workspace.ScriptStateChanged += _ => Rebuild();
            _workspace.ActiveChanged += doc =>
            {
                _selectedPath = doc?.RelativePath;
                Rebuild();
            };
            _workspace.PathChanged += (oldPath, newPath) =>
            {
                if (string.Equals(_selectedPath, oldPath, StringComparison.OrdinalIgnoreCase))
                    _selectedPath = newPath;

                RemapExpanded(oldPath, newPath);
                Rebuild();
            };

            _locale.LocaleChanged += _ => RefreshLocale();

            RegisterCallback<PointerMoveEvent>(OnRootPointerMove);
            RegisterCallback<PointerUpEvent>(OnRootPointerUp);

            RefreshLocale();
            Rebuild();
        }

        public void RegisterTooltips(HoverTooltipController tooltips)
        {
            _tooltips = tooltips;

            if (_tooltips == null)
                return;

            _tooltips.Register(
                _newScript,
                () => _locale.Get("Tooltips", "new_script"));

            _tooltips.Register(
                _newFolder,
                () => _locale.Get("Tooltips", "new_folder"));

            // Rebuild so currently visible dynamic tree buttons are registered too.
            Rebuild();
        }

        public string SelectedPath => _selectedPath;

        public void SetFontSize(float fontSize)
        {
            var clamped = Mathf.Clamp(fontSize, MinimumFontSize, MaximumFontSize);
            if (Mathf.Approximately(clamped, _fontSize))
                return;

            _fontSize = clamped;
            style.fontSize = _fontSize;
            ApplyToolbarFontSize();
            Rebuild();
        }

        private void OnWheel(WheelEvent evt)
        {
            if (!evt.ctrlKey)
                return;

            var direction = evt.delta.y < 0f ? 1f : -1f;
            var next = Mathf.Clamp(
                _fontSize + (direction * ZoomStep),
                MinimumFontSize,
                MaximumFontSize);

            if (!Mathf.Approximately(next, _fontSize))
            {
                _fontSize = next;
                style.fontSize = _fontSize;
                ApplyToolbarFontSize();
                Rebuild();
                FontSizeChanged?.Invoke(_fontSize);
            }

            // Ctrl+wheel belongs to Explorer zoom, not its ScrollView.
            evt.StopImmediatePropagation();
        }

        public string GetPreferredCreationFolder()
        {
            return Normalize(_creationTargetFolder);
        }

        public void Select(string path)
        {
            _selectedPath = Normalize(path);

            if (_storage.DirectoryExists(_selectedPath))
                _creationTargetFolder = _selectedPath;
            else
                _creationTargetFolder = Normalize(Path.GetDirectoryName(_selectedPath) ?? string.Empty);

            Rebuild();
        }

        private void ApplyToolbarFontSize()
        {
            _newScript.style.fontSize = _fontSize;
            _newFolder.style.fontSize = _fontSize;
        }

        private void RefreshLocale()
        {
            // Reuse the same visual vocabulary used by the tree:
            // "≡" means script and "▰" means folder.
            _newScript.text = "＋≡";
            _newFolder.text = "＋▰";

            _newScript.tooltip = _locale.Get("ScriptExplorer", "new_script");
            _newFolder.tooltip = _locale.Get("ScriptExplorer", "new_folder");
        }

        private void Rebuild()
        {
            _tree.Clear();
            _tree.Add(BuildRootNode());
            BuildFolderContents(string.Empty, _tree.contentContainer, 0);
        }

        private VisualElement BuildRootNode()
        {
            var row = new VisualElement();
            row.AddToClassList("tree-row");
            row.AddToClassList("root-row");
            row.style.fontSize = _fontSize;
            row.userData = new NodeInfo
            {
                Path = string.Empty,
                IsFolder = true,
                IsRoot = true
            };

            if (string.IsNullOrEmpty(_creationTargetFolder))
                row.AddToClassList("creation-target");

            var spacer = new VisualElement();
            spacer.AddToClassList("tree-root-spacer");

            var state = new VisualElement();
            state.AddToClassList("tree-state");

            var hasError = HasErrorRecursive(string.Empty, true);
            var dirty = HasDirtyRecursive(string.Empty, true);

            if (hasError)
                row.AddToClassList("state-error");
            else if (dirty)
                row.AddToClassList("state-dirty");

            if (hasError && dirty)
                state.AddToClassList("state-dirty-dot");

            var typeIcon = new Label("▰");
            typeIcon.AddToClassList("tree-type-icon");
            typeIcon.style.fontSize = _fontSize;
            typeIcon.AddToClassList("folder-icon");
            typeIcon.tooltip = _locale.Get("ScriptExplorer", "root");

            var label = new Label("/");
            label.AddToClassList("tree-label");
            label.style.fontSize = _fontSize;
            label.AddToClassList("root-label");

            row.Add(spacer);
            row.Add(state);
            row.Add(typeIcon);
            row.Add(label);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                // Root is a selection/drop target only:
                // no rename, duplicate, delete, drag source or context menu.
                if (evt.button == 0)
                {
                    _creationTargetFolder = string.Empty;
                    HideContextMenu();
                    Rebuild();
                    evt.StopPropagation();
                }
                else if (evt.button == 1)
                {
                    HideContextMenu();
                    evt.StopPropagation();
                }
            });

            return row;
        }

        private void BuildFolderContents(string folder, VisualElement parent, int depth)
        {
            foreach (var dir in _storage.EnumerateDirectories(folder))
                parent.Add(BuildFolderNode(dir, depth));

            foreach (var script in _storage.EnumerateScripts(folder))
                parent.Add(BuildScriptNode(script, depth));
        }

        private VisualElement BuildFolderNode(string path, int depth)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("tree-wrapper");

            var row = CreateBaseRow(path, true, depth);
            var toggle = new Button(() =>
            {
                if (_expanded.Contains(path))
                    _expanded.Remove(path);
                else
                    _expanded.Add(path);

                Rebuild();
            });

            toggle.AddToClassList("tree-toggle");
            toggle.text = _expanded.Contains(path) ? "▼" : "▶";

            _tooltips?.Register(
                toggle,
                () => _expanded.Contains(path)
                    ? _locale.Get("Tooltips", "collapse_folder")
                    : _locale.Get("Tooltips", "expand_folder"));

            row.Insert(0, toggle);

            var children = new VisualElement();
            children.AddToClassList("tree-children");

            wrapper.Add(row);

            if (_expanded.Contains(path))
            {
                BuildFolderContents(path, children, depth + 1);
                wrapper.Add(children);
            }

            return wrapper;
        }

        private VisualElement BuildScriptNode(string path, int depth)
        {
            var row = CreateBaseRow(path, false, depth);

            if (_workspace.IsDirty(path))
            {
                var save = new Button(() => SaveQuickRequested?.Invoke(path)) { text = "✓" };
                save.AddToClassList("tree-mini-button");

                var revert = new Button(() => RevertQuickRequested?.Invoke(path)) { text = "↶" };
                revert.AddToClassList("tree-mini-button");

                _tooltips?.Register(
                    save,
                    () => _locale.Get("Tooltips", "save"));

                _tooltips?.Register(
                    revert,
                    () => _locale.Get("Tooltips", "revert"));

                row.Add(save);
                row.Add(revert);
            }

            return row;
        }

        private VisualElement CreateBaseRow(string path, bool isFolder, int depth)
        {
            var row = new VisualElement();
            row.AddToClassList("tree-row");
            row.style.fontSize = _fontSize;
            row.style.paddingLeft = 6 + depth * 14;
            row.userData = new NodeInfo { Path = path, IsFolder = isFolder };

            var state = new VisualElement();
            state.AddToClassList("tree-state");

            var hasError = HasErrorRecursive(path, isFolder);
            var dirty = HasDirtyRecursive(path, isFolder);

            if (hasError)
                row.AddToClassList("state-error");
            else if (dirty)
                row.AddToClassList("state-dirty");

            if (hasError && dirty)
                state.AddToClassList("state-dirty-dot");

            var typeIcon = new Label(isFolder ? "▰" : "≡");
            typeIcon.AddToClassList("tree-type-icon");
            typeIcon.style.fontSize = _fontSize;
            typeIcon.AddToClassList(isFolder ? "folder-icon" : "script-icon");
            typeIcon.tooltip = isFolder
                ? _locale.Get("ScriptExplorer", "folder")
                : _locale.Get("ScriptExplorer", "script");

            var label = new Label(Path.GetFileName(path));
            label.AddToClassList("tree-label");
            label.style.fontSize = _fontSize;

            if (string.Equals(_selectedPath, path, StringComparison.OrdinalIgnoreCase))
                row.AddToClassList("selected");

            if (isFolder &&
                string.Equals(_creationTargetFolder, path, StringComparison.OrdinalIgnoreCase))
            {
                row.AddToClassList("creation-target");
            }

            row.Add(state);
            row.Add(typeIcon);
            row.Add(label);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    ShowContextMenu(path, isFolder, evt.position);
                    evt.StopPropagation();
                    return;
                }

                if (evt.button != 0)
                    return;

                _dragSource = new NodeInfo { Path = path, IsFolder = isFolder };
                _dragStart = evt.position;
                _dragging = false;

                if (isFolder)
                {
                    _creationTargetFolder = path;
                }
                else
                {
                    if (string.Equals(_selectedPath, path, StringComparison.OrdinalIgnoreCase) &&
                        evt.target == label)
                    {
                        BeginInlineRename(row, label, path, false);
                        evt.StopPropagation();
                        return;
                    }
                    else
                    {
                        _selectedPath = path;
                        _creationTargetFolder = Normalize(Path.GetDirectoryName(path) ?? string.Empty);
                        ScriptSelected?.Invoke(path);
                    }
                }

                Rebuild();
            });

            return row;
        }

        private void BeginInlineRename(VisualElement row, Label label, string path, bool isFolder)
        {
            var initial = isFolder
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);

            var field = new TextField { value = initial };
            field.AddToClassList("inline-rename");
            field.style.fontSize = _fontSize;

            var renameHeight = Mathf.Max(22f, Mathf.Min(26f, _fontSize + 8f));
            field.style.height = renameHeight;
            field.style.minHeight = renameHeight;
            field.style.marginTop = 0f;
            field.style.marginBottom = 0f;
            field.style.paddingTop = 0f;
            field.style.paddingBottom = 0f;

            var index = row.IndexOf(label);
            if (index < 0)
                return;

            row.Remove(label);
            row.Insert(index, field);

            var finished = false;

            Action submit = () =>
            {
                if (finished)
                    return;

                finished = true;

                var value = field.value?.Trim();
                if (!string.IsNullOrWhiteSpace(value) &&
                    !string.Equals(value, initial, StringComparison.Ordinal))
                {
                    RenameRequested?.Invoke(path, value);
                }
                else
                {
                    Rebuild();
                }
            };

            Action cancel = () =>
            {
                if (finished)
                    return;

                finished = true;
                Rebuild();
            };

            // Trickle-down is important here: TextField's internal text input can
            // consume Enter before a normal bubbling callback sees it.
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    submit();
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    cancel();
                    evt.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);

            field.schedule.Execute(() =>
            {
                var input = field.Q<VisualElement>(className: "unity-text-field__input");
                if (input != null)
                {
                    input.style.height = renameHeight;
                    input.style.minHeight = renameHeight;

                    input.style.marginTop = 0f;
                    input.style.marginBottom = 0f;

                    input.style.paddingTop = 0f;
                    input.style.paddingBottom = 0f;
                    input.style.paddingLeft = 4f;
                    input.style.paddingRight = 4f;

                    input.style.unityTextAlign = TextAnchor.MiddleLeft;
                }

                field.Focus();
                field.SelectAll();
            }).ExecuteLater(1);
        }

        private void ShowContextMenu(string path, bool isFolder, Vector3 position)
        {
            HideContextMenu();

            _contextMenu = new VisualElement();
            _contextMenu.AddToClassList("context-menu");
            _contextMenu.style.left = position.x;
            _contextMenu.style.top = position.y;

            var rename = new Button(() =>
            {
                HideContextMenu();
                PromptRename(path, isFolder);
            }) { text = _locale.Get("ScriptExplorer", "rename") };

            var duplicate = new Button(() =>
            {
                HideContextMenu();
                DuplicateRequested?.Invoke(path);
            }) { text = _locale.Get("ScriptExplorer", "duplicate") };

            var delete = new Button(() =>
            {
                HideContextMenu();
                DeleteRequested?.Invoke(path);
            }) { text = _locale.Get("ScriptExplorer", "delete") };

            _contextMenu.Add(rename);
            _contextMenu.Add(duplicate);
            _contextMenu.Add(delete);

            _popupLayer.Add(_contextMenu);
        }

        private void PromptRename(string path, bool isFolder)
        {
            _selectedPath = path;
            var row = FindRowByPath(path);
            if (row == null)
            {
                RenameRequested?.Invoke(path,
                    isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path));
                return;
            }

            var label = row.Q<Label>(className: "tree-label");
            if (label != null)
                BeginInlineRename(row, label, path, isFolder);
        }

        private VisualElement FindRowByPath(string path)
        {
            return _tree.Query<VisualElement>(className: "tree-row")
                .ToList()
                .FirstOrDefault(x => x.userData is NodeInfo n &&
                                     string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        private void HideContextMenu()
        {
            if (_contextMenu == null)
                return;

            _contextMenu.RemoveFromHierarchy();
            _contextMenu = null;
        }

        private void OnRootPointerMove(PointerMoveEvent evt)
        {
            if (_dragSource == null)
                return;

            if (!_dragging && Vector2.Distance(_dragStart, evt.position) > 6f)
                _dragging = true;

            if (!_dragging)
                return;

            var picked = panel?.Pick(new Vector2(evt.position.x, evt.position.y));
            var row = FindAncestorRow(picked);

            if (_dragOverRow != null)
                _dragOverRow.RemoveFromClassList("drag-over");

            _dragOverRow = null;

            if (row?.userData is NodeInfo node && node.IsFolder &&
                !string.Equals(node.Path, _dragSource.Path, StringComparison.OrdinalIgnoreCase))
            {
                _dragOverRow = row;
                _dragOverRow.AddToClassList("drag-over");
            }
            else if (row == null)
            {
                // Empty explorer space means root "/".
                var rootRow = FindRootRow();
                if (rootRow != null)
                {
                    _dragOverRow = rootRow;
                    _dragOverRow.AddToClassList("drag-over");
                }
            }
        }

        private void OnRootPointerUp(PointerUpEvent evt)
        {
            if (_dragSource == null)
                return;

            if (_dragging)
            {
                var picked = panel?.Pick(new Vector2(evt.position.x, evt.position.y));
                var row = FindAncestorRow(picked);

                if (row?.userData is NodeInfo target && target.IsFolder)
                {
                    MoveRequested?.Invoke(_dragSource.Path, target.Path);
                }
                else if (row == null)
                {
                    // Dropping on unused explorer space moves the item to root "/".
                    MoveRequested?.Invoke(_dragSource.Path, string.Empty);
                }
            }

            if (_dragOverRow != null)
                _dragOverRow.RemoveFromClassList("drag-over");

            _dragSource = null;
            _dragOverRow = null;
            _dragging = false;
        }

        private VisualElement FindRootRow()
        {
            return _tree.Query<VisualElement>(className: "root-row").First();
        }

        private static VisualElement FindAncestorRow(VisualElement element)
        {
            while (element != null)
            {
                if (element.ClassListContains("tree-row"))
                    return element;
                element = element.parent;
            }

            return null;
        }

        private bool HasErrorRecursive(string path, bool isFolder)
        {
            if (!isFolder)
                return _workspace.HasErrors(path);

            foreach (var script in EnumerateScriptsRecursive(path))
                if (_workspace.HasErrors(script))
                    return true;

            return false;
        }

        private bool HasDirtyRecursive(string path, bool isFolder)
        {
            if (!isFolder)
                return _workspace.IsDirty(path);

            foreach (var script in EnumerateScriptsRecursive(path))
                if (_workspace.IsDirty(script))
                    return true;

            return false;
        }

        private IEnumerable<string> EnumerateScriptsRecursive(string folder)
        {
            foreach (var script in _storage.EnumerateScripts(folder))
                yield return script;

            foreach (var child in _storage.EnumerateDirectories(folder))
                foreach (var script in EnumerateScriptsRecursive(child))
                    yield return script;
        }

        private void RemapExpanded(string oldPath, string newPath)
        {
            var affected = _expanded
                .Where(p => p.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ||
                            p.StartsWith(oldPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var path in affected)
            {
                _expanded.Remove(path);
                _expanded.Add(newPath + path.Substring(oldPath.Length));
            }
        }

        private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/');
    }
}
