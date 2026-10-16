# Directiva — contexto técnico del proyecto Unity

> **Documento de orientación para agentes de IA y colaboradores.** Es un mapa del proyecto, no una especificación exhaustiva ni un reemplazo del código fuente.
>
> **Última actualización:** 2026-10-15 22:39:05 -03:00 (America/Santiago)  
> **Base examinada:** `Assets15-10-2026-2222.zip` (copia de `Assets/` entregada el 15-10-2026).  
> **Alcance:** exclusivamente el videojuego Unity **Directiva**. No cubre informes académicos, evidencias, gestión externa ni archivos que no vinieron en este paquete.  
> **Criterio de verdad:** código, escenas y assets actuales > este resumen > documentación histórica > planes conversados. En caso de discrepancia, inspeccionar el repositorio y `git log`.

## 0. Lectura rápida para otro agente

**Directiva** es un videojuego educativo y competitivo de PC hecho con **Unity 6 / C#**, con **UI Toolkit** y recursos **URP**. El objetivo es que el jugador programe el comportamiento de **unidades** en un lenguaje textual propio, **DScript**, inspirado en Python, dentro de un entorno de simulación con un **Nexo** (base), recursos y objetivos/misiones. *Unidad* es el término del juego; evitar llamarlas «agentes» para no confundirlas con agentes de IA.

**Estado al corte:** el proyecto tiene interfaz de edición de código, menú y opciones, parser de **DIL**, VM ejecutable con memoria/GC y tipos escalares, String y Tuple. **Todavía no tiene compilador DScript → DIL ni simulación jugable de unidades/Nexo/recursos.** El HUD de simulación y los objetos 3D de las escenas son, por ahora, principalmente maquetas visuales.

**Prioridad inmediata acordada:** completar estructuras de datos del heap: **Tuple está implementada y probada**; siguen **List, Dict y Set**, que implican mutación y cambios de tamaño. **Después** desarrollar el compilador **DScript → DIL**. No anticipar esa segunda tarea ni inventar funcionalidades de gameplay como si estuvieran implementadas.

### Estado por subsistema

| Sistema | Estado conocido | Dónde empezar |
|---|---|---|
| Menú principal y selección de niveles | Implementado como interfaz, navegación y carga de escenas; progreso/desbloqueo real pendiente | `Scripts/UI/MainMenu/`, `Scenes/MainMenu.unity` |
| Opciones persistentes | JSON/PlayerPrefs y aplicación de resolución, fullscreen, cursor y ejecución en segundo plano; audio/idioma sin integración global | `Scripts/Settings/`, `Documentation/MainMenu/SETTINGS.md` |
| Editor de scripts | Funcional para **`.dil`**; árbol de archivos, edición, backups, historial undo/redo, consola, breakpoints DIL | `Scripts/UI/CodeEditor/`, `Scripts/Code/{Storage,Workspace}/` |
| Lenguaje DScript | Diseñado conceptualmente; **sin compilador ni parser funcional** | `Scripts/Code/Compilation/` (actualmente solo `DIL/`) |
| DIL (texto intermedio) | Parser con múltiples módulos/imports, funciones, saltos, constantes y operaciones soportadas | `Scripts/Code/Compilation/DIL/` |
| Máquina virtual | Ejecuta `CodeContext`: stack, llamadas, locals, globals, saltos, operaciones, casts, print y límites | `Scripts/Code/Execution/VirtualMachine/` |
| Heap y GC | Heap con límites lógicos; **mark-and-sweep lazy** al fallar una reserva, referencias anidadas y compartidas | `Scripts/Code/Execution/VirtualMachine/Memory/` |
| Tipos | `None`, `Uninitialized`, `Bool`, `Int`, `Decimal`, `String`, `Tuple`; List/Set/Dict y entidades solo reservadas | `Scripts/Code/Execution/Types/` |
| Interfaz de simulación | HUD y editor acoplables; controles de simulación **solo visuales** | `Scripts/UI/Simulation/`, `Scenes/BaseSimulation.unity` |
| Simulación jugable y misiones | **No implementadas**: lógica de unidades, combate, minería, Nexo, recursos, victoria, tutorial | Crear subsistemas de simulación y orquestación |
| Análisis/refactorización DScript | Interfaces/diagnósticos preparados; implementaciones **NoOp** | `Scripts/Code/{Analysis,Refactoring,Core}/` |
| Debug de código | DIL: breakpoints, línea activa y step simple; depurador DScript y Step In/Out pendientes | `VMExecutionController`, `DirectivaCodeUIController` |
| DIB binario | Nombre/formato futuro acordado; sin serializador/cargador binario en este paquete | Diseño posterior, no asumir implementado |

## 1. Qué se está construyendo

- El jugador programará en **DScript** para controlar unidades autónomas dentro de misiones/simulaciones con recursos, base/Nexo y potencial enfrentamiento entre equipos. Entre las acciones de juego previstas están **moverse, atacar, minar y descargar**. El Nexo y las reglas de juego aún no están implementados como sistemas funcionales.
- La intención pedagógica es aprender programación escribiendo código que produzca resultados observables; DIL es una fase técnica/intermedia, **no** el lenguaje definitivo para jugadores.
- Dirección visual: estética tecnológica/geométrica, low-poly, con inspiración tipo Armagetron; ya existen logotipos, iconos, fuentes, modelos primitivos y materiales. El entorno y la UI son todavía evolutivos.
- Plataforma objetivo inicial: **PC**, con publicación planificada en **itch.io**. Español como idioma inicial, posibilidad de inglés. El paquete incluye un perfil Linux, pero **un perfil no equivale a verificar una build**.
- Proyecto de título desarrollado por dos integrantes: responsable técnico principal y **Álvaro Castro** (gestión, planificación, documentación, pruebas/comunicación). El repositorio completo puede aportar detalles formales y cronología.
- Los **playtests con público general están pendientes/reprogramados**: todavía no hay experiencia suficientemente jugable y DIL resulta demasiado técnico para representar la experiencia definitiva.

