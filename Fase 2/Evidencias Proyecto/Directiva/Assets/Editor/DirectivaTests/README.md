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
| `Members/TestEverything` | Parser y ejecución de miembros DIL, herencia, getters/setters y métodos continuados (53 pruebas nuevas) |
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

**Limites de cobertura:** la suite cubre el **runtime DIL implementado**, no el futuro compilador DScript, gameplay ni UI visual/escenas. `InvalidCastValue` tiene solo una prueba de contrato del error, porque todavia no existe un camino de DIL que lo produzca. Pruebas con `CodeContext`/`Instruction` manuales se identifican en los reportes y no deben confundirse con errores que un DIL bien parseado permite provocar.

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
