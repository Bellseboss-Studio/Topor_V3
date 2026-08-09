# Especificación — Granja de Doña Florinda (Vertical Slice)

> **Change**: `dona-florinda-farm`
> **Estado**: visión / spec extendida → lista para `sdd-design` y `sdd-tasks`
> **Objetivo**: convertir el prototipo (1 partida = 1 timer) en un juego de **niveles (granja → días → huertos)** con economía de mordidas, power-ups del jugador y arquetipos de topo. Todo configurable y testeable (reglas puras).

---

## 1. Contexto

Hoy (`GameRules.cs`) es una sola partida cronometrada: topos salen de un damero de 17 huecos, las **cosechas son vidas** (todas iguales), y ganas si sobrevivís el timer.

**Feedback de playtest**: *"que a más tiempo, más topos"* — rampa monótona, no una curva con pico al medio.

**Visión del autor**: un vertical slice en que cuidás la **Granja de Doña Florinda** en **3 niveles**, con verduras que tienen **puntos de vida (mordidas)** hasta ser devoradas, power-ups del jugador, y distintas especies de topo. Cada granja = mínimo 5 huertos con frutas/verduras y topos distintos, escalando a otras ubicaciones (Colombia, Canadá…) **solo cambiando contenido, no código**.

---

## 2. Principio de diseño: todo es data-driven

- Una **ubicación (granja)** = un **`FarmProfile`**: nombre, tema visual, paleta, especies de topo habilitadas, economía, huertos.
- Cada **huerto** = un conjunto de huecos + asignación de cultivos + set de topos.
- Cada **día (nivel)** = un **`LevelProfile`** (ver §7).
- "Colombia" o "Canadá" en el futuro = un par de assets nuevos, **cero código nuevo**.

---

## 3. El bucle del vertical slice

Progresión a 3 niveles, uno por huerto de la granja:

1. **Nivel 1 — Tomates** (solo topo normal, baja presión): enseña aviso → golpe → mordida.
2. **Nivel 2 — Repollo + Tomate** (se suma topo puya): doble golpe, nuevo arquetipo.
3. **Nivel 3 — Repollo + Zanahoria** (se suma topo ninja, rampa a fondo): la "hora de comer", clímax.

- **Ganar/nivel** = sobrevivir el día (timer) con ≥ 1 cultivo vivo. Pasa al siguiente nivel de la misma granja.
- **Perder** = te comen todos los cultivos vivos del nivel. Reintentás el *día* (no toda la granja) — retry suave.
- **Ganar granja** = completar los 3 días.

---

## 4. Mordidas: los cultivos ya no son vida plana

Cada cultivo declara `BitesToEat` (mordidas para morir). Un topo que escapa **roba 1 mordida**, no el cultivo entero.

- Cuando `bites == BitesToEat` → el cultivo **MUERE** (se pierde esa vida).
- Ejemplo: zanahoria `BitesToEat = 2`; repollo y tomate `= 1`.
- La mordida deja una marca visual en el cultivo (brilla/queda pelado) mientras aún vive.

**Reglas**:
> MUST: un escape del topo sustrae **1** mordida del cultivo elegido (nunca la vida completa si `BitesToEat > 1`).
> MUST: al quedar `0` mordidas se marca el cultivo como **devorado** y se pierde como vida.
> MUST NOT: la semilla de replanteo no cuenta como vida extra; solo revive el cultivo devorado.
> SHOULD: mostrar el cultivo con mordeduras/brillo al ser golpeado para feedback claro.

---

## 5. Economía del jugador — poderes

Moneda: **semillas**, ganadas por golpes a topos (golpe = +seeds; golpe "perfecto" en `Rising` da bonus). El banco se guarda entre los días de la misma granja y se resetea al cambiar de granja.

Poderes (consumibles, de activación por barra de poder en HUD):

| Poder | Efecto | Costo | Slot |
|---|---|---|---|
| **Semilla de replanteo** | Revive un cultivo devorado (vuelve a `1` mordida) | media | pasiva/precompra |
| **Fruto señuelo** | Planta un señuelo en un hueco; el próximo topo ataca el señuelo, no tu cultivo | baja | activa |
| **Espantapájaros** | Distrae 10 s: los topos retrasan su salida y no telegrafían la señal | medio | activa |

**Extras futuros** (MAY): arado para apurar la brotación, ajo-repelente, doble fondo.

> MUST: los poderes son consumibles; usar un señuelo consume la copia.
> MUST: el topo ninja NO respeta el señuelo (huele el engaño). El señuelo fuerza el destino del próximo topo = señuelo.
> SHOULD: el banco persiste entre días de la misma granja.