## 2. Arquitectura y flujo de ejecución

### Pipeline previsto y situación real

```text
[FUTURO] Código DScript del jugador (.dscript)
                |
[FUTURO] Lexer / Parser / AST / análisis / compilador DScript
                v
[EXISTE] DIL textual (.dil) <--- se edita directamente por ahora
                |
[EXISTE] DILParser + IDILSourceProvider (+ imports)
                v
[EXISTE] CodeContext / FunctionContext / Instruction[]
                |
[EXISTE] DirectivaVM + OperationDispatcher + DataType
                |
[EXISTE] VMMemoryContext + HeapMemory + GC
                v
[EXISTE] valores retornados / evento PRINT / errores / debug DIL
                |
[FUTURO] comandos sobre unidades, Nexo y simulación jugable

[FUTURO] .dib = representación binaria cargable del programa compilado.
```

**Capas a mantener desacopladas:** UI y almacenamiento de scripts; análisis/compilación; VM/operaciones/memoria; simulación y orquestación de entidades. La UI puede manejar una VM de prueba sin depender de un mundo 3D real.

**No confundir estos dos «play»:** los botones del editor **sí** ejecutan DIL mediante `VMExecutionController`; los botones del HUD de simulación (`Play`, `Pause`, `Stop`, velocidades) **no** dirigen todavía las unidades ni el reloj de simulación.

### Convenciones relevantes

- Código del runtime organizado principalmente en namespaces `DSCompilation.DIL`, `DSExecution.*`, `Directiva.CodeUI`, `Directiva.Settings`, `Directiva.UI`, `Directiva.MainMenu` y `Directiva.SimulationUI`.
- Instrucciones VM (`InstructionId`) y operaciones (`OperationId`) **tienen IDs explícitos**; `DataTypeId` también. **No renumerar identificadores ya usados**: se preservan en previsión de serialización DIB.
- La VM usa datos tipados mediante `DataValue`. Los escalares pueden ir en el payload del valor; `String` y `Tuple` usan referencias (`uint`) a objetos del heap. No asumir que toda variable C# es un objeto heap ni usar referencias de C# como identidad del lenguaje.
- Conservar **GUIDs y `.meta`** de Unity. En cambios futuros, preferir ZIP diferencial con raíz `Assets/`, únicamente archivos nuevos/modificados y una lista de eliminaciones/renombres. Mantener rutas, mayúsculas y separadores válidos en Windows/Linux.
- Mantener una semántica **determinista** para el runtime; la física/animación visual de Unity **no constituye** todavía un simulador determinista. La futura ejecución de múltiples VMs con scheduling round-robin sigue siendo una meta, no un sistema jugable existente.

### Evolución resumida del desarrollo (contexto histórico)

1. **Definición del proyecto:** concepto de juego educativo de programación de unidades, Nexo, recursos, misiones y dirección visual geométrica. Se eligió Unity y se delimitaron responsabilidades del equipo.
2. **Primera infraestructura de UI:** editor integrado con explorador y archivos persistentes, consola, botones, localización parcial, estilos y numerosos ajustes iterativos de usabilidad (`Documentation/CodeEditor/CHANGELOG_*`). Se prepararon contratos para analizador/refactor/debug sin fingir que estaban implementados.
3. **Diseño y ejecución del lenguaje:** separación DScript (objetivo del jugador) / DIL (intermedio) / DIB (formato binario futuro). Desarrollo de `DataValue`, operaciones/tipos, instrucciones y `DirectivaVM`; editor cambiado provisionalmente a `.dil` para probar el runtime.
4. **Parser, memoria y colecciones inmutables:** soporte textual de DIL con funciones/imports; heap con GC lazy mark-and-sweep, String y después Tuple con hashing, longitud, casting, referencias anidadas y pruebas en Unity.
5. **Presentación y flujo de juego:** menú principal con identidad visual y selección de niveles, sistema persistente de opciones y escena de simulación/host del editor; la simulación sigue siendo maqueta.

**Estos hitos son una síntesis conceptual, no una cronología exhaustiva de commits**. Para fechas, autores y cambios línea por línea usar Git y los changelogs del repositorio.

## 3. Mapa de `Assets/`

Rutas relativas a la raíz **`Assets/`**; los nombres indicados existen en el paquete salvo cuando se señalan expresamente como futuros.

