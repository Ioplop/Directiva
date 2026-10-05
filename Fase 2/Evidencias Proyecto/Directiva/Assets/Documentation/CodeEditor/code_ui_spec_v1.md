# Directiva — Code UI Specification v1

**Estado:** especificación para la primera generación funcional de la interfaz de programación de Directiva.  
**Propósito:** definir con suficiente precisión la arquitectura, comportamiento y alcance para generar posteriormente la implementación en Unity usando UI Toolkit.  
**Nota:** esta versión debe dejar conectados los puntos de extensión del futuro IDE, pero no implementar todavía análisis semántico real, refactorización real, syntax highlighting avanzado ni debugging completo.

---

## 1. Objetivo general

Directiva tendrá una interfaz de programación integrada dentro del juego, con una organización similar a un IDE sencillo.

La primera generación debe permitir que el usuario:

- navegue scripts y carpetas reales;
- cree, abra, edite, guarde, revierta, renombre, duplique, elimine y mueva scripts;
- mantenga cambios sin guardar mediante archivos de backup;
- edite texto multilínea con comportamiento cómodo para código;
- vea números de línea;
- añada o quite breakpoints visuales;
- utilice controles básicos de ejecución;
- vea mensajes en un panel Output;
- cambie el idioma de la interfaz en runtime;
- trabaje sobre una arquitectura ya preparada para integrar posteriormente análisis, compilación, refactorización y debugging.

La prioridad es tener una base robusta, modular y extensible, no implementar todavía un IDE completo.

---

## 2. Tecnología de UI

La interfaz se implementará utilizando **Unity UI Toolkit**.

La UI principal no dependerá de Canvas/uGUI/TMPro.

La escena puede contener un GameObject mínimo con un `UIDocument`, pero el resto de la interfaz debe vivir dentro del árbol de `VisualElement`.

Los elementos visuales del mundo de juego —por ejemplo círculos de selección, indicadores en terreno, efectos, modelos o partículas— quedan fuera de esta especificación y pueden usar GameObjects/rendering normal.

---

## 3. Layout general

La interfaz sigue aproximadamente esta distribución conceptual:

```text
┌──────────────────────────────┬─────────────────────────────────────────────┐
│ Opciones scripts             │ Título del script                         │
├──────────────────────────────┼─────────────────────────────────────────────┤
│                              │ Control           │ Opciones               │
│ Explorador                   │                   │                        │
│ de scripts                   ├─────────────────────────────────────────────┤
│                              │                                             │
│                              │                Código                       │
│                              │                                             │
├──────────────────────────────┴─────────────────────────────────────────────┤
│ Output — panel colapsable                                                │
└────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 Zona izquierda

Contiene:

- árbol de scripts;
- carpetas colapsables;
- scripts seleccionables;
- indicadores de estado;
- operaciones de creación y organización.

### 3.2 Zona superior derecha

Contiene:

- nombre del script activo;
- controles de ejecución;
- acciones de guardado/reversión;
- acceso futuro a opciones generales del juego;
- botón Atrás.

### 3.3 Zona central derecha

Contiene el editor del script activo.

### 3.4 Zona inferior

Contiene el panel `Output`, colapsable y redimensionable.

---

## 4. Sistema de archivos

Los scripts de Directiva deben ser archivos reales.

### 4.1 Extensiones oficiales

Script:

```text
.dscript
```

Backup de working copy:

```text
.dscript.back
```

Ejemplo:

```text
combat.dscript
combat.dscript.back
```

### 4.2 Directorio persistente

En escritorio, los scripts se almacenarán bajo una carpeta persistente de la aplicación, conceptualmente:

```text
Application.persistentDataPath/
└── DirectivaScripts/
```

Ejemplo:

```text
DirectivaScripts/
├── main.dscript
├── Combat/
│   └── attacker.dscript
└── Economy/
    ├── miner.dscript
    └── hauler.dscript
