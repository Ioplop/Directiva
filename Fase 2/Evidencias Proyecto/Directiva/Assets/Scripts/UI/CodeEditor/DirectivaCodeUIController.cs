using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Values;
using DSExecution.VirtualMachine;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using Directiva.UI;

namespace Directiva.CodeUI
{
    public sealed class DirectivaCodeUIController : MonoBehaviour
    {
        [Header("Localization")]
        [SerializeField] private string defaultLocale = "es";
        [SerializeField] private string initialLocale = "es";

        [Header("Shared UI Theme")]
        [SerializeField] private DirectivaUITheme uiTheme;

        [Header("Standalone navigation")]
        [SerializeField] private string menuSceneName = "MainMenu";

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

        [Header("Execution")]
        [SerializeField, Min(1)] private int executionStepsPerFrame = 2000;

        public DirectivaUIEventHub Events { get; private set; }
        public LocalizationService Localization { get; private set; }
        public OutputService Output { get; private set; }
        public DirectivaCode Codebase { get; private set; }

        public ICodeAnalyzer Analyzer { get; set; }
        public IRefactorer Refactorer { get; set; }
        public IScriptExecutionStateProvider ExecutionState { get; set; }

        private DirectivaUITheme _theme;
        private UIDocument _document;
        private bool _mounted;
        private VisualElement _explorerResizeHandle;
        private Button _explorerToggle;
        private bool _explorerCollapsed;
        private IScriptStorage _storage;
        private ScriptWorkspace _workspace;
        private EditorDocumentMetadataStore _editorMetadataStore;
        private VMExecutionController _executionController;
        private string _runningFunctionName;
        private VMMemoryContext _runningMemory;
        private bool _runningDILDebugEnabled;
        private readonly Dictionary<string, HashSet<int>> _runningBreakpointsByModule =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

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

        private string _activeMetadataPath;
        private bool _editorMetadataDirty;
        private float _editorMetadataFlushAt;

        private void OnValidate()
        {
            editorFontSize = Mathf.Clamp(editorFontSize, 6f, 48f);
            explorerFontSize = Mathf.Clamp(explorerFontSize, 6f, 48f);
            outputFontSize = Mathf.Clamp(outputFontSize, 6f, 48f);
            tooltipDelaySeconds = Mathf.Max(0f, tooltipDelaySeconds);
            executionStepsPerFrame = Mathf.Max(1, executionStepsPerFrame);

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
            _theme = DirectivaUITheme.Resolve(uiTheme);
            _document = GetComponent<UIDocument>();

            Events = new DirectivaUIEventHub();
            Localization = new LocalizationService(defaultLocale);
            Output = new OutputService(outputCapacity);

            var scriptsRoot = Path.Combine(Application.persistentDataPath, "DirectivaScripts");
            _storage = new FileSystemScriptStorage(scriptsRoot);
            _storage.EnsureRoot();
            _editorMetadataStore = new EditorDocumentMetadataStore(_storage.RootPath);

            _workspace = new ScriptWorkspace(_storage)
            {
                BackupIntervalSeconds = backupIntervalSeconds
            };

            Codebase = new DirectivaCode(_storage);
            Analyzer = new NoOpCodeAnalyzer();
            Refactorer = new NoOpRefactorer();

            _executionController = new VMExecutionController();
            ExecutionState = _executionController;
            _executionController.Completed += OnExecutionCompleted;
            _executionController.Faulted += OnExecutionFaulted;
            _executionController.BreakpointHit += OnBreakpointHit;
            _executionController.Printed += OnExecutionPrinted;

            Localization.LocalizationWarning += message => Output.WriteWarning(message);

            // Old standalone editor scenes still mount themselves automatically.
            // Simulation scenes deliberately have no UIDocument on this component:
            // their host calls Mount(...) with a child VisualElement later in Start().
            if (_document != null)
            {
                // Standalone has an outer shell too, so the editor never owns
                // the navigation button. No changes to existing scene objects.
                var host = StandaloneEditorShell.Create(
                    _document.rootVisualElement, _theme, ReturnToMainMenu);
                Mount(host);
                SetHosted(true);
            }
        }