| Ruta | Qué contiene / para qué sirve |
|---|---|
| `Documentation/CodeEditor/` | Especificación inicial, README, notas de integración y changelogs v1.1–v1.16 del editor. **Histórico**, no necesariamente estado vigente. |
| `Documentation/MainMenu/` | README evolutivo del menú; **`SETTINGS.md`** detalla sistema persistente reciente y sus límites. |
| `Documentation/Simulation/README.md` | HUD de simulación y editor integrado; **ojo:** documenta la generación de una escena llamada `Game`, mientras que en este snapshot existe `BaseSimulation`. |
| `Documentation/UITheme/README.md` | Guía del tema compartido, sprites y fuentes. |
| `Editor/` | Dos baterías manuales de pruebas de Tuple, invocables desde menú de Unity Editor. |
| `Scripts/Code/` | Almacenamiento/workspace, interfaces de análisis/refactor, parser DIL y ejecución/VM. |
| `Scripts/UI/` | UI Toolkit de editor, menú, simulación y tema común. |
| `Scripts/Settings/` | Preferencias, almacenamiento y aplicación a APIs de Unity. |
| `Scripts/Localization/` | Carga y formato de cadenas localizadas del editor. |
| `Scripts/MainMenuScene/` | Comportamiento **decorativo de fondo del menú**: cámara, flotación y patrulla visual. |
| `Scenes/` | `MainMenu.unity`, `BaseSimulation.unity`, `Coding/BasicCoding.unity`, `Test.unity` y `SampleScene.unity`. |
| `Prefabs/MainMenu/` | Prefabs visuales `Nexus`, `Resource`, `Unit` del fondo del menú; **no** equivalen a gameplay. |
| `Models/`, `Materials/` | Geometría simple (`regular_octahedron`, `square_pyramid`, `triangular_isosceles_prism`) y materiales del menú/mockup. |
| `Resources/Styles/` | USS de CodeEditorUI, MainMenu y Simulation. Las interfaces se construyen principalmente con C#, no mediante UXML de pantalla. |
| `Resources/Localization/CodeEditorUI/` | Archivos `es.txt`/`en.txt` por componente: Common, Dialogs, Output, ScriptExplorer, Storage y Tooltips. |
| `Resources/UI/DirectivaUITheme.asset` | Tema central de UI Toolkit: iconos, fuentes SDF y sprite del logotipo principal, accesible desde `Resources`. |
| `Fonts/`, `Sprites/` | Fuentes (NaturalMono y SourceCodePro), identidad/logo/splash, iconos de editor, simulación, ventanas y recursos. |
| `Input/` | `InputSystem_Actions.inputactions`; **no tomar su presencia como prueba de controles jugables conectados**. |
| `Settings/` | Ajustes URP de PC/móvil, renderers, volúmenes y `Build Profiles/Linux.asset`. |
| `UI/CodeEditor.asset`, `UI Toolkit/` | PanelSettings y tema base de UI Toolkit. |
| `TutorialInfo/` | Contenido auxiliar de ejemplo/plantilla Unity-URP; no es el tutorial jugable de Directiva. |

### Escenas inspeccionadas

- **`Scenes/MainMenu.unity`:** UI de menú, cámara, suelo/iluminación y elementos decorativos 3D. Contiene seis entradas de nivel de ejemplo, vinculadas a **`BaseSimulation`** (`level_01` a `level_06`); estrellas y bloqueos de muestra, **sin progreso ganado persistente**.
- **`Scenes/BaseSimulation.unity`:** cámara/terreno, representaciones visuales de equipos rojo/azul y recursos, `SimulationUIController`, **una instancia compartida de `DirectivaCodeUIController`** y `LevelBootstrapper`. Sin reglas de unidades ni simulación real.
- **`Scenes/Coding/BasicCoding.unity`:** editor de código independiente, útil para ejecutar/probar DIL sin HUD de simulación.
- **`Scenes/Test.unity`:** escena experimental de fondo 3D/cámara; no asumir que es el juego principal.
- **`Scenes/SampleScene.unity`:** escena de plantilla de Unity/URP; no confundir con nivel funcional.
- **No existe `Scenes/Game.unity` en este paquete**; documentación antigua del instalador de simulación puede mencionarla. Confirmar escenas presentes y Build Profiles antes de cambiar navegación.

## 4. Inventario de scripts: responsabilidades y puntos de entrada

Esta sección pretende permitir localizar rápidamente **qué archivo tocar**; no enumera métodos internos. Todos los `.cs` citados son relativos a `Assets/Scripts/` salvo los de `Assets/Editor`.

### 4.1 Código del jugador: archivos, workspace, análisis

| Archivo(s) | Responsabilidad |
|---|---|
| `Code/Storage/IScriptStorage.cs`, `FileSystemScriptStorage.cs` | Contrato de almacenamiento y backend filesystem; rutas relativas seguras, operaciones CRUD, backups y metadata auxiliar. |
| `Code/Workspace/ScriptWorkspace.cs` | Documento activo, copia guardada/working, SHA-256 para estado dirty, backups, diagnósticos por archivo y operaciones de archivo/carpeta. |
| `Code/Core/DirectivaCode.cs` | Índice ligero de scripts/rutas existentes, preparado para un futuro grafo de imports/símbolos. |
| `Code/Analysis/Diagnostics.cs` | Clases/severidades de diagnósticos con rangos de texto. |
| `Code/Analysis/ICodeAnalyzer.cs`, `NoOpCodeAnalyzer.cs` | Punto de extensión de análisis; **analizador real no implementado**. |
| `Code/Refactoring/IRefactorer.cs`, `NoOpRefactorer.cs` | Contrato/plan para renombrar, mover o borrar con actualización de referencias; **refactor real no implementado**. |
| `Localization/LocalizationService.cs`, `DirectivaLocalizedException.cs` | Localización por componentes, fallback, parámetros y mensajes de errores de UI/almacenamiento. |

### 4.2 DIL: parseo y ensamblado

| Archivo | Responsabilidad |
|---|---|
| `Code/Compilation/DIL/DILParser.cs` | Parser textual multipaso; resuelve grafo de imports, globals, funciones, símbolos, labels/saltos y produce `CodeContext`. Puede inyectar metadata de depuración. |
| `Code/Compilation/DIL/DILParserOptions.cs` | Opciones/límites del parser (globals, metadata debug). |
| `Code/Compilation/DIL/DILParseException.cs` | Errores de parseo con módulo/línea. |
| `Code/Compilation/DIL/IDILSourceProvider.cs` | Contrato para recuperar texto de módulos DIL. |
| `Code/Compilation/DIL/FileSystemDILSourceProvider.cs` | Implementación filesystem de módulos `utils.vectors` → `utils/vectors.dil`, comprobando capitalización exacta. |

