# Main Menu - primera implementacion

## Instalacion
1. Copia la carpeta `Assets/` del ZIP diferencial encima del `Assets/` de tu proyecto.
2. Espera a que Unity importe los scripts y estilos.
3. Ejecuta `Directiva > Main Menu > Create MainMenu Scene` desde la barra superior de Unity. Este comando crea `Assets/Scenes/MainMenu.unity` con un `UIDocument`, el `PanelSettings` existente y el controlador. No reemplaza escenas existentes.
4. Agrega `MainMenu.unity` y las escenas destino a la lista de escenas del Build Profile activo (o Build Settings).
5. Abre MainMenu y ejecuta Play.

## Inspector
En `Main Menu UI > MainMenuController > Levels`, usa `+`, `-` y reordena entradas. Cada entrada contiene:
- `Name`: texto del boton.
- `Stars`: cantidad de estrellas (0 a 3).
- `Locked`: desactiva el boton.
- `Scene Name`: nombre de escena registrada en el Build Profile.
- `Launch Id`: identificador especifico de nivel, aunque muchos niveles usen la misma escena.
- `On Before Load`: UnityEvent que se ejecuta en el menu antes del cambio de escena. NO debe referenciar objetos de la escena destino.

## Eventos en la escena destino
1. Crea un GameObject (por ejemplo `Level Bootstrapper`) en la escena destino.
2. Agrega el componente `LevelBootstrapper`.
3. Configura la lista `Launch Events`: asigna cada `Launch Id` y su evento `On Load`, que podra llamar metodos de objetos de ESA escena.
4. Si no existe un ID coincidente, el sistema muestra un warning en consola (no ejecuta nivel ficticio).

`LevelLaunchContext` almacena un ID solo hasta que el `LevelBootstrapper` lo consume, sin necesidad de que la escena del menu persista. El evento de la escena destino se ejecuta en `Start`, una vez que los `Awake` y `OnEnable` de la escena ya han ocurrido. Si el nivel necesita esperar alguna inicializacion asincrona, esa coordinacion debe manejarla su controlador cuando exista.

## Alcance
- Menus de inicio, opciones y niveles; navegacion y boton salir funcionales.
- Opciones visuales temporales (sliders solo cambian su cifra en pantalla, no guardan ni aplican cambios).
- 6 entradas de ejemplo, editables en Inspector; niveles bloqueados no se pueden clicar.
- Carga de escenas reales por nombre y transferencia de `Launch Id`.
- Sin implementacion de niveles, progreso, desbloqueo ni preferencias persistentes.
- No toca los componentes de editor, VM ni escenas existentes.

## Nota de estilo
La nueva UI usa el mismo PanelSettings del editor, pero su propia hoja USS. Se genera enteramente con UI Toolkit C# para que las filas de nivel sean dinamicas y la integracion de escena requiera un solo comando. El aspecto es oscuro, siguiendo la interfaz actual del editor, sin intentar reproducir literalmente el estilo dibujado a mano de los mockups.

## Fondo 3D, transparencia y eventos del menu (v2)

Este parche actualiza **solo** `MainMenuController.cs`, `MainMenu.uss` y este README. No recrea ni altera la escena, las camaras, la iluminacion ni los niveles. Puedes mantener tu escena 3D y tu objeto `Main Menu UI` existentes.

### Hacer visible la escena 3D

El color opaco anterior estaba en `.dm-root` (toda la pantalla) y `.dm-frame` (el rectangulo grande). `MainMenu.uss` es una hoja de estilos **USS**, no un archivo JSON.

Ahora `Main Menu UI > MainMenuController > Appearance` permite editar los colores con canal alfa:

- `Screen Overlay`: tinte de pantalla completa. Por defecto **alfa 0** (transparente).
- `Frame Background`: fondo del marco. Por defecto semitransparente; alfa 0 para eliminar totalmente el tinte.
- `Frame Border`: bordes del marco, tambien con alfa.
- `Button Background`, `Button Hover`, `Button Pressed`, `Active Tab Background`, `Disabled Button Background`: colores de botones, incluidos sus estados de interaccion.

Alfa 0 = transparente; alfa 1 = opaco (en la escala flotante de Unity). Para ver la escena 3D completamente en el fondo, usa `Screen Overlay` con alfa 0 y `Frame Background` con alfa 0. Los botones pueden conservar un fondo mas opaco para mejorar la legibilidad.

Estas propiedades del Inspector tienen prioridad sobre los colores de `MainMenu.uss`; el USS sigue definiendo tamanos, bordes, margenes, tipografia y otros detalles de estilo. Las modificaciones del Inspector se aplican al construirse la UI (al iniciar/reabrir/rehabilitar el componente), no necesariamente en caliente mientras el menu esta ejecutandose.

El `Assets/UI/CodeEditor.asset` que se reutiliza como `PanelSettings` ya tenia `m_ClearColor: 0`, por lo que no fue necesario modificarlo. Asegurate de que tu camara 3D este activada, renderice en el display correcto y no haya otros paneles/fondos opacos superpuestos.

