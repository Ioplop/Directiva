# Directiva — contexto técnico del proyecto Unity

> **Documento de orientación para agentes de IA y colaboradores.** Es un mapa del proyecto, no una especificación exhaustiva ni un reemplazo del código fuente.
>
> **Última actualización:** 2026-10-22 02:10:45 -0300 (America/Santiago)  
> **Base examinada:** `Assets.tar(1).gz` + `Directiva_Iterators_Range_Differential.zip` + `Directiva_NativeLibrary_Differential.zip` + `Directiva_NativeLibrary_TestFix_Differential.zip`, de los paquetes ya trabajados. La suite DIL previa tiene **862 casos** (748 anteriores + 114 nativos); el usuario indicó que las pruebas ya pasaban antes de solicitar DScript. El usuario confirmó **1000/1000 PASS en Unity** después del parche de comas finales del frontend. Esta revisión cambia `global` por `shared`, admite ciclos/imports y agrega una batería para el plan de inicialización; el nuevo total **1041 pruebas previstas**, aún por ejecutar en Unity.  
> **Alcance:** exclusivamente el videojuego Unity **Directiva**. No cubre informes académicos, evidencias, gestión externa ni archivos que no vinieron en este paquete.  
> **Criterio de verdad:** código, escenas y assets actuales > este resumen > documentación histórica > planes conversados. En caso de discrepancia, inspeccionar el repositorio y `git log`.

## 0. Lectura rápida para otro agente

**Directiva** es un videojuego educativo y competitivo de PC hecho con **Unity 6 / C#**, con **UI Toolkit** y recursos **URP**. El objetivo es que el jugador programe el comportamiento de **unidades** en un lenguaje textual propio, **DScript**, inspirado en Python, dentro de un entorno de simulación con un **Nexo** (base), recursos y objetivos/misiones. *Unidad* es el término del juego; evitar llamarlas «agentes» para no confundirlas con agentes de IA.

**Estado al corte:** runtime DIL/VM, heap/GC, funciones como valores, `Range`/iteradores y biblioteca nativa (`math`, `random`, builtins) implementados. El usuario confirmó **1000/1000 pruebas PASS en Unity** tras el último parche DScript. Este cambio revisa el frontend para implementar **`shared` a nivel de módulo, slots canónicos entre archivos, imports circulares y un plan determinista de inicialización con pasos independientes**. Se añaden **41 pruebas nuevas/actualizadas en catálogo** (total esperado **1041**, pendiente ejecución). El backend DScript → DIL, los inicializadores ejecutables y el orquestador todavía **NO existen**; la simulación y los objetos externos siguen pendientes.

**Prioridad inmediata:** aplicar el ZIP diferencial de esta revisión, ejecutar `Directiva > Tests > DScript > TestEverything` y `Directiva > Tests > TestAll` (**1041 casos esperados**). Ante fallos, entregar el reporte Unity. Luego implementar el **backend DScript AST → DIL → CodeContext** y un **bootstrap que evalúe cada inicializador aislado, informe cada fallo en consola y CONTINÚE con los siguientes y con las VMs normales**. No convertir errores de runtime de inicialización en abortos de partida; los errores de sintaxis/binding sí impiden compilar.

### Estado por subsistema

| Sistema | Estado conocido | Dónde empezar |
|---|---|---|
| Menú principal y selección de niveles | Implementado como interfaz, navegación y carga de escenas; progreso/desbloqueo real pendiente | `Scripts/UI/MainMenu/`, `Scenes/MainMenu.unity` |
| Opciones persistentes | JSON/PlayerPrefs y aplicación de resolución, fullscreen, cursor y ejecución en segundo plano; audio/idioma sin integración global | `Scripts/Settings/`, `Documentation/MainMenu/SETTINGS.md` |
| Editor de scripts | Funcional para **`.dil`**; árbol de archivos, edición, backups, historial undo/redo, consola, breakpoints DIL | `Scripts/UI/CodeEditor/`, `Scripts/Code/{Storage,Workspace}/` |
| Lenguaje DScript | **Frontend revisado:** `shared` solo a nivel módulo, un único slot por nombre en todos los archivos, imports circulares y plan DFS de inicializadores. **1000/1000 PASS anterior en Unity; 1041 esperado en esta revisión, pendientes de validar**. Generador DIL y bootstrap aún pendientes. | `Scripts/Code/Compilation/DScript/`, `GRAMMAR.md`, `Editor/DirectivaTests/DScript/` |
| DIL (texto intermedio) | Parser multipaso, VM, iteradores, funciones nativas; **862 tests registrados anteriormente** con parche de expectativas, confirmados funcionales por el usuario | `Scripts/Code/Compilation/DIL/`, `Editor/DirectivaTests/DIL/` |
| Miembros de tipo | **Implementados y validados en Unity**: anotaciones getter/setter/método, herencia y `override`, acceso por DIL, continuaciones | `Scripts/Code/Execution/Types/DataTypes/{DScriptMemberAttributes.cs,DataType.Members.cs}`; `DirectivaVM`, `DILParser` |
| Funciones como valores | **Implementadas y validadas en Unity (748/748)**: `PUSH_FUNCTION`, `CALL_INDIRECT n`, `DTFunction`; ID compartido por `CodeContext` | `Code/Execution/Types/DataTypes/DTValues/DTFunction.cs`, `DirectivaVM`, `DILParser` |
| Iteración y rangos | **Implementados y validados en Unity:** `RANGE n` (constructor DIL), `ITER_INIT`, `ITER_NEXT destino`, Range/Iterator en heap; List/Tuple/Set/Dict y Range iterables | `Code/Execution/VirtualMachine/Memory/{IteratorHeapObject,RangeHeapObject}.cs` |
| Biblioteca nativa | **Integrada; pruebas corregidas y posteriormente confirmadas por el usuario:** 11 builtins (`print`, `range`, `len`, `abs`, `min`, `max`, `clamp`, `int`, `decimal`, `str`, `bool`), 13 funciones `math`, 3 `random`; `PUSH_NATIVE`, `PUSH_FUNCTION` y `CALL_INDIRECT` comparten referencias | `Scripts/Code/Execution/Native/` |
| Máquina virtual | Ejecuta `CodeContext`: stack, llamadas, locals, globals, saltos, operaciones, casts, print y límites | `Scripts/Code/Execution/VirtualMachine/` |
| Heap y GC | Heap con límites lógicos; **mark-and-sweep lazy** al fallar una reserva, referencias anidadas y compartidas | `Scripts/Code/Execution/VirtualMachine/Memory/` |
| Tipos | `None`, `Uninitialized`, `Bool`, `Int`, `Decimal`, `String`, `Tuple`, `List`, `Set`, `Dict`, `Function` (no heap), **Range e Iterator (heap)**; simulación pendiente | `Scripts/Code/Execution/Types/` |
| Interfaz de simulación | HUD y editor acoplables; controles de simulación **solo visuales** | `Scripts/UI/Simulation/`, `Scenes/BaseSimulation.unity` |
| Simulación jugable y misiones | **No implementadas**: lógica de unidades, combate, minería, Nexo, recursos, victoria, tutorial | Crear subsistemas de simulación y orquestación |
| Análisis/refactorización DScript | Frontend (AST y símbolos) nuevo e independiente; interfaces de análisis del editor y refactorización **siguen NoOp**, sin conexión aún a la UI | `Scripts/Code/{Analysis,Refactoring,Core}/` |
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
[EXISTE] Código fuente DScript (.dscript)
                |
[EXISTE, NUEVO] Lexer / Parser / AST / imports / análisis semántico
                |
[FUTURO] Generación DIL (entrega 2)
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
- La VM usa datos tipados mediante `DataValue`. Los escalares y **Function** usan el payload del valor; `String`, `Tuple`, `List`, `Set`, `Dict`, **Range** e **Iterator** usan referencias (`uint`) a objetos del heap. No asumir que toda variable C# es un objeto heap ni usar referencias de C# como identidad del lenguaje.
- Conservar **GUIDs y `.meta`** de Unity. En cambios futuros, preferir ZIP diferencial con raíz `Assets/`, únicamente archivos nuevos/modificados y una lista de eliminaciones/renombres. Mantener rutas, mayúsculas y separadores válidos en Windows/Linux.
- Mantener una semántica **determinista** para el runtime; la física/animación visual de Unity **no constituye** todavía un simulador determinista. La futura ejecución de múltiples VMs con scheduling round-robin sigue siendo una meta, no un sistema jugable existente.