**No hay** en este árbol un parser/AST/compilador de **DScript**; `Compilation/` contiene actualmente `DIL/`.

### 4.3 Sistema de tipos, valores y operaciones

| Archivo(s) | Responsabilidad |
|---|---|
| `Code/Execution/Values/DataValue.cs` | Valor etiquetado por `DataTypeId` y payload; constructores de escalares/referencias. |
| `Code/Execution/Types/DataTypeId.cs`, `DataTypes/DataType.cs`, `CastResult.cs` | IDs estables, registro/despacho de tipos, truthiness/igualdad base, casting y resultado de cast. |
| `Code/Execution/Types/DataTypes/DTValues/DTValue.cs`, `DTInt.cs`, `DTDecimal.cs`, `DTBool.cs` | Base y semántica numérica: aritmética, comparación, casts, igualdad/hash compatibles Int/Bool/Decimal. Decimal de representación determinista, no float binario. |
| `Code/Execution/Types/DataTypes/DTEmptyTypes/DTNone.cs`, `DTUninitialized.cs` | `None` y estado inválido de variables no inicializadas; este último produce errores si se opera con él. |
| `Code/Execution/Types/DataTypes/DTReferences/DTString.cs`, `DTTuple.cs` | Tipos de referencia **implementados**; String inmutable; Tuple inmutable y ordenada, igualdad por elementos, indexación, hash, longitud, concatenación y cast a String. |
| `Code/Execution/Types/DataTypes/DTReferences/` | **No contiene** todavía implementaciones de List/Dict/Set. |
| `Code/Execution/Operations/OperationId.cs`, `OperationCatalog.cs`, `OperationInfo.cs` | Catálogo de operaciones, aridad, fallback, índices de receiver e IDs numéricos estables. |
| `Code/Execution/Operations/OperationCall.cs`, `OperationResult.cs`, `OperationStatus.cs`, `OperationBinding.cs` | Contexto/resultado de invocación, estado de operación, atributo para enlazar interfaces con tipos. |
| `Code/Execution/Operations/OperationInterfaces/` | Contratos `IOpArithmetic.cs`, `IOpBoolean.cs`, `IOpContainer.cs`, `IOpHash.cs`, `IOpLength.cs`, `IOpOrdering.cs`, `IOpReferenceEquality.cs`, `IOpTruthiness.cs`, `IOpValueEquality.cs`. Las interfaces enlazan métodos con `OperationId`. |
| `Code/Execution/Operations/OperationStates/OperationState.cs`, `IntPowerState.cs` | Infraestructura de operaciones continuadas, para distribuir trabajo en varios avances VM. |
| `Code/Execution/Errors/RuntimeError.cs` | Errores tipados del runtime (stack, memoria, cast, operación, índice, hash no disponible, etc.). |

**Precaución:** que un `OperationId` o `DataTypeId` exista **no significa** que haya implementación usable en un tipo concreto o exposición desde el parser DIL.

### 4.4 VM y administración de memoria

| Archivo(s) | Responsabilidad |
|---|---|
| `Code/Execution/VirtualMachine/DirectivaVM.cs` | Máquina stack-based determinista: `Advance()`/`Run(budget)`, ejecución de instrucciones, llamadas/retorno, cast/print, errores, debug, enumeración de raíces GC. |
| `Code/Execution/VirtualMachine/Code/InstructionId.cs`, `Instruction.cs` | Identificadores e instancias de instrucciones VM. |
| `Code/Execution/VirtualMachine/Code/FunctionContext.cs`, `CodeContext.cs` | Funciones compiladas con parámetros/locals/instrucciones y programa con globals/entry point. |
| `Code/Execution/VirtualMachine/Runtime/EvaluationStack.cs`, `LocalMemory.cs`, `CallFrame.cs` | Stack de evaluación, slots locales y frames/contextos de llamada. |
| `Code/Execution/VirtualMachine/Runtime/OperationDispatcher.cs` | Despacho de operaciones a tipos, fallback entre receivers y continuaciones. |
| `Code/Execution/VirtualMachine/Runtime/VMOptions.cs`, `VMExecutionStatus.cs`, `VMAdvanceResult.cs`, `VMRunResult.cs`, `VMDebugState.cs` | Límites, estado de VM, resultados/presupuesto y metadata de depuración. |
| `Code/Execution/VirtualMachine/Memory/HeapMemory.cs` | Objetos por ID `uint`, límite y conteo de memoria lógica, reserva/liberación, sweep. |
| `Code/Execution/VirtualMachine/Memory/VMMemoryContext.cs`, `IMemContext.cs` | Heap y globals potencialmente compartidos entre VMs; asignación con GC lazy y recorrido mark-and-sweep. |
| `Code/Execution/VirtualMachine/Memory/HeapObject.cs` | Objeto base; `Size` lógico y `GetReferences()` para referencias directas hacia hijos. |
| `Code/Execution/VirtualMachine/Memory/StringHeapObject.cs`, `TupleHeapObject.cs` | Implementaciones de almacenamiento heap, tamaño y contenido inmutables. |
| `Code/Execution/VMExecutionController.cs` | Adaptador entre Unity/Editor y VM; ticks acotados por frame, Start/Stop/Pause/Continue/Step, eventos Print/Completed/Faulted/breakpoint y métricas. |
| `Code/Execution/IScriptExecutionStateProvider.cs`, `NoOpExecutionStateProvider.cs` | Abstracción de estado de ejecución y alternativa sin ejecución; UI real usa `VMExecutionController`. |