### Eventos configurables para TODOS los botones fijos

En `Main Menu UI > MainMenuController > Buttons`, despliega la entrada del boton y en `On Click` usa `+` para enlazar un GameObject de la escena y un metodo `public void` de su componente. Tambien se permiten metodos publicos con parametros compatibles con UnityEvent desde el selector habitual del Inspector.

Botones disponibles:

- `Play`: Jugar.
- `Options`: Opciones.
- `Exit`: Salir.
- `Back From Options` y `Back From Levels`: los dos botones Atrás.
- `General Tab`, `Controls Tab`, `Audio Tab`, `Graphics Tab`: las cuatro pestañas.

`On Click` se ejecuta **antes** de la accion normal de ese boton. `Execute Default Action` viene activado: Jugar cambia a Niveles, Opciones cambia a Opciones, las pestañas seleccionan su apartado y Salir cierra la aplicacion. Puedes desmarcarlo individualmente para controlar el cambio de pantalla por tu cuenta (por ejemplo, esperar a que la camara termine su animacion antes de mostrar la siguiente pantalla).

Metodos publicos del `MainMenuController` que puedes ejecutar desde tus scripts, animaciones o UnityEvents:

- `ShowHomeScreen()`
- `ShowOptionsScreen()`
- `ShowLevelsScreen()`
- `QuitApplication()`

Ejemplo: conecta `Play > On Click` a `CameraAnimator.MoveToLevels()` (tu metodo personalizado). Si solo quieres que la camara comience a moverse y el menu cambie inmediatamente, deja `Execute Default Action` activado. Si prefieres esperar a que finalice la animacion, desmarcalo y llama a `ShowLevelsScreen()` cuando termine.

**Precaucion con Salir**: si necesitas una animacion antes de cerrar la aplicacion, desmarca `Execute Default Action` en `Exit`. Cuando finalice, llama a `QuitApplication()`.

Los niveles siguen teniendo su propio `On Before Load` dentro de `Levels`, independiente de estos eventos fijos. No se cambia `LevelBootstrapper` ni el mecanismo `LaunchId`.

### Instalacion de esta actualizacion

1. Copia los tres archivos de este ZIP sobre los mismos paths de tu proyecto, conservando sus `.meta` existentes.
2. Deja intacta tu escena 3D `MainMenu.unity` y todos sus GameObjects.
3. Espera a que Unity recompile y abre `Main Menu UI > MainMenuController`.
4. Configura la paleta `Appearance` y los eventos `Buttons` desde el Inspector.
5. Pulsa Play para comprobar transparencia y llamadas a tus metodos. No es necesario volver a ejecutar el instalador de escenas.

Esta modificacion fue preparada contra el codigo de la primera entrega. Se hizo inspeccion estatica de los archivos; no se pudo probar en el Editor de Unity desde este entorno.

## Etiquetas de los mockups (v3)

Las antiguas etiquetas exteriores "Menu principal", "Opciones" y "Niveles" se eliminaron: eran anotaciones de los bocetos, no elementos de la interfaz final. Se conserva el titulo grande DIRECTIVA y el encabezado interior de seleccion de niveles.

## Textos configurables y nuevas opciones visuales (v4)

**Instalacion sobre una escena existente:** reemplaza los archivos del ZIP diferencial en las mismas rutas dentro de `Assets/`. No vuelvas a ejecutar el instalador; el objeto Main Menu UI, los eventos que ya configuraste, tus camaras y la escena 3D se conservan.

### Inspector

- En `MainMenuController > Buttons`, cada boton fijo incluye ahora un campo **Label** para renombrarlo sin editar codigo: Jugar, Opciones, Salir, los dos Atrás, General, Controles, Audio, Graficos y Pantalla completa. Si el campo esta vacio se utiliza el texto original. Los `On Click` y `Execute Default Action` anteriores siguen funcionando.
- Los **botones de niveles** siguen usando `Levels > [entrada] > Name`, igual que antes.
- En `Presentation > Levels Title` puedes cambiar el titulo central de la pantalla de niveles. Atrás y el contador de estrellas se posicionan independientemente del titulo; ya no desplazan su centro.
- En `Placeholder Settings (visual only)`, `Language Setting Label` y `Resolution Setting Label` personalizan los encabezados **Idioma** y **Resolucion**. `Resolution Choices` permite editar los valores provisionales del desplegable de resoluciones.

### Opciones

- Al entrar en Opciones se muestra siempre **General**.
- **General** tiene el desplegable **Idioma** con **Español** e **Inglés** (Español preseleccionado).
- **Audio** conserva los cinco sliders temporales.
- **Graficos** tiene un boton **Pantalla completa: Si/No** y el desplegable **Resolucion** con 1280x720, 1600x900, 1920x1080, 2560x1440 y 3840x2160 como ejemplos. El boton alterna su apariencia y su texto; tambien expone `On Click` como los demas.
- **Controles** conserva el placeholder anterior.