```

### 4.3 Abstracción de almacenamiento

La UI no debe depender directamente de `System.IO`.

Debe existir una abstracción de almacenamiento responsable de:

- listar carpetas;
- listar scripts;
- leer scripts;
- escribir scripts;
- crear carpetas;
- crear scripts;
- eliminar;
- renombrar;
- duplicar;
- mover;
- leer/escribir backups.

Esto permitirá sustituir posteriormente la implementación de filesystem por otra compatible con WebGL, navegador u otro medio persistente.

La interfaz y el editor no deben necesitar cambios para ello.

---

## 5. Explorador de scripts

### 5.1 Navegación

El explorador representa visualmente la estructura real de carpetas.

Las carpetas son colapsables.

Un solo click sobre un script:

1. lo selecciona;
2. lo convierte en el script activo;
3. carga su working copy en el editor.

No existen pestañas.

### 5.2 Un único script activo

Puede haber múltiples scripts con cambios pendientes, pero solo existe **un script trabajado activamente**: el que está actualmente visible en el editor.

Los demás scripts dirty existen como archivos con backup, pero no se consideran abiertos simultáneamente.

### 5.3 Selección y renombrado

Un click sobre un script no seleccionado:

- selecciona;
- abre.

Un click sobre el nombre de un script ya seleccionado:

- entra en modo de renombrado inline.

Comportamiento esperado:

- `Enter` confirma;
- `Escape` cancela.

### 5.4 Menú contextual

Click derecho sobre script o carpeta:

- Renombrar;
- Duplicar;
- Eliminar.

No es necesario añadir más acciones en esta primera generación.

### 5.5 Drag & drop

Scripts y carpetas pueden moverse arrastrándolos a otras carpetas.

El movimiento debe modificar realmente el filesystem.

Restricciones mínimas:

- una carpeta no puede moverse dentro de sí misma;
- una carpeta no puede moverse dentro de uno de sus descendientes;
- las colisiones de nombre deben impedir la operación o manejarse explícitamente;
- el backup asociado a un script se mueve junto con este.

Ejemplo:

```text
Combat/
├── attack.dscript
└── attack.dscript.back
```

al mover `attack.dscript`:

```text
AI/
├── attack.dscript
└── attack.dscript.back
```

### 5.6 Eliminación

Eliminar siempre requiere confirmación.

Que el script esté dirty o no es irrelevante para esta confirmación: eliminar es siempre una operación destructiva.

Al eliminar un script:

- se elimina `.dscript`;
- se elimina también `.dscript.back` si existe.

Un backup nunca debe sobrevivir a la desaparición conceptual de su script.

---

## 6. Estado dirty y backups

### 6.1 Definición de dirty

Un script está dirty cuando su contenido de trabajo es distinto de su contenido guardado.

La comparación puede realizarse usando un hash SHA-256.

Conceptualmente:

```text
SHA256(workingContent) != SHA256(savedContent)
```

significa dirty.

Si ambos vuelven a coincidir:

- el script deja de estar dirty;
- el `.dscript.back` debe eliminarse si existe;
- desaparece el indicador visual de cambios pendientes.

### 6.2 Creación del backup

El archivo `.dscript.back` solo debe existir si hay cambios reales sin guardar.

Debe actualizarse:

- periódicamente mientras el usuario edita;
- obligatoriamente antes de cambiar a otro script activo.

Esto evita pérdida de trabajo aunque el intervalo periódico todavía no haya ocurrido.

### 6.3 Recuperación tras cierre o crash

Si al iniciar Directiva existe:

```text
foo.dscript
foo.dscript.back
```

se interpreta que `foo` tenía cambios pendientes.

El `.back` debe recuperarse como working copy.

El script aparecerá dirty.

### 6.4 Guardar

Guardar significa:

1. escribir el working content en `.dscript`;
2. actualizar el estado guardado;
3. eliminar `.dscript.back`;
4. dejar `IsDirty = false`.

Guardar **no es automático**.

### 6.5 Revertir

Revertir significa:

1. pedir confirmación;
2. descartar la working copy;
3. eliminar `.dscript.back`;
4. recargar `.dscript`;
5. dejar `IsDirty = false`.

Revertir siempre debe preguntar antes de actuar.

### 6.6 Cambiar de script

Cambiar de script **no guarda automáticamente**.

Si el actual está dirty:

1. se asegura que el `.back` esté actualizado;
2. se cambia al nuevo script;
3. el anterior sigue apareciendo marcado como dirty en el árbol.

---

## 7. Indicadores visuales del árbol

Los scripts pueden tener estados visuales.

### 7.1 Estados principales

Normal:

- sin cambios;
- sin errores conocidos.

Dirty:

- destacado amarillo.

Error:

- destacado rojo.

### 7.2 Dirty + Error

Si un script tiene errores y cambios pendientes:

- rojo tiene prioridad como estado principal;
- un indicador secundario debe mostrar que también está dirty.

No mezclar simplemente colores.

### 7.3 Carpetas

Una carpeta refleja el estado agregado de todo su subárbol, incluso cuando está colapsada.

Prioridad:

1. si cualquier descendiente tiene error → carpeta roja;
2. si no hay errores pero hay dirty → carpeta amarilla;
3. si no hay ninguno → normal.

Si coexisten error y dirty en descendientes, se conserva rojo como prioridad y un indicador secundario puede representar dirty.

Esto evita que colapsar carpetas oculte problemas.

---

## 8. Duplicar

Duplicar debe copiar la versión que el usuario considera actualmente visible/relevante.

Si el script está dirty:

- se duplica la working copy;
- no la versión guardada antigua.

La copia resultante se crea como un nuevo `.dscript` ya guardado.

No nace dirty.

---

## 9. Editor de código — alcance inicial

La primera versión del editor debe priorizar edición fiable.

No requiere todavía:

- syntax highlighting;
- subrayado de errores;
- autocomplete;
- tooltips semánticos;
- go-to-definition;
- refactoring visual;
- code folding;
- minimap;
- multicursor.

### 9.1 Funciones mínimas

Debe permitir:

- texto multilínea;
- edición normal;
- caret;
- selección;
- copiar;
- cortar;
- pegar;
- borrar;
- scroll vertical;
- scroll horizontal;
- fuente monoespaciada;
- números de línea;
- gutter para breakpoints.

### 9.2 Números de línea

Los números de línea:

- siempre visibles;
- sincronizados con scroll;
- no editables;
- fuera del contenido real del script.

### 9.3 Breakpoints

Debe existir una columna separada en el gutter para breakpoints.

Click sobre una línea:

- cambia visualmente el breakpoint;
- emite un evento de add/remove breakpoint.

Los breakpoints:

- no necesitan tener comportamiento real de debugging todavía;
- no necesitan persistir entre sesiones en esta primera versión.

---

## 10. Indentación

No se usarán caracteres TAB reales.

La indentación usa espacios.

### 10.1 Tab sin selección multilínea

Los tab stops ocurren cada 4 columnas.

`Tab` mueve el caret hasta el siguiente múltiplo de 4 columnas insertando los espacios necesarios.

Ejemplo:

```text
ab|
```

pasa a:

```text
ab  |
```

si el siguiente tab stop es la columna 4.

### 10.2 Shift+Tab

`Shift+Tab` retrocede al tab stop anterior eliminando espacios de indentación.

Si no hay indentación disponible:

- no hace nada.

Nunca debe borrar código no perteneciente a la indentación inicial.

### 10.3 Selección multilínea

Si hay una selección que toca varias líneas:

`Tab`:

- indenta todas las líneas seleccionadas un nivel.

`Shift+Tab`:

- desindenta todas las líneas seleccionadas un nivel.

La selección debe mantenerse coherentemente después de la operación para permitir pulsaciones repetidas.

### 10.4 Indentación irregular

Al desindentar, una línea retrocede hasta el tab stop anterior.

Ejemplos:

- 2 espacios → 0;
- 6 espacios → 4;
- 8 espacios → 4.

---

## 11. Barra de control

Controles visibles:

```text
Stop
Pause
Continue
Step
Step In
Step Out
```

### 11.1 Primera generación

Funcionales:

- Stop;
- Pause;
- Continue.

Visibles pero desactivados:

- Step;
- Step In;
- Step Out.

### 11.2 Semántica

La UI no controla directamente VM, simulación ni orquestador.

Emite intención.

Conceptualmente:

```text
StopRequested
PauseRequested
ContinueRequested
```

### 11.3 Comportamiento temporal de Continue

Mientras no exista simulación completa:

- si no hay ejecución activa, `Continue` inicia la ejecución del script actual;
- si está pausado, `Continue` continúa.

En el futuro su significado se conectará al orquestador/simulación.

### 11.4 Stop

Actualmente termina/reseteará la ejecución correspondiente.

En el futuro podrá indicar al orquestador que detenga y reinicie la simulación.

### 11.5 Pause

Actualmente pausa la ejecución.

En el futuro pausará simulación y VMs según corresponda.

---

## 12. Arquitectura basada en eventos

Las acciones discretas de UI deben comunicarse mediante eventos o un mecanismo equivalente desacoplado.

Ejemplos conceptuales:

```text
CreateScriptRequested
CreateFolderRequested
SaveRequested
RevertRequested
RenameRequested
DuplicateRequested
DeleteRequested
MoveRequested