### Evolución resumida del desarrollo (contexto histórico)

1. **Definición del proyecto:** concepto de juego educativo de programación de unidades, Nexo, recursos, misiones y dirección visual geométrica. Se eligió Unity y se delimitaron responsabilidades del equipo.
2. **Primera infraestructura de UI:** editor integrado con explorador y archivos persistentes, consola, botones, localización parcial, estilos y numerosos ajustes iterativos de usabilidad (`Documentation/CodeEditor/CHANGELOG_*`). Se prepararon contratos para analizador/refactor/debug sin fingir que estaban implementados.
3. **Diseño y ejecución del lenguaje:** separación DScript (objetivo del jugador) / DIL (intermedio) / DIB (formato binario futuro). Desarrollo de `DataValue`, operaciones/tipos, instrucciones y `DirectivaVM`; editor cambiado provisionalmente a `.dil` para probar el runtime.
4. **Parser, memoria y colecciones inmutables:** soporte textual de DIL con funciones/imports; heap con GC lazy mark-and-sweep, String y después Tuple con hashing, longitud, casting, referencias anidadas y pruebas en Unity.
5. **List mutable (19-10-2026, validada en Unity):** reserva/redimensionamiento lógico por objeto, raíz auxiliar por VM para operaciones continuadas, actualización atómica por intercambio de buffers, conflictos deterministas entre VMs y pruebas DIL nuevas.
6. **Caché de hashes inmutables (19-10-2026):** String/Tuple guardan hash tras el primer cálculo válido; los objetos mutables siguen sin HASH. Las pruebas de esta mejora ya forman parte de la línea base de 380 pruebas aprobadas.
7. **Set y Dictionary (19-10-2026):** Set confirmado con 380 pruebas y Dictionary con 530/530, según ejecución realizada por el responsable en Unity; pares clave/valor, hashing y continuaciones implementados.
8. **Presentación y flujo de juego:** menú principal con identidad visual y selección de niveles, sistema persistente de opciones y escena de simulación/host del editor; la simulación sigue siendo maqueta.

**Estos hitos son una síntesis conceptual, no una cronología exhaustiva de commits**. Para fechas, autores y cambios línea por línea usar Git y los changelogs del repositorio.

## 3. Mapa de `Assets/`

Rutas relativas a la raíz **`Assets/`**; los nombres indicados existen en el paquete salvo cuando se señalan expresamente como futuros.

| Ruta | Qué contiene / para qué sirve |
|---|---|
| `Documentation/CodeEditor/` | Especificación inicial, README, notas de integración y changelogs v1.1–v1.16 del editor. **Histórico**, no necesariamente estado vigente. |
| `Documentation/MainMenu/` | README evolutivo del menú; **`SETTINGS.md`** detalla sistema persistente reciente y sus límites. |
| `Documentation/Simulation/README.md` | HUD de simulación y editor integrado; **ojo:** documenta la generación de una escena llamada `Game`, mientras que en este snapshot existe `BaseSimulation`. |
| `Documentation/UITheme/README.md` | Guía del tema compartido, sprites y fuentes. |
| `Editor/` | Suite automatizada DIL por grupos (`DirectivaTests/`) y dos suites historicas de Tuple integradas como regresion; menus y reportes externos. |
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
| `Code/Execution/Types/DataTypes/DTReferences/DTString.cs`, `DTTuple.cs`, `DTList.cs`, `DTSet.cs`, `DTDict.cs`, `DTRange.cs`, `DTIterator.cs` | String/Tuple inmutables; List mutable con Get/Set/Append/Insert/Delete/Pop/Extend/Copy/Add/Contains/IndexOf, longitud, igualdad, impresión y control de memoria. List validada en Unity con la suite DIL. |
| `Code/Execution/Types/DataTypes/DTReferences/SequenceEquality.cs`, `SequenceFormatter.cs` | Igualdad estructural y conversión a texto de Tuple/List/Set/Dict; recorrido iterativo y ciclos seguros. |
| `Code/Execution/Types/DataTypes/DTReferences/` | **Set y Dict implementados** en DTSet.cs/DTDict.cs, con contenedores heap propios. |
| `Code/Execution/Operations/OperationId.cs`, `OperationCatalog.cs`, `OperationInfo.cs` | Catálogo de operaciones, aridad, fallback, índices de receiver e IDs numéricos estables. |
| `Code/Execution/Operations/OperationCall.cs`, `OperationResult.cs`, `OperationStatus.cs`, `OperationBinding.cs` | Contexto/resultado de invocación, estado de operación, atributo para enlazar interfaces con tipos. |
| `Code/Execution/Operations/OperationInterfaces/` | Contratos `IOpArithmetic.cs`, `IOpBoolean.cs`, `IOpContainer.cs`, `IOpHash.cs`, `IOpLength.cs`, `IOpOrdering.cs`, `IOpReferenceEquality.cs`, `IOpTruthiness.cs`, `IOpValueEquality.cs`, además de `IOpExtend.cs`, `IOpSearch.cs`, `IOpSet.cs` e `IOpDictionary.cs`. Las interfaces enlazan métodos con `OperationId`. |
| `Code/Execution/Operations/OperationStates/OperationState.cs`, `IntPowerState.cs`, `ListBuildState.cs`, `ListSearchState.cs`, `SetOperationState.cs`, `DictOperationState.cs` | Progreso de operaciones continuadas; los estados no guardan referencias al heap. La VM puede conservar una referencia auxiliar como raíz independiente. |
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
| `Code/Execution/VirtualMachine/Memory/HeapMemory.cs` | Objetos por ID `uint`, reserva/liberación, sweep, **tamaño contabilizado por objeto**, TryResize y cambio de buffers/contabilidad atómico. |
| `Code/Execution/VirtualMachine/Memory/VMMemoryContext.cs`, `IMemContext.cs` | Heap/globals compartidos, GC lazy mark-and-sweep, TryResizeHeap y TrySwapHeapContents. Durante el resize protege el objeto como raíz temporal. |
| `Code/Execution/VirtualMachine/Memory/HeapObject.cs` | Objeto base; `Size` lógico y `GetReferences()` para referencias directas hacia hijos. |
| `Code/Execution/VirtualMachine/Memory/StringHeapObject.cs`, `TupleHeapObject.cs`, `ListHeapObject.cs`, `SetHeapObject.cs`, `DictHeapObject.cs`, `RangeHeapObject.cs`, `IteratorHeapObject.cs`, `IDirectivaIterator.cs` | String/Tuple inmutables y List/Set/Dict mutables con referencias hijas, versiones y swap atómico de buffers. |
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

