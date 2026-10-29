# Validator — revisar los mods sin jugar

El Validator lee cada mod del juego en marcha — manifiestos, archivos de datos, escenarios, parches de escenas y fuentes C# — e informa de lo que está roto o es sospechoso: una referencia a un objeto, escena o fondo que nadie declara, un comando de escenario desconocido, un parámetro incorrecto, una dependencia ausente. Se ejecuta en segundo plano cada vez que arranca un juego y escribe un informe, así que una errata se descubre antes de que un jugador llegue a la línea que la contiene.

Comprueba lo que se ve en los archivos. No juega la partida: un escenario que el Validator acepta todavía puede comportarse distinto de lo que el autor quería, y el código que construye id al vuelo no se puede seguir.

## Un error y su corrección

Una línea de escenario que da al jugador carne de lobo, con una errata en el id del objeto:

```scenario
scene: home_kitchen
priority: 0
---
action item.add id=food/wolf_meet count=1
end
```

Tras el siguiente arranque de Cthangover, el informe contiene:

```json
{
  "severity": "error",
  "code": "REF_NOT_FOUND",
  "message": "item 'food/wolf_meet' is referenced but never declared",
  "mod": "cthangover_game",
  "file": "scenarios/doc_validator_demo.scenario",
  "line": 4,
  "suggestion": "declare item 'food/wolf_meet' or fix the reference",
  "related": null
}
```

El mod, el archivo y la línea señalan el error. Con `food/wolf_meat` el problema desaparece y el contador de errores del resumen baja en uno (de 18 a 17 en esta pasada).

## Cómo se ejecuta y cómo leer el informe

- Arranca solo al entrar en un juego que lo incluye (Cthangover lo incluye con sus herramientas de desarrollo), cuando los mods ya están cargados, y corre en un hilo de fondo; no bloquea el juego.
- El registro recibe una línea por pasada: `validator: started, 33 mod(s) queued`, y después los totales de errores, avisos e informaciones.
- El informe se escribe en `user://validation/report.json` (en la carpeta de datos de usuario del juego). Contiene un resumen, los mods comprobados con sus versiones y sumas de comprobación, y la lista de incidencias.

Cada incidencia tiene:

- `severity` — `error` (algo está roto), `warning` (probablemente un fallo) o `info` (merece una mirada, por ejemplo un objeto al que nada remite);
- `code` — el tipo de problema, por ejemplo `REF_NOT_FOUND`, `JSON_PARSE_ERROR`, `SCHEMA_MISSING_FIELD`, `SCHEMA_UNKNOWN_PARAM`, `DECL_UNUSED`, `DYNAMIC_KEY`;
- `mod`, `file`, `line` — dónde corregirlo; `suggestion` — qué hacer.

Las informaciones `DYNAMIC_KEY` significan que la clave se construye al ejecutarse, de modo que el Validator no puede comprobarla; no son errores.

## Qué se comprueba

Las comprobaciones corren en tres fases:

1. **Esquema** — `manifest.json` tiene un `id` y una `version` de texto; los archivos JSON se analizan.
2. **Contratos** — lo que cada mod declara y remite: entidades de todos los tipos (personajes, objetos, misiones, habilidades, recetas, acciones y los tipos descritos por esquemas), comandos y acciones de escenario con sus parámetros, parches de escenas, propiedades que el código C# lee y escribe.
3. **Reconciliación** — cada referencia tiene una declaración, `depends` cubre lo que el mod toma de otros, y se listan las declaraciones que nada usa.

Las acciones de escenario y sus parámetros se comprueban contra los esquemas que los mods incluyen (ver Schemas), con las mismas reglas que la paleta de comandos.

## Ajustes

En la pestaña de ajustes del mod, dentro del gestor de mods:

- **Enabled**, **Run on start** — apagan la pasada;
- **Parse C#**, **Parse GDScript** (desactivado por defecto, lento), **Parse scenarios** — qué fuentes se leen;
- **Write report**, **Report path** — dónde va `report.json`;
- **Block mods on error** — está en los ajustes, pero en la versión actual no cambia nada: nunca se bloquean mods.

## Si algo sale mal

1. **No hay informe.** El juego se cerró antes de acabar la pasada (Cthangover tarda cerca de medio minuto) o **Write report** está desactivado.
2. **Muchas líneas `DYNAMIC_KEY`.** Normal en código que construye claves; son informaciones.
