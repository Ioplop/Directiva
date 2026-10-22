# Directiva — suite de pruebas automatizadas (Unity Editor)

## Ejecucion

1. Esperar a que Unity termine de compilar los scripts de `Assets/Editor/`.
2. Abrir **Directiva > Tests > Open Test Runner and Reports...**.
3. En **Reports folder**, pulsar **Browse...** y seleccionar una carpeta externa al proyecto **y al repositorio Git** (por ejemplo `Documentos/DirectivaTestReports`). La seleccion se guarda mediante `EditorPrefs`, no en el repositorio.
4. Pulsar **TestAll**, **DIL / TestEverything**, **Run selected** o utilizar el menu **Directiva > Tests > DIL > [grupo] > TestEverything**.
5. Consultar la consola y los archivos en el directorio `run_YYYYMMDD_HHMMSS_fff_xxxxxx/` generado automaticamente para cada ejecucion:
   - `SUMMARY.md`: indice por test y PASS/FAIL/ERROR, duracion y numero de aserciones.
   - `SUMMARY.json`: indice equivalente legible por agentes/scripts.
   - `tests/DIL.Grupo.Caso.md`: informe por **cada** prueba, incluso las que pasan; contiene objetivo, evidencia, DIL/fixture, ultima traza VM, estados, uso del heap, GC, eventos PRINT, resultado, mensaje y stack de excepcion si corresponde.

**No requiere Play Mode**, escenas ni crear proyectos de prueba. Las pruebas corren de a una por `EditorApplication.update`, con budgets finitos cuando usan VM. Puedes detener una bateria con **Cancel**; se conservan los reportes de las pruebas ya ejecutadas. Unity puede no permitir cancelar una prueba individual hasta que esa prueba termine.

**Importante:** un fallo deliberadamente provocado (ej. division por cero, OutOfMemory) se muestra como **PASS** si el `RuntimeErrorId` coincide con el esperado. FAIL indica una asercion incumplida; ERROR indica una excepcion inesperada del propio test. Las pruebas no escriben archivos de usuario `DirectivaScripts` ni alteran escenas; el test del provider DIL usa un directorio temporal del sistema y lo elimina.

## Grupos actuales

| Menu `Directiva/Tests/DIL/` | Alcance |
|---|---|
| `Parser/TestEverything` | Sintaxis DIL, constantes, etiquetas, modulos e imports, debug metadata, validaciones, provider filesystem y errores de parseo |
| `VM/TestEverything` | Stack, locals/globals, funciones, scopes, saltos, presupuestos, PRINT, debug y estados terminales |
| `Controller/TestEverything` | Controlador de ejecucion del editor: Start/Stop, Pause/Continue, Step, metadata, breakpoints y eventos |
| `Numeric/TestEverything` | Int, Decimal, Bool, None; operadores, casts actualmente soportados, comparaciones, hashes y continuaciones |
| `References/TestEverything` | String y Tuple, construccion/indexacion, Unicode, igualdad, identidad, hashing, impresion y casts |
| `Hashes/TestEverything` | Cache de HASH en String/Tuple, tuplas anidadas, valores no hasheables, GC, hashes numericos equivalentes y regresion DIL |
| `Lists/TestEverything` | LIST n, operaciones de mutacion, conversion, igualdad, busqueda y parser DIL |
| `ListMemory/TestEverything` | Redimensionamiento, swap de buffers, atomicidad OOM, raices auxiliares, GC y ciclos |
| `ListConcurrency/TestEverything` | Listas compartidas por varias VMs, aislamiento de resultados parciales y conflictos de version |
| `Sets/TestEverything`, `SetAlgebra`, `SetMemory`, `SetConcurrency`, `SetRobustness` | Conjuntos, hash, algebra, GC, concurrencia y estres (126 pruebas validadas en Unity) |
| `Members/TestEverything` | Parser y ejecución de miembros DIL, herencia, getters/setters y métodos continuados (53 pruebas; validadas en Unity) |
| `Iterators/TestEverything` | Iteración de Tuple/List/Set/Dict, None vs fin, saltos DIL y errores de cursor (39 pruebas) |
| `Ranges/TestEverything` | Construcción `RANGE 1/2/3`, rangos perezosos, límites Int64, Length, casts y OOM (37 pruebas) |
| `IteratorMemory/TestEverything` | Raíces GC, presión de memoria, invalidación por mutación y varias VMs (16 pruebas) |
| `Functions/TestEverything` | Function como valor, imports, aridad, CALL_INDIRECT, hashing, colecciones y fallos esperados (63 nuevas) |
| `FunctionConcurrency/TestEverything` | Selector global de comportamiento entre VMs, snapshots, determinismo, programa compartido y GC (10 nuevas) |
| `Dictionaries/TestEverything` | Constructor, GET/SET/DELETE/POP_ITEM, hashing, igualdad y conversiones |
| `DictViews/TestEverything` | DICT_KEYS/VALUES/ITEMS, formato anidado y continuidad |
| `DictMemory/TestEverything` | Carga de dos slots, GC, ciclos, OOM y buffers auxiliares |
| `DictConcurrency/TestEverything` | Update atomico, colisiones de versiones y multiples VMs |
| `DictRobustness/TestEverything` | Colisiones, orden, churn determinista y seguridad de parser/VM |
| `Memory/TestEverything` | Heap, limites, mark-and-sweep lazy, OOM, GC anidado/ciclos y raices en stacks, locals, globals y cast |
| `Concurrency/TestEverything` | Multiples VMs con memoria compartida, round-robin determinista, interleavings, carreras reproducibles y aislamiento |
| `Errors/TestEverything` | IDs de RuntimeError: casos DIL alcanzables y fixtures VM directos para instrucciones invalidas |
| `Robustness/TestEverything` | Stress reproducible, cientos de operaciones muestreadas, carga GC y tuplas profundamente anidadas |
| `Regression/TestEverything` | Las dos suites de Tuple preexistentes, ahora integradas en el catalogo y con un reporte por caso |