- `Assets/Editor/DirectivaTests/README.md`: **guía operativa** de la suite DIL, menús, reportes, grupos y extensión futura a DScript/Simulation.
- `Editor/DirectivaTests/Core/DirectivaTestModel.cs`: contrato y registro de casos; `DirectivaTestRunner.cs`: ejecución Editor main-thread con cancelación; `DirectivaTestReports.cs`: Markdown por caso y resumen Markdown/JSON externo al repo; `DirectivaTestWindow.cs`: elección de carpeta, selección/filtros y progreso; `DirectivaTestMenu.cs`: rutas `Directiva/Tests/TestAll` y `Directiva/Tests/DIL/.../TestEverything`.
- `Scripts/Code/Execution/EditorTestVisibility.cs` permite que la suite de Unity Editor acceda a mutadores internos del runtime (ensamblado `Assembly-CSharp-Editor`) para verificar invariantes sin hacer públicos esos mutadores.
- `Editor/DirectivaTests/DIL/`: fixtures y tests separados por Parser, VM, Controller, Numeric, References, Memory, Concurrency, Errors, Robustness; `DILTestHarness.cs` centraliza parseo en memoria, recolección de eventos/errores/trazas, assertions y budget de VM. Los archivos `DILListTests`, `DILListMemoryTests` y `DILListConcurrencyTests` amplían los grupos del runner; `DILHashCacheTests` incorpora 12 comprobaciones verificadas y `DILSet*Tests` aportó 126 casos ya aprobados; `DILDict*Tests` agregó 150 casos ya confirmados en Unity.
- `Assets/Editor/Directiva_TupleSmokeTests.cs` y `Directiva_TupleCompletionSmokeTests.cs`: 8 + 13 pruebas previas, **ahora incluidas en** `Directiva/Tests/DIL/Regression/TestEverything`. Se retiraron sus accesos de menú antiguos; el catálogo genera un informe por cada caso.
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
- `PUSH NONE`, `PUSH BOOL TRUE/FALSE`, `PUSH INT n`, `PUSH DECIMAL n`, `PUSH STRING "..."`; `TUPLE n`, `LIST n` y `SET n` consumen `n` valores; `DICT n` consume `n` pares `(clave, valor)` (2n valores) y apila una referencia. `SET n` elimina elementos duplicados por hash e igualdad; `SET` sin operando sigue asignando por índice en List. Cast estático: `CAST STRING` (y otros destinos registrados). También existen `POP`, `PRINT`, `LOAD/STORE_LOCAL`, `LOAD/STORE_GLOBAL`, `CALL`, `RETURN`, `CONTEXT_START/END`, saltos y `PASS` (no-op).
- Operaciones expuestas por DIL: aritmética, comparaciones, `NOT`, `IS_TRUTHY`, `HASH`, `LENGTH`, `GET`, `SET`, `COPY`, `APPEND`, `INSERT`, `DELETE`, `POP_ITEM`, `EXTEND`, `CONTAINS`, `INDEX_OF`, operaciones de conjuntos `SET_ADD`/`SET_REMOVE`/`SET_DISCARD`/`SET_POP`/`SET_UPDATE` y álgebra de conjuntos (`SET_UNION`, `SET_INTERSECTION`, `SET_DIFFERENCE`, `SET_SYMMETRIC_DIFFERENCE`, `SET_IS_SUBSET`, `SET_IS_SUPERSET`, `SET_IS_DISJOINT`), operaciones de diccionarios `DICT_GET_DEFAULT`, `DICT_CLEAR`, `DICT_UPDATE`, `DICT_KEYS`, `DICT_VALUES`, `DICT_ITEMS`, igualdad por valor/referencia. `POP` sigue descartando un valor del stack, distinto de `POP_ITEM` de lista. `AND`/`OR` se conciben como control de flujo con short-circuit mediante saltos, no una operación binaria simple del tipo.
- El parser puede inyectar `DEBUG_FILE`, `DEBUG_LINE` y `DEBUG_SPAN` en el programa compilado para trazar origen DIL; los saltos se reajustan al insertar metadata. `VMDebugState` conserva esta información. Debugging de **DScript** a partir de AST/source map queda pendiente.
- No asumir que todas las operaciones declaradas son universales: las mutaciones de List no están implementadas para Tuple/String. El parser expone exclusivamente los mnemónicos de `DILParser.OperationMnemonics`.

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
- Tipos actualmente utilizables: escalares `Bool`, `Int`, `Decimal`, `None`, `Uninitialized`; referencias `String`, `Tuple`, `List`, `Set` y `Dict` (validados en Unity) y `Function` (**656/656 validaron el primer corte**), y `Range`/`Iterator` (**748/748 validados en Unity**). `StringHeapObject` es inmutable; su tamaño lógico depende de unidades UTF-16. `TupleHeapObject` conserva copia privada de elementos, tiene tamaño lógico `1 + número de elementos` y expone hijos para GC. **Tamaño heap y `LENGTH` no son la misma medida**.
- `TUPLE n` deja referencias originales en el stack durante la reserva para protegerlas ante GC. Las tuplas soportan orden e indexación negativa, igualdad estructural, identidad, concatenación, `LENGTH`, `HASH`, `CAST STRING`, truthiness y `COPY` por referencia. Su `HASH` combina hashes de elementos y ahora almacena el resultado exitoso en caché dentro de `TupleHeapObject` (incluidas tuplas anidadas), respeta igualdad numérica cruzada (p. ej. `1 == True`) y reporta la ruta si un elemento anidado no es hasheable. `String` tiene hash determinista FNV-1a cacheado en `StringHeapObject` tras el primer cálculo, longitud por puntos de código Unicode; los hashes pueden ser negativos.
- Casting pertenece a `DataType.CastTo` y se solicita desde **`InstructionId.Cast`**; no hay tipo de primera clase `Type` ni operación genérica `Cast` en `OperationId`. El cast identidad vive en `DataType` base; cada tipo incorpora sus destinos adicionales. Para visualización se usa una única conversión a string, **sin `repr()` público separado**. `Tuple → String` entrecomilla cadenas contenidas y formatea tuplas anidadas.
- **Limitación deliberada:** operaciones grandes de List como INSERT interno/EXTEND/DELETE/COPY/ADD y búsquedas trabajan con pasos continuados; `ValueEquals` estructural y conversión a texto aún recorren los elementos de forma síncrona. Los buffers auxiliares consumen heap extra y pueden producir OOM aunque el tamaño final de la lista cupiera por sí solo. Cambios intercalados de otra VM causan `InvalidOperation`; el resultado parcial nunca se publica.
- `VMOptions` controla stack de evaluación y profundidad de llamadas; el límite de heap está en `HeapMemory`/`VMMemoryContext`. **El crecimiento de List está implementado y validado en Unity**: `TryResizeHeap` autoriza unidades antes de modificar el objeto; reducción libera unidades lógicas; los bytes/capacidad reales de C# no son la medida del heap. Las mutaciones grandes construyen un `ListHeapObject` auxiliar en heap; la VM mantiene su ID como raíz fuera de `OperationState`; se trabaja por bloques de 16 elementos y se confirma intercambiando contenidos/contabilidad de forma atómica. Versiones detectan modificaciones intercaladas entre VMs y producen error explícito, en lugar de sobrescribirlas. `Set` utiliza el mismo contrato de heap, versiones y raíz auxiliar; `Dict` reutiliza el mismo contrato para dos slots por par (clave/valor).

### 5.3.1 Set: estado validado (19-10-2026)

- `SetHeapObject` almacena miembros únicos con hashes deterministas por miembro y buckets `hash -> índices`; las colisiones **siempre se resuelven con `ValueEquals`**, no se asume que mismo hash implica mismo valor. La iteración es determinista para la misma secuencia de mutaciones, pero no es un orden garantizado por el lenguaje.
- `SET n` construye desde los últimos n valores del stack; **`SET` sin operando sigue siendo la operación antigua de asignación por índice de List**. `CAST SET` admite identidad. Mutaciones: `SET_ADD`, `SET_DISCARD`, `SET_REMOVE`, `SET_POP`, `SET_UPDATE`; consultas: `CONTAINS`, `LENGTH`, `VALUE_EQUALS`, `REFERENCE_EQUALS`, `IS_TRUTHY`, `CAST STRING` y `COPY`; álgebra: `SET_UNION`, `SET_INTERSECTION`, `SET_DIFFERENCE`, `SET_SYMMETRIC_DIFFERENCE`, `SET_IS_SUBSET`, `SET_IS_SUPERSET`, `SET_IS_DISJOINT`.
- List/Set/Dict son **no hasheables**; Set admite como miembros Int/Bool/Decimal, String, Tuple hasheable y None. Claves duplicadas se colapsan usando hashing e igualdad numérica compatible (p. ej. `True` y `1`). Falla con `UnhashableType` si el miembro no es hasheable (incluidas tuplas anidadas no hasheables). Un Set **no contiene directamente otro Set**.
- Modelo de memoria `1 + miembros únicos`; add/remove/pop pasan por `TryResizeHeap`. Copia, álgebra y update trabajan hasta 16 entradas por continuación; preparan auxiliares en heap bajo raíz de VM, comprueban versiones y publican cambios de `SET_UPDATE` en un swap atómico. GC mark/sweep recorre las referencias directas de miembros. Igualdad y formateo siguen siendo sincrónicos, igual que en List/Tuple; operaciones de colisión de un mismo bucket pueden tener costo variable.
- `Editor/DirectivaTests/DIL/DILSet*Tests.cs` contiene **126 pruebas nuevas** en `Sets`, `SetAlgebra`, `SetMemory`, `SetConcurrency` y `SetRobustness`. Fueron ejecutadas satisfactoriamente en Unity: **380/380 PASS** (incluyendo las 254 pruebas previas).

### 5.3.2 Dictionary: estado de implementación (19-10-2026)

