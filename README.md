# Home Builder

Plugin para Godot 4 (C#) que permite construir edificios directamente en el editor de escenas mediante clics en el viewport 3D.

## Requisitos
Godot 4.x con soporte .NET (Mono)

---

## Instalación
Tener el plugin en la carpeta de addons del proyecto. El plugin se activa en **Project → Project Settings → Plugins**

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
Mantén pulsado y arrastra en el viewport para rellenar un rectángulo de losetas. Una sola pulsación coloca una loseta de 1×1 m; arrastrando se cubre toda la habitación de golpe (un único `MeshInstance3D` para todo el rectángulo, no una loseta por celda). El snap es a la celda de 1 m. Puedes configurar:
- **Grosor** de la losa
- Material de la **cara superior**, **inferior** y **laterales**

### Paredes
- Primer clic: punto de inicio de la pared.
- Segundo clic: punto final. La pared se alinea automáticamente al centro de las losetas del suelo.
- Las intersecciones entre paredes (esquinas en L, T o X) se resuelven automáticamente con juntas en inglete, sin huecos visibles en ningún ángulo.
- Las paredes soportan **puertas y ventanas** (ver más abajo).
- Configurable: **altura** de la pared y material de **cara A**, **cara B** y **cantos**.

> El grosor de la pared es fijo (0.1 m) y no se expone en el panel.

### Puertas y ventanas
Con el modo Puertas o Ventanas activo, haz clic sobre una pared existente para abrir un hueco. El hueco se recorta en la geometría y en la colisión de la pared.
- Puertas: ancho y alto configurables.
- Ventanas: ancho, alto y altura del alféizar configurables.

### Escaleras
- Primer clic: base de la escalera.
- Segundo clic: dirección y longitud. La escalera conecta la planta actual con la siguiente.
- Configurable: número de escalones, anchura y profundidad (huella) de cada escalón.
- La **altura de cada escalón** se calcula automáticamente como `altura de pared / número de escalones`, de forma que la escalera siempre conecta exactamente con la planta superior. Subir el número de escalones los hace más bajos; bajarlo, más altos.

### Tejados
Mantén pulsado y arrastra en el viewport para definir el footprint del tejado sobre la planta activa. El snap es a media loseta (0.5 m), para que el tejado pueda alinearse con caras exteriores de pared, no solo con el centro de la celda. El footprint se extiende automáticamente medio grosor de pared hacia fuera por cada lado, de modo que el alero cubre la cara exterior de las paredes perimetrales. Tipos disponibles:
- **Plano**
- **A un agua** (shed) — configurable: dirección y pendiente
- **A dos aguas** (gable) — configurable: dirección y pendiente
- **A cuatro aguas** (hip) — configurable: pendiente. La cumbrera se orienta automáticamente al lado más largo del rectángulo.

### Vallas / Barandillas
- Primer clic: esquina inicial del segmento.
- Segundo clic: esquina final. Los módulos se instancian a lo largo del eje dominante (X o Z).
- Requiere asignar una `PackedScene` como asset de valla en el panel.

> **Limitación**: las vallas solo admiten ejes alineados (X o Z). No se pueden colocar en diagonal.

#### Convención del asset de valla

El asset debe seguir este contrato para que los módulos encajen correctamente:

| Propiedad     | Valor                         |
|---------------|-------------------------------|
| Anchura       | 1 m (eje X)                   |
| Pivot         | Centro de la base (0, 0, 0)   |
| Orientación   | Mirando hacia +X              |

---

## Plantas múltiples

El selector de planta (▲ / ▼ junto al número de planta) controla en qué nivel se colocan los nuevos elementos. Al subir de planta, las plantas inferiores se ocultan automáticamente en el editor para facilitar el trabajo. 

> La altura entre plantas es la misma que la altura de pared configurada.

---

## Bake (exportación optimizada)

El modo **Bakear** genera una escena `.tscn` lista para usar en un nivel. Abre el panel de bake y configura:

| Parámetro                 | Descripción                                                           |
|---------------------------|-----------------------------------------------------------------------|
| **Carpeta de salida**     | Ruta `res://` donde se guarda el `.tscn`                              |
| **LOD0 distancia fin**    | Distancia máxima (metros) a la que se muestra la geometría completa   |
| **LOD1 distancia inicio** | Distancia a partir de la cual se muestra la versión simplificada      |
| **Fade**                  | Modo de transición entre LOD0 y LOD1: `Sin fade` o `Self` (ver abajo) |

#### Distancias de LOD

Se recomienda usar valores a partir de **80 m** para que el cambio de LOD ocurra cuando el edificio ya es pequeño en pantalla y el jugador no aprecie la diferencia de detalle. Con distancias cortas el cambio es claramente visible.

Si LOD0 fin y LOD1 inicio coinciden (p. ej. 80 m y 81 m), el margen de transición es mínimo y el efecto equivale a un cambio casi instantáneo.

#### Modos de Fade

- **Sin fade** — el cambio entre LOD0 y LOD1 es instantáneo. Sin artefactos visuales. Recomendado cuando los valores de distancia son suficientemente altos para que el cambio pase desapercibido.
- **Self** — se aplica una transición suave entre ambos LODs. Puede producir artefactos visuales si el edificio tiene geometría interior (escaleras, elementos en el interior) que se hace visible a través de las paredes durante la transición, ya que éstas se vuelven semitransparentes. También puede interactuar con materiales transparentes de la escena (agua, cristales). Úsalo si los edificios son principalmente exteriores o si la transición ocurre a distancia suficiente.

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