**Limites de cobertura:** los casos DIL cubren el **runtime DIL implementado** y los casos DScript cubren **solo lexer/parser/imports/semántica**, no aún ejecución DScript, gameplay ni UI visual/escenas. `InvalidCastValue` tiene solo una prueba de contrato del error, porque todavia no existe un camino de DIL que lo produzca. Pruebas con `CodeContext`/`Instruction` manuales se identifican en los reportes y no deben confundirse con errores que un DIL bien parseado permite provocar.

## Agregar mas pruebas

- Crear un archivo `Assets/Editor/DirectivaTests/DIL/DILAlgoTests.cs` con `internal static void Register(List<DirectivaTestCase> tests)` y utilizar `DirectivaTestCatalog.Add(tests, "Grupo", "IdCaso", "descripcion", t => { ... });`.
- Registrar la clase en `DirectivaTestCatalog.GetAll()` de `Core/DirectivaTestModel.cs`. Para un nuevo grupo, sumar el `MenuItem` en `Core/DirectivaTestMenu.cs`.
- Usar `DirectivaTestContext` para `Check`, `Equal`, `Contains`, `Note` y `Attach`; `DILTestHarness` para adjuntar fuente, ejecutar VM con presupuesto, recoger `PRINT` y registrar estado/errores/traza. Los IDs deben ser unicos (`DIL.Grupo.IdCaso`).
- Para DScript o Simulation, crear nuevas carpetas/suites y registrar instancias de `DirectivaTestCase("DScript", ...)` o `DirectivaTestCase("Simulation", ...)`, ademas de su menu `Directiva/Tests/<categoria>/TestEverything`. **`Directiva/Tests/TestAll` ya ejecuta automaticamente todos los tests registrados, sin cambios al ejecutor ni al formato de reportes.**
- Al agregar pruebas de regresion por un bug descubierto, usar una muestra minima reproducible y dejar el motivo del test en la descripcion/`Note`.

## Diagnosticos y desarrollo

Los scripts de test y sus utilidades son **Editor-only**. El runtime `Assets/Scripts/` fue ampliado para soportar List y memoria mutable en esta entrega; las suites existentes siguen registradas. Hay dos niveles de prueba: de texto DIL real (parser + VM) y de `CodeContext`/VM creados directamente (para ejercitar errores que el parser impide). Los tests de concurrencia hacen interleaving secuencial de varias VMs; no simulan hilos OS.

Esta entrega fue sometida a revisiones estaticas de codigo y estructura del ZIP, **pero requiere compilacion y ejecucion en Unity antes de poder afirmar que todos los casos pasan**. Si alguno falla, compartir `SUMMARY.md`, `SUMMARY.json` y los reportes de `tests/` de los casos fallidos (evitar adjuntar datos personales de rutas en caso de compartir publicamente).