- `DictHeapObject` mantiene pares clave/valor con un índice de buckets por hash y resolución real de colisiones mediante `ValueEquals`; conserva **orden de inserción determinista**. Solo las claves deben ser hasheables; los valores pueden contener cualquier tipo, incluso el mismo diccionario o ciclos con listas. **El diccionario no es hasheable.**
- Memoria lógica de una instancia: **`1 + 2 * entradas únicas`**, sin contar nuevamente los objetos referenciados. `DICT n` consume **2n valores** (clave, valor, ...). La última clave equivalente actualiza el valor anterior, preservando la clave original y su posición. `GetReferences()` expone **claves y valores** para el GC lazy mark-and-sweep.
- DIL: `DICT n`, `GET`, `SET` sin operando (que ahora también sirve en Dict), `DELETE`, `POP_ITEM`, `CONTAINS`, `LENGTH`, `COPY`, `VALUE_EQUALS`, `REFERENCE_EQUALS`, `IS_TRUTHY`, `CAST STRING`, `CAST DICT`; además `DICT_GET_DEFAULT`, `DICT_CLEAR`, `DICT_UPDATE`, `DICT_KEYS`, `DICT_VALUES`, `DICT_ITEMS` (las tres últimas devuelven listas; Items produce tuplas `(key, value)`). Los resultados de mutaciones son `None`, excepto `POP_ITEM`, que devuelve el valor eliminado. Las claves inexistentes de GET/DELETE/POP_ITEM producen `InvalidOperation`.
- `COPY`, `DICT_UPDATE` y vistas son **continuadas**, de a 16 entradas por paso. Buffer en heap bajo raíz auxiliar exclusiva de la VM; `DICT_UPDATE` confirma con swap atómico, conserva el HeapId del destino y rechaza modificaciones intercaladas de origen o destino mediante versiones. Un resultado auxiliar puede requerir más memoria lógica que la modificación final. Eliminaciones compactan el arreglo de entradas sin romper orden (actualmente O(n) síncrono); hashing y comparaciones de claves de una misma colisión son también síncronos.
- `SequenceEquality` y `SequenceFormatter` ahora admiten Dict junto a List/Tuple/Set, incluso ciclos; no hay API `repr()`.
- **Verificación:** las 150 pruebas de Dictionary, junto a las 380 anteriores, fueron aprobadas por el responsable en Unity (**530/530 PASS, 19-10-2026**). Los informes históricos quedan fuera de `Assets/`.

### 5.4 Menús, opciones y temas

- `MainMenuController` construye las pantallas Home/Options/Levels desde C# UI Toolkit, con propiedades Inspector para etiquetas, colores, botones y UnityEvents. Las entradas de nivel tienen `sceneName`, `launchId`, `stars`, `locked` y `requiredStars` (este último es presentación/propuesta, **no un desbloqueo automático real**).
- `LevelLaunchContext` comunica nombre/ID de nivel entre escenas sin mantener el objeto menú; `LevelBootstrapper` ejecuta el `UnityEvent` coincidente en destino. Actualmente los seis IDs de ejemplo apuntan a la maqueta `BaseSimulation`.
- Preferencias se almacenan de forma versionada: en PC, `Application.persistentDataPath/settings.json` con backup/reparación; WebGL usa PlayerPrefs. Datos: idioma, ejecución en segundo plano, audio por categorías, fullscreen/resolución, captura del cursor.
- `SettingsRuntimeApplier` aplica **fullscreen/resolución**, **`Application.runInBackground`** y **confinamiento del puntero** cuando la plataforma lo admite. La resolución tiene previsualización con confirmación y reversión. **Audio y lenguaje se guardan, pero no gobiernan todavía un AudioMixer/sistema de sonidos ni localizan automáticamente todo el juego**. El servicio propio `LocalizationService` sí cambia textos localizados del editor cuando se le solicita; no asumir sincronización global con `SettingsManager`.
- `DirectivaUITheme.asset` es la referencia visual central: logo principal, iconos y fuentes SDF para editor/menú/simulación. Se usa por `Resources.Load` como fallback o por referencia Inspector. Estilos USS por subsistema.
- `SimulationUIController` integra el editor oculto/lateral/fullscreen sin recrearlo, expone `IsPointerOverUI` e `IsKeyboardCaptured` para futuros controles 3D. Los controles de velocidad/play del HUD invocan eventos opcionales pero no gobiernan simulación real.

## 6. Trabajo pendiente y prioridades

### P0 — Compromisos inmediatos acordados

1. **Heap/colecciones:** String, Tuple, List, Set y Dict se consideran implementados y aprobados en Unity (**530/530 pruebas confirmadas**). No añadir nuevas estructuras del heap salvo necesidad justificada; preservar regresión.
2. **Iteradores y Range:** implementación concluida, con **748/748 pruebas aprobadas en Unity**. Conservar cobertura de GC, versiones, `None`, extremos de rangos y metadatos de depuración.
3. **Validar frontend DScript y después generar DIL:** la suite DIL previa tiene **862 casos** y Unity registró **134** DScript (132 PASS, 2 FAIL). Este parche añade 4 casos: **1000 previstos en TestAll**. Compilar y ejecutar todos, corregir problemas y, en la segunda entrega, implementar generación DIL. `range()` ya es nativa y `for` se reconoce sintácticamente; todavía no se ejecuta código fuente DScript.

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

- Las suites de Tuple originales (8 y 13 casos) fueron **ejecutadas exitosamente por el responsable el 15-10-2026** y ahora se registran bajo `Directiva > Tests > DIL > Regression > TestEverything`.
- Se agregó una **suite de pruebas DIL automatizada Editor-only** (parser, VM, control de ejecución, operaciones, heap/GC, concurrencia, errores y estrés), invocable en `Directiva > Tests > DIL > TestEverything`, grupos y `Directiva > Tests > TestAll`. Cada caso exporta Markdown y cada corrida Markdown/JSON; la carpeta se configura mediante ventana Editor y **debe estar fuera del repositorio**. El responsable confirmó **184/184** (15-10), **242/242** tras List, **254/254** tras Hash, **380/380** tras Set y **530/530** tras Dictionary (19-10-2026). **Miembros, funciones como valores, Range e iteradores validados (748 casos anteriores). Esta entrega añade 114 pruebas nativas (862 en total), ya ejecutadas una vez en Unity; tres expectativas incorrectas corregidas, repetir suite pendiente.** Leer `Assets/Editor/DirectivaTests/README.md`.
- El responsable también probó la demo DIL real de tuplas desde el editor: representación anidada, `GET`, hash, `LENGTH` y retorno `None` mostraron resultados plausibles. Las pruebas de List incluyen GC circular, OOM atómico, root auxiliar, búsqueda continuada y conflictos entre VMs.
- **Este documento se elaboró leyendo el paquete `Assets/` más los diferenciales indicados y confirmaciones de pruebas del responsable**; no ejecuta Unity ni reemplaza una compilación/CI. No extrapolar las pruebas de Tuple a los subsistemas todavía no construidos.

### No asumir que existe / riesgos habituales al continuar

- No hay **simulación jugable**, IA de unidades, sistema real de daño/minado/recolección, reglas de niveles, scheduler de VMs por entidades ni misiones completables, aunque existan iconos, prefabs o GameObjects con esos nombres.
- El editor opera sobre **`.dil`**, aunque la documentación original y el diseño futuro mencionen **`.dscript`**. Evitar cambios globales de extensión sin plan de migración del workspace.
- `ScriptExplorer` puede mostrar diagnósticos y acciones de refactor, pero **análisis y refactor reales son NoOp**. Los breakpoints ejecutables actualmente corresponden a **DIL**, no a DScript.
- `Enum` de tipos/operaciones lista funcionalidades planificadas; verificar implementación efectiva y soporte textual en `DILParser` antes de anunciarla.
- Documentación histórica puede estar desfasada (p. ej. `Game.unity`, editor sin undo o Step siempre deshabilitado). Priorizar fuente y escenas actuales.
- `MainMenuScene/UnitPatrol.cs` y los `Prefabs/MainMenu/` son **decoración**, no simulación de unidades.
- Los archivos `Settings/` bajo la raíz de `Assets/` son **configuración URP/Build Profiles**, distintos del sistema de opciones de juego en `Scripts/Settings/`.
- El archivo `Assets.tar.gz` recibido **no incluye** `Packages/`, `ProjectSettings/`, `UserSettings/`, historial Git, documentación externa ni configuraciones CI; otro agente con acceso al repositorio completo debe inspeccionarlos para versión exacta de Unity, paquetes, escenas incluidas en build, estado de ramas y cobertura histórica.