### 4.5 UI del editor (UI Toolkit)

| Archivo(s) | Responsabilidad |
|---|---|
| `UI/CodeEditor/DirectivaCodeUIController.cs` | **Composición principal**: crea/monta UI, conecta workspace y VM, abre scripts, ejecuta DIL, traza líneas y maneja estados/errores. |
| `UI/CodeEditor/CodeEditorView.cs` | Campo de código, gutter/números de línea, guías de indentación, scroll, zoom, Tab/Shift+Tab, breakpoints, undo/redo y línea ejecutada. |
| `UI/CodeEditor/ScriptExplorerView.cs` | Árbol de scripts/carpetas, selección, estados, menús contextuales, drag-and-drop y operaciones de organización. |
| `UI/CodeEditor/TopBarView.cs` | Toolbar, nombre de función de entrada, toggle debug DIL, Save/Revert, controles VM y métricas. |
| `UI/CodeEditor/OutputService.cs`, `OutputPanel.cs` | Buffer y visualización de mensajes/advertencias/errores, filtros, panel colapsable, zoom. |
| `UI/CodeEditor/EditorUndoHistory.cs`, `EditorDocumentMetadata.cs` | Historial undo/redo y sidecar `.dil.editor` (incluye entry function); asociado a cada documento. |
| `UI/CodeEditor/DirectivaUIEventHub.cs` | Eventos desacoplados de UI (archivos, ejecución, breakpoint, navegación). |
| `UI/CodeEditor/ModalLayer.cs`, `HoverTooltipController.cs` | Confirmaciones, prompts y tooltips. |
| `UI/CodeEditor/HorizontalPaneResizeHandle.cs` | Resizer de paneles horizontales. |
| `UI/CodeEditor/StandaloneEditorShell.cs` | Contenedor/navegación para modo independiente `BasicCoding`. |

### 4.6 Menú, simulación visual, tema y ajustes

| Archivo(s) | Responsabilidad |
|---|---|
| `UI/MainMenu/MainMenuController.cs` | UI de inicio/opciones/niveles, nivel seleccionado, configuración de botones/colores, menú de preferencias. |
| `UI/MainMenu/LevelMenuEntry.cs` | Datos serializables de entrada de nivel (nombre, estrellas, bloqueo, escena, ID, evento). |
| `UI/MainMenu/LevelLaunchContext.cs`, `LevelBootstrapper.cs` | Paso temporal de `launchId`/nombre de nivel entre escenas; en la escena destino dispara evento configurado. |
| `UI/MainMenu/Editor/MainMenuSceneInstaller.cs` | Utilidad Editor para **crear escena**, no runtime de juego. |
| `UI/Simulation/SimulationUIController.cs` | HUD de simulación, montaje del mismo editor, modos Hidden/Docked/Fullscreen y exclusión de input cuando cursor/foco está en UI. |
| `UI/Simulation/SimulationEditorResizeHandle.cs` | Redimensionamiento del editor lateral. |
| `UI/Simulation/Editor/SimulationSceneInstaller.cs` | Generador de escena/maqueta desde Unity Editor; revisar versión vigente antes de regenerar escenas reales. |
| `UI/Shared/DirectivaUITheme.cs`, `UIIconView.cs` | Tema ScriptableObject compartido; renderizado de iconos y tipografía. |
| `UI/Shared/Editor/DirectivaUIThemeMenu.cs` | Atajos de Inspector: seleccionar tema/migrar sprites anteriores de niveles. |
| `MainMenuScene/Camera/CameraRotator.cs`, `MainMenuScene/Floater.cs`, `MainMenuScene/UnitPatrol.cs` | Movimiento/efectos decorativos de menú; **`UnitPatrol` no es la lógica de una unidad del juego**. |
| `Settings/GameSettings.cs` | Modelo de preferencias: general, audio, gráficos y controles. |
| `Settings/SettingsManager.cs` | Servicio estático, validación, mutadores, persistencia y evento de cambios. |
| `Settings/SettingsStorage.cs`, `JsonFileSettingsStorage.cs`, `PlayerPrefsSettingsStorage.cs` | Serialización común y backends JSON desktop / PlayerPrefs para WebGL. |
| `Settings/SettingsRuntimeApplier.cs` | Componente persistente/autoinicializado que aplica preferencias disponibles a APIs Unity. |

### 4.7 Pruebas y scripts de plantilla

- `Assets/Editor/Directiva_TupleSmokeTests.cs`: primera batería de construcción, indexación, igualdad, GC anidado y errores por límites.
- `Assets/Editor/Directiva_TupleCompletionSmokeTests.cs`: batería de parser DIL, hash, length, Unicode, casting/print y presión de memoria.
- `Assets/TutorialInfo/Scripts/Readme.cs` y `TutorialInfo/Scripts/Editor/ReadmeEditor.cs`: scripts de plantilla/ejemplo de Unity; **no** están relacionados con las misiones tutoriales del juego.

## 5. Comportamiento implementado: detalle operativo útil

### 5.1 Workspace/editor de código

