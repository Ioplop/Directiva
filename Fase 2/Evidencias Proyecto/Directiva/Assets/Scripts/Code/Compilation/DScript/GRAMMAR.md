# DScript — gramática, frontend y backend (fases 1 y 2)

**Estado (2026-10-22):** frontend, backend DScript → DIL por módulos y ejecutor de inicializadores `shared` implementados. Pendiente de ejecutar las pruebas en Unity. El editor ahora puede compilar y ejecutar DScript; la integración con la simulación jugable sigue pendiente.

## Objetivo

DScript se inspira en Python, pero no es Python. Se escriben funciones de módulo y se controlan unidades mediante código dinámicamente tipado. No hay clases, lambdas, funciones anidadas, closures, generadores, decoradores ni argumentos nombrados. Se permiten referencias a funciones compiladas y nativas como valores.

Los imports usan nombres **absolutos desde la raíz lógica `DirectivaScripts`**: `utils.vectors` → `DirectivaScripts/utils/vectors.dscript`. No hay `.`/`..` relativo al importador. `math` y `random` son módulos nativos reservados, no archivos del jugador. La carpeta raíz real la selecciona el proveedor de código fuente, que el editor ya conecta mediante WorkspaceDScriptSourceProvider.

## Ejemplo

**state.dscript:**

```python
shared action = miner
shared counter = 0

def miner():
    print("Mining")

def attacker():
    print("Attacking")
```

**controller.dscript (módulo de entrada):**

```python
import state
shared action
shared counter

def update():
    counter += 1
    action()

def change_action():
    action = state.attacker
```

En estos dos módulos `action` y `counter` **son un único símbolo/slot por nombre para todo el programa**. `state.dscript` aporta los únicos inicializadores; `controller.dscript` utiliza esos mismos slots sin getters ni setters. Todas las VMs que compartan su memoria de simulación verán los mismos valores. La fase 2 compila estas expresiones para la VM.

## Gramática inicial

```ebnf
module            := { NEWLINE | top_level }
top_level         := import | from_import | function | shared_declaration
import            := 'import' module_path ['as' identifier] NEWLINE
from_import       := 'from' module_path 'import' ('*' | import_item {',' import_item}) NEWLINE
import_item       := identifier ['as' identifier]
module_path       := identifier {'.' identifier}
shared_declaration:= 'shared' identifier ['=' expression] NEWLINE
function          := 'def' identifier '(' [identifier {',' identifier} [',']] ')' suite
suite             := ':' NEWLINE INDENT statement {statement} DEDENT
statement         := assignment | expression NEWLINE | if | while | for | return
                  | break | continue | pass
if                := 'if' expression suite {'elif' expression suite} ['else' suite]
while             := 'while' expression suite
for               := 'for' identifier 'in' expression suite
assignment        := (name | member | index) assignment_operator expression NEWLINE
return            := 'return' [expression] NEWLINE
break             := 'break' NEWLINE
continue          := 'continue' NEWLINE
pass              := 'pass' NEWLINE
```

