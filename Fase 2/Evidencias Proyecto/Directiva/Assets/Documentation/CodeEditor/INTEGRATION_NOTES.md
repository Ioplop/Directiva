# Integration notes

Este paquete está pensado para incorporarse en pasos pequeños.

La entrega está organizada por tipo de asset. Copia o fusiona su carpeta `Assets/` con la carpeta `Assets/` del proyecto.

La primera prueba recomendada, una vez importado, es exclusivamente:

1. verificar que Unity compile;
2. crear un GameObject con `UIDocument`;
3. asignar `PanelSettings`;
4. añadir `DirectivaCodeUIController`;
5. ejecutar;
6. comprobar que aparece la interfaz.

Después conviene probar por módulos:

- locales;
- creación de scripts;
- edición y dirty state;
- `.dscript.back`;
- explorer y drag/drop;
- Output;
- eventos externos.

No hace falta conectar VM, parser ni simulación todavía.