## Semantica y nuevas pruebas de List (2026-10-19)

- `LIST n`: toma n valores de stack (igual que `TUPLE n`). `GET`, `SET` y `POP_ITEM` aceptan indices negativos. `INSERT` limita indices fuera de rango a ambos extremos; GET/SET/DELETE/POP_ITEM en cambio fallan si se sale de rango.
- `APPEND`/`INSERT`/`DELETE`/`SET`/`EXTEND`: consumen sus operandos y apilan `None`. `POP_ITEM` devuelve el elemento quitado; `COPY` hace copia superficial nueva; `ADD` concatena y devuelve nueva List. `CONTAINS` devuelve Bool; `INDEX_OF` devuelve primer indice o -1.
- Continuaciones: el buffer auxiliar es un objeto real del heap y lo enraiza un campo dedicado de la VM. Los `OperationState` solo contienen indices y versiones, nunca referencias de heap. La publicacion es atomica; una mutacion de otra VM durante la preparacion produce InvalidOperation, no un merge automatico.
- `Hash` de List produce `UnhashableType`. Formateo de listas con referencias circulares muestra `[...]` y evita bucles infinitos; Tuple usa el mismo formateador.
- **Verificacion:** el responsable confirmo que los 242 casos existentes (incluida List) pasaron en Unity el 19-10-2026. El responsable confirmo 254/254 casos con Hashes; Set paso 380/380 pruebas en Unity el 19-10-2026.

## Semantica y nuevas pruebas de Set (2026-10-19)

- Menus: `Directiva/Tests/DIL/Sets`, `SetAlgebra`, `SetMemory`, `SetConcurrency` y `SetRobustness`, cada uno con `TestEverything`; `TestAll` incluye estos grupos automaticamente.
- **Linea base verificada:** 380/380 pruebas en Unity el 19-10-2026 (incluye las 126 nuevas de Set). Los informes detallados deben guardarse fuera del repositorio.
- `SET n` construye un conjunto desde n valores del stack; **`SET` sin numero sigue siendo la operacion de asignacion por indice de listas**.
- `SET_ADD`, `SET_DISCARD`, `SET_REMOVE`, `SET_POP`, `SET_UPDATE`, `COPY`; `CONTAINS`, `LENGTH`, `VALUE_EQUALS`, `REFERENCE_EQUALS`, `IS_TRUTHY`, `CAST STRING`; `SET_UNION`, `SET_INTERSECTION`, `SET_DIFFERENCE`, `SET_SYMMETRIC_DIFFERENCE`, `SET_IS_SUBSET`, `SET_IS_SUPERSET`, `SET_IS_DISJOINT`.
- Los elementos deben ser hasheables (por ejemplo String, Tuple hasheable, Int, Bool, Decimal, None); Set/List/Dict no son hasheables. Las colisiones de hash se verifican por igualdad real. No se garantiza orden conceptual; la implementacion conserva una secuencia reproducible bajo el mismo historial de operaciones.
- Mutaciones basicas usan reserva logica previa; operacion continuada crea un buffer en heap y lo protege a traves de la VM hasta la publicacion atomica. Otra VM que modifica alguno de los operandos durante la preparacion causa `InvalidOperation`.
- Limitacion: igualdad estructural de conjuntos, creacion `SET n` y conversion a texto se ejecutan en una sola instruccion; los recorridos de algebra y relaciones usan continuaciones por bloques.

## Semantica y nuevas pruebas de Dictionary (2026-10-19)