## 8. Orientaciones de trabajo para agentes posteriores

1. **Leer primero** este documento y el estado de `git status`, el backlog más reciente y las diferencias de la rama actual. El presente resumen captura un snapshot, no un contrato inmutable.
2. **Antes de editar una clase**, abrir código real, firmas y clases consumidoras. Documentación de `Documentation/` sirve para intención y evolución, no para sustituir revisión de implementación.
3. **No romper compatibilidad** de `DataTypeId`, `OperationId`, `InstructionId`, `.meta`/GUID ni rutas de scripts. Preferir cambios pequeños, tipados y testeables.
4. **Mantener la frontera VM/simulación.** Evitar referencias Unity UI dentro del núcleo DSExecution; usar eventos/controladores/contratos para integración.
5. **Revisar raíces del GC y presupuesto de memoria** al introducir contenedores o casts; las listas mutables ya tienen TryResizeHeap y operación auxiliar en la VM. No almacenar DataValue/HeapId en OperationState, ni exponer buffers provisionales desde DIL.
6. **Diferenciar implementado / parcial / previsto / no implementado** en los informes. No sustituir evidencia por nombres de clases o assets.
7. **Pruebas:** ejecutar primero `Directiva > Tests > TestAll` (seleccionar carpeta externa desde la ventana), analizar `SUMMARY.md` y los informes fallidos, luego probar `BasicCoding`, navegación `MainMenu → BaseSimulation` y cambios de UI/configuración. Añadir casos mínimos reproducibles al cambiar VM/parser.
8. **Entregas de código:** usar diferencial `Assets/` con `.meta` preservados y declarar eliminaciones/renombres; actualizar este archivo cuando cambien arquitectura, estado de una función relevante, decisiones principales o prioridades.

## 9. Mantenimiento de este contexto

- **Ubicación canónica:** `Assets/DIRECTIVA_CONTEXT.md` (este archivo). **Mantenerlo en la raíz de Assets**, no moverlo a `Documentation/` sin acuerdo del responsable.
- **Al actualizar:** cambiar la fecha/hora exacta de cabecera con zona horaria, indicar el snapshot/commit fuente si se conoce, revisar tabla de estados, rutas y prioridades; corregir afirmaciones que dejaron de ser ciertas. Preferir **editar el estado actual** en vez de ir acumulando un registro enorme de parches.
- **Periodicidad:** acompañar cambios importantes en futuros ZIP diferenciales `Assets/*`; **no** es necesario tocarlo por ajustes cosméticos o cambios menores sin impacto contextual.
- **Auditoría histórica:** commits, changelogs del editor, documentos externos y evidencias quedan en el repositorio; este documento es una **foto mantenible del presente** con el contexto suficiente para continuar el proyecto.

## Sistema de Miembros de Tipo (2026-10-20, validado en Unity)

- **Infraestructura añadida:** `[DScriptMethod("name", argumentCount)]` (cantidad opcional; -1 variádico), `[DScriptGetter("name")]`, `[DScriptSetter("name")]` sobre métodos C#. Reutilizan `OperationCall`, `OperationResult`, presupuesto y continuaciones, sin un `OperationId` por miembro.
- **Polimorfismo:** métodos de `DataType` `virtual`/`abstract` pueden declararse en la clase base y sobrescribirse con `override` en clases descendientes sin repetir la anotación. Registro por reflexión una vez al crear el `DataType`; después despacho por delegado, sin reflexión por invocación. Duplicados accidentales/método y propiedad con el mismo nombre se rechazan en registro.
- **DIL:** `GET_PROPERTY length` (consume receptor; devuelve valor); `SET_PROPERTY property` (consume receptor y valor; no deja resultado); `CALL_METHOD append 1` (consume receptor y N parámetros; devuelve resultado o `None`). Los nombres se conservan con su capitalización, y las instrucciones siguen en mayúsculas.
- **GC y continuaciones:** los métodos continuados comparten la raíz auxiliar y los argumentos pendientes de la VM. No guardar referencias heap en `OperationState`. `SetProperty` admite `Continued` y exige `Success(None)` al terminar.
- **Primeros miembros sobre tipos existentes:** `String`, `Tuple`, `List`, `Set`, `Dict`: getter `.length`; `List`: `.append()`, `.insert()`, `.extend()`, `.contains()`, `.indexOf()`; `Set`: `.add()`, `.discard()`, `.remove()`, `.contains()`; `Dict`: `.keys()`, `.values()`, `.items()`, `.contains()`. Solo son adaptadores a operaciones que ya existían. `String.split()` aún no implementado.
- **Validación:** `Directiva > Tests > DIL > Members > TestEverything` añade 53 pruebas de parser, errores, métodos reales, herencia, getters/setters mock, GC, concurrencia y continuaciones. **583/583 pruebas (incluidos miembros) aprobadas en Unity, según el responsable.**
- **Simulación futura:** referencias a entidades (`SimulationId`) estarán administradas por `SimulationManager` y no en el heap; usarán el mismo sistema de miembros. Aún no existe una implementación jugable de esos tipos.

---

## Funciones compiladas como valores (2026-10-20; validadas en Unity)

- **Tipo nuevo:** `DataTypeId.Function = 13`; `DTFunction` es inmutable, **no usa heap** y contiene un `int FunctionId` válido dentro del `CodeContext` compilado. Soporta igualdad e identidad por identificador, hash determinista, truthiness (`FunctionId 0` también es verdadero), casting identidad y `CAST STRING` (`<function #id>`). No son closures.
- **DIL:** `PUSH_FUNCTION nombre` coloca referencia a función ya compilada (nombre simple del módulo actual o cualificado del grafo de imports); el parser valida el nombre durante el enlace. `CALL_INDIRECT n` consume `Function`, seguido de los n argumentos, crea frame normal y retorna un valor. `CALL` directo sigue disponible. Opcodes 28/29; IDs previos inalterados.
- **Uso:** funciones almacenables en locals/globals, parámetros, retornos, listas, tuplas, sets y diccionarios. Un global compartido como `action` puede almacenar referencias `miner`/`attacker` y los bots ejecutarlas dinámicamente al consultarlo de nuevo.
- **Identidad entre VMs:** los `FunctionId` son locales a un programa compilado; las VMs que comparten referencias deben usar **la misma instancia `CodeContext`**. `VMMemoryContext` rechaza llamadas/referencias Function desde un CodeContext diferente cuando está vinculado. No intercambiar funciones entre programas independientes. El ID `0` es una función válida.
- **Restricciones deliberadas:** no hay `def` anidados, lambdas, cierres/capturas, código generado o compilado durante ejecución, ni funciones de simulación (`create_bot`) por ahora. Los argumentos de llamadas indirectas son explícitos y se valida aridad. No se crean funciones nuevas al hacer `PUSH_FUNCTION`.
- **Pruebas:** `Directiva > Tests > DIL > Functions` y `FunctionConcurrency` (73 casos); **656/656 PASS confirmados en Unity**, después de corregir una expectativa errónea en un test. Demostración DIL de función global intercambiable incluida por separado en el entregable.
- **Siguiente paso probable:** DScript → DIL debe emitir `PUSH_FUNCTION` al referenciar nombres de funciones como valores y `CALL_INDIRECT` cuando el receptor sea un valor; no requiere un compilador dinámico.

---

## Iteradores y rangos perezosos (2026-10-21; validados en Unity)