**Nada de esto aplica o guarda preferencias reales.** No se llama a `Screen.fullScreen`, `Screen.SetResolution`, ni se modifica el idioma ni archivos de configuracion. Las selecciones de los desplegables se mantienen solo temporalmente mientras exista el controlador y se reconstruyan las pestañas.

**Compatibilidad:** no se modifican escenas, `LevelMenuEntry`, el sistema de `LaunchId`, ni los eventos ya asociados a los botones. Los nuevos campos de texto tienen valores de reserva para que una escena serializada antes de esta actualizacion siga mostrando los nombres originales.

## Iconos reemplazables en la seleccion de niveles (v5)

En `Main Menu UI > MainMenuController > Level Icons (optional sprites)` hay tres referencias opcionales a `Sprite`:

- `Earned Star Sprite`: icono para cada estrella obtenida y para el contador total de la esquina superior derecha.
- `Unearned Star Sprite`: icono para las estrellas que faltan por obtener.
- `Locked Level Sprite`: icono para los niveles bloqueados.

Cada campo tiene **fallback individual**: si queda en `None`, se utiliza el caracter original correspondiente (estrella llena `U+2605`, estrella vacia `U+2606` o emoji de candado `U+1F512`). Sin asignar sprites se mantiene exactamente la presentacion anterior.

Puedes sustituir las estrellas por cualquier otro recurso o simbolo: la logica de puntuaciones y desbloqueo no cambia. Si usas imagenes PNG, importalas en Unity como `Texture Type: Sprite (2D and UI)` y arrastralas a los campos del Inspector. Las imagenes se presentan conservando su aspecto (`ScaleToFit`) y sus colores originales.

Los tamanos por defecto se definen en `MainMenu.uss`: `.dm-score-sprite` (24 x 24 px) y `.dm-total-star-icon` (27 x 27 px); modificalos si tus nuevos sprites necesitan otra escala. Los cambios hechos en los campos de sprites se ven al reconstruir la UI (por ejemplo, al volver a ejecutar Play), tal como ocurre con el resto de la configuracion visual.

**Instalacion v5:** copia los tres archivos modificados de este ZIP sobre sus rutas dentro de `Assets/`. No necesitas recrear la escena ni volver a asignar UnityEvents o niveles. No se han modificado `LevelMenuEntry`, las escenas ni los archivos `.meta`.

## Opciones unificadas y requisitos de estrellas (v6)

Este parche se aplica directamente sobre la escena existente: reemplaza `MainMenuController.cs`, `LevelMenuEntry.cs`, `MainMenu.uss` y este README. **No incluye cambios a `MainMenu.unity` ni archivos `.meta`.** Los objetos 3D, referencias a la camara y UnityEvents del menu se conservan. No ejecutes el instalador de escenas de nuevo.

### Una sola pagina de opciones

- No hay pestanas: General (Idioma), Controles (texto provisional), Audio (cinco controles de volumen) y Graficos (pantalla completa y resolucion) aparecen **en una unica columna de secciones**.
- El boton Atrás permanece fijo y ejecuta el mismo UnityEvent de siempre. El resto esta dentro de un `ScrollView` vertical con scrollbar automatico y desplazamiento con la rueda del mouse. El contenido parte arriba y se reinicia al principio al abrir Opciones.
- Se conservan los textos personalizados de los antiguos botones de pestana como nombres de secciones. Las acciones de esas pestanas desaparecidas se conservan serializadas por compatibilidad, pero ya **no se ejecutan**, puesto que no existen botones. En `MainMenuController > Option Section Titles` se pueden cambiar los cuatro titulos independientemente (vacio = nombre antiguo).
- Todas las opciones siguen siendo **solo visuales**, sin modificar configuraciones reales ni guardarse.

### Niveles bloqueados: coste de desbloqueo

- `Levels > [entrada] > Required Stars`: numero de estrellas que muestra un nivel bloqueado. Si es **0**, usa un valor sugerido de `indice_en_lista * Max Stars`: los niveles 4, 5 y 6 originales mostraran 9, 12 y 15, respectivamente. Para fijar un valor independiente del orden, escribe un numero mayor que cero.
- `Level Icons > Required Stars Sprite`: icono **independiente** para el requisito de desbloqueo; si es `None`, muestra el caracter `★`. Puedes asignarle el mismo sprite que `Earned Star Sprite` si quieres.
- Los botones bloqueados presentan `numero + icono` en lugar del candado. `Locked` continua determinando manualmente si el boton puede pulsarse; el requisito de estrellas no desbloquea niveles automaticamente. Los niveles desbloqueados siguen mostrando estrellas obtenidas y pendientes, y el contador superior conserva su comportamiento.
- El antiguo `Locked Level Sprite` se conserva internamente solo para no perder referencias serializadas, pero ya no se muestra en el Inspector ni se usa para dibujar niveles.

La instalacion no requiere reconstruir escenas y no cambia el intercambio de `LaunchId` ni los eventos de carga de nivel.