- **Linea base validada:** 380/380 pruebas anteriores pasaron en Unity. **150 pruebas nuevas** de Dict repartidas en cinco grupos (530 previstas) se revisaron estaticamente y **deben compilarse y ejecutarse en Unity**.
- `DICT n` construye un diccionario de los ultimos **n pares** `(clave, valor)` del stack (consume 2n). Tamaño logico `1 + 2n_unicos`; hashes de claves con igualdad real y almacenamiento de insercion determinista. Claves duplicadas actualizan el valor sin reemplazar la clave ni su posicion. Set sin argumento mantiene su semantica anterior de asignacion para List y ahora Dict.
- Operadores: `GET`, `SET`, `DELETE`, `POP_ITEM`, `CONTAINS`, `LENGTH`, `COPY`, `VALUE_EQUALS`, `REFERENCE_EQUALS`, `IS_TRUTHY`, `CAST DICT`, `CAST STRING`, `DICT_GET_DEFAULT`, `DICT_CLEAR`, `DICT_UPDATE`, `DICT_KEYS`, `DICT_VALUES`, `DICT_ITEMS`. `DICT_GET_DEFAULT` consume dict, clave, fallback; devuelve fallback solo si falta la clave. `DICT_KEYS/VALUES/ITEMS` devuelven List; ITEMS crea pares Tuple.
- Solo las **claves** necesitan hash estable (Int/Bool/Decimal, None, String, Tuple hasheable); Dict/List/Set son no hasheables, aunque pueden usarse como valores y formar ciclos. Los errores por claves ausentes son `InvalidOperation`; claves no hasheables retornan `UnhashableType`.
- `COPY`, `DICT_UPDATE` y vistas avanzan 16 entradas por paso con buffer protegido por la VM; update publica atomicamente y se cancela ante modificaciones concurrentes a los operandos. GET/SET/DELETE/POP_ITEM y comparaciones son sincronicas; eliminacion con preservacion de orden requiere actualmente O(n).
- Los informes detallados de **cada** caso siguen generandose fuera del repositorio. Si falla alguna prueba, compartir ZIP de reports (SUMMARY y tests/*.md) para depuracion. No considerar los 530 casos validados hasta ejecutarlos en Unity.


## Miembros de tipo (2026-10-20)

`DILMemberTests.cs` aporta 53 casos registrados en el catálogo, con menú `Directiva/Tests/DIL/Members/TestEverything`. Valida DIL, adaptadores de miembros sobre tipos actuales, errores de aridad, reflexión solo al registrar, herencia y sobrescritura `virtual`/`abstract`/`override`, setters mediante tipos de prueba, GC y concurrencia de métodos continuados. No se agrega ningún setter ficticio a los tipos de juego: los setters de integración a futuras entidades se probarán cuando existan.

**Línea base confirmada:** 530/530 casos anteriores aprobados en Unity. **Total objetivo:** 583 con 53 nuevas aún pendientes de compilar y ejecutar. Los informes se guardan fuera del repositorio mediante la ventana existente.

## Funciones como valores (2026-10-20; validación Unity pendiente)

- Nuevas instrucciones: `PUSH_FUNCTION nombre` y `CALL_INDIRECT n`. Para llamadas indirectas apilar **primero la función** y después los n argumentos. Funciones compiladas, no anónimas ni closures. `FunctionId` es local al `CodeContext` del programa.
- `DTFunction` es un escalar no heap (nuevo `DataTypeId.Function = 13`); puede guardarse en variables, retornos y colecciones. `CAST STRING` muestra `<function #id>`; hashing y truthiness admitidos. `FunctionId` 0 es válido.
- **Memoria compartida:** VMs que intercambian funciones por globales deben compartir la **misma instancia** `CodeContext`. El contexto de memoria rechaza el uso de referencias Function de otro programa para evitar invocaciones equivocadas.
- **73 casos nuevos**: 63 de semántica/parsing/errores y 10 de concurrencia (583 previos aprobados en Unity; 656 previstos con esta entrega).
- Ejecutar `Directiva > Tests > DIL > Functions > TestEverything`, luego `FunctionConcurrency`, y finalmente `TestAll`. Los informes se guardan fuera del repositorio como de costumbre.

## Iteradores y Range (2026-10-21; pendientes de Unity)

- **Instrucciones:** `RANGE 1/2/3` crea un Range en heap a partir de argumentos `Int`/`Bool`. `ITER_INIT` toma el iterable en el stack y lo reemplaza por un cursor. `ITER_NEXT end` requiere el cursor en el tope: al avanzar deja `[iterator, value]` (el cuerpo consume `value`); al agotarse elimina el cursor y salta a `end`.
- **Error y None:** `None` es un elemento normal. El fin de iteración se representa por el salto, nunca con un valor especial.
- **Fuentes:** Tuple, List, Set, Dictionary (claves), Range; los mutable cambian `Version` en cada mutación efectiva. El iterador detecta cambios y devuelve `IteratorModified` antes de avanzar.
- **GC:** los cursores residen en heap y su `GetReferences()` conserva el objeto original. Ambos tienen tamaño lógico fijo; ningún valor individual de Range se preasigna.
- **Pruebas:** menús `DIL/Iterators`, `DIL/Ranges` y `DIL/IteratorMemory`; 39+37+16=92 nuevas, total real **748** partiendo de las 656 aprobadas. **No afirmar PASS hasta ejecutar Unity.**
- **Estado:** `range()` ya está implementada como nativa en DIL y el frontend DScript reconoce `for`; falta el backend DScript → DIL para ejecutarlo. `RANGE n` continúa como constructor DIL.

## DScript Frontend — primera entrega (2026-10-21)

La categoría **`Directiva/Tests/DScript`** se integra en `TestAll` y se puede ejecutar por separado:

- `Lexer/TestEverything`: tokens, sangría, strings, líneas, límites y errores léxicos.
- `Parser/TestEverything`: AST, precedencia, funciones, bloques, colecciones y errores de gramática.
- `Modules/TestEverything`: grafo de módulos, imports de usuario y nativos, `as`, `from`, wildcard, errores y provider filesystem.
- `Semantics/TestEverything`: `shared`/locals/parámetros, nombres, slots, funciones como valores, ámbitos y errores estáticos.
- `Shared/TestEverything`: símbolos compartidos únicos entre módulos, declaraciones e inicializadores, ciclos, orden de imports y política de recuperación prevista.

El grupo `Shared` comprueba **el plan de inicialización, no su ejecución**: un error runtime de un paso deberá registrarse y no cancelar los posteriores ni la partida, cuando exista el backend/orquestador DScript.

Todas las pruebas del frontend usan **fuentes `.dscript` en memoria** (excepto una prueba del provider que usa y limpia una carpeta temporal externa). No generan DIL ni ejecutan VM. Se conserva el formato y la carpeta externa de reportes existente. La gramática concreta y las limitaciones están documentadas en `Assets/Scripts/Code/Compilation/DScript/GRAMMAR.md`. Las pruebas requieren compilarse y ejecutarse en Unity antes de declararse aprobadas.

## Despacho `CALL_METHOD` por tipo (2026-10-22; pendiente de Unity)

El frontend DScript no necesita conocer el tipo de la variable receptora para compilar
`obj.pop(...)`: siempre emite `CALL_METHOD pop n` para invocaciones a miembros. La VM
resuelve el miembro en el DataType del receptor en tiempo de ejecucion. Un adaptador
de ese tipo puede invocar su operacion nativa sin convertir el nombre globalmente a
un `OperationId` (por ejemplo, `List.pop`/`Dict.pop` -> `Pop`, `Set.pop` -> `SetPop`).

- `List.pop()` quita el ultimo elemento; `List.pop(indice)` permite indices negativos.
- `Set.pop()` no recibe indice y devuelve un miembro segun el orden interno determinista.
- `Dict.pop(clave)` exige clave; la variante con valor predeterminado no esta implementada.
- `List.pop(a,b)`, `Set.pop(0)`, `Dict.pop()` y los receptores sin `pop` deben generar
  errores de script tipificados, no excepciones internas.
- Se agregan **21 pruebas** al grupo `DIL/Members` (antes 53, ahora 74): incluye
  resolucion dinamica, operaciones nativas equivalentes, GC/continuaciones ya existentes
  y un nuevo conflicto de mutacion concurrente durante `List.pop(index)`.
- **Objetivo:** 1063 pruebas en `TestAll` sobre la linea base 1042/1042 confirmada
  por el usuario. No hay ejecucion ni compilacion Unity desde este entorno;
  verificar `Directiva > Tests > DIL > Members > TestEverything` y luego `TestAll`.

## DScript backend — fase 2 (2026-10-22)

- `Directiva/Tests/DScript/Backend/TestEverything`: compilación integral
  DScript -> DIL -> `CodeContext` y ejecución real en la VM; verifica módulos,
  cortocircuito, funciones, `CALL_METHOD`, propiedades, indexación, bucles,
  depuración, memoria aislada y bootstrap `shared` con errores recuperables.
- Las pruebas del grupo `Backend` adjuntan fuentes originales y DIL emitido.
- Ejecutar después `Directiva/Tests/TestAll` para comprobar regresiones de DIL
  y del frontend. No afirmar que pasaron hasta ejecutar Unity Editor.

### 2026-10-22: integración editor DScript

`DScript/EditorIntegration/TestEverything`: 20 pruebas nuevas de `FileSystemScriptStorage`
con extensión `.dscript`, copias activas sin guardar, backups importados, bootstrap,
aislamiento entre ejecuciones, diagnósticos, entry points, rutas debug y publicación
segura de `.dil`. Todas trabajan en carpetas temporales, nunca en DirectivaScripts
de jugadores. Después ejecutar `Directiva/Tests/TestAll`. Los resultados no se
consideran PASS hasta ejecutarlos dentro de Unity.

## DScript: resaltado de sintaxis (v1.17)

- `Directiva > Tests > DScript > SyntaxHighlight > TestEverything`: pruebas del
  coloreado lexico/contextual, manejo de codigo incompleto, comentarios, strings,
  palabras clave, operadores, imports, `shared`, llamadas por miembro, escape de
  texto enriquecido, recorte horizontal, CRLF y copia de paleta.
- Estas pruebas no necesitan Play Mode ni cambian los archivos `DirectivaScripts`.
- Prueba manual recomendada: editar `main.dscript`, escribir codigo incompleto y
  comprobar colores, cursor, seleccion, tabulacion, undo/redo, breakpoints,
  scroll horizontal y vertical, zoom Ctrl+rueda y cambios de documento.
- La paleta se configura en `Resources/UI/DirectivaUITheme` > `Code Syntax Colors`;
  el controller expone `ApplySyntaxColors` y `SetSyntaxHighlightingEnabled` para
  conectar el futuro menu de opciones. No se persisten preferencias del jugador
  todavia. El resaltado es visual; no altera compilacion ni la VM.

### Selección de función inicial en el editor (2026-10-22)

El campo `fn:` vacío selecciona la **primera declaración `def` del archivo `.dscript` abierto** según el AST (orden textual, sin tomar funciones de imports ni del módulo bootstrap). Si se indica un nombre, se usa exclusivamente ese nombre. No se omiten funciones que requieren argumentos: el editor informa el error de aridad; sin funciones locales se presenta un error antes de iniciar `shared`. Los documentos nuevos tienen selección vacía por defecto; los nombres guardados explícitamente en `.dscript.editor` se conservan.

Ocho regresiones nuevas se incluyen en `Directiva > Tests > DScript > EditorIntegration > TestEverything`. Total esperado **1166** (1158 anteriores + 8).

### Corrección de caracteres literales en resaltado (2026-10-22)

- `Label.text` de UI Toolkit **no** interpreta las entidades HTML `&lt;`,
  `&gt;` ni `&amp;`: aparecían escritas y desplazaban la columna visual.
- `DScriptSyntaxHighlighter.FormatLine()` protege ahora cada `<` con
  `<noparse><</noparse>`, que TextCore presenta como **un solo glifo**. `>` y `&`
  quedan literales. El cierre de `noparse` que pudiera venir del jugador también
  queda protegido, evitando que strings o comentarios inyecten etiquetas.
- Se agregaron seis pruebas (`ComparisonAmpersand`, `LiteralHtmlEntities`,
  `NoparseInjection`, `UnicodePreservation`, `CroppedLiteralTag`,
  `NoColorLiteralTag`) y se actualizó `RichTextEscape`.
  **Grupo SyntaxHighlight: 27 pruebas; TestAll esperado: 1172** (1166 + 6).
- Revisión visual aconsejada: editar código con `<`, `>`, `&`, `áéíóú`, `¡¿`,
  secuencias como `<color=red>`, y comprobar alineación de cursor y selección.
  El test automatizado valida cadenas, no el rasterizado real de Unity.

### 2026-10-22 — Sincronización de zoom y selección coloreada

- `CodeEditorView` ya no calcula la alineación **únicamente durante** el evento
  Ctrl+rueda: UI Toolkit puede conservar la geometría anterior hasta la siguiente
  actualización de layout, sin producir un `GeometryChangedEvent` si el input
  mantiene su rectángulo exterior.
- Tras zoom de rueda o `SetFontSize`, el editor ejecuta **dos resincronizaciones
  programadas y coalescentes**: la primera actualiza métricas/altura de contenido,
  la segunda vuelve a muestrear posición y scroll después de otro ciclo del panel.
  Mientras tanto se usa el texto nativo del `TextField` (no la capa potencialmente
  desfasada), manteniendo selección y caret funcionales.
- Se vuelve a calcular el origen también cuando cambia la geometría del viewport
  del scroll. La función `CalculateTextOrigin` aísla la transformación de
  coordenadas respecto del `TextField` nativo; todos los dibujos reutilizan
  `_textOriginX` / `_textOriginY`.
- Se añaden **4 regresiones matemáticas** al grupo `DScript/SyntaxHighlight`
  (`NativeTextOrigin`, `OriginInvariantUnderScroll`, `ZoomReflowsInput`,
  `ZoomScrollCaretGrid`). Total previsto: **1176** (1172 + 4).
- Verificación manual imprescindible en Play Mode: seleccionar varias líneas,
  hacer Ctrl+rueda hasta zoom máximo/mínimo, desplazar vertical y horizontalmente,
  volver al zoom anterior, redimensionar paneles y confirmar que selección/caret,
  texto coloreado, números de línea y guías de indentación coincidan.
  Las pruebas automatizadas no renderizan UI Toolkit.
- No se modifican fuente, DScript, VM, compilador, `.meta`, almacenamiento
  ni preferencias de color. Sin eliminaciones ni renombres.

### 2026-10-22 — Hotfix: colores desaparecian tras Ctrl+rueda

- El ajuste anterior dejaba a veces la capa coloreada en `DisplayStyle.None` tras
  hacer zoom. La segunda resincronizacion se intentaba programar con
  `ExecuteLater()` **desde el callback de una tarea de una sola ejecucion**.
  Al finalizar esa tarea, Unity podia desprogramarla antes de la segunda pasada,
  dejando `_zoomLayoutSyncPassesRemaining` en 1 y ocultando el resaltado.
- `CodeEditorView.RequestZoomLayoutSync()` utiliza ahora
  `schedule.Execute(ContinueZoomLayoutSync).Every(34)`, reprogramable con
  `ExecuteLater(1)` en cada nuevo cambio de zoom. `ContinueZoomLayoutSync()`
  consume las dos pasadas y llama a `Pause()` al concluir. La ultima pasada
  restaura la capa coloreada con coordenadas actualizadas.
- Se conserva la correccion anterior de geometria/seleccion, la paleta editable
  y el comportamiento del TextField. No se alteran fuente, documentos DScript,
  parser, compilador, VM ni metadatos `.meta`.
- La regresion requiere **comprobacion visual en Play Mode**: Ctrl+rueda
  repetidamente (sin redimensionar paneles), verificar recuperacion de colores
  y seleccion alineada, probar zoom minimo/maximo, scroll y editar codigo.
  Las pruebas existentes de geometria son unitarias y no ejecutan el scheduler
  del panel, por lo que el recuento previsto permanece en **1176**.
- Sin archivos eliminados o renombrados.

### 2026-10-22 - Correccion de origen de scroll tras zoom

- La clase `unity-text-field__input` es el contenedor **fijo** del `ScrollView` de un `TextField` multilinea. Su `worldBound` no se desplaza con `scrollOffset`; sumar el offset al calcular `CalculateTextOrigin` desplazaba erróneamente el gutter, overlay y guías al hacer zoom después de bajar.
- `CalculateTextOrigin` recibe ahora solo bounds del contenedor fijo, borde y padding; el scroll se resta una sola vez al colocar cada capa. `RebuildGutter` actualiza explícitamente la traslación vertical incluso cuando Unity limita el offset al cambiar la altura del contenido.
- Se sustituyen las suposiciones erróneas de las 4 pruebas de coordenadas y se añaden `ZoomWhileScrolled` y `ZoomScrollEndRange`: **33** casos en SyntaxHighlight y **1178** casos esperados en TestAll.
- Prueba manual indispensable: bajar a media página, Ctrl+rueda, scroll hasta arriba y abajo, confirmar que primera/última línea, color, gutter, cursor, selección y scrollbar están sincronizados sin mover separadores.
- La inspección estática y las pruebas puras no equivalen a una ejecución real en Unity.

### Zoom anclado al centro (2026-10-22)

`DScript/SyntaxHighlight/TestEverything`: cinco pruebas de coordenadas nuevas para
zoom alrededor de la linea central, posicion fraccional, cambios de viewport,
limites de scroll y eventos rapidos consecutivos. Total previsto: **1183**
(1178 previos + 5). El `TextField` y el overlay mantienen un unico scroll.

Verificar manualmente en Play Mode: abrir un script largo, bajar hasta mitad,
hacer Ctrl+rueda lento y rapido y comprobar que la misma linea permanece cerca
del centro; probar primera/ultima linea, cursor, seleccion, gutter, colores
y cambios de fuente desde opciones.