- **Fuera de funciones:** únicamente imports, `shared name [= expression]` y `def`. `counter = 0`, `print(...)`, bucles o condicionales al nivel superior son errores **sintácticos DS2013**.
- **Dentro de funciones/bloques:** `shared` es un **error sintáctico DS2014**. El antiguo `global` de Python **no está admitido** (DS2015 en cualquier ámbito).
- Cada declaración `shared` lleva **un nombre** (no `shared a, b`); puede ubicarse antes o después de las funciones, aunque por claridad se aconseja ponerlas arriba.
- Listas, llamadas y declaraciones `def` admiten coma final opcional (`[1,2,]`, `f(1,)`, `def f(a,):`). No se aceptan comas dobles ni argumentos nombrados.
- `print()` admite **0..16 argumentos** (límite actual de llamadas nativas). Evalúa los argumentos de izquierda a derecha, los convierte con la misma semántica que `str`, los separa con un espacio y emite **una sola línea** (inclusive vacía). Por ejemplo `print("Primos:", [2, 3, 5])` produce `Primos: [2, 3, 5]`. No hay `sep=` ni `end=` y el compilador no reescribe esta llamada: genera `CALL_INDIRECT n` como con cualquier función nativa.
- Expresiones, de menor a mayor precedencia: `or`, `and`, `not`, comparaciones `== != < <= > >= in not in` (sin chaining), `+ -`, `* / // %`, unarios `+ -`, `**` asociativo a derecha; miembros, índices y llamadas.
- Literales: Int64, Decimal de cuatro decimales como máximo, String con escapes, Bool (`True`/`False`), `None`, listas `[a,b]`, tuplas `(a,b)`, sets `{a,b}` y diccionarios `{clave:valor}`. `{}` es diccionario vacío; `()` es tupla vacía.
- Sangría **solo con espacios**; comentarios `#`; bloques no vacíos (usar `pass`); continuación de línea dentro de `()[]{}`. No se admiten strings multilínea, slices, comprensiones, `is`, asignaciones múltiples ni desestructuración.

## Símbolos y almacenamiento `shared`

1. El compilador descubre **todos los módulos alcanzables** desde el módulo inicial, sin ejecutar ninguno, **incluidos grafos con imports circulares**.
2. Registra todas las funciones y declaraciones `shared` **antes de analizar los cuerpos y los inicializadores**. Una función puede referenciar símbolos declarados más adelante, incluso en módulos importados cíclicamente.
3. **Un único slot por nombre compartido en todo el programa:** `shared action` en varios módulos apunta al mismo `DScriptSymbol` (`QualifiedName = "shared.action"`, `SymbolKind.Global` por compatibilidad con DIL). Las declaraciones repetidas son legales. Los slots se asignan en orden **ordinal alfabético por nombre**, independientemente del orden de imports.
4. **Solo un inicializador por nombre en todos los módulos:** `shared action = f` puede coexistir con múltiples `shared action`, pero un segundo `shared action = g` genera **DS4018** en el frontend. Un shared sin inicializador permanece `Uninitialized` hasta que una función lo escriba.
5. Una asignación a una variable que el módulo declaró `shared` vincula ese slot automáticamente, **sin escribir `shared` dentro de funciones**. Las demás asignaciones simples de función crean locales; los parámetros ocupan los primeros slots locales. Declarar un parámetro con el nombre de un `shared` del módulo genera DS4006.
6. Un módulo debe **declarar** un shared para usarlo directamente (`shared action`) o **importarlo** (`from state import action` / `state.action`). Importar cualquier módulo no hace aparecer implícitamente todos los shared en el ámbito local. Se puede importar y declarar el mismo shared si ambos nombres se refieren al mismo símbolo.
7. Cada módulo conserva su espacio de nombres de funciones/imports, pero los slots shared **no** se califican por módulo. Una función y un `shared` con idéntico nombre **en el mismo módulo** se consideran conflicto.
8. `DScriptSymbol.DILGlobalName` expone el identificador sin colisiones de `# GLOBAL`. El backend los emite en el bootstrap siguiendo exactamente el orden de `GlobalSlots`.

Tipado dinámico: el frontend conoce el símbolo de cada expresión, pero no comprueba el tipo de runtime de `a+b`, miembros como `unit.attack` ni índices. Las lecturas anteriores a una asignación producen errores **de runtime**, no de compilación.

## Grafo de imports e inicialización

La visita del grafo usa estados de módulo para evitar reanalizar un archivo; un borde circular hacia un módulo ya visto **no es un error**. Los exports de todos los módulos quedan disponibles antes de enlazar imports y funciones. Los módulos nativos nunca necesitan archivos `.dscript`.