- **Infraestructura:** `DataTypeId.Range = 14`, `DataTypeId.Iterator = 15`, nuevos `RangeHeapObject` (inmutable, tamaño lógico constante 4), `IteratorHeapObject` (estado y referencia al iterable, tamaño 4) e interfaz `IDirectivaIterator.TryGetNext`. Ambos objetos se alojan en el heap; el GC sigue la referencia de un Iterator a su origen.
- **DIL:** `RANGE 1` consume *stop*, `RANGE 2` consume *start/stop*, `RANGE 3` consume *start/stop/step* (Int64, paso distinto de cero). `ITER_INIT` convierte Tuple/List/Set/Dict/Range al iterador; aplicado a un Iterator lo conserva, sin reiniciarlo. `ITER_NEXT etiqueta` mantiene el cursor y apila el siguiente elemento, o consume el cursor y salta a `etiqueta` cuando se acaba. Es perfectamente válido iterar un elemento `None`; no es sentinela de fin.
- **Iteración:** tuplas y listas en orden; diccionarios por claves en orden de inserción; sets en orden interno determinista pero no garantizado. `Range` genera números sin materializar la secuencia, incluso para límites Int64 grandes. `LENGTH`/`.length` de Range se calcula en O(1), con `Overflow` si supera el entero soportado.
- **Concurrencia y mutabilidad:** una List, Set o Dict modificada después de crear el iterador produce `IteratorModified` al avanzar; las VMs que compartan el *mismo iterador* comparten su progreso en el orden determinista del scheduler. `ITER_NEXT` consume un paso de presupuesto, como cualquier instrucción. Se agregan los errores `NotIterable`, `InvalidIterator` e `IteratorModified` sin desplazar IDs anteriores.
- **Backend DScript futuro:** `for x in iterable` podrá traducirse a `ITER_INIT` + `ITER_NEXT etiqueta` + cuerpo + `JUMP`, con reglas explícitas de limpieza de stack para `break`/`continue`/`return`. La función nativa `range()` ya se agregó en la entrega posterior de biblioteca nativa (por validar en Unity). Ya existe parser/frontend DScript sin codegen; `RANGE n` continúa disponible.
- **Pruebas nuevas:** 92 casos en `DIL/Iterators`, `DIL/Ranges` y `DIL/IteratorMemory`. **748/748 aprobadas en Unity**, según confirmó el responsable el 2026-10-21. Casos cubren tipos, None, grafos de referencias/GC, errores, saltos con debug, rangos extremos, conflictos de versiones y varias VMs.

---

## Biblioteca nativa y módulos incorporados (2026-10-21; por validar en Unity)

- **Ubicación:** `Scripts/Code/Execution/Native/` con `[DScriptNativeFunction(module, name, minArgs, maxArgs)]`. `NativeLibrary` registra una sola vez, ordena por nombre completo (ordinal) y asigna IDs locales al `CodeContext` después de los IDs de funciones DIL. `NativeFunction` no se aloja en el heap; sigue siendo `DataTypeId.Function=13` para cualquier invocación `CALL_INDIRECT n`. `CALL $nombre` también funciona para nativas de aridad fija; las de aridad variable requieren llamada indirecta con contador. Si una global usa un nombre reservado para un builtin, sus instrucciones `LOAD_GLOBAL/STORE_GLOBAL` mantienen la prioridad del slot global; `PUSH_FUNCTION` sigue resolviendo la función nativa. Para el `CALL` directo en ese caso usar explícitamente `PUSH_FUNCTION`/`CALL_INDIRECT`.
- **Builtins (sin import):** `print(value)` (emite `Printed`, devuelve None), `range(stop|start,stop|start,stop,step)` (heap Range perezoso), `len(value)`, `abs(value)`, `min(a,b)`, `max(a,b)`, `clamp(x,min,max)`, `int(x)`, `decimal(x)`, `str(x)`, `bool(x)`. `min/max/clamp` aceptan exclusivamente números en esta primera entrega. `print` acepta exactamente un argumento por ahora. String agrega cast `String->Int/Decimal` en cultura invariante, con `InvalidCastValue` frente a entrada malformada y truthiness correcta para cadena vacía.
- **math:** `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `atan2(y,x)`, `sqrt`, `hypot`, `floor`, `ceil`, `degrees`, `radians`; constantes `math.pi`, `math.tau`, `math.e`. Ángulos en radianes. Funciones trigonométricas y matemáticas utilizan `System.Math` y convierten resultados al `DTDecimal` de cuatro cifras decimales; **la equivalencia bit a bit entre plataformas para funciones trascendentes aún no está garantizada** y merece revisión si el replay exige ese nivel de determinismo. Los resultados no finitos se rechazan como error de script, los no representables como Overflow.
- **random:** `random.random()`, `random.randint(a,b)` (ambos extremos incluidos), `random.uniform(a,b)` (intervalo semiabierto). PRNG SplitMix64 propio con semilla host (`VMMemoryContext.RandomSeed`/`ReseedRandom`), estado único del contexto compartido; el orden de avance depende del scheduler determinista. Sin `UnityEngine.Random`, sin estado global del proceso. Valores `Decimal` cuantizados a 0.0001. El intervalo entero completo Int64 está soportado.
- **DIL/enlace:** `# IMPORT : math` y `# IMPORT : random` son módulos reservados y no requieren archivos `.dil`; nombres completamente calificados (`PUSH_FUNCTION math.sin`) y nueva forma `PUSH_NATIVE math.pi` o `PUSH_NATIVE math.sin` para constantes/referencias. La instrucción `PUSH_NATIVE` se **expande** a `PUSH`/`PUSH_FUNCTION` durante el parseo y no añade opcodes. La infraestructura de import/símbolos se consume ya desde el frontend DScript mediante `NativeLibrary.GetFunctionNames/GetConstantNames`. Una importación de módulo nativo debe declararse en cada archivo DIL que use sus miembros.
- **GC/presupuestos:** los argumentos permanecen en el stack como raíces GC mientras se ejecuta la llamada nativa. La invocación nativa ocupa un paso VM, no abre CallFrame y debe estar limitada a trabajo acotado. Los proveedores no reciben acceso al stack ni a las escenas Unity. El protocolo actual **no admite nativas continuadas**: algoritmos potencialmente grandes requerirán una extensión en el futuro.
- **Pruebas nuevas:** `DIL/NativeBuiltins` (59), `NativeLinking` (18), `NativeMath` (24), `NativeRandom` (13): **114 casos nuevos, 862 registrados; primera ejecución de Unity: 859 PASS, 2 FAIL, 1 ERROR; tres fallos corregidos en pruebas, reejecución pendiente**. Incluye conversiones, funciones como valores, errores, OOM, GC, imports, aridad, algoritmos matemáticos y semillas compartidas.
- **Preservación de proveedores en builds:** `Assets/link.xml` protege métodos C# descubiertos por reflexión frente a code stripping (Assembly-CSharp). Actualizarla al añadir nuevos proveedores de biblioteca.
- **Todavía no implementado:** **generación/ejecución DScript desde su AST** (aunque `import` y llamadas sí se parsean/resuelven), objetos externos, APIs de simulación, `random.choice`, iteración genérica de String, funciones nativas de múltiples pasos, objetos de módulo como valores de primera clase. El frontend ya resuelve `math.sin` como símbolo nativo, pero su ejecución desde DScript espera la entrega del backend; mientras tanto se valida mediante DIL.

---


---

## Frontend DScript — entrega 1 (2026-10-21, registro histórico previo a `shared`)

> **ATENCIÓN:** las descripciones originales de `global` por módulo, rechazo de ciclos e inicializadores de módulo de este apartado han quedado **SUSTITUIDAS** por «Revisión frontend `shared`» al final de este documento y por `GRAMMAR.md`. Conservar solo como historial de lo entregado antes del cambio de semántica.

**Estado: implementado y compilado en Unity según el reporte del responsable; 134 casos DScript ejecutados, 132 PASS y 2 FAIL de parser. Este parche corrige ambos y añade 4 pruebas. Pendiente confirmar la nueva ejecución.** Archivos nuevos bajo `Assets/Scripts/Code/Compilation/DScript/`, documento `GRAMMAR.md`, y 134 casos originalmente ejecutados bajo `Assets/Editor/DirectivaTests/DScript/` (Lexer 21, Parser 34, Modules 27, Semantics 52), más 4 casos de regresión añadidos en este parche (Parser 38, total DScript 138). Menús `Directiva/Tests/DScript/TestEverything` y subgrupos; `TestAll` incluye ambos lenguajes (862 DIL existentes + 138 DScript tras parche = 1000).