ContinueRequested
PauseRequested
StopRequested

BreakpointAdded
BreakpointRemoved
```

La UI comunica:

> “el usuario pidió X”

No:

> “ejecuta directamente X sobre tal sistema concreto”.

### 12.1 Intención vs resultado

Debe mantenerse conceptualmente la diferencia entre:

- evento de intención;
- resultado real de la operación.

Ejemplo:

```text
SaveRequested
↓
Storage intenta guardar
↓
ScriptSaved
```

Solo después de éxito debe considerarse guardado.

Esto permite manejar correctamente errores futuros de filesystem, WebGL, etc.

### 12.2 Cambios de texto

Editar texto no debe tratarse como una acción global equivalente a Save o Pause.

El editor mantiene su propio working buffer.

Un cambio de texto:

- actualiza el buffer;
- actualiza dirty;
- actualiza backup cuando corresponda;
- puede disparar internamente análisis futuro con debounce.

No es un guardado.

---

## 13. Output

El Output es una consola genérica.

No conoce:

- `print`;
- compiler;
- VM;
- filesystem;
- refactorer.

Solo recibe mensajes.

### 13.1 Severidades

Tres niveles:

```text
Message
Warning
Error
```

Visualmente:

- Message → blanco;
- Warning → amarillo;
- Error → rojo.

### 13.2 Filtros

Debe ofrecer filtros independientes para:

- mensajes;
- warnings;
- errores.

Filtrar no elimina entradas.

### 13.3 Limpiar

Debe existir una acción `Clear/Limpiar`.

Vacía completamente el buffer.

### 13.4 Buffer máximo

El Output usa un buffer acotado.

Valor inicial recomendado:

```text
1000 entradas
```

Debe ser fácil de configurar.

Cuando se supera el máximo:

- se descartan primero las entradas más antiguas.

### 13.5 Panel colapsable

El Output puede:

- expandirse;
- colapsarse.

Colapsar no borra contenido.

Idealmente su altura expandida puede ajustarse arrastrando su borde superior.

### 13.6 Notificación visual mientras está colapsado

Si llega un nuevo mensaje mientras está colapsado:

- Message → destello/parpadeo blanco;
- Warning → destello/parpadeo amarillo;
- Error → destello/parpadeo rojo.

La prioridad visual pendiente es:

```text
Error > Warning > Message
```

El panel no debe abrirse automáticamente.

### 13.7 Localización y Output

El Output almacena texto final ya renderizado.

No debe conservar claves de locale ni reinterpretar mensajes antiguos.

Por tanto:

- mensajes del sistema se localizan antes de enviarlos;
- `print()` entra literalmente;
- cambiar idioma no modifica mensajes antiguos;
- mensajes nuevos usan el idioma nuevo.

Esto mantiene el Output simple.

---

## 14. Localización

Todo texto perteneciente al sistema debe ser localizable.

Esto incluye:

- botones;
- labels;
- diálogos;
- confirmaciones;
- mensajes de error;
- warnings;
- mensajes del sistema;
- errores futuros del lenguaje.

No incluye:

- strings producidos por el código del usuario;
- contenido literal de `print()`.

### 14.1 Locales por componente

Cada componente debe poder mantener su propio archivo de locale.

Ejemplo conceptual:

```text
Localization/
├── FileExplorer/
├── CodeEditor/
├── Output/
├── Refactoring/
├── Diagnostics/
└── Common/
```

No es necesario que esta jerarquía exacta sea definitiva, pero debe evitarse un único archivo monolítico con todas las claves del proyecto.

### 14.2 Formato de locale

Formato textual esperado:

```text
[key]: Mensaje
```

Con parámetros:

```text
[search_result]: Resultado {{1}} de {{2}}
[revert_confirm]: ¿Seguro que quieres revertir {{1}}?
```

Los placeholders empiezan en 1.

### 14.3 API conceptual

Ejemplos:

```text
Localize("save")
Localize("revert_confirm", scriptName)
Localize("search_result", current, total)
```

La función acepta un número variable de parámetros opcionales.

### 14.4 Sustitución

```text
{{1}}
{{2}}
{{3}}
...
```

se sustituyen con los argumentos entregados.

Reglas recomendadas:

- parámetros extra → ignorados;
- parámetros faltantes → placeholder visible y warning;
- una clave inexistente → usar fallback;
- si también falta en fallback → mostrar una señal evidente tipo `[missing:key]` y emitir warning.

### 14.5 Locale por defecto

Primera versión:

```text
Español
```

Versión final prevista:

```text
Inglés
```

La elección debe ser configurable.

### 14.6 Cambio en runtime

El idioma puede cambiar mientras el juego está ejecutándose.

Debe existir un evento conceptual:

```text
LocaleChanged
```

Los componentes visibles actualizan inmediatamente sus textos.

El Output antiguo, como excepción deliberada, no se retraduce.

---

## 15. Diagnósticos futuros

Los diagnósticos del lenguaje no deben producir directamente strings traducidos.

Conceptualmente deben producir:

```text
Diagnostic
- key/code
- severity
- arguments
- ubicación
```

Ejemplo:

```text
key: unknown_identifier
severity: Error
arguments: ["enemyy"]
```

La capa correspondiente localiza el mensaje antes de mostrarlo o enviarlo al Output.

Esto permitirá soportar varios idiomas sin acoplar lexer/parser/análisis al español.

---

## 16. DirectivaCode

`DirectivaCode` representa el índice estructural/semántico del código del proyecto.

No debe entenderse como un gran string con todos los scripts cargados.

### 16.1 Contenido futuro esperado

Podrá contener:

- scripts conocidos;
- nombres;
- rutas;
- referencias a archivos;
- imports;
- grafo de dependencias;
- funciones declaradas por script;
- símbolos;
- referencias semánticas;
- índices rápidos.

El código fuente completo de todos los scripts no necesita residir permanentemente en memoria.

### 16.2 Estado guardado

`DirectivaCode` representa principalmente el estado guardado del proyecto.

Los otros scripts se consultan desde sus versiones guardadas.

### 16.3 Script actualmente trabajado

Cuando se analiza el script activo:

- se utiliza su working copy completa;
- puede reconocer sus propias funciones/variables nuevas aún no guardadas;
- los demás scripts aportan únicamente su estado guardado/indexado.

Regla central:

> El script actualmente editado puede usar sus propios cambios no guardados. Los demás scripts no deben considerar cambios no guardados de otros archivos.

Esto evita que el proyecto dependa de múltiples estados fantasma simultáneos.

---

## 17. Análisis de código — primera generación

La arquitectura debe preparar el análisis de código, pero no implementarlo realmente.

Debe existir una abstracción equivalente a:

```text
ICodeAnalyzer
```

o componentes equivalentes.

La primera implementación será un **No-Op Analyzer**.

Debe comportarse como:

```text
sin errores
sin warnings
sin información semántica
```

permitiendo continuar siempre.

La firma exacta puede evolucionar, pero debe poder recibir:

- contexto del proyecto (`DirectivaCode`);
- script actual;
- working content actual.

No limitar el diseño únicamente a `CheckSyntax(string code)` porque análisis futuro necesitará contexto adicional.

---

## 18. Syntax vs semantic analysis

Debe preservarse la diferencia conceptual entre:

### Syntax

Determina si el código cumple la gramática.

### Semantic analysis

Determina cosas como:

- si un símbolo existe;
- si un import resuelve;
- si hay colisiones;
- si una llamada es válida;
- warnings de shadowing.

La infraestructura general debe poder devolver diagnósticos de ambos tipos.

No es necesario implementarlos todavía.

---

## 19. Refactorización — arquitectura futura

La primera generación **no realizará refactorizaciones reales**.

Debe existir una abstracción equivalente a:

```text
IRefactorer
```

La implementación inicial será un **No-Op Refactorer** que:

- no modifica código;
- no produce errores;
- no produce warnings;
- permite continuar.

El objetivo es dejar el punto de extensión ya conectado.

---

## 20. Imports — convención prevista

La sintaxis prevista usa puntos para navegación relativa.

Regla conceptual:

```text
.   = directorio actual
..  = un nivel atrás
... = dos niveles atrás
```

Ejemplo:

```text
import ...Attacks.targetter
```

significa:

- subir dos carpetas;
- entrar a `Attacks`;
- importar `targetter`.

La implementación real de imports pertenece al lenguaje/compilación, no a esta UI.

---

## 21. Movimiento, rename y refactor futuro

En la versión final, mover o renombrar un script/carpeta puede afectar imports.

Aunque la primera implementación use un refactorer no-op, la arquitectura debe prever el flujo futuro.

### 21.1 Preguntar si refactorizar

Cuando una operación afecte imports, el sistema debe poder preguntar:

```text
Refactorizar imports
Cambiar sin refactorizar
Cancelar
```

Y ofrecer:

```text
No volver a preguntarme durante esta sesión
```

Esta preferencia debe durar solo la sesión actual.

No debe convertirse automáticamente en configuración persistente.

### 21.2 Por qué preguntar

No siempre refactorizar representa la intención del usuario.

Ejemplo:

```text
script.dscript
script_v2.dscript
```

El usuario podría:

1. renombrar `script.dscript` → `old.dscript` sin refactorizar;
2. renombrar `script_v2.dscript` → `script.dscript`;

de modo que las referencias existentes sigan apuntando al nombre lógico `script`.

---

## 22. Refactor futuro — referencias semánticas

Actualizar un import no será suficiente.

Si cambia el nombre lógico de un import, también deben cambiar todas las referencias semánticas asociadas.

Ejemplo:

```text
import .Combat