El frontend entrega `DScriptAnalysis.SharedInitializers`: una lista **plana, ordenada y con una entrada por cada declaración `shared` que tiene inicializador**. Para generarla recorre el módulo inicial siguiendo el orden de sus instrucciones: al encontrar un import, recorre el módulo importado **en ese punto**; al encontrar un inicializador `shared`, agrega ese paso. Al reencontrar un módulo ya visitado, no lo procesa otra vez. Un `from x import ...` también cuenta como una importación para este recorrido. Esto es **determinista, sin ejecución** y admite ciclos.

Ejemplo:

**main.dscript:**
```python
shared a = 10
import helper
shared c = b + 5
shared b
```

**helper.dscript:**
```python
shared a
shared b = a * 2
import main
```

Si `main` es entrada: plan `a, b, c`. Si `helper` es entrada: plan `b, a, c`. En ambos casos todos los slots `a`, `b`, `c` ya existen cuando se construye el plan. El segundo orden puede intentar leer `a` en estado `Uninitialized`, pero **no es un error de análisis estático**.

**Importante:** este orden depende **también de la posición textual de cada import**. Si en `helper` se moviera `import main` por encima de `shared b = ...`, el recorrido desde `helper` visitaría `main` primero. No hay un orden implícito basado en dependencias de las expresiones ni una inicialización de módulos ejecutada dinámicamente por la VM.

### Ejecución y recuperación implementadas en fase 2

- **Cada elemento de `SharedInitializers` debe compilarse como unidad de ejecución independiente**, no como una única llamada DIL susceptible de terminar por el primer error no recuperable. Todas usan el **mismo `CodeContext` y el mismo `VMMemoryContext`**.
- Se ejecutan secuencialmente, una sola vez por simulación, antes de las VMs normales. Si una falla (incluida lectura de `Uninitialized`, exceso de presupuesto o error nativo), se informa **claramente** en la consola con variable, archivo, línea y causa. **Se continúa con el siguiente inicializador**, y **la simulación puede arrancar las VMs normales aun cuando hubo errores**.
- Si un paso falla **antes del `STORE_GLOBAL` de su variable**, su slot destino permanece sin el nuevo valor; puede seguir `Uninitialized`. Los pasos posteriores podrían fallar también, y se reportarán por separado. Los inicializadores son **no transaccionales**: las funciones llamadas pueden haber causado otros efectos antes de fallar; estos no se revierten automáticamente.
- `DScriptBootstrapRunner.Initialize` ejecuta cada paso por separado; devuelve `DScriptInitializationReport` y acepta callbacks de error y salida para la futura UI. El host controla cuándo lo invoca y debe hacerlo antes de arrancar las unidades.
- Los inicializadores **también** estarán sujetos a presupuestos de VM, memoria e instrucciones; el costo exacto competitivo de la inicialización se definirá con la simulación.

## Imports

- `import math`, `import random` se resuelven con `NativeLibrary` y no inicializan variables al ser descubiertos.
- **Colisiones de nombres:** un import sin alias introduce su nombre de módulo en el mismo espacio de nombres que los símbolos `shared` declarados en ese archivo. Si existe `shared b`, usar `import b` en ese módulo es ambiguo y genera **DS4004**. Se resuelve con `import b as modulo_b`; las expresiones `b` siguen referenciando el slot `shared.b`. El alias no modifica el orden de importación ni el plan de inicializadores.
- `import utils.calc` hace accesible `utils.calc`; `import utils.calc as c` liga el alias `c` al módulo completo.
- `from utils.calc import f as other` liga `other` a `utils.calc.f`. `from math import *` incluye sus funciones/constantes públicas. `*` sobre archivos DScript exporta únicamente **funciones y shared declarados en el archivo**, excluyendo nombres que comienzan con `_`, no imports transitivos.
- Los módulos son espacios de nombres de compilación, no valores runtime; `math.sin` sí es una referencia a función. `math` por sí solo no es un valor.
- Los imports son **absolutos**, **case-sensitive** y no admiten componentes vacíos ni `..`. El `FileSystemDScriptSourceProvider` evita escapar de su raíz mediante segmentos inválidos y rechaza enlaces simbólicos/junctions dentro de ella. La raíz física concreta debe proporcionarla el host al construir el source provider.
- `shared` es palabra reservada; para un archivo dedicado a las variables compartidas se recomienda un nombre como `state.dscript` o `shared_state.dscript`, no `shared.dscript`.