- Actualmente **`FileSystemScriptStorage.ScriptExtension = ".dil"`**. Originalmente se diseñó la edición de `.dscript`, pero el proyecto cambió temporalmente a DIL para probar el runtime antes del compilador DScript. Al integrar DScript habrá que revisar la extensión, asociaciones y modo de ejecución, no asumir que basta con cambiar un literal.
- Scripts persistidos en **`Application.persistentDataPath/DirectivaScripts`**, fuera de `Assets/` (datos de usuario). Backups de cambios: **`.dil.back`**; metadata editor: **`.dil.editor`**. `ScriptWorkspace` compara SHA-256 de guardado/working y mantiene dirty state; usa backups periódicos.
- Permite crear, guardar, revertir, renombrar, duplicar, mover y borrar scripts/carpetas. El explorador muestra estados y permite arrastrar. Hay Undo/Redo con historial y persistencia de metadata; confirmar combinaciones de teclas en `CodeEditorView`.
- Editor con gutter, números de línea, indentación de 4 espacios, guías, scroll y zoom independiente de editor/explorador/output; consola con filtros. Los breakpoints **DIL** son operativos. `Step` funciona para DIL; `Step In`/`Step Out` siguen deshabilitados.
- Entrada de función: nombre **local** dentro del archivo `.dil` seleccionado (o primera función del módulo raíz si campo vacío), **sin parámetros** desde este runner. El parser carga imports y el editor puede leer contenido working/backups sin obligar a guardar primero.
- Resaltado de código DScript, diagnóstico real y refactor semántico siguen pendientes. Los indicadores de errores tienen infraestructura, pero el analizador activo es `NoOpCodeAnalyzer`.

### 5.2 DIL hoy

- DIL = **Directiva Intermediate Language**, archivo **`.dil`**, forma textual/assembly-like del programa. DScript (`.dscript`) es el lenguaje futuro del jugador; DIB (`.dib`) es la futura representación binaria cargable; ninguna de estas dos capas futuras funciona en este snapshot.
- El parser es **case-sensitive**. Instrucciones/operaciones usan mnemónicos en mayúsculas; comentarios `//`, anotaciones `#`; etiquetas de salto con `nombre:`. Constantes de parseo numéricas como `# nombre : 10`, usadas mediante `$nombre`, no macros generales.
- Encabezados: `# IMPORT : modulo.submodulo` al comienzo, `# GLOBAL : nombre`, `# FUNCTION : nombre`, `# PARAMS : N`, `# LOCALS : N`, cierre `# END_FUNCTION`. El parser valida su orden/contexto. Cada función queda cualificada como `modulo.funcion`; el namespace de globales es compartido por todo `CodeContext`.
- `PUSH NONE`, `PUSH BOOL TRUE/FALSE`, `PUSH INT n`, `PUSH DECIMAL n`, `PUSH STRING "..."`; `TUPLE n` consume `n` valores y apila una referencia. Cast estático: `CAST STRING` (y otros destinos registrados). También existen `POP`, `PRINT`, `LOAD/STORE_LOCAL`, `LOAD/STORE_GLOBAL`, `CALL`, `RETURN`, `CONTEXT_START/END`, saltos y `PASS` (no-op).
- Operaciones actualmente expuestas por mnemónicos en DIL: aritmética, comparaciones, `NOT`, `IS_TRUTHY`, `HASH`, `LENGTH`, `GET`, `COPY`, igualdad por valor/referencia. `AND`/`OR` se conciben como control de flujo con short-circuit mediante saltos, no una operación binaria simple del tipo.
- El parser puede inyectar `DEBUG_FILE`, `DEBUG_LINE` y `DEBUG_SPAN` en el programa compilado para trazar origen DIL; los saltos se reajustan al insertar metadata. `VMDebugState` conserva esta información. Debugging de **DScript** a partir de AST/source map queda pendiente.
- No asumir que las operaciones declaradas pero aún sin implementaciones (`Append`, `Insert`, `Delete`, `Set`, etc.) estén disponibles desde DIL. El parser solo expone el conjunto indicado en `DILParser.OperationMnemonics`.

**Ejemplo mínimo válido de DIL** (guardar como `demo.dil`; desde el editor, seleccionar y ejecutar `main`):

```dil
# FUNCTION : main
# PARAMS : 0
# LOCALS : 0
PUSH INT 10
PUSH INT 20
TUPLE 2
CAST STRING
PRINT
PUSH NONE
RETURN
# END_FUNCTION
```

La consola debe mostrar `(10, 20)` y la función debe devolver `None`.

### 5.3 VM, GC y tipos

