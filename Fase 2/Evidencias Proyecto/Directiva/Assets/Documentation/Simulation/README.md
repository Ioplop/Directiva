# Directiva - Simulacion (primer mockup)

Este paquete incorpora una interfaz de simulacion y monta **la misma instancia** de
`DirectivaCodeUIController` existente dentro de un `VisualElement` del nuevo `UIDocument`.
El editor de `BasicCoding` sigue funcionando como editor independiente.

## Crear una escena

1. Copiar los archivos nuevos/modificados del diff dentro de `Assets/`.
2. En Unity, esperar a que compile y elegir **Directiva > Simulation > Create Game Scene**.
3. Se crea `Assets/Scenes/Game.unity` **solo si no existe**. El generador no toca `MainMenu.unity` ni `BasicCoding.unity`.
4. Agregar `Game.unity` y `MainMenu.unity` al Build Profile activo.
5. Seleccionar el objeto `Main Menu UI` en `MainMenu.unity` y cambiar `sceneName` de los niveles que quieras probar a `Game` (los `launchId` pueden quedar como `level_01`, etc.).
6. En el `Game` generado, `Level Bootstrapper (launch events)` ofrece seis `UnityEvent` inicialmente vacios. Puedes enlazar inicializadores propios de la escena a cada `launchId` mas adelante.

El generador crea una camara ortografica, iluminacion, un plano y marcadores 3D
estaticos usando `square_pyramid.obj`, `triangular_isosceles_prism.obj` y
`regular_octahedron.obj`, con materiales existentes. Si hace falta, crea
`Assets/Materials/Simulation_MVP_Red.mat`, que puedes editar o reemplazar.
Son **placeholders visuales**; no hay movimiento, fisica de unidades ni recursos funcionales.

## Vista del editor

- `Editor ▶`: abre el editor lateral.
- Arrastrar el borde derecho de la ventana: cambia su ancho (con limite para dejar simulacion visible).
- `⛶`: expandir a pantalla completa; `↙`: volver a lateral.
- `—`: cerrar el lateral o volver a lateral desde pantalla completa.
- Flecha `◀` / `▶` dentro del editor: ocultar/mostrar la columna Solucion.
- `‹ Menú principal`: volver a la escena `MainMenu` sin confirmacion.

Ocultar el editor solo cambia `VisualElement.style.display`: no desactiva el
`MonoBehaviour`, no reconstruye el editor, no pierde el documento y no detiene
la VM. Al cambiar de escena, como siempre, se destruye la escena anterior.
En el lateral se oculta el grupo de controles de ejecucion DIL del toolbar
interno por falta de espacio (reaparece en pantalla completa); sus servicios
siguen disponibles.

**Los botones Play, Stop, Pause, Slowmo, x2 y x5 de la barra de simulacion son
solo mockups**: cambian el estado visual y opcionalmente invocan un `UnityEvent`
configurable, pero no afectan ninguna VM ni la simulacion. No confundirlos con
los botones del editor que ya ejecutan DIL.

El texto de nivel se toma del nombre de la entrada seleccionada en el menu,
mediante `LevelLaunchContext.CurrentLevelName` y sin consumir el `launchId`.
Si abres `Game` directamente, se muestra el titulo predeterminado del Inspector.

## Input futuro: UI vs mundo 3D

`SimulationUIController` expone:

```csharp
bool uiBlocked = simulationUI.IsPointerOverUI(mouseScreenPosition);
bool textHasFocus = simulationUI.IsKeyboardCaptured();
```

`mouseScreenPosition` tiene origen abajo-izquierda como `Input.mousePosition`;
si usas Input System, puedes pasar `Mouse.current.position.ReadValue()`.
La conversion al sistema de coordenadas de UI Toolkit contempla el `PanelSettings`
y su escala. `IsPointerOverUI` consulta `panel.Pick`: el root/overlay transparente
**no bloquea** el mundo; botones, barra, editor y ventanas emergentes si.
Tambien bloquea el arrastre que comienza en la UI mientras el boton sigue presionado.

Ejemplo para usar en el **futuro**, cuando exista la logica de seleccion:

```csharp
if (!simulationUI.IsPointerOverUI(mouseScreenPosition))
{
    // Lanzar Physics.Raycast desde la camara a la simulacion.
}
```

`IsKeyboardCaptured` es true si el foco esta en un `TextField`, incluido el editor.
No hay raycasts ni control de unidades en este paquete: solo la infraestructura
para que su futura implementacion pueda respetar los limites de la UI.

## Estructura y mantenimiento

- `DirectivaCodeUIController.Mount(VisualElement)`: punto de montaje del editor.
- `SimulationUIController`: unica composicion de HUD + contenedor del editor.
- `SimulationEditorResizeHandle`: arrastre del borde lateral.
- `SimulationSceneInstaller`: generador de escena editable, solo para Unity Editor.
- `Assets/Resources/Styles/Simulation/SimulationUI.uss`: presentacion del host.

El metodo `Mount` esta protegido contra montajes duplicados. Un controlador con
su propio `UIDocument` (como `BasicCoding`) se monta automaticamente; un
controlador sin `UIDocument` espera al host. `ScriptWorkspace`, editor, consola,
explorador y metadata no se duplican entre los modos de presentacion.

Pendientes deliberados: vinculacion de los controles de simulacion, mecanismos
de seleccion 3D, scripts ligados a entidades, congelacion/instantaneas de codigo
en ejecucion para depuracion y comportamiento real de los niveles.

## Comprobacion en Unity

- Abrir `BasicCoding` y verificar que editor, consola, explorador y ejecucion DIL
  sigan trabajando como antes.
- Crear y abrir `Game`; alternar Hidden / Docked / Fullscreen y arrastrar borde.
- Contraer Solucion, editar un documento, expandir/reducir el editor y comprobar
  que el texto y el historial de edicion siguen intactos.
- Pulsar controles de simulacion y confirmar que solo cambia su aspecto.
- Probar la consulta `IsPointerOverUI` sobre la escena, los controles y el editor.
- Ir desde MainMenu a Game y comprobar el titulo actual y el boton de regreso.

No se incluyeron cambios en escenas existentes; la nueva escena se genera dentro
 de tu Unity Editor, donde puedes editar posicion, materiales y objetos a gusto.
