# DScript — autocompletado semántico (Directiva)

El editor propone nombres según el archivo activo, el punto de escritura y las
referencias conocidas. Las sugerencias son **asistencia de edición**, no una
comprobación de tipos en tiempo de ejecución.

## Uso

- Escribe un identificador o `objeto.` para abrir sugerencias tras una breve pausa.
- `Arriba` / `Abajo`: cambia la selección.
- `Enter`: acepta el nombre elegido.
- `Espacio`: acepta el nombre elegido y agrega un espacio.
- `Escape`: cierra el menú hasta el próximo cambio de texto.
- Los clics sobre una sugerencia también la insertan. Los cambios usan el
  historial habitual de Undo/Redo del editor.

El análisis utiliza el texto **sin guardar** del archivo activo; el proveedor
de la carpeta de scripts también consulta las copias de trabajo de los otros
módulos. Las sugerencias no ejecutan código DScript ni la VM.

## Sintaxis opcional de tipos

```python
def escoger_objetivo(candidato: SimulationObject, prioridad: Int) -> SimulationObject:
    return candidato

def primero() -> List:
    return list()
```

Se pueden anotar parámetros y retornos, de forma completamente opcional.
Tipos reconocidos para sugerencias: `Int`, `Decimal`, `Bool`, `String`,
`List`, `Set`, `Tuple`, `Dict`, `Range`, `SimulationObject` y `None`.
Las anotaciones se almacenan en el AST, pero **no se emiten a DIL** ni alteran
el tipado dinámico, el coste de instrucciones o el resultado de la VM.
En esta versión no se realizan comprobaciones de tipo en la compilación.

## Capacidades

- Palabras clave, símbolos locales, parámetros, `shared`, nombres de funciones
  del usuario, imports, módulos nativos y enumeraciones de la simulación.
- Completa métodos y propiedades conocidos de `self` y otros
  `SimulationObject`, incluidas observaciones y órdenes según el catálogo
  existente de la simulación.
- Ofrece miembros de `List`, `Set`, `Dict`, `String`, `Tuple` y `Range` a
  partir de los atributos de reflexión existentes.
- Propaga tipos conocidos a variables en asignaciones, llamadas y funciones
  del usuario; los retornos sencillos se infieren, y los complejos pueden
  llevar una anotación explícita.
- Tolera fragmentos incompletos y no ofrece sugerencias dentro de comentarios
  ni cadenas de texto.

## Límites conocidos

- La inferencia es conservadora. Si distintos caminos retornan tipos
  incompatibles o un valor es dinámico, no muestra miembros específicos.
- No se infieren tipos de elementos extraídos de contenedores dinámicos,
  ni se verifica que una anotación coincida con los valores retornados.
- Los tipos de `self` y de las operaciones del catálogo reflejan el tipo
  general de entidad; las sugerencias pueden incluir miembros que no aplican
  a un tipo específico de entidad en una partida concreta.
- La información puede ser incompleta mientras un módulo tiene sintaxis
  inválida. El autocompletado no sustituye a la compilación y sus errores.

Pruebas: `DScript > Completion` en el menú de pruebas de Directiva.