- `DirectivaVM.Advance()` procesa un paso; `Run(instructionBudget)` ejecuta un máximo de pasos y permite trabajo acotado. Operaciones continuadas pueden conservar estado entre avances (p. ej. infraestructura de potencia). VM de pila con frames/callstack, locals precontados, globals y retorno; errores controlados en `RuntimeError`.
- Los locals empiezan como **Uninitialized**; el uso antes de asignar es error. Conjunto de globals pertenece a `VMMemoryContext`; **varias VMs pueden compartir memoria**, pero todavía no hay orquestador completo que planifique unidades en round-robin.
- `HeapMemory`: objetos direccionados con ID `uint` estable, unidad de memoria **lógica**, independiente de bytes reales de C#. `VMMemoryContext.TryAllocHeap()` intenta reservar; si falla ejecuta **mark-and-sweep** desde globals y raíces de VMs registradas, recorre `HeapObject.GetReferences()` **no recursivamente por objeto** (el GC gestiona el grafo) y reintenta. Visitas repetidas/ciclos se evitan con un conjunto de referencias marcadas. El GC es **lazy/on pressure**, no un barrido fijo cada frame.
- Tipos actualmente utilizables: escalares `Bool`, `Int`, `Decimal`, `None`, `Uninitialized`; referencias `String` y `Tuple`. `StringHeapObject` es inmutable; su tamaño lógico depende de unidades UTF-16. `TupleHeapObject` conserva copia privada de elementos, tiene tamaño lógico `1 + número de elementos` y expone hijos para GC. **Tamaño heap y `LENGTH` no son la misma medida**.
- `Tuple n` deja referencias originales en el stack durante la reserva para protegerlas ante GC. Las tuplas soportan orden e indexación negativa, igualdad estructural, identidad, concatenación, `LENGTH`, `HASH`, `CAST STRING`, truthiness y `COPY` por referencia. Su `HASH` combina hashes de elementos, respeta igualdad numérica cruzada (p. ej. `1 == True`) y reporta la ruta si un elemento anidado no es hasheable. `String` tiene hash determinista, longitud por puntos de código Unicode; los hashes pueden ser negativos.
- Casting pertenece a `DataType.CastTo` y se solicita desde **`InstructionId.Cast`**; no hay tipo de primera clase `Type` ni operación genérica `Cast` en `OperationId`. El cast identidad vive en `DataType` base; cada tipo incorpora sus destinos adicionales. Para visualización se usa una única conversión a string, **sin `repr()` público separado**. `Tuple → String` entrecomilla cadenas contenidas y formatea tuplas anidadas.
- `VMOptions` controla stack de evaluación y profundidad de llamadas; el límite de heap está en `HeapMemory`/`VMMemoryContext`. **El soporte de crecimiento/cambio de tamaño contabilizado para List/Dict/Set aún no existe**: es el siguiente desafío de arquitectura, junto con operaciones de mutación atómicas ante OOM.

### 5.4 Menús, opciones y temas

- `MainMenuController` construye las pantallas Home/Options/Levels desde C# UI Toolkit, con propiedades Inspector para etiquetas, colores, botones y UnityEvents. Las entradas de nivel tienen `sceneName`, `launchId`, `stars`, `locked` y `requiredStars` (este último es presentación/propuesta, **no un desbloqueo automático real**).
- `LevelLaunchContext` comunica nombre/ID de nivel entre escenas sin mantener el objeto menú; `LevelBootstrapper` ejecuta el `UnityEvent` coincidente en destino. Actualmente los seis IDs de ejemplo apuntan a la maqueta `BaseSimulation`.
- Preferencias se almacenan de forma versionada: en PC, `Application.persistentDataPath/settings.json` con backup/reparación; WebGL usa PlayerPrefs. Datos: idioma, ejecución en segundo plano, audio por categorías, fullscreen/resolución, captura del cursor.
- `SettingsRuntimeApplier` aplica **fullscreen/resolución**, **`Application.runInBackground`** y **confinamiento del puntero** cuando la plataforma lo admite. La resolución tiene previsualización con confirmación y reversión. **Audio y lenguaje se guardan, pero no gobiernan todavía un AudioMixer/sistema de sonidos ni localizan automáticamente todo el juego**. El servicio propio `LocalizationService` sí cambia textos localizados del editor cuando se le solicita; no asumir sincronización global con `SettingsManager`.
- `DirectivaUITheme.asset` es la referencia visual central: logo principal, iconos y fuentes SDF para editor/menú/simulación. Se usa por `Resources.Load` como fallback o por referencia Inspector. Estilos USS por subsistema.
- `SimulationUIController` integra el editor oculto/lateral/fullscreen sin recrearlo, expone `IsPointerOverUI` e `IsKeyboardCaptured` para futuros controles 3D. Los controles de velocidad/play del HUD invocan eventos opcionales pero no gobiernan simulación real.

## 6. Trabajo pendiente y prioridades

### P0 — Compromisos inmediatos acordados

1. **Completar heap mutable: List, Dict, Set.** Después de Tuple, definir modificación de tamaño y comunicación con administrador de memoria, rechazo por OOM sin mutaciones parciales, referencias hijas para GC y operaciones correspondientes; Dict/Set agregan igualdad, claves hashables y unicidad. No hay implementaciones concretas actualmente.
2. **Implementar compilador DScript → DIL** solo cuando termine la tarea anterior. Incluye diseño sintáctico real, tokenización, AST, scopes, variables, estructuras de control, funciones/imports y mapeo de líneas fuente. No existe todavía en este árbol.

### P1 — Transformar infraestructura en juego

- Crear modelo de simulación y entidades reales: **unidades, Nexo/base, recursos**, acciones (mover, atacar, minar, descargar), reglas de mundo, tiempos, victoria/derrota, límites y balance.
- Orquestar programas/VMs por unidad o Nexo con presupuesto de instrucciones, scheduler determinista (dirección prevista: round-robin), eventos y API segura hacia simulación.
- Conectar HUD Play/Pause/Stop/velocidades al simulador, configurar niveles reales, tutoriales/misiones y feedback del mundo 3D.
- Asociar scripts DScript a unidades/entidades, compilar/ejecutar y diseñar la experiencia de jugador que no conoce DIL.
- Implementar progresión real de niveles, desbloqueos, estrellas/puntuación y persistencia de progreso, en lugar de entradas visuales/de prueba.
- Ejecutar playtests con usuarios **cuando exista un sistema jugable en DScript**; no presentar las baterías técnicas actuales como playtests de UX.

### P2 — Mejoras técnicas y de producto previstas

- Analizador sintáctico/semántico real, diagnósticos en editor, resaltado y refactor de imports; sustituir `NoOpCodeAnalyzer`/`NoOpRefactorer`.
- Depuración de **DScript**: ejecución resaltada en código original, breakpoints de fuente, Step In/Out, inspección de variables y estrategia de snapshot/freeze del código en ejecución.
- Serialización **DIB** del código compilado; formatos de snapshots de VMs no son una necesidad actual.
- Aplicación global de audio y locale guardados; controles/rebinding reales, pulido de UI, accesibilidad y localización completa.
- Validar empaquetado Windows/Linux, permisos/rutas, builds reproducibles, rendimiento, seguridad y pruebas de integración continuas.