        private void Update()
        {
            _workspace?.Tick(Time.unscaledTime);

            if (_editorMetadataDirty && Time.unscaledTime >= _editorMetadataFlushAt)
                FlushActiveEditorMetadata();

            _executionController?.Tick(executionStepsPerFrame);
            RefreshDILDebugHighlight();

            if (_analysisPending && Time.unscaledTime >= _analysisAt)
            {
                _analysisPending = false;
                AnalyzeActiveDocument();
            }
        }

        private void OnDisable()
        {
            FlushActiveEditorMetadata();
            _workspace?.FlushActiveBackup();
            _executionController?.Stop();
            _editor?.ClearExecutionLine();
        }

        private void OnApplicationQuit()
        {
            FlushActiveEditorMetadata();
            _workspace?.FlushActiveBackup();
        }

        public void SetLocale(string locale) => Localization.SetLocale(locale);

        /// <summary>Allows a simulation host to supply the same theme before mounting.</summary>
        public void SetTheme(DirectivaUITheme theme)
        {
            if (_mounted)
                throw new InvalidOperationException("Choose the editor theme before mounting it.");
            _theme = DirectivaUITheme.Resolve(theme);
        }

        private void ReturnToMainMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(menuSceneName))
            {
                Debug.LogError($"[Directiva] Menu scene '{menuSceneName}' is not in Build Profiles.", this);
                return;
            }
            SceneManager.LoadScene(menuSceneName);
        }

        /// <summary>
        /// Builds the one editor instance in an arbitrary UI Toolkit container.
        /// Standalone scenes mount automatically; the simulation host passes a child
        /// VisualElement. Never disable this component merely to hide the editor.
        /// </summary>
        public void Mount(VisualElement container)
        {
            if (_mounted)
                throw new InvalidOperationException("This editor is already mounted.");
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            BuildUI(container);
            WireEvents();
            Localization.SetLocale(initialLocale);
            RefreshCodebase();
            _mounted = true;
        }

        /// <summary>Hides the explorer without destroying its tree or workspace state.</summary>
        public void SetExplorerCollapsed(bool collapsed)
        {
            _explorerCollapsed = collapsed;
            if (_explorer == null)
                return;
            _explorer.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_explorerResizeHandle != null)
                _explorerResizeHandle.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_explorerToggle != null)
            {
                UIIconView.SetButton(_explorerToggle,
                    collapsed ? _theme?.icons?.arrowRight : _theme?.icons?.arrowLeft, 14f);
                _explorerToggle.tooltip = collapsed ? "Mostrar solución" : "Ocultar solución";
            }
        }

        /// <summary>Adapts the toolbar for a narrow docked editor, without replacing its views.</summary>
        public void SetCompactLayout(bool compact)
        {
            _topBar?.SetCompactLayout(compact);
        }

        /// <summary>In a simulation host, navigation belongs to the outer shell.</summary>
        public void SetHosted(bool hosted)
        {
            _topBar?.SetHosted(hosted);
        }

        private void BuildUI(VisualElement root)
        {
            // Clear only the container we own; never clear the simulation UI root.
            root.Clear();
            root.AddToClassList("directiva-code-ui-root");
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            root.style.minWidth = 0f;
            _theme?.ApplyInterfaceFont(root);

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
                explorerFontSize,
                _theme);

            var right = new VisualElement();
            right.AddToClassList("right-area");

            _topBar = new TopBarView(Localization, ExecutionState, _theme);
            _editor = new CodeEditorView(
                _theme != null && _theme.codeFont != null ? _theme.codeFont : normalFont,
                _theme != null && _theme.codeBoldFont != null ? _theme.codeBoldFont : boldFont,
                editorFontSize,
                editorPaddingLeftPx,
                editorPaddingTopPx,
                editorPaddingRightPx,
                editorPaddingBottomPx,
                indentGuideFineOffsetPx,
                _theme?.icons?.breakpoint);

            right.Add(_topBar);
            right.Add(_editor);

            var explorerResizeHandle = new HorizontalPaneResizeHandle(
                _explorer,
                minWidth: 180f,
                maxWidth: 520f);

            _explorerResizeHandle = explorerResizeHandle;
            _explorerToggle = new Button(() => SetExplorerCollapsed(!_explorerCollapsed));
            _explorerToggle.AddToClassList("explorer-collapse-toggle");
            _explorerToggle.focusable = true;

            main.Add(_explorer);
            main.Add(explorerResizeHandle);
            main.Add(_explorerToggle);
            main.Add(right);
            SetExplorerCollapsed(_explorerCollapsed);

            _outputPanel = new OutputPanel(
                Output,
                Localization,
                outputFontSize,
                _theme);
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
                MarkActiveEditorMetadataDirty();
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

                UpdateRunningBreakpoint(path, line, added);
            };

            _topBar.EntryFunctionChanged += _ => MarkActiveEditorMetadataDirty();

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
            _topBar.StepClicked += Events.RaiseStepRequested;
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

            Events.ContinueRequested += HandleContinueRequested;
            Events.PauseRequested += HandlePauseRequested;
            Events.StopRequested += HandleStopRequested;
            Events.StepRequested += HandleStepRequested;

            Events.OptionsRequested += () =>
                Output.Write(Localization.Get("Common", "options_not_implemented"));
        }

        private void OnActiveChanged(ScriptWorkspace.ActiveDocument doc)
        {
            var nextPath = doc?.RelativePath;
            bool sameMetadataDocument =
                !string.IsNullOrWhiteSpace(_activeMetadataPath) &&
                !string.IsNullOrWhiteSpace(nextPath) &&
                string.Equals(_activeMetadataPath, nextPath, StringComparison.OrdinalIgnoreCase);

            if (!sameMetadataDocument)
                FlushActiveEditorMetadata();

            if (doc == null)
            {
                _activeMetadataPath = null;
                _editor.ClearDocument();
                _topBar.SetEntryFunctionName("main");
                _topBar.SetDocument(null, false);
                return;
            }

            EditorDocumentMetadata metadata = null;
            if (!sameMetadataDocument)
                metadata = _editorMetadataStore.Load(doc.RelativePath);

            _activeMetadataPath = doc.RelativePath;

            _editor.OpenDocument(
                doc.RelativePath,
                doc.WorkingContent,
                GetBreakpoints(doc.RelativePath),
                metadata?.undoHistory);

            if (metadata != null)
                _topBar.SetEntryFunctionName(metadata.entryFunction);

            // Revert and path-remap can replace the active editor buffer without producing
            // a normal TextChanged event. In that case the in-memory history may have gained
            // a new state and must be persisted again. Keep the current fn: selection intact.
            if (sameMetadataDocument)
                MarkActiveEditorMetadataDirty();

            RefreshTopBar();
            ScheduleAnalysis();
            RefreshDILDebugHighlight();
        }

        private void OnPathChanged(string oldPath, string newPath)
        {
            _editor?.RemapDocumentHistory(oldPath, newPath);
            _activeMetadataPath = RemapEditorPath(_activeMetadataPath, oldPath, newPath);

            if (_breakpoints.TryGetValue(oldPath, out var points))
            {
                _breakpoints.Remove(oldPath);
                _breakpoints[newPath] = points;
            }

            RefreshCodebase();
        }

        private void MarkActiveEditorMetadataDirty()
        {
            if (string.IsNullOrWhiteSpace(_activeMetadataPath))
                return;

            _editorMetadataDirty = true;
            _editorMetadataFlushAt = Time.unscaledTime + Mathf.Max(0.5f, backupIntervalSeconds);
        }

        private void FlushActiveEditorMetadata()
        {
            if (!_editorMetadataDirty ||
                _editorMetadataStore == null ||
                _editor == null ||
                _topBar == null ||
                string.IsNullOrWhiteSpace(_activeMetadataPath))
            {
                return;
            }

            try
            {
                var history = _editor.ExportDocumentHistory(_activeMetadataPath);
                if (history == null)
                    return;

                _editorMetadataStore.Save(
                    _activeMetadataPath,
                    new EditorDocumentMetadata
                    {
                        entryFunction = _topBar.EntryFunctionName,
                        undoHistory = history
                    });

                _editorMetadataDirty = false;
            }
            catch (Exception ex)
            {
                _editorMetadataDirty = false;
                Debug.LogWarning($"Could not save editor metadata for '{_activeMetadataPath}': {ex.Message}");
            }
        }

        private static string RemapEditorPath(string candidate, string oldPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return candidate;

            candidate = candidate.Replace('\\', '/');
            oldPath = (oldPath ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            newPath = (newPath ?? string.Empty).Replace('\\', '/').TrimEnd('/');

            if (string.Equals(candidate, oldPath, StringComparison.OrdinalIgnoreCase))
                return newPath;

            var prefix = oldPath + "/";
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? newPath + candidate.Substring(oldPath.Length)
                : candidate;
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

                    _editor?.ForgetDocumentHistory(path, isFolder);

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

        private void HandleContinueRequested()
        {
            if (_executionController == null)
                return;

            if (_executionController.State == ScriptExecutionState.Paused)
            {
                _executionController.Continue();
                return;
            }

            if (_executionController.State == ScriptExecutionState.Stopped)
                StartActiveDILExecution();
        }

        private void HandleStepRequested()
        {
            if (_executionController == null ||
                _executionController.State == ScriptExecutionState.Running)
            {
                return;
            }

            if (_executionController.State == ScriptExecutionState.Stopped &&
                !StartActiveDILExecution(startPaused: true))
            {
                return;
            }

            if (_executionController.State != ScriptExecutionState.Paused)
                return;

            _executionController.Step(skipDebugMetadata: _runningDILDebugEnabled);
            RefreshDILDebugHighlight();
        }

        private void HandlePauseRequested()
        {
            _executionController?.Pause();
        }

        private void HandleStopRequested()
        {
            if (_executionController == null ||
                _executionController.State == ScriptExecutionState.Stopped)
            {
                return;
            }

            _executionController.Stop();
            _topBar.SetExecutionMetrics(
                _executionController.TotalAdvances,
                _executionController.ElapsedMilliseconds);
            _runningFunctionName = null;
            _runningMemory = null;
            _runningDILDebugEnabled = false;
            _runningBreakpointsByModule.Clear();
            _executionController.BreakpointPredicate = null;
            _editor.ClearExecutionLine();
            Output.Write("[DIL] Ejecución detenida.");
        }

        private bool StartActiveDILExecution(bool startPaused = false)
        {
            var active = _workspace.Active;
            if (active == null)
            {
                Output.WriteError("[DIL] No hay ningún archivo seleccionado.");
                return false;
            }

            if (!TryGetModuleName(active.RelativePath, out string moduleName, out string moduleError))
            {
                Output.WriteError("[DIL] " + moduleError);
                return false;
            }

            try
            {
                bool debugDIL = _topBar.DebugDILEnabled;
                BuildRunningBreakpointMap();

                // Breakpoints need the same source metadata used by visual DIL debugging.
                // If at least one breakpoint exists, instrument the parsed code silently even
                // when the debug checkbox is off so breakpoints work by themselves.
                bool generateSourceMetadata =
                    debugDIL || _runningBreakpointsByModule.Count > 0;

                var provider = new WorkspaceDILSourceProvider(Codebase, _workspace);
                var parser = new DILParser(
                    provider,
                    new DILParserOptions(generateDebugMetadata: generateSourceMetadata)
                );
                var code = parser.Parse(moduleName);

                string requestedFunction = _topBar.EntryFunctionName;
                FunctionContext entryFunction;

                if (string.IsNullOrWhiteSpace(requestedFunction))
                {
                    entryFunction = code.GetFunction(code.EntryFunctionId);
                }
                else
                {
                    string qualifiedName = moduleName + "." + requestedFunction;
                    entryFunction = code.Functions.FirstOrDefault(
                        function => string.Equals(
                            function.Name,
                            qualifiedName,
                            StringComparison.Ordinal));

                    if (entryFunction == null)
                    {
                        Output.WriteError(
                            $"[DIL] La función '{requestedFunction}' no existe en '{active.RelativePath}'. " +
                            "Escribe sólo el nombre local de una función declarada en ese archivo."
                        );
                        return false;
                    }
                }

                if (entryFunction.ParameterCount != 0)
                {
                    Output.WriteError(
                        $"[DIL] No se puede ejecutar '{entryFunction.Name}' desde la interfaz: " +
                        $"requiere {entryFunction.ParameterCount} parámetro(s). Por ahora el runner sólo admite entry points sin parámetros."
                    );
                    return false;
                }

                var vm = new DirectivaVM(code, entryFunction.Id);
                _runningFunctionName = entryFunction.Name;
                _runningMemory = vm.Memory;
                _runningDILDebugEnabled = generateSourceMetadata;
                _executionController.BreakpointPredicate =
                    _runningBreakpointsByModule.Count > 0 ? ShouldPauseAtBreakpoint : null;
                _editor.ClearExecutionLine();
                _topBar.ClearExecutionMetrics();
                _executionController.Start(vm, paused: startPaused);
                Output.Write($"[DIL] Ejecutando {_runningFunctionName}...");
                return true;
            }
            catch (DILParseException ex)
            {
                Output.WriteError("[DIL] Error de parseo: " + ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                Output.WriteError("[DIL] No se pudo iniciar la ejecución: " + ex.Message);
                return false;
            }
        }

        private void OnExecutionPrinted(string text)
        {
            Output.Write(text ?? string.Empty);
        }

        private void OnExecutionCompleted(DataValue returnValue)
        {
            string functionName = string.IsNullOrWhiteSpace(_runningFunctionName)
                ? "función"
                : _runningFunctionName;

            Output.Write(
                $"[DIL] {functionName} retornó {FormatDataValue(returnValue, _runningMemory)}"
            );

            _topBar.SetExecutionMetrics(
                _executionController.TotalAdvances,
                _executionController.ElapsedMilliseconds);

            _runningFunctionName = null;
            _runningMemory = null;
            _runningDILDebugEnabled = false;
            _runningBreakpointsByModule.Clear();
            _executionController.BreakpointPredicate = null;
            _editor.ClearExecutionLine();
        }

        private void OnExecutionFaulted(DSExecution.Errors.RuntimeError error)
        {
            string functionName = string.IsNullOrWhiteSpace(_runningFunctionName)
                ? "función"
                : _runningFunctionName;

            Output.WriteError(
                $"[DIL] {functionName} falló ({error.Id}): {error.Message}"
            );

            _topBar.SetExecutionMetrics(
                _executionController.TotalAdvances,
                _executionController.ElapsedMilliseconds);

            _runningFunctionName = null;
            _runningMemory = null;
            _runningDILDebugEnabled = false;
            _runningBreakpointsByModule.Clear();
            _executionController.BreakpointPredicate = null;
            _editor.ClearExecutionLine();
        }

        private void OnBreakpointHit(DirectivaVM vm)
        {
            if (vm == null)
                return;

            VMDebugState debug = vm.Debug;
            string file = string.IsNullOrWhiteSpace(debug.File) ? "?" : debug.File;
            string line = debug.Line >= 0 ? (debug.Line + 1).ToString(CultureInfo.InvariantCulture) : "?";

            Output.Write($"[DIL] Breakpoint alcanzado en {file}:{line}.");
            RefreshDILDebugHighlight();
        }

        private void RefreshDILDebugHighlight()
        {
            if (!_runningDILDebugEnabled ||
                _executionController == null ||
                _executionController.CurrentVM == null)
            {
                _editor?.ClearExecutionLine();
                return;
            }

            DirectivaVM vm = _executionController.CurrentVM;

            // DEBUG_FILE, DEBUG_LINE and DEBUG_SPAN still consume separate Advance() calls in this
            // iteration. If a frame budget ends immediately after DEBUG_FILE, File has changed but
            // Line still belongs to the previous source location. Keep the previous highlight until
            // DEBUG_LINE has executed so the UI never displays that transient mismatched pair.
            if (vm.CurrentFunction != null &&
                vm.CurrentFunction.TryGetInstruction(vm.InstructionPointer, out Instruction nextInstruction) &&
                nextInstruction.Id == InstructionId.DebugLine)
            {
                return;
            }

            VMDebugState debug = vm.Debug;
            if (debug.Line < 0 || string.IsNullOrWhiteSpace(debug.File))
            {
                _editor?.ClearExecutionLine();
                return;
            }

            var active = _workspace?.Active;
            if (active == null ||
                !TryGetModuleName(active.RelativePath, out string activeModule, out _))
            {
                _editor?.ClearExecutionLine();
                return;
            }

            // DEBUG_FILE injected by the DIL parser uses the canonical dot-qualified module name.
            // We only highlight when that module is the document currently visible in the editor.
            if (!string.Equals(activeModule, debug.File, StringComparison.Ordinal))
            {
                _editor?.ClearExecutionLine();
                return;
            }

            _editor?.SetExecutionLine(debug.Line + 1, ensureVisible: true);
        }

        private static string FormatDataValue(DataValue value, VMMemoryContext memory)
        {
            switch (value.dataType)
            {
                case DataTypeId.None:
                    return "None";

                case DataTypeId.Bool:
                    return value.value == 0 ? "False" : "True";

                case DataTypeId.Int:
                    return unchecked((long)value.value).ToString(CultureInfo.InvariantCulture);

                case DataTypeId.Decimal:
                    return FormatDecimalRaw(unchecked((long)value.value));

                case DataTypeId.String:
                    if (memory != null &&
                        value.TryGetHeapReference(out uint stringReference) &&
                        memory.TryGetHeap(stringReference, out HeapObject heapObject) &&
                        heapObject is StringHeapObject stringObject)
                    {
                        return "\"" + EscapeStringForOutput(stringObject.Value) + "\"";
                    }

                    return $"String({value.value})";

                case DataTypeId.Uninitialized:
                    return "<Uninitialized>";

                default:
                    return $"{value.dataType}({value.value})";
            }
        }

        private static string EscapeStringForOutput(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string FormatDecimalRaw(long rawValue)
        {
            bool negative = rawValue < 0;
            long whole = rawValue / DTDecimal.Scale;
            long fractional = rawValue % DTDecimal.Scale;

            if (whole < 0)
                whole = -whole;
            if (fractional < 0)
                fractional = -fractional;

            string fractionalText = fractional
                .ToString("D4", CultureInfo.InvariantCulture)
                .TrimEnd('0');

            if (fractionalText.Length == 0)
                fractionalText = "0";

            return (negative ? "-" : string.Empty) +
                   whole.ToString(CultureInfo.InvariantCulture) +
                   "." +
                   fractionalText;
        }

        private static bool TryGetModuleName(
            string relativeScriptPath,
            out string moduleName,
            out string error)
        {
            moduleName = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(relativeScriptPath) ||
                !relativeScriptPath.EndsWith(
                    FileSystemScriptStorage.ScriptExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "El archivo seleccionado no es un script .dil válido.";
                return false;
            }

            string withoutExtension = relativeScriptPath.Substring(
                0,
                relativeScriptPath.Length - FileSystemScriptStorage.ScriptExtension.Length
            );

            moduleName = withoutExtension
                .Replace('\\', '.')
                .Replace('/', '.');

            if (!DILParser.IsValidQualifiedName(moduleName))
            {
                error =
                    $"La ruta '{relativeScriptPath}' no puede convertirse en un nombre de módulo DIL. " +
                    "Usa sólo identificadores válidos en nombres de carpetas y archivos.";
                moduleName = string.Empty;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Exposes the editor's current working copies to the DIL parser, including .back content,
        /// while preserving exact case-sensitive module lookup.
        /// </summary>
        private sealed class WorkspaceDILSourceProvider : IDILSourceProvider
        {
            private readonly DirectivaCode _codebase;
            private readonly ScriptWorkspace _workspace;

            public WorkspaceDILSourceProvider(
                DirectivaCode codebase,
                ScriptWorkspace workspace)
            {
                _codebase = codebase ?? throw new ArgumentNullException(nameof(codebase));
                _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            }

            public bool TryReadModule(string moduleName, out string sourceText)
            {
                string expectedPath = moduleName.Replace('.', '/') +
                                      FileSystemScriptStorage.ScriptExtension;

                var script = _codebase.Scripts.FirstOrDefault(
                    candidate => string.Equals(
                        candidate.RelativePath,
                        expectedPath,
                        StringComparison.Ordinal));

                if (script == null)
                {
                    sourceText = string.Empty;
                    return false;
                }

                sourceText = _workspace.GetWorkingContent(script.RelativePath);
                return true;
            }
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

        private void BuildRunningBreakpointMap()
        {
            _runningBreakpointsByModule.Clear();

            foreach (var pair in _breakpoints)
            {
                if (pair.Value == null || pair.Value.Count == 0)
                    continue;

                if (!TryGetModuleName(pair.Key, out string moduleName, out _))
                    continue;

                _runningBreakpointsByModule[moduleName] = new HashSet<int>(pair.Value);
            }
        }

        private void UpdateRunningBreakpoint(string path, int line, bool added)
        {
            // If this execution was parsed without DEBUG_* metadata, a breakpoint added after
            // execution started cannot become source-aware until the next run. Existing runs that
            // already have metadata (debug checkbox or pre-existing breakpoints) can update live.
            if (!_runningDILDebugEnabled ||
                _executionController == null ||
                _executionController.State == ScriptExecutionState.Stopped ||
                !TryGetModuleName(path, out string moduleName, out _))
            {
                return;
            }

            if (!_runningBreakpointsByModule.TryGetValue(moduleName, out var lines))
            {
                if (!added)
                    return;

                lines = new HashSet<int>();
                _runningBreakpointsByModule[moduleName] = lines;
            }

            if (added)
                lines.Add(line);
            else
            {
                lines.Remove(line);
                if (lines.Count == 0)
                    _runningBreakpointsByModule.Remove(moduleName);
            }

            _executionController.BreakpointPredicate =
                _runningBreakpointsByModule.Count > 0 ? ShouldPauseAtBreakpoint : null;
        }

        private bool ShouldPauseAtBreakpoint(DirectivaVM vm)
        {
            if (vm == null ||
                vm.CurrentFunction == null ||
                vm.Debug.Line < 0 ||
                string.IsNullOrWhiteSpace(vm.Debug.File))
            {
                return false;
            }

            // DEBUG_* describes the source instruction that follows it. Do not stop while the
            // metadata itself is still being consumed; pause immediately before the real DIL/VM
            // instruction executes.
            if (vm.CurrentFunction.TryGetInstruction(vm.InstructionPointer, out Instruction instruction) &&
                (instruction.Id == InstructionId.DebugFile ||
                 instruction.Id == InstructionId.DebugLine ||
                 instruction.Id == InstructionId.DebugSpan))
            {
                return false;
            }

            return _runningBreakpointsByModule.TryGetValue(vm.Debug.File, out var lines) &&
                   lines.Contains(vm.Debug.Line + 1);
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
