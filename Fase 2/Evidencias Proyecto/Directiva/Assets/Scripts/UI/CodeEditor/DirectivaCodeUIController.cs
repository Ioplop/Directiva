using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class DirectivaCodeUIController : MonoBehaviour
    {
        [Header("Localization")]
        [SerializeField] private string defaultLocale = "es";
        [SerializeField] private string initialLocale = "es";

        [Header("Fonts")]
        [SerializeField] private FontAsset normalFont;
        [SerializeField] private FontAsset boldFont;

        [Header("Editor")]
        [SerializeField, Min(6f)] private float editorFontSize = 14f;

        [Header("Explorer")]
        [SerializeField, Min(6f)] private float explorerFontSize = 14f;

        [Header("Output")]
        [SerializeField, Min(6f)] private float outputFontSize = 14f;

        [Header("Tooltips")]
        [Tooltip("Seconds the pointer must remain over a symbol control before its help popup appears.")]
        [SerializeField, Min(0f)] private float tooltipDelaySeconds = 1.2f;

        [Tooltip("Left padding of the editable code area, in pixels. This is the real TextField padding and is also the origin used by indentation guides.")]
        [SerializeField, Min(0f)] private float editorPaddingLeftPx = 8f;

        [Tooltip("Top padding of the editable code area, in pixels.")]
        [SerializeField, Min(0f)] private float editorPaddingTopPx = 4f;

        [Tooltip("Right padding of the editable code area, in pixels.")]
        [SerializeField, Min(0f)] private float editorPaddingRightPx = 8f;

        [Tooltip("Bottom padding of the editable code area, in pixels.")]
        [SerializeField, Min(0f)] private float editorPaddingBottomPx = 4f;

        [Tooltip("Optional fine horizontal correction for indentation guides, in pixels. This value does not scale with zoom. Keep it at 0 unless a visual correction is deliberately wanted.")]
        [SerializeField] private float indentGuideFineOffsetPx = 0f;

        [Header("Workspace")]
        [SerializeField, Min(0.5f)] private float backupIntervalSeconds = 5f;
        [SerializeField, Min(0.02f)] private float analysisDebounceSeconds = 0.15f;

        [Header("Output")]
        [SerializeField, Min(1)] private int outputCapacity = 1000;

        public DirectivaUIEventHub Events { get; private set; }
        public LocalizationService Localization { get; private set; }
        public OutputService Output { get; private set; }
        public DirectivaCode Codebase { get; private set; }

        public ICodeAnalyzer Analyzer { get; set; }
        public IRefactorer Refactorer { get; set; }
        public IScriptExecutionStateProvider ExecutionState { get; set; }

        private UIDocument _document;
        private IScriptStorage _storage;
        private ScriptWorkspace _workspace;

        private ScriptExplorerView _explorer;
        private CodeEditorView _editor;
        private TopBarView _topBar;
        private OutputPanel _outputPanel;
        private ModalLayer _modal;

        private VisualElement _popupLayer;
        private VisualElement _tooltipLayer;
        private HoverTooltipController _tooltips;

        private readonly Dictionary<string, HashSet<int>> _breakpoints =
            new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        private bool _analysisPending;
        private float _analysisAt;

        private void OnValidate()
        {
            editorFontSize = Mathf.Clamp(editorFontSize, 6f, 48f);
            explorerFontSize = Mathf.Clamp(explorerFontSize, 6f, 48f);
            outputFontSize = Mathf.Clamp(outputFontSize, 6f, 48f);
            tooltipDelaySeconds = Mathf.Max(0f, tooltipDelaySeconds);

            editorPaddingLeftPx = Mathf.Max(0f, editorPaddingLeftPx);
            editorPaddingTopPx = Mathf.Max(0f, editorPaddingTopPx);
            editorPaddingRightPx = Mathf.Max(0f, editorPaddingRightPx);
            editorPaddingBottomPx = Mathf.Max(0f, editorPaddingBottomPx);

            if (!Application.isPlaying)
                return;

            _editor?.SetFontSize(editorFontSize);
            _editor?.SetEditorPadding(
                editorPaddingLeftPx,
                editorPaddingTopPx,
                editorPaddingRightPx,
                editorPaddingBottomPx);
            _editor?.SetIndentGuideFineOffsetPx(indentGuideFineOffsetPx);

            _explorer?.SetFontSize(explorerFontSize);
            _outputPanel?.SetFontSize(outputFontSize);
            _tooltips?.SetDelay(tooltipDelaySeconds);
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();

            Events = new DirectivaUIEventHub();
            Localization = new LocalizationService(defaultLocale);
            Output = new OutputService(outputCapacity);

            var scriptsRoot = Path.Combine(Application.persistentDataPath, "DirectivaScripts");
            _storage = new FileSystemScriptStorage(scriptsRoot);
            _storage.EnsureRoot();

            _workspace = new ScriptWorkspace(_storage)
            {
                BackupIntervalSeconds = backupIntervalSeconds
            };

            Codebase = new DirectivaCode(_storage);
            Analyzer = new NoOpCodeAnalyzer();
            Refactorer = new NoOpRefactorer();
            ExecutionState = new NoOpExecutionStateProvider();

            Localization.LocalizationWarning += message => Output.WriteWarning(message);

            BuildUI();
            WireEvents();

            Localization.SetLocale(initialLocale);
            RefreshCodebase();
        }

        private void Update()
        {
            _workspace?.Tick(Time.unscaledTime);

            if (_analysisPending && Time.unscaledTime >= _analysisAt)
            {
                _analysisPending = false;
                AnalyzeActiveDocument();
            }
        }

        private void OnDisable()
        {
            _workspace?.FlushActiveBackup();
        }

        private void OnApplicationQuit()
        {
            _workspace?.FlushActiveBackup();
        }

        public void SetLocale(string locale) => Localization.SetLocale(locale);

        private void BuildUI()
        {
            var root = _document.rootVisualElement;
            root.Clear();
            root.AddToClassList("directiva-code-ui-root");

            var style = Resources.Load<StyleSheet>("Styles/CodeEditorUI/DirectivaCodeUI");
            if (style != null)
                root.styleSheets.Add(style);

            var app = new VisualElement();
            app.AddToClassList("app-shell");
            root.Add(app);

            var main = new VisualElement();
            main.AddToClassList("main-area");

            var popupLayer = new VisualElement();
            popupLayer.AddToClassList("popup-layer");
            popupLayer.pickingMode = PickingMode.Ignore;
            _popupLayer = popupLayer;

            var tooltipLayer = new VisualElement();
            tooltipLayer.AddToClassList("tooltip-layer");
            tooltipLayer.pickingMode = PickingMode.Ignore;
            _tooltipLayer = tooltipLayer;

            _tooltips = new HoverTooltipController(
                root,
                _tooltipLayer,
                tooltipDelaySeconds);

            _explorer = new ScriptExplorerView(
                _storage,
                _workspace,
                Localization,
                _popupLayer,
                explorerFontSize);

            var right = new VisualElement();
            right.AddToClassList("right-area");

            _topBar = new TopBarView(Localization, ExecutionState);
            _editor = new CodeEditorView(
                normalFont,
                boldFont,
                editorFontSize,
                editorPaddingLeftPx,
                editorPaddingTopPx,
                editorPaddingRightPx,
                editorPaddingBottomPx,
                indentGuideFineOffsetPx);

            right.Add(_topBar);
            right.Add(_editor);

            var explorerResizeHandle = new HorizontalPaneResizeHandle(
                _explorer,
                minWidth: 180f,
                maxWidth: 520f);

            main.Add(_explorer);
            main.Add(explorerResizeHandle);
            main.Add(right);

            _outputPanel = new OutputPanel(
                Output,
                Localization,
                outputFontSize);
            _modal = new ModalLayer(Localization);

            app.Add(main);
            app.Add(_outputPanel);
            app.Add(_popupLayer);
            app.Add(_tooltipLayer);
            app.Add(_modal);

            RegisterSymbolTooltips();
        }

        private void RegisterSymbolTooltips()
        {
            _topBar?.RegisterTooltips(_tooltips);
            _explorer?.RegisterTooltips(_tooltips);
            _outputPanel?.RegisterTooltips(_tooltips);
        }

        private void WireEvents()
        {
            _explorer.ScriptSelected += path => _workspace.Open(path);
            _explorer.NewScriptRequested += () => Events.RaiseCreateScriptRequested();
            _explorer.NewFolderRequested += () => Events.RaiseCreateFolderRequested();
            _explorer.SaveQuickRequested += path => Events.RaiseSaveRequested(path);
            _explorer.RevertQuickRequested += path => Events.RaiseRevertRequested(path);
            _explorer.RenameRequested += (path, name) => Events.RaiseRenameRequested(path, name);
            _explorer.DuplicateRequested += path => Events.RaiseDuplicateRequested(path);
            _explorer.DeleteRequested += path => Events.RaiseDeleteRequested(path);
            _explorer.MoveRequested += (source, target) => Events.RaiseMoveRequested(source, target);

            _editor.TextChanged += text =>
            {
                _workspace.UpdateActiveWorkingContent(text);
                RefreshTopBar();
                ScheduleAnalysis();
            };

            _editor.FontSizeChanged += size =>
            {
                // Keeps the current runtime zoom visible in the Inspector.
                // As usual in Unity, changes made during Play Mode are not persisted
                // after leaving Play Mode unless copied manually.
                editorFontSize = size;
            };

            _explorer.FontSizeChanged += size =>
            {
                explorerFontSize = size;
            };

            _outputPanel.FontSizeChanged += size =>
            {
                outputFontSize = size;
            };

            _editor.BreakpointChanged += (line, added) =>
            {
                var path = _workspace.Active?.RelativePath;
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var set = GetBreakpoints(path);
                if (added)
                {
                    set.Add(line);
                    Events.RaiseBreakpointAdded(path, line);
                }
                else
                {
                    set.Remove(line);
                    Events.RaiseBreakpointRemoved(path, line);
                }
            };

            _topBar.SaveClicked += () =>
            {
                if (_workspace.Active != null)
                    Events.RaiseSaveRequested(_workspace.Active.RelativePath);
            };

            _topBar.RevertClicked += () =>
            {
                if (_workspace.Active != null)
                    Events.RaiseRevertRequested(_workspace.Active.RelativePath);
            };

            _topBar.StopClicked += Events.RaiseStopRequested;
            _topBar.PauseClicked += Events.RaisePauseRequested;
            _topBar.ContinueClicked += Events.RaiseContinueRequested;
            _topBar.BackClicked += Events.RaiseBackRequested;
            _topBar.OptionsClicked += Events.RaiseOptionsRequested;

            _workspace.ActiveChanged += OnActiveChanged;
            _workspace.TreeChanged += RefreshCodebase;
            _workspace.ScriptStateChanged += _ => RefreshTopBar();
            _workspace.PathChanged += OnPathChanged;

            Events.CreateScriptRequested += ShowCreateScriptPrompt;
            Events.CreateFolderRequested += ShowCreateFolderPrompt;
            Events.SaveRequested += SaveScript;
            Events.RevertRequested += ShowRevertConfirmation;
            Events.RenameRequested += Rename;
            Events.DuplicateRequested += ShowDuplicatePrompt;
            Events.DeleteRequested += ShowDeleteConfirmation;
            Events.MoveRequested += Move;

            Events.OptionsRequested += () =>
                Output.Write(Localization.Get("Common", "options_not_implemented"));
        }

        private void OnActiveChanged(ScriptWorkspace.ActiveDocument doc)
        {
            if (doc == null)
            {
                _editor.ClearDocument();
                _topBar.SetDocument(null, false);
                return;
            }

            _editor.OpenDocument(doc.RelativePath, doc.WorkingContent, GetBreakpoints(doc.RelativePath));
            RefreshTopBar();
            ScheduleAnalysis();
        }

        private void OnPathChanged(string oldPath, string newPath)
        {
            if (_breakpoints.TryGetValue(oldPath, out var points))
            {
                _breakpoints.Remove(oldPath);
                _breakpoints[newPath] = points;
            }

            RefreshCodebase();
        }

        private void RefreshTopBar()
        {
            var doc = _workspace.Active;
            _topBar.SetDocument(doc?.RelativePath, doc?.IsDirty ?? false);
        }

        private void RefreshCodebase()
        {
            Codebase?.Refresh();
        }

        private void ShowCreateScriptPrompt()
        {
            var folder = _explorer.GetPreferredCreationFolder();

            _modal.Prompt(
                Localization.Get("Dialogs", "create_script_title"),
                Localization.Get("Dialogs", "create_script_message", folder.Length == 0 ? "/" : folder),
                "new_script",
                name => SafeAction(() =>
                {
                    var path = _workspace.CreateScript(folder, StripScriptExtension(name));
                    RefreshCodebase();
                    _workspace.Open(path);
                }),
                Localization.Get("Common", "create"),
                Localization.Get("Common", "cancel"));
        }

        private void ShowCreateFolderPrompt()
        {
            var folder = _explorer.GetPreferredCreationFolder();

            _modal.Prompt(
                Localization.Get("Dialogs", "create_folder_title"),
                Localization.Get("Dialogs", "create_folder_message", folder.Length == 0 ? "/" : folder),
                "NewFolder",
                name => SafeAction(() =>
                {
                    _workspace.CreateFolder(folder, name);
                    RefreshCodebase();
                }),
                Localization.Get("Common", "create"),
                Localization.Get("Common", "cancel"));
        }

        private void SaveScript(string path)
        {
            SafeAction(() =>
            {
                _workspace.Save(path);
                RefreshCodebase();
                Output.Write(Localization.Get("Common", "saved", path));
            });
        }

        private void ShowRevertConfirmation(string path)
        {
            _modal.Confirm(
                Localization.Get("Dialogs", "revert_title"),
                Localization.Get("Dialogs", "revert_message", path),
                () => SafeAction(() => _workspace.Revert(path)),
                Localization.Get("Common", "revert"),
                Localization.Get("Common", "cancel"));
        }

        private void Rename(string path, string newName)
        {
            SafeAction(() =>
            {
                var isFolder = _storage.DirectoryExists(path);
                var targetName = isFolder ? newName : StripScriptExtension(newName);

                var request = new RefactorRequest(
                    RefactorOperationKind.Rename,
                    path,
                    targetName);

                var plan = Refactorer.Prepare(Codebase, request);
                // NoOpRefactorer devuelve NoChanges en v1.
                // El flujo de confirmación de refactor real se conectará aquí.
                Refactorer.Apply(Codebase, plan);

                if (isFolder)
                    _workspace.RenameFolder(path, targetName);
                else
                    _workspace.RenameScript(path, targetName);

                RefreshCodebase();
            });
        }

        private void ShowDuplicatePrompt(string path)
        {
            var isFolder = _storage.DirectoryExists(path);
            var originalName = isFolder
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);

            _modal.Prompt(
                Localization.Get("Dialogs", "duplicate_title"),
                Localization.Get("Dialogs", "duplicate_message", originalName),
                originalName + "_copy",
                name => SafeAction(() =>
                {
                    if (isFolder)
                        _workspace.DuplicateFolder(path, name);
                    else
                        _workspace.DuplicateScript(path, StripScriptExtension(name));

                    RefreshCodebase();
                }),
                Localization.Get("ScriptExplorer", "duplicate"),
                Localization.Get("Common", "cancel"));
        }

        private void ShowDeleteConfirmation(string path)
        {
            _modal.Confirm(
                Localization.Get("Dialogs", "delete_title"),
                Localization.Get("Dialogs", "delete_message", path),
                () => SafeAction(() =>
                {
                    var isFolder = _storage.DirectoryExists(path);

                    var plan = Refactorer.Prepare(
                        Codebase,
                        new RefactorRequest(RefactorOperationKind.Delete, path, string.Empty));

                    Refactorer.Apply(Codebase, plan);

                    if (isFolder)
                        _workspace.DeleteFolder(path);
                    else
                        _workspace.DeleteScript(path);

                    RefreshCodebase();
                }),
                Localization.Get("ScriptExplorer", "delete"),
                Localization.Get("Common", "cancel"));
        }

        private void Move(string source, string targetFolder)
        {
            SafeAction(() =>
            {
                var isFolder = _storage.DirectoryExists(source);

                var plan = Refactorer.Prepare(
                    Codebase,
                    new RefactorRequest(RefactorOperationKind.Move, source, targetFolder));

                Refactorer.Apply(Codebase, plan);

                if (isFolder)
                    _workspace.MoveFolder(source, targetFolder);
                else
                    _workspace.MoveScript(source, targetFolder);

                RefreshCodebase();
            });
        }

        private void ScheduleAnalysis()
        {
            if (_workspace.Active == null)
                return;

            _analysisPending = true;
            _analysisAt = Time.unscaledTime + analysisDebounceSeconds;
        }

        private void AnalyzeActiveDocument()
        {
            var active = _workspace.Active;
            if (active == null || Analyzer == null)
                return;

            try
            {
                var result = Analyzer.Analyze(
                    new CodeAnalysisRequest(Codebase, active.RelativePath, active.WorkingContent));

                _workspace.SetDiagnostics(active.RelativePath, result?.Diagnostics);
            }
            catch (Exception ex)
            {
                Output.WriteError(Localization.Get("Common", "analysis_exception", ex.Message));
            }
        }

        private HashSet<int> GetBreakpoints(string path)
        {
            if (!_breakpoints.TryGetValue(path, out var set))
            {
                set = new HashSet<int>();
                _breakpoints[path] = set;
            }

            return set;
        }

        private void SafeAction(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (DirectivaLocalizedException ex)
            {
                Output.WriteError(Localization.Get(ex.Component, ex.Key, ex.Arguments));
            }
            catch (Exception ex)
            {
                Output.WriteError(Localization.Get("Common", "operation_failed", ex.Message));
            }
        }

        private static string StripScriptExtension(string name)
        {
            name = (name ?? string.Empty).Trim();
            return name.EndsWith(FileSystemScriptStorage.ScriptExtension, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - FileSystemScriptStorage.ScriptExtension.Length)
                : name;
        }
    }
}
