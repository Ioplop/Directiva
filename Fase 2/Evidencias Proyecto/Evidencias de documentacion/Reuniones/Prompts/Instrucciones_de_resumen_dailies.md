# Formato estándar para resúmenes de Dailies - Proyecto Directiva

Este documento define la estructura y los criterios que deben utilizarse al transformar una transcripción de una reunión Daily del proyecto **Directiva** en un resumen formal.

El objetivo es que todas las reuniones queden documentadas con un formato consistente, breve y útil como evidencia del proyecto, sin agregar información que no esté respaldada por la transcripción.

---

## 1. Archivos de salida

Cuando se solicite resumir una Daily, generar normalmente dos archivos:

- Un archivo Markdown (`.md`).
- Un archivo PDF (`.pdf`).

Ambos deben contener **exactamente la misma información y redacción**. El PDF puede adaptar únicamente aspectos visuales como tipografía, espaciado, saltos de página y jerarquía gráfica.

### Convención de nombres

Si la reunión está identificada, por ejemplo, como `S05-D2`, utilizar:

- `S05-D2.md`
- `S05-D2.pdf`

---

## 2. Encabezado

El título debe expresar la semana y el número de Daily en lenguaje natural.

Ejemplo:

```md
# Semana 5, Daily 2
```

No utilizar `Daily S05-D2` como título visible.

Inmediatamente debajo deben aparecer los siguientes datos:

```md
**Proyecto:** Directiva  
**Participantes:** Álvaro Castro y Angel Rojas  
**Fecha:** 11 de septiembre de 2026  
**Duración aproximada:** 8 min 51 s
```

### Reglas del encabezado

- **Proyecto:** utilizar `Directiva`.
- **Participantes:** indicar las personas que efectivamente participaron de la reunión.
- El nombre **Angel** debe escribirse siempre **sin tilde**.
- **Fecha:** corresponde al día en que se está procesando/documentando la Daily. Debe escribirse en formato largo en español, por ejemplo: `11 de septiembre de 2026`.
- **Duración aproximada:** obtenerla de la duración de la transcripción cuando sea posible. Puede expresarse en minutos y segundos.

---

## 3. Resumen general

Utilizar el encabezado:

```md
## Resumen general
```

Esta sección debe entregar una visión breve de los **avances, cambios o decisiones relevantes de esa Daily en particular**.

### Incluir

- Avances principales desde la Daily anterior.
- Tareas que hayan terminado o cambiado significativamente de estado.
- Inicio de trabajos importantes.
- Decisiones relevantes que ayuden a entender el estado actual del sprint.

### Evitar

No explicar actividades que son inherentes a una Daily y, por tanto, resultan redundantes.

Por ejemplo, evitar frases como:

> Se revisó el estado y el porcentaje de avance de las tareas del sprint.

Revisar avances, porcentajes y tareas es parte normal de la reunión y no constituye por sí mismo un hallazgo relevante.

El resumen debe centrarse en **qué cambió o qué ocurrió**, no en describir el ritual de la reunión.

---

## 4. Estado de tareas por participante

Crear una sección independiente para cada participante que tenga tareas o avances relevantes.

Para el equipo actual:

```md
## Estado de tareas - Angel
```

```md
## Estado de tareas - Álvaro
```

Dentro de cada sección, utilizar una lista de puntos.

Ejemplo:

```md
- **Arquitectura de software:** avance de **99%**. La tarea está esencialmente completada; solo falta subir el trabajo correspondiente.
- **Diseño e implementación del lenguaje:** comenzó en este periodo y quedó marcada como en curso. Su fecha de finalización se mantiene para el **18/09**.
```

### Qué registrar para una tarea

Cuando la información esté disponible en la transcripción, incluir:

- Nombre de la tarea.
- Estado actual.
- Porcentaje de avance informado.
- Cambio respecto del estado anterior, si es relevante.
- Fecha objetivo o fecha de término mencionada.
- Breve contexto necesario para entender el avance.

No es necesario que todos estos elementos aparezcan en todas las tareas.

### Porcentajes

Conservar el porcentaje informado durante la reunión.

No reemplazar automáticamente un porcentaje cercano al 100% por `100%`. Por ejemplo, si una tarea se informa al **99%** porque el trabajo está terminado pero todavía falta subir el archivo, debe quedar registrada al **99%** y explicar brevemente qué falta.

### Información faltante o ambigua

No inventar porcentajes, fechas, tareas, estados ni responsables.

Si la transcripción omite una respuesta o contiene un segmento incompleto:

- utilizar únicamente aquello que pueda sostenerse con claridad;
- omitir el dato específico si no es recuperable;
- o redactar de forma prudente indicando solo el estado que sí puede determinarse.

La prioridad es que el resumen sea fiel a la reunión, no completar artificialmente todos los campos.

---

## 5. Bloqueos y dependencias

Cuando existan bloqueos o dependencias relevantes, incluir:

```md
## Bloqueos y dependencias
```

Esta sección sirve para registrar tanto impedimentos directos como relaciones entre tareas que condicionan el trabajo futuro.

### Bloqueos

Un bloqueo es algo que impide o dificulta continuar una tarea.

Ejemplos:

- Falta de acceso a una herramienta.
- Espera de una decisión necesaria.
- Problema técnico que impide avanzar.
- Falta de un recurso indispensable.

### Dependencias

Una dependencia no necesariamente bloquea el trabajo de inmediato, pero una tarea requiere el avance o finalización de otra.

Ejemplo del proyecto:

```md
- La preparación de las pruebas a cargo de Álvaro depende del avance de Angel en el **diseño e implementación del lenguaje**, ya que necesita contar con una base suficientemente desarrollada para definir y ejecutar dichas pruebas.
```

Si durante la reunión se discuten dependencias claras, deben registrarse incluso si nadie utiliza explícitamente la palabra “dependencia”.

Si no existe ningún bloqueo ni dependencia relevante, esta sección puede omitirse. Si resulta útil dejar constancia explícita, puede indicarse que no se reportaron bloqueos directos, pero no debe agregarse una sección vacía únicamente por cumplir la plantilla.

---

## 6. Acuerdos y organización

Utilizar:

```md
## Acuerdos y organización
```

Esta sección reúne decisiones compartidas o cambios organizativos que no pertenecen exclusivamente al estado de una tarea individual.

Puede incluir, por ejemplo:

- Decisiones tomadas por el equipo.
- Reasignación de responsabilidades.
- Cambios acordados en fechas o planificación.
- Convenciones para almacenar documentación o evidencias.
- Definiciones de nombres, herramientas o formas de trabajo.
- Ajustes relevantes realizados al tablero durante la conversación, cuando representen una decisión y no simplemente la revisión rutinaria de una Daily.

Evitar repetir información que ya esté suficientemente explicada en el estado de tareas, salvo que represente además un acuerdo entre ambos participantes.

---

## 7. Próximos pasos

Utilizar:

```md
## Próximos pasos
```

Esta sección debe cerrar el documento dejando claro qué trabajo continúa después de la reunión.

Preferentemente indicar el responsable al comienzo de cada punto.

Ejemplo:

```md
- Angel: continuar con el **diseño e implementación del lenguaje**, con fecha objetivo de término el **18/09**.
- Álvaro: continuar con el **plan de pruebas**.
- Álvaro: comenzar la creación de los **documentos asociados a Scrum**.
```

Incluir solamente acciones que hayan quedado explícitas o razonablemente determinadas por la conversación.

No transformar comentarios casuales o posibilidades hipotéticas en compromisos definitivos.

---

## 8. Criterios generales de redacción

El resumen debe ser breve, profesional y descriptivo.

### Fidelidad a la transcripción

- No inventar información ausente.
- No asumir porcentajes o fechas que no hayan sido informados.
- No atribuir una decisión a una persona si la conversación no permite determinarlo.
- Si una frase de la transcripción parece contener un error evidente de reconocimiento de voz, puede normalizarse únicamente cuando el significado correcto sea inequívoco por contexto.

### Nivel de detalle

- Resumir la conversación; no transcribirla nuevamente.
- Conservar números, porcentajes, fechas, dependencias y decisiones relevantes.
- Omitir saludos, pausas, repeticiones, correcciones verbales y conversación incidental sin impacto en el proyecto.
- No describir como avance el mero hecho de revisar el tablero o realizar la Daily.

### Terminología

- Mantener los nombres reales de tareas utilizados por el equipo cuando puedan identificarse.
- Utilizar **Angel** sin tilde.
- Utilizar **Álvaro** con tilde.
- Mantener nombres técnicos como Unity, Scrum, Capstone, lenguaje, arquitectura, etc., según corresponda.

### Fechas dentro de las tareas

Para fechas objetivo o de término mencionadas en la reunión puede utilizarse el formato breve:

```text
18/09
```

La fecha del encabezado debe mantenerse en formato largo.

---

## 9. Orden estándar del documento

El orden preferido es:

1. Título: `Semana X, Daily Y`.
2. Proyecto, participantes, fecha y duración aproximada.
3. `Resumen general`.
4. `Estado de tareas - Angel`.
5. `Estado de tareas - Álvaro`.
6. `Bloqueos y dependencias`, cuando aplique.
7. `Acuerdos y organización`.
8. `Próximos pasos`.

Si en el futuro participan otras personas, agregar sus respectivas secciones de estado de tareas manteniendo la misma lógica.

---

## 10. Plantilla base

```md
# Semana X, Daily Y

**Proyecto:** Directiva  
**Participantes:** [Participantes]  
**Fecha:** [día de mes de año]  
**Duración aproximada:** [duración]

## Resumen general

[Resumen breve de los avances, cambios y decisiones más importantes de esta reunión.]

## Estado de tareas - Angel

- **[Tarea]:** [estado, porcentaje, avance, fecha o contexto relevante].

## Estado de tareas - Álvaro

- **[Tarea]:** [estado, porcentaje, avance, fecha o contexto relevante].

## Bloqueos y dependencias

- [Bloqueo o dependencia relevante.]

## Acuerdos y organización

- [Acuerdo o decisión compartida.]

## Próximos pasos

- Angel: [acción siguiente].
- Álvaro: [acción siguiente].
```

La sección `Bloqueos y dependencias` debe eliminarse de la plantilla final cuando realmente no exista nada relevante que registrar.

---

## 11. Validación antes de entregar

Antes de generar los archivos finales, comprobar:

- Que semana y número de Daily sean correctos.
- Que la fecha corresponda al día actual.
- Que los participantes estén correctamente escritos.
- Que `Angel` aparezca sin tilde.
- Que los porcentajes coincidan con los informados.
- Que no se hayan inventado datos ausentes de la transcripción.
- Que las dependencias importantes estén registradas.
- Que los acuerdos no se confundan con simples actualizaciones rutinarias.
- Que los próximos pasos tengan responsables claros cuando sea posible.
- Que el `.md` y el `.pdf` contengan exactamente la misma información y redacción.