- **API de entrada:** `DSCompilation.DScript.DScriptFrontend.Analyze(entryModule, IDScriptSourceProvider, DScriptFrontendOptions)` → `DScriptAnalysis`. `Success`/`Diagnostics`, AST por módulo, `DependenciesFirst`, tablas de imports/exports, símbolos globales y locales, bindings de cada expresión y variables destino de `for`.
- **Lexer:** tokens con `SourceSpan` (offset UTF-16 + línea/columna 1-based), comentarios `#`, indent/dedent por espacios, continuidad dentro de brackets, String con escapes, Int64/Decimal fijo 4 posiciones, límite tamaño/nesting/diagnósticos. Error DS1xxx.
- **Parser:** AST orientado a objetos (Statement/Expression) para `def`, `global`, imports (`as`, `from`, `*`), `return`, `if/elif/else`, `while`, `for`, `break`/`continue`/`pass`, asignaciones y operaciones, colecciones, llamadas, índices/miembros; sin closures/clases/kwargs/comprehensions. Error DS2xxx. Gramática detallada en `GRAMMAR.md`.
- **Módulos:** provider en memoria o `FileSystemDScriptSourceProvider` que valida segmentos y **capitalización exacta**. Grafo DFS, ciclos rechazados; módulos nativos `math` y `random` reconocidos desde `NativeLibrary` sin archivos. Imports de usuario referencian nombres únicos `modulo.funcion`; `from m import *` solo incluye exports públicos directos. Error DS3xxx.
- **Análisis semántico:** registro de funciones/globals de todos los módulos ANTES de examinar cuerpos, incluyendo declaraciones `global` dentro de las funciones; parámetros primero y locales por primer binding; las globales se ordenan por nombre completamente calificado. `DScriptSymbol.DILGlobalName` aplica mangling sin colisiones para ser compatible con `DILParser` (que solo permite `# GLOBAL : identifier`); el backend **deberá emitir globales en orden compatible**. Tipado sigue siendo dinámico; solo valida nombres, ámbitos, colisiones, break/continue, aridad de funciones DScript conocidas, función como valor, nombres nativos. Error DS4xxx.
- **Módulos y memoria compartida:** `import shared; shared.action = worker` asigna un global del módulo importado; las VMs del mismo `CodeContext` conservarán el almacenamiento compartido existente. Un simple `global action` siempre se refiere al módulo **actual**, no a todos los módulos por accidente.
- **Fuente de verdad de límites:** `GRAMMAR.md`. El módulo solo acepta imports, definiciones, globals y asignación simple en nivel superior; estos últimos requerirán **inicializadores DIL en la entrega 2**. No hay aún runtime DScript, ni modificación a opcodes, DataType, heap o VM en esta entrega.
- **QA entregado:** auditoría estática de estructura y ZIP. Unity ejecutó 996 pruebas (994 PASS / 2 FAIL / 0 ERROR), con los 2 fallos de comas finales de listas; este parche requiere repetir las 1000 pruebas previstas. Los errores de compilación/interacción que aparezcan serán corregidos sobre un nuevo diferencial.

**Fin del snapshot.** Si otro agente detecta una contradicción con código/escena/git posterior, debe reportarla y actualizar el documento con hechos verificables; no asumir que el resumen es infalible.

### Corrección tras la primera ejecución de la biblioteca nativa (2026-10-21)

- Reporte `run_20261021_211518_226_1b1543`: **862 registrados / 859 PASS / 2 FAIL / 1 ERROR**. La cifra previa de 865 procedía de contar 95 casos de iteración cuando los archivos contienen **92** (39 Iterators, 37 Ranges y 16 IteratorMemory). Por tanto el total correcto es 656 + 92 + 114 = **862**; no se eliminaron pruebas para corregir este desfase.
- `DIL.NativeBuiltins.UninitializedArg`: el test declaraba `# LOCALS : 0` y usaba `LOAD_LOCAL 0`. Se corrigió a 1 slot; el error esperado sigue siendo `VariableUsedBeforeInitialization`. Es una validación de la protección de lectura de locales antes de ejecutar el builtin.
- `DIL.NativeLinking.MissingModuleName`: el parser identifica correctamente al módulo ausente (`nonexistent`) en el diagnóstico. Se corrigió la expectativa que lo confundía con el módulo de origen (`test`).
- `DIL.Numeric.UnsupportedStringToInt`: la nueva versión de `DTString` permite parsear texto numérico; el test ahora se llama `DIL.Numeric.CastStringToInt`, comprueba `"21" -> Int(21)` y que el texto inválido produce `InvalidCastValue`.
- **Alcance de la corrección:** solo dos archivos de pruebas y este documento; **ninguna modificación a VM, compilador, biblioteca nativa, `.meta` ni GUID**. Candidata a 862/862, aún pendiente de ejecución en Unity.

## Corrección DScript: comas finales opcionales (2026-10-22)

- **Reporte:** `run_20261022_004159_347_36ed08` — 996 ejecutadas, 994 PASS, 2 FAIL, 0 ERROR. Únicos fallos: `DScript.Parser.Collections` y `DScript.Parser.MultilineList`. Todo DIL, Lexer, Modules y Semantics pasó.
- **Causa:** `ReadElements` consultaba el delimitador de cierre antes de consumir la coma. Un literal `[1, 2,]` intentaba interpretar `]` como una nueva expresión (`DS2005`); la prueba multilínea también disparó un `DS2003` secundario durante la recuperación.
- **Cambio:** `ReadElements`, `ReadFunction` (lista de parámetros) y `ReadPostfix` (argumentos de llamada) consumen comas y detectan correctamente el delimitador final. Se mantiene el rechazo de comas duplicadas y de expresiones faltantes.
- **Regresión agregada:** `TrailingCommaParameters`, `TrailingCommaArguments`, `TrailingCommaNestedCollections`, `RepeatedCommaInListRejected` (4 nuevos). Total esperado: 862 DIL + 138 DScript = **1000**. **Aún no verificado por Unity tras la corrección.**
- **Archivos modificados:** `DScriptParser.cs`, `DScriptParserTests.cs`, `GRAMMAR.md`, `DIRECTIVA_CONTEXT.md`. Se conservan GUIDs y `.meta`. Sin cambios en VM, DIL, lexer, AST, semántica ni catálogo de tests.


## Revisión frontend `shared` (2026-10-22; CONTRATO VIGENTE)

**Motivo:** se descubrió que copiar la semántica de `global` de Python contradice el modelo de almacenamiento de Directiva. El usuario confirmó que **las 1000 pruebas antiguas pasaron en Unity**, pero esas reglas antiguas se reemplazan aquí.

**Cambios implementados en C#:**

- `shared name` y `shared name = expression` son declaraciones **exclusivas de módulo** (`SharedStatement`). Ya no se acepta `global` de Python, ni ejecución libre al nivel superior (solo imports, `shared` y `def`). `shared` dentro de una función/bloque genera un diagnóstico sintáctico. Es una declaración por variable.
- Todas las declaraciones de **un mismo nombre `shared`** en módulos alcanzables se refieren a **un solo `DScriptSymbol` y un solo slot** en la memoria compartida de todas las VMs. Slots en orden ordinal por **nombre compartido**, sin prefijo de módulo. `SymbolKind.Global`/`GlobalSlots` se conservan internamente para representar las instrucciones y almacenamiento de DIL; su `QualifiedName` canónico tiene la forma `shared.action`.
- Repetir declaraciones sin inicializador es válido. **Más de un inicializador por nombre** es error de frontend `DS4018`. Una asignación a un shared declarado en el módulo se vincula automáticamente, sin repetir `shared` en la función. Parámetro de función con ese nombre: `DS4006`.
- Imports **absolutos desde `DirectivaScripts`**, incluidos `math` y `random` como nativos. **Imports circulares permitidos**: se descubre cada módulo una sola vez, se declaran todos los símbolos y se enlazan las referencias después. El `FileSystemDScriptSourceProvider` rechaza rutas con segmentos inválidos y entradas con `ReparsePoint` dentro de su raíz (symlink/junction); falta conexión con el editor.
- `DScriptAnalysis.SharedDeclarations`: mapa AST → símbolo canónico. `DScriptAnalysis.SharedInitializers`: lista **plana y ordenada** de pasos con `Order`, `Module`, `Symbol`, `Expression`, `Span` y declaración origen. La lista se construye recorriendo imports **en su posición textual** (DFS inlined, una visita por módulo). Los slots ya están resueltos antes de crear la lista. Dependencias circulares no afectan los slots. Ejemplos A primero `a→b→c`, B primero `b→a→c` dependen de dónde aparece `import` respecto a cada inicializador; ver casos de prueba.
- **Nueva política de recuperación solicitada:** si un inicializador falla **en runtime** (p. ej. acceso a `Uninitialized`), la consola mostrará un error con nombre, módulo y línea; **se intentarán los inicializadores siguientes** y **no se bloqueará el arranque de las VMs normales** por esta clase de fallo. El orquestador deberá aislar cada elemento de `SharedInitializers` en invocaciones independientes (los errores no capturados detienen actualmente la VM); no se garantiza rollback de efectos laterales anteriores al error. **ESTO ESTÁ DOCUMENTADO/PREPARADO, PERO NO SE EJECUTA TODAVÍA: falta backend DIL y bootstrap**.