---

## 6. Arquetipos de topos (spawn data-driven)

`MoleArchetype` = record que parametriza nacimiento / telegraph / golpes requeridos / ventana / escape. Default = normal.

| Arquetipo | Telegraph | Golpes | Ventana arriba | Mordidas al escapar | Notas |
|---|---|---|---|---|---|
| **Normal** | sí | 1 | estándar | 1 (default) | el que ya existe |
| **Puya** | sí, más corto | **2** (1.er golpe lo aturde, el 2.º lo noquea) | más rápido | 1 | más resistente |
| **Ninja** | **no** (aparece directo en `Rising`) | 1 | corto | 1 | la sorpresa |

> MUST: `TryHit` consume `hitsToKill - 1` golpes; el topo se mantiene `Rising`/`Up` mientras le queden golpes.
> MUST: el ninja nace en `Rising` sin pasar por `Telegraphing`.
> MUST: el ninja no sigue el señuelo.
> SHOULD: la mezcla de arquetipos la define `LevelProfile` por frecuencia.

**Futuros** (MAY, fuera del slice): bruto (te rompe el combo), escudado (el primer golpe lo revierte), saltarín (salta a otro hueco) — todos vía `MoleArchetype`.

---

## 7. Dificultad por día (LevelProfile) + rampa monótona

`LevelProfile` (por día/huerto), config puro y testeable:

| Parámetro | Nivel 1 | Nivel 2 | Nivel 3 |
|---|---|---|---|
| `IntensityCurve` | 1 cte | 1→2 escalón | 2→3 gradual |
| `SpawnIntervalMs` | 3000 | 2600 | 2200 |
| `TelegraphDurationMs` | 800 | 650 | 500 |
| `UpWindowMs` | 1600 | 1300 | 1000 |
| Topos simultáneos máx | 2 | 3 | 4 |

> MUST: dentro de cada día, `SpawnIntervalMs` no aumenta (a más tiempo, más topos) — cumple el feedback de playtest.
> MUST: entre días, el perfil es estrictamente más agresivo en ≥2 ejes (intervalo, ventanas, telegraph, mezcla).
> MUST NOT: `BitesToEat` **no** es palanca de rampa — es la identidad del cultivo.

**Framing (historia, no mecánica)**: el último tercio del día 3 = "la hora de comer": los topos se alborotan. Juice sugerido: sol que se pone en el fondo — sin tocar gameplay.

---

## 8. Modelo de datos

```
FarmProfile (ScriptableObject)
  id, displayName, theme, paleta
  crops: cropId -> { displayName, bitesToEat, seedValue, sprite }
  plots: list<Plot>
    plot: { id, holePattern, cropAssign: [{crop, holeIdx}], allowedArchetypes,
            levels: LevelProfile[] }

LevelProfile (data)
  durationMs, spawnIntervalMs, telegraphDurationMs, upWindowMs, riseMs, sinkMs
  intensityCurve (puntos de rampa), archetypeMix: [{archetype, weight}]
  seedsBase

PowerUpCatalog (ScriptableObject) — defs de poderes (id, costo, slot)
```

Reglas puras: `GameRules` consume `FarmProfile`, `LevelProfile` y `MoleArchetype` **sin UnityEngine**, manteniendo los EditMode tests actuales y sumando los nuevos.

---

## 9. Aceptación del vertical slice

Un dev build permite (sin tocar código de reglas):
- jugar los 3 días de la granja con los perfiles de arriba,
- ver topos puya en Nivel 2 y ninjas en Nivel 3,
- ganar la granja, perder y reintentar el día,
- usar la tienda de semillas (señuelo y espantapájaros accionan en partida),
- comprobar que la **zanahoria resiste 2 escapes** antes de morir.

**Fuera de scope**: otras granjas/ubicaciones (Colombia, Canadá…), meta-progresión, audio/música, jefes (topos-boss), multijugador.

---

## 10. Integración con el código actual

- `GameRulesConfig`: sumar `cropBites[], MoleArchetype[]`, `hitsToKill`, `TelegraphDurationMs` por arquetipo (ninja = 0).
- `TrySpawn`: mezcla de arquetipos del día por frecuencia condicionada.
- `TryHit` y `StealBoundCrop`: el escape roba **1 mordida** (no destruye completo); destrucción solo al agotar `BitesToEat`.
- UI: vida → densidad de cultivo (mord1/mord2), barra de semillas, HUD poderes, tienda entre días.
- Tests EditMode: arquetipos, zanahoria 2 mordidas→muerte, ninja sin telegraph, señuelo fuerza destino, rampa monótona por día.