Combat.attack()
Combat.target()
x = Combat.DEFAULT_RANGE
```

No se debe usar un simple `string.Replace`.

`DirectivaCode` deberá conocer:

- qué referencias pertenecen realmente al import;
- su posición;
- su significado.

Esto evita modificar:

- comentarios;
- strings;
- variables no relacionadas;
- coincidencias accidentales.

---

## 23. Refactor futuro — transaccionalidad

Una refactorización real debe seguir:

```text
prepare
→ validate
→ commit
```

No:

```text
modify
→ discover problems later
```

Flujo futuro:

1. calcular archivos afectados;
2. generar versiones temporales en memoria;
3. analizar esas versiones;
4. comparar diagnostics antes/después;
5. si todo es aceptable, aplicar;
6. si falla, no tocar estado real.

La operación completa debe comportarse como una transacción lógica.

---

## 24. Refactor futuro — errores y warnings

### 24.1 Error nuevo

Si la refactorización introduce un error nuevo:

- cancelar por defecto;
- indicar exactamente archivo y diagnóstico;
- ofrecer opcionalmente realizar el cambio físico sin refactorizar.

### 24.2 Warning nuevo

Si introduce warnings nuevos:

- informar cuáles;
- ofrecer `Continuar` o `Cancelar`.

Warnings anteriores que ya existían:

- no deben molestar;
- no son responsabilidad de la refactorización.

### 24.3 Shadowing

Colisiones por shadowing pueden considerarse warning, no error.

Ejemplo:

- el nuevo nombre de un import oculta una variable local.

La refactorización puede continuar si el usuario acepta el warning.

---

## 25. Operaciones sin refactorizar

Si el usuario decide mover/renombrar/eliminar sin refactorizar:

- la operación física puede realizarse;
- los imports rotos deben detectarse posteriormente por análisis real;
- los scripts con errores deben aparecer rojos;
- las carpetas correspondientes deben reflejar esos errores.

En la primera generación, como el analyzer será no-op, esta detección todavía no existirá.

---

## 26. Eliminar scripts con dependencias — comportamiento futuro

Cuando `DirectivaCode` pueda conocer dependencias reales, eliminar un script importado por otros debe mostrar:

- advertencia explícita;
- lista de archivos dependientes afectados.

La eliminación sigue siendo posible si el usuario confirma.

La primera generación puede dejar esta capacidad conectada pero no funcional.

---

## 27. Botones de script visibles

Para el script activo deben existir al menos:

```text
Guardar
Revertir
```

`Revertir` siempre confirma.

En el árbol, scripts dirty pueden disponer también de accesos rápidos equivalentes a guardar/revertir, si la implementación visual lo permite de forma limpia.

Esto no debe obligar a abrir el archivo primero para resolver su estado.

---

## 28. Opciones de scripts

La zona `Opciones scripts` debe incluir inicialmente acciones básicas:

- Nuevo script;
- Nueva carpeta.

Renombrar, duplicar y eliminar pueden vivir principalmente en menú contextual.

---

## 29. Opciones generales

La zona superior de opciones generales debe contemplar:

- Atrás;
- Opciones.

`Opciones` puede existir como botón placeholder aunque todavía no haya sistema real de gráficos/sonido.

---

## 30. Separación de responsabilidades

La UI no debe conocer directamente:

- parser;
- lexer;
- compiler;
- VM;
- simulation;
- game orchestrator;
- filesystem concreto.

Debe comunicarse mediante:

- abstracciones;
- servicios;
- eventos;
- interfaces.

Ejemplo conceptual:

```text
UI
↓ event
Script/Project service
↓
Storage / DirectivaCode / Analyzer / Refactorer
```

La UI no reescribe imports ni toma decisiones semánticas.

---

## 31. Componentes conceptuales esperados

La implementación puede elegir nombres concretos distintos, pero debería existir una separación equivalente a:

### UI

- UI root;
- script explorer;
- code editor;
- output panel;
- dialogs;
- toolbar/control bar;
- localization bindings.

### Project/scripts

- script model;
- active script controller;
- dirty/backups manager;
- project tree;
- script operations service.

### Storage

- storage interface;
- desktop filesystem implementation.

### Localization

- localization service;
- locale parser/loader;
- runtime locale change event.

### Output

- output service/buffer;
- severity filtering;
- collapsed notification state.

### Language extension points

- `DirectivaCode`;
- analyzer interface;
- no-op analyzer;
- refactorer interface;
- no-op refactorer.

### Execution extension points

- events for Stop/Pause/Continue;
- breakpoint events.

---

## 32. Funcionalidades deliberadamente fuera de v1

No implementar todavía:

- syntax highlighting;
- autocomplete;
- semantic highlighting;
- syntax checker real;
- semantic checker real;
- lexer integration con UI;
- parser integration con UI;
- refactor real;
- import resolver real;
- go-to-definition;
- find references;
- rename symbol;
- code folding;
- minimap;
- multicursor;
- multiple editor tabs;
- debugger real;
- Step;
- Step In;
- Step Out;
- persisted breakpoints;
- simulación del juego;
- visualización de VM instruction;
- web persistence real;
- opciones gráficas/sonido reales.

Los puntos de extensión deben existir cuando corresponda, pero sus implementaciones pueden ser vacías/no-op.

---

## 33. Criterio de éxito de la primera generación

La primera generación se considera exitosa si permite este flujo completo:

1. Abrir Directiva.
2. Ver una carpeta persistente de scripts.
3. Crear carpetas y `.dscript`.
4. Seleccionar un script.
5. Editarlo cómodamente.
6. Ver números de línea.
7. Añadir/quitar breakpoints visuales.
8. Indentar con Tab/Shift+Tab.
9. Cambiar a otro archivo sin perder cambios.
10. Ver scripts dirty destacados.
11. Recuperar working copies desde `.dscript.back`.
12. Guardar.
13. Revertir con confirmación.
14. Renombrar.
15. Duplicar.
16. Eliminar con confirmación.
17. Mover mediante drag & drop.
18. Usar Stop/Pause/Continue mediante eventos.
19. Ver Step/Step In/Step Out desactivados.
20. Escribir y filtrar mensajes en Output.
21. Colapsar Output y recibir destellos según severidad.
22. Cambiar el idioma en runtime.
23. Mantener analyzer/refactorer conectados mediante implementaciones no-op.
24. Mantener la arquitectura lista para reemplazar esas implementaciones sin rediseñar la UI.

---

## 34. Principio rector

La primera versión debe favorecer:

- claridad;
- modularidad;
- desacoplamiento;
- comportamiento predecible;
- persistencia segura;
- extensibilidad.

No debe intentar “simular” funcionalidades de IDE que aún no existen.

Cuando un módulo todavía no esté implementado, debe existir como abstracción limpia con una implementación no-op, en vez de contaminar UI o filesystem con lógica temporal.

La intención es que la primera generación ya sea una base real de Directiva, no un prototipo desechable.
