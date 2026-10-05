# Directiva Code UI v1

Primera implementación de la interfaz de programación de Directiva basada en Unity UI Toolkit.

Esta entrega implementa la infraestructura descrita en `code_ui_spec_v1.md`:

- explorador de scripts y carpetas reales;
- `.dscript` y `.dscript.back`;
- dirty state con SHA-256;
- guardado, revertir, crear, renombrar, duplicar, eliminar y mover;
- drag & drop entre carpetas;
- editor multilínea con números de línea;
- breakpoints visuales;
- indentación Tab / Shift+Tab con tab stops de 4 espacios;
- Output colapsable, filtrable y con buffer limitado;
- localización por componente y cambio de locale en runtime;
- eventos desacoplados para acciones de UI;
- `DirectivaCode`, analyzer y refactorer conectados mediante implementaciones no-op;
- Stop / Pause / Continue;
- Step / Step In / Step Out visibles pero desactivados.

## Requisitos

- Unity 6.x
- UI Toolkit runtime
- Un `UIDocument` con un `PanelSettings` válido

## Instalación mínima

Copia o fusiona la carpeta `Assets/` de esta entrega con la carpeta `Assets/` de tu proyecto.

Después:

1. Crea un GameObject vacío.
2. Añade `UIDocument`.
3. Asigna un `PanelSettings`.
4. Añade `DirectivaCodeUIController` al mismo GameObject.
5. Ejecuta la escena.

No hace falta crear UXML. La interfaz se construye por código y carga su USS y locales desde `Resources`.

## Directorio de scripts

En escritorio se usa:

`Application.persistentDataPath/DirectivaScripts`

## API externa útil

Desde otros sistemas puedes acceder al controller y usar:

- `controller.Events`
- `controller.Output`
- `controller.Localization`
- `controller.Codebase`
- `controller.SetLocale("es")`
- `controller.SetLocale("en")`

Ejemplo conceptual para conectar una VM futura:

```csharp
controller.Events.ContinueRequested += () => vm.Continue();
controller.Events.PauseRequested += () => vm.Pause();
controller.Events.StopRequested += () => vm.Stop();

controller.Events.BreakpointAdded += (path, line) => debugger.AddBreakpoint(path, line);
controller.Events.BreakpointRemoved += (path, line) => debugger.RemoveBreakpoint(path, line);
```

## Alcance deliberadamente no implementado

- parser/lexer real;
- syntax highlighting;
- semantic analysis;
- import resolution;
- refactorización real;
- debugger real;
- Step / Step In / Step Out;
- persistencia WebGL;
- simulación.

Las interfaces y puntos de extensión ya existen para conectar esas piezas después.


## Fuentes del editor

`DirectivaCodeUIController` expone dos referencias en el Inspector:

- `Normal Font`
- `Bold Font`

Ambas esperan `UnityEngine.TextCore.Text.FontAsset`.

Para v1.7:

- `Normal Font` se utiliza para el editor, números de línea y breakpoints.
- `Bold Font` queda conectado/reservado para futuras capas de syntax highlighting y texto enfatizado.

Una vez asignada la fuente monoespaciada, el editor mide su ancho de carácter y altura de línea en runtime para alinear las guías de indentación y el gutter.


## Ajustes del editor (v1.8)

`DirectivaCodeUIController` expone en el Inspector:

- `Editor Font Size`: tamaño inicial de la fuente del editor.
- `Indent Guide Offset Columns`: corrección horizontal de las guías, medida en columnas de la fuente monoespaciada.

El offset está expresado en columnas, no píxeles, para que conserve la misma relación visual al hacer zoom.

En runtime:

- `Ctrl + rueda arriba`: aumenta 1 px el tamaño de fuente.
- `Ctrl + rueda abajo`: reduce 1 px el tamaño de fuente.
- rango de zoom: 6–48 px.
- el valor runtime de `Editor Font Size` se refleja en el Inspector durante Play Mode.

Los cambios realizados durante Play Mode siguen las reglas normales de Unity y no persisten automáticamente al salir de Play Mode.


## Modelo de coordenadas del editor (v1.9)

Los ajustes visuales del código viven en `DirectivaCodeUIController`:

- `Editor Font Size`
- `Editor Padding Left Px`
- `Editor Padding Top Px`
- `Editor Padding Right Px`
- `Editor Padding Bottom Px`
- `Indent Guide Fine Offset Px`

Los cuatro valores de padding son el **padding real** aplicado al input de UI Toolkit. Las guías usan exactamente esos mismos valores; no existe un segundo padding oculto del sistema Directiva.

`Indent Guide Fine Offset Px` es una corrección opcional fija en píxeles y **no escala con Ctrl+rueda**. El valor normal es `0`.

Al hacer zoom sólo cambian las métricas tipográficas medidas desde el `FontAsset` efectivo: ancho de carácter y altura de línea. El padding y el fine offset permanecen fijos en píxeles.


## Métricas tipográficas (v1.10)

El editor ya no estima el ancho de columna usando una cadena de prueba como ruta principal.

Con `Normal Font` asignado:

- ancho de columna = `horizontalAdvance` del glifo espacio (`U+0020`)
- altura de línea = `FontAsset.faceInfo.lineHeight`
- ambas métricas se escalan automáticamente al `Editor Font Size`

Por lo tanto, cambiar el `FontAsset` recalcula automáticamente el espaciado de
indentación. La medición mediante UI Toolkit existe sólo como fallback de seguridad.


## Zoom independiente de paneles (v1.11)

`DirectivaCodeUIController` expone tres tamaños independientes:

- `Editor Font Size`
- `Explorer Font Size`
- `Output Font Size`

Durante runtime, `Ctrl + rueda` aplica zoom sólo al panel bajo el cursor:

- editor -> editor
- explorador -> explorador
- Output -> Output

Cada panel usa un rango de 6 a 48 px con pasos de 1 px.


## Tooltips de ayuda (v1.13)

`DirectivaCodeUIController` expone `Tooltip Delay Seconds`.

Los controles representados principalmente por símbolos muestran una ayuda localizada
cuando el puntero permanece encima durante ese tiempo. El tooltip se recoloca
automáticamente para mantenerse completamente dentro de la UI, incluso cerca de
los bordes de la pantalla.