Estas prioridades son **orientativas**; contrastar con backlog y compromisos más recientes del repositorio antes de fijar fechas.

## 7. Verificación, pruebas y limitaciones conocidas

### Evidencia disponible

- Dos suites manuales en `Assets/Editor`: ejecutar desde Unity **`Directiva > Tests > Run Tuple Smoke Tests`** y **`Directiva > Tests > Run Tuple Completion Smoke Tests`**. El responsable del proyecto **reportó ambas exitosas el 15-10-2026**; la segunda cubre 13 casos, y la primera incluye 8 pruebas base.
- El responsable también probó la demo DIL real de tuplas desde el editor: representación anidada, `GET`, hash, `LENGTH` y retorno `None` mostraron resultados plausibles.
- **Este documento se elaboró leyendo el paquete `Assets/`**; no ejecuta Unity ni reemplaza una compilación/CI. No extrapolar las pruebas de Tuple a los subsistemas todavía no construidos.

### No asumir que existe / riesgos habituales al continuar

- No hay **simulación jugable**, IA de unidades, sistema real de daño/minado/recolección, reglas de niveles, scheduler de VMs por entidades ni misiones completables, aunque existan iconos, prefabs o GameObjects con esos nombres.
- El editor opera sobre **`.dil`**, aunque la documentación original y el diseño futuro mencionen **`.dscript`**. Evitar cambios globales de extensión sin plan de migración del workspace.
- `ScriptExplorer` puede mostrar diagnósticos y acciones de refactor, pero **análisis y refactor reales son NoOp**. Los breakpoints ejecutables actualmente corresponden a **DIL**, no a DScript.
- `Enum` de tipos/operaciones lista funcionalidades planificadas; verificar implementación efectiva y soporte textual en `DILParser` antes de anunciarla.
- Documentación histórica puede estar desfasada (p. ej. `Game.unity`, editor sin undo o Step siempre deshabilitado). Priorizar fuente y escenas actuales.
- `MainMenuScene/UnitPatrol.cs` y los `Prefabs/MainMenu/` son **decoración**, no simulación de unidades.
- Los archivos `Settings/` bajo la raíz de `Assets/` son **configuración URP/Build Profiles**, distintos del sistema de opciones de juego en `Scripts/Settings/`.
- Este ZIP **no incluye** `Packages/`, `ProjectSettings/`, `UserSettings/`, historial Git, documentación externa ni configuraciones CI; otro agente con acceso al repositorio completo debe inspeccionarlos para versión exacta de Unity, paquetes, escenas incluidas en build, estado de ramas y cobertura histórica.

## 8. Orientaciones de trabajo para agentes posteriores

1. **Leer primero** este documento y el estado de `git status`, el backlog más reciente y las diferencias de la rama actual. El presente resumen captura un snapshot, no un contrato inmutable.
2. **Antes de editar una clase**, abrir código real, firmas y clases consumidoras. Documentación de `Documentation/` sirve para intención y evolución, no para sustituir revisión de implementación.
3. **No romper compatibilidad** de `DataTypeId`, `OperationId`, `InstructionId`, `.meta`/GUID ni rutas de scripts. Preferir cambios pequeños, tipados y testeables.
4. **Mantener la frontera VM/simulación.** Evitar referencias Unity UI dentro del núcleo DSExecution; usar eventos/controladores/contratos para integración.
5. **Revisar raíces del GC y presupuesto de memoria** al introducir contenedores o casts que asignen heap; el GC existe y funciona para referencias anidadas, pero los contenedores mutables son la siguiente dificultad.
6. **Diferenciar implementado / parcial / previsto / no implementado** en los informes. No sustituir evidencia por nombres de clases o assets.
7. **Pruebas:** comenzar por suites Unity Editor disponibles y `BasicCoding`; luego probar navegación `MainMenu → BaseSimulation` y cambios de UI/configuración. Añadir casos mínimos reproducibles al cambiar VM/parser.
8. **Entregas de código:** usar diferencial `Assets/` con `.meta` preservados y declarar eliminaciones/renombres; actualizar este archivo cuando cambien arquitectura, estado de una función relevante, decisiones principales o prioridades.

## 9. Mantenimiento de este contexto

- **Ubicación canónica:** `Assets/DIRECTIVA_CONTEXT.md` (este archivo). **Mantenerlo en la raíz de Assets**, no moverlo a `Documentation/` sin acuerdo del responsable.
- **Al actualizar:** cambiar la fecha/hora exacta de cabecera con zona horaria, indicar el snapshot/commit fuente si se conoce, revisar tabla de estados, rutas y prioridades; corregir afirmaciones que dejaron de ser ciertas. Preferir **editar el estado actual** en vez de ir acumulando un registro enorme de parches.
- **Periodicidad:** acompañar cambios importantes en futuros ZIP diferenciales `Assets/*`; **no** es necesario tocarlo por ajustes cosméticos o cambios menores sin impacto contextual.
- **Auditoría histórica:** commits, changelogs del editor, documentos externos y evidencias quedan en el repositorio; este documento es una **foto mantenible del presente** con el contexto suficiente para continuar el proyecto.

---

**Fin del snapshot.** Si otro agente detecta una contradicción con código/escena/git posterior, debe reportarla y actualizar el documento con hechos verificables; no asumir que el resumen es infalible.
