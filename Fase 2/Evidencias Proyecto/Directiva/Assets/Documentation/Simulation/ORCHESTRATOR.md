# Orchestrator: contrato del backend (2026-11-10)

Este componente ejecuta una partida completamente en memoria; **no** construye escenas, selecciona niveles, ni decide de donde vienen los scripts de cada faccion. Esa configuracion se incorporara cuando exista el sistema de niveles.

## Arranque

1. Crear `DSimulation.Simulation` con una semilla y las dimensiones validas.
2. Encolar los dos nexos iniciales y cualquier otra entidad inicial mediante `EnqueueCreation`.
3. Compilar separadamente el codigo de cada faccion (`DScriptCompiler.Compile` con `SimulationDScriptProfile.Options`). Los fuentes enemigos pueden mantenerse fuera de la vista del jugador: el orquestador recibe **programas ya compilados**.
4. Instanciar `new Orchestrator(world, playerProgram, enemyProgram)`; esto llama `world.Start()` si fuese necesario, valida que ambos nexos existan y ejecuta los inicializadores `shared` **una vez por bando**.
5. Antes del primer `Update`, asociar cada entidad programada a su funcion de entrada: `orchestrator.BindInitialEntity(entity.id, "modulo.funcion")`. Ambas facciones se configuran de la misma forma. No se elige ninguna funcion por nombre implicito.
6. Llamar `orchestrator.Update()` para avanzar **exactamente un tick de simulacion** (50 Hz). Solo el frontend decidira a que ritmo de tiempo real solicitar esos ticks.

## Planificacion

- Valor inicial: `StepsPerTick = 10` pasos *efectivos* por VM por tick (configurable); 50 ticks de simulacion por segundo equivalen a hasta 500 pasos efectivos por segundo simulado por VM.
- Por ronda, recorrer todos los bindings vivos de ambas facciones **por ID global de entidad**, y ejecutar un unico paso efectivo de cada VM. Los metadatos de depuracion `DEBUG_*` cuestan cero.
- Despues de completar las rondas, llamar una sola vez a `Simulation.Update()` y entonces procesar construcciones y eliminaciones.
- Una VM que termina naturalmente reinicia su misma funcion de entrada en su siguiente turno, con pila y variables locales nuevas, sin perder los `shared` ni su identidad. Una VM con error queda parada y no se reinicia.
- La nueva unidad de `build(funcion)` conserva el ID de solicitud y se asocia a una nueva VM solamente **despues** de completar el Update del mundo; empieza a ejecutarse en el siguiente tick.
- Las ordenes pueden actualizar inmediatamente los estados deseados de las entidades, pero el movimiento fisico, los ataques y la progresion de construcciones avanzan solo en `Simulation.Update()`.

## Fin de partida

Una vez iniciado el mundo con ambos nexos, si uno muere gana el otro bando. Si ambos estan muertos en el mismo limite de simulacion, `Outcome == MatchOutcome.Draw`. `FinishedAtTick` identifica el limite donde termino la partida. No se ejecutan VMs ni fisica despues de `IsFinished == true`. La muerte se evalua al entrar a Update (si ocurrio externamente) y justo despues de Simulation.Update (para detectar muertes de ese tick en conjunto).

## Consola y trazabilidad

`GetOutput(sideId, entityId?, kind?)` retorna una copia del historial filtrada opcionalmente por entidad y tipo `Print` / `Warning` / `Error`. Cada `VMOutputMessage` trae `SideId`, `EntityId` (nulo para inicializacion de `shared`), `Tick`, `Sequence` global, `Text` y localizacion de fuente (archivo, linea, span). Los mensajes sobreviven la muerte y reinicio de la entidad mientras permanezcan en el buffer de capacidad configurable `outputCapacityPerSide` (4096 por defecto). `OutputEmitted` permite recibir nuevos mensajes en vivo sin consultar todo el historial.

## Pruebas

`Directiva > Tests > Simulation > Orchestrator > TestEverything`. Incluye planificacion, presupuesto, reinicio, fallos, ordenes implicitas, logging por VM/bando, construcciones, muerte de nexos y determinismo. `Directiva > Tests > TestAll` cubre las regresiones existentes.