## API pública del frontend

```csharp
DScriptAnalysis result = DScriptFrontend.Analyze(entryModule, sourceProvider, options);
```

Además de las tablas previas `Modules`, `ModuleSymbols`, `FunctionSymbols`, `ExpressionSymbols`, `ForTargetSymbols`, `GlobalSlots`, `DependenciesFirst` y `Diagnostics`, se agregaron:

- `SharedDeclarations`: cada nodo de declaración -> símbolo shared canónico.
- `SharedInitializers`: plan ordenado con `Order`, `Module`, `Declaration`, `Symbol`, `Expression`, `Span`. Una entrada representa una inicialización que deberá poder fallar **sin saltarse las posteriores**.

El AST conserva offsets, línea y columna para mensajes/debug `DEBUG_*`. Si ocurre un error léxico/sintáctico/import, no se intenta análisis semántico. Los códigos son DS1xxx (lexer), DS2xxx (parser), DS3xxx (carga), DS4xxx (semántica). Límites por defecto: 128 módulos, 500.000 caracteres por fuente, 100 diagnósticos y 64 niveles de anidamiento.

## Backend de DScript (fase 2)

La API de compilación se encuentra en `DSCompilation.DScript.Backend`:

```csharp
DScriptCompilationResult compiled = DScriptCompiler.Compile("main", sourceProvider);
if (compiled.Success)
{
    DScriptCompiledProgram program = compiled.Program;
    var teamMemory = new VMMemoryContext(program.Code.GlobalCount);
    DScriptInitializationReport report = DScriptBootstrapRunner.Initialize(program, teamMemory);
    // El host crea cada VM con program.Code, teamMemory y su función de entrada.
}
```

- Se genera DIL **por módulo**, además de un bootstrap interno. Se conserva el texto de los módulos en `program.DILModules`, pero el backend no escribe archivos automáticamente (la publicación es responsabilidad del host después de compilar satisfactoriamente).
- Todas las declaraciones `# GLOBAL` se colocan en el bootstrap, sin divergencias de slots.
- El DIL generado siempre emite `DEBUG_FILE`, `DEBUG_LINE` y `DEBUG_SPAN` con ubicaciones originales. El costo de las instrucciones `DEBUG_*` es cero en presupuestos lógicos, no en tiempo real de CPU.
- Las operaciones sintácticas generan opcodes nativos; **toda llamada dinámica a un miembro genera `CALL_METHOD`**. Los métodos siguen pudiendo delegar internamente a `OperationId` sin que el compilador adivine el tipo.
- `and` / `or` evalúan por cortocircuito y devuelven el operando pertinente; no fuerzan un Bool.
- Cada inicializador `shared` se ejecuta en una VM independiente sobre la misma memoria del equipo, con presupuesto finito y reporte de errores por variable. El próximo paso se ejecuta aunque uno anterior falle. La memoria no puede inicializarse dos veces mediante el bootstrap.
- `VMMemoryContext` es por equipo y partida, no por unidad ni global a toda la partida. El mismo `CodeContext` se puede reutilizar con memorias aisladas.

Consultar `Backend/README.md` para un ejemplo de integración con el orquestador.

## Fuera del alcance actual

Arranque de la simulación real, objetos y acciones de unidades/Nexo, stepping por sentencia DScript (actualmente Step avanza instrucciones VM), tipado estático e instrucciones DIB. El editor ya oculta `.dil`, compila `.dscript` y publica opcionalmente DIL generado sin sobrescribir archivos manuales. Las pruebas del backend **sí ejecutan la VM desde DScript**, pero deben validarse dentro de Unity antes de afirmar PASS.