**Pruebas:** anteriormente 862 DIL + 138 DScript = **1000/1000 PASS reportado por el usuario en Unity**. Esta revisión conserva la suite y cambia expectativas antiguas de `global`/ciclos, añade 31 casos `DScript.Shared`, 2 fixtures léxicos y 8 casos de gramática/AST: **179 casos DScript esperados + 862 DIL = 1041 en `TestAll`**. No afirmar PASS hasta ejecución en Unity. Menú: `Directiva/Tests/DScript/Shared/TestEverything`.

**Archivos y próximos pasos:** `DScriptFrontend.cs`, AST, lexer/token, parser, module loader/provider, binder y símbolos, pruebas y `GRAMMAR.md`. No se cambiaron VM, opcodes, DataTypes ni el enlazador DIL. El segundo backend debe consumir los slots `GlobalSlots` y el plan `SharedInitializers`, compilar cada paso como función independiente, ejecutarlos sobre **un único `VMMemoryContext` compartido** registrando errores y sin cancelar la simulación cuando falla un paso. Los **errores estáticos** de parsing/importación/binding siguen bloqueando la compilación, diferenciados de fallos runtime de inicialización.


## Parche de pruebas Shared — 2026-10-22

- **Reporte real:** `run_20261022_021533_213_6b8c9b`, Unity 6000.3.23f1: **1039 PASS, 2 FAIL, 0 ERROR, 1041 ejecutadas**. Solo fallaron `DScript.Shared.ImportInterleaving` y `DScript.Shared.CycleEntrypointChangesOrder`.
- **Causa:** ambas fixtures utilizaban simultáneamente `import b` y `shared b` en el módulo `test`; el frontend emitió correctamente `DS4004` porque ambos símbolos compartirían el identificador desnudo `b`. No es un error del parser, linker ni DFS de inicialización.
- **Corrección:** las dos fixtures usan `import b as module_b`. Se añadió una aserción de que `b` en `shared c = b + 5` sigue resolviendo a `shared.b`, y una nueva prueba `SharedModuleNameCollision` verifica que el uso ambiguo sin alias devuelve `DS4004`. El frontend y la VM no cambian.
- **Estado:** la suite pasa de **1041 a 1042 pruebas registradas** (862 DIL + 180 DScript), resultado previsto y **pendiente de ejecutar en Unity**.
- **Sin eliminaciones, renombres ni cambios de GUID/.meta.**

---

## DScript: despacho por miembro y operaciones internas (2026-10-22, parche previo al backend fase 2)

- **Contrato de compilacion:** una llamada DScript `obj.metodo(a, ...)` se debe convertir a `CALL_METHOD metodo n`, sin elegir el `OperationId` por nombre textual. La VM busca el miembro en el `DataType` real del receptor; solo el adaptador C# del tipo decide si llama a `Operate(OperationId.X, call)` o a otra implementacion propia. Los operadores sintacticos (`+`, `-`, etc.) siguen generando opcodes de operacion. Los metodos vinculados como valores (`f = obj.pop`) siguen pendientes/no admitidos en este MVP.
- **Despacho resuelto:** `List.pop()` y `List.pop(index)` llaman a `OperationId.Pop`; la variante sin argumentos aporta internamente el indice -1. `Set.pop()` llama a `OperationId.SetPop` sin indice. `Dictionary.pop(key)` llama al mismo `OperationId.Pop` que List, pero la implementacion real pertenece a `DTDict`; no hay `pop(key, default)` todavia. Un tipo que no declare `pop` produce `MemberNotFound` y nunca se intenta una sustitucion basada solo en el nombre.
- **Aridades:** `[DScriptMethod("nombre", minArgs, maxArgs)]` permite un intervalo cerrado, ademas de la forma anterior con un arity exacto o `-1` variadico. Las aridades invalidas se comprueban al registrar miembros y las llamadas erroneas dan `InvalidMemberArgumentCount` con limites descriptivos. Se conserva la propiedad publica `ArgumentCount` del atributo como API de compatibilidad (valor -1 en el overload de rango).
- **Reutilizacion:** los adaptadores de List/Set/Dict redirigen sus miembros nativos por `DataType.Operate(OperationId, OperationCall)`. No se cambian codigos DIL ya existentes, semantica de primitivas, ni funcionamiento de continuaciones/GC. La VM ya mantiene el receptor original, los argumentos, el estado y la raiz auxiliar cuando un miembro retorna `Continued`.
- **Validacion:** 21 pruebas nuevas en `DIL/Members`: `pop` dinamico sobre List/Set/Dict, indices opcionales, errores, aridad, sensibilidad a mayusculas, equivalencia frente a opcode nativo, continuacion larga y conflicto de version bajo concurrencia. Linea base confirmada por el usuario antes del parche: **1042/1042**. Con este cambio se esperan **1063** pruebas totales; **no han sido ejecutadas en Unity en este entorno**. Ejecutar `Directiva > Tests > DIL > Members > TestEverything` y despues `Directiva > Tests > TestAll`.
- **Pendiente:** fase 2 DScript -> DIL, depuracion en origen y presupuesto 0 de debug. `str.strip` sigue sin existir. Ninguna implementacion de los metodos descritos implica que los miembros DScript sean funciones de primera clase.


## DScript fase 2 — backend + bootstrap (2026-10-22; pruebas Unity pendientes)

El usuario confirmó **1063/1063 PASS** al concluir el parche de despacho dinámico
`CALL_METHOD`. En la fase 2 se añadió `DSCompilation.DScript.Backend`:

- `DScriptCompiler.Compile(entryModule, provider)` analiza, emite un módulo
  `.dil` por cada `.dscript` y un módulo bootstrap, comprueba todo con
  `DILParser` y devuelve `DScriptCompiledProgram` inmutable (CodeContext,
  fuentes generadas, tabla de funciones y pasos inicializadores). Los archivos
  no se escriben aún en DirectivaScripts; el editor/host puede publicarlos
  solo después de una compilación exitosa.
- `# GLOBAL` se emite exclusivamente en el bootstrap, siguiendo los slots
  semánticos alfabéticos; cada inicializador es una función independiente.
  `DScriptBootstrapRunner.Initialize` usa VMs efímeras sobre UN `VMMemoryContext`
  por equipo y partida, guarda errores y ejecuta los pasos siguientes incluso
  después de un fallo o agotamiento de presupuesto. Bloquea un segundo bootstrap.
- DScript `obj.metodo(...)` emite siempre `CALL_METHOD`; operaciones aritméticas
  y de contenedores que tienen sintaxis propia generan operaciones DIL.
  `and/or` devuelven el operando con cortocircuito. `for` limpia iteradores
  al hacer break y conserva cursor en continue. Las referencias a funciones
  y natives se emiten con `PUSH_FUNCTION`/`PUSH_NATIVE` y `CALL_INDIRECT`.
- `DEBUG_FILE`, `DEBUG_LINE`, `DEBUG_SPAN` explícitos por instrucción apuntan
  al código DScript, no a los offsets DIL. Coste lógico 0 en `Run` y controlador
  de ejecución; `.AdvancesExecuted` conserva el número físico de avances de VM.
  DIL escrito manualmente conserva la inyección automática opcional.
- Se añadieron 54 casos en `Directiva/Tests/DScript/Backend/TestEverything`,
  para un **total previsto de 1117** en TestAll. Revisados estáticamente;
  **sin ejecución de Unity en este entorno**. Menú, gramática, README y metas
  actualizados.

Pendiente: probar en Unity y luego conectar compilación y inicialización al
editor/orquestador. Los límites por inicializador son configurables y no
representan aún presupuestos de balance final.
