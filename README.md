# Home Builder

Plugin para Godot 4 (C#) que permite construir edificios directamente en el editor de escenas mediante clics en el viewport 3D.

## Requisitos

- Godot 4.x con soporte .NET (Mono)
- El plugin se activa en **Project → Project Settings → Plugins**

---

## Flujo de trabajo

1. Abre o crea una escena 3D.
2. Activa un modo de construcción en el panel **Home Builder** (parte inferior del editor).
3. Construye el edificio planta por planta usando el selector de planta.
4. Cuando el edificio esté listo, usa el modo **Bakear** para exportarlo como escena optimizada.
5. Instancia la escena bakeada en tu nivel.

---

## Modos de construcción

### Suelos
Clic en el viewport para colocar una loseta de suelo. El tamaño de la loseta es siempre 1×1 m. Puedes configurar:
- **Grosor** de la losa
- Material de la **cara superior**, **inferior** y **laterales**

### Paredes
- Primer clic: punto de inicio de la pared.
- Segundo clic: punto final. La pared se alinea automáticamente al centro de las losetas del suelo.
- Las paredes soportan **puertas y ventanas** (ver más abajo).
- Configurable: **altura** de la pared y material de **cara A**, **cara B** y **cantos**.

### Puertas y ventanas
Con el modo Puertas o Ventanas activo, haz clic sobre una pared existente para abrir un hueco. El hueco se recorta en la geometría y en la colisión de la pared.
- Puertas: ancho y alto configurables.
- Ventanas: ancho, alto y altura del alféizar configurables.

### Escaleras
- Primer clic: base de la escalera.
- Segundo clic: dirección y longitud. La escalera conecta la planta actual con la siguiente.
- Configurable: número de escalones, anchura y profundidad de cada escalón.

### Tejados
Clic en el viewport para colocar el tejado sobre la planta activa. Tipos disponibles:
- **Plano**
- **A un agua** (shed) — configurable: dirección y pendiente
- **A dos aguas** (gable) — configurable: dirección y pendiente
- **A cuatro aguas** (hip) — configurable: pendiente

### Vallas / Barandillas
Requiere asignar una `PackedScene` como asset de valla. Los segmentos se instancian a lo largo del borde indicado.

---

## Plantas múltiples

El selector de planta (▲ / ▼ junto al número de planta) controla en qué nivel se colocan los nuevos elementos. Al subir de planta, las plantas inferiores se ocultan automáticamente en el editor para facilitar el trabajo. La altura entre plantas es la misma que la altura de pared configurada.

---

## Bake (exportación optimizada)

El modo **Bakear** genera una escena `.tscn` lista para usar en un nivel. Abre el panel de bake y configura:

| Parámetro | Descripción |
|---|---|
| **Carpeta de salida** | Ruta `res://` donde se guarda el `.tscn` |
| **LOD0 distancia fin** | Distancia máxima (metros) a la que se muestra la geometría completa |
| **LOD1 distancia inicio** | Distancia a partir de la cual se muestra la versión simplificada |
| **Fade** | Modo de transición entre LOD0 y LOD1 (desactivado / self / opaque) |

### Qué contiene la escena bakeada

```
StaticBody3D  (nombre del edificio)
├── LOD0           — geometría completa, un surface por material original
├── LOD1           — geometría simplificada, un draw call por material
├── Occluder       — OccluderInstance3D para occlusion culling
├── Collision      — ConcavePolygonShape3D (paredes, suelo, tejado)
└── Staircase_N    — ConvexPolygonShape3D por cada escalera
```

**LOD0 / LOD1** usa el sistema `VisibilityRange` nativo de Godot. A distancia, las escaleras, vallas y suelo se eliminan del LOD1 para reducir polígonos, y las paredes se simplifican a caras planas sin huecos.

**Occluder** permite a Godot descartar objetos que quedan detrás del edificio sin renderizarlos. Para que tenga efecto debes activarlo en el proyecto:
> `Project Settings → Rendering → Occlusion Culling → Use Occlusion Culling = ON`
> 
> Para visualizar los occluders en el editor: `Debug → Visible Occlusion Culling Debug`

**Collision** es un único `ConcavePolygonShape3D` construido a partir de la geometría visual, de modo que los huecos de puertas y ventanas son colisión real. Las escaleras se mantienen como `ConvexPolygonShape3D` independientes para que `CharacterBody3D` pueda subir por ellas correctamente mediante `move_and_slide`.

---

## Materiales

Cada modo expone selectores de material en el panel. Los materiales se asignan antes de colocar elementos; los elementos ya colocados no se ven afectados al cambiar el material activo. Los materiales se conservan en el bake y se simplifican en LOD1 (se eliminan mapas de normales, AO y roughness para reducir el coste a distancia).

---

## Roadmap

- ✅ 0.1.0 Generar casas de una planta
- ✅ 0.2.0 Construir varias plantas
- ✅ 0.3.0 Construir tejados
- ✅ 0.4.0 Usar vallas y barandillas
- ✅ 1.0.0 Bake optimizado (LOD, oclusión, colisión unificada)
