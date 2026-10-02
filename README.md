# Armado automático de vigas — add-in Revit 2027

Genera la armadura de **vigas estructurales de sección rectangular, en T o en L**, de
**sección constante o variable a lo largo de su longitud** (cartelas rectas y
escalones), a partir de la **geometría real del elemento**, sin depender de los nombres
de parámetros de tus familias: barras longitudinales corridas por capas (con varios
diámetros), bastones arriba o abajo y estribos rectangulares.

Es el hermano del add-in de columnas
([Acero-columnas](https://github.com/Andy-rba30/Acero-columnas)) y del de muros de
contención ([Acero-automatico](https://github.com/Andy-rba30/Acero-automatico)): misma
base (lectura del sólido con rebanadas, ventana previa con esquema y tema oscuro de
Revit, red de seguridad que deshace el elemento entero si una barra queda fuera del
hormigón, `config.json`) y los tres comparten la pestaña **ARBA** y el desplegable
**Acero** de la cinta.

Antes de crear nada abre una **ventana** en la que se ve qué se ha detectado en cada
viga seleccionada y se elige el armado: capas de barras de cada cara, bastones,
prolongaciones y patillas, distribución de estribos (global y por viga), ganchos y
recubrimiento, con un esquema de la sección y del alzado.

## Cómo deduce la viga

1. Toma el **eje** de la viga (su curva de ubicación; si no la tiene, la orientación de
   la familia o el lado largo del elemento). Sistema local: `w` a lo largo del eje desde
   la cara de inicio, `u` horizontal perpendicular, `v` perpendicular a los dos (vertical
   en una viga horizontal; en una viga inclinada sigue la inclinación).
2. Toma el sólido del elemento. Si la viga está **unida** a columnas o losas que le
   quitan hormigón, según la opción **Viga unida a otros elementos**:
   - **Auto** (por defecto): la sección se lee de la geometría completa de la familia
     (`GetOriginalGeometry`, así una losa unida no le "roba" la parte alta del canto) y la
     longitud del sólido cortado (la viga entre caras de columna). Las barras se
     comprueban contra la geometría completa recortada a esa longitud.
   - **Solo el sólido cortado**: tal como lo ve Revit.
   - **Geometría completa de la familia**: también en longitud (las barras y estribos
     entran en la columna).
3. Corta el sólido con rebanadas finas **perpendiculares al eje** en estaciones a lo largo
   de toda la longitud (`prismCheckStepMm`, 250 mm) y lee el contorno de cada una. Tiene
   que ser **un único contorno cerrado, sin huecos, con bordes rectos y paralelos a dos
   ejes** (rectangular, T, T invertida, L, I...).
4. Descompone cada contorno en sus rectángulos máximos y toma como **alma** el de mayor
   canto: ahí va el estribo. En una T el alma es el rectángulo de toda la altura; el ala
   es la losa.
5. Agrupa las estaciones en **tramos**:
   - estaciones consecutivas iguales forman un **tramo constante**;
   - entre dos tramos constantes distintos hay un **escalón**, cuya cota exacta se toma
     de la cara plana perpendicular al eje que hay ahí (o por bisección con más cortes);
   - las estaciones que cambian de una a la siguiente forman un **tramo de canto variable**
     (cartela): tienen que cambiar **linealmente** (todos los vértices en la recta entre
     los dos extremos), con el mismo ancho de alma. El quiebro con el tramo constante
     vecino se calcula donde la recta de la cartela alcanza la sección constante.
   - el **ancho del alma** tiene que ser el mismo en toda la viga.

Cualquier otra forma (circular, con esquinas redondeadas, hueca, cartela curva, ancho
variable, eje en arco, viga vertical) se **rechaza** con un mensaje claro y sin crear
ninguna barra. Un cambio de sección en un tramo más corto que dos estaciones pide bajar
`prismCheckStepMm`.

## Armado de la sección (`BeamPlan`)

- Un único **estribo rectangular cerrado** en el alma, a `coverMm` de sus caras (medido
  al exterior del estribo), con el tipo de gancho elegido en los dos extremos (esquina
  superior izquierda). Si el gancho asoma fuera del hormigón, el plugin invierte su
  orientación solo y lo avisa.
- **Capas** de barras corridas en cada cara (superior e inferior), de fuera hacia dentro:
  la capa 1 va pegada al estribo y las siguientes se apilan hacia el interior con la
  **separación libre entre capas** (`layerClearMm`, 25). Cada capa lleva un **total de
  barras**, un tipo para las dos **barras extremas** (en la capa 1, las esquinas del
  estribo, tangentes a sus ramas) y otro para las **intermedias**, repartidas por igual
  entre las extremas: así una capa puede ser "2 Ø3/4" + 1 Ø5/8"" y la segunda "2 Ø3/4"".
  Hasta 3 capas por cara. Para un control más fino, en el esquema de la sección se
  pueden **seleccionar barras con un clic y asignarles otro tipo** solo en esa viga
  (repitiendo con otras selecciones para usar varios diámetros).
- **Capa intermedia (barras laterales)**: pares simétricos de barras de alma, una en cada
  rama del estribo a la misma altura, repartidos por igual en el canto libre entre el
  paquete superior y el inferior (en el centro de la sección no va nada). Tipo de barra
  y número de pares general, y número de pares propio de cada viga.
- **Bastones**: barras cortas de refuerzo en la cara superior o inferior, con su tipo,
  su número de barras y su tramo: **inicio**, **fin** o **ambos extremos** (longitud desde
  la cara del apoyo hacia el vano, más un **anclaje** dentro del apoyo más allá de la
  cara) o **centro del vano** (longitud total centrada, o dos longitudes distintas hacia
  el inicio y hacia el fin desde el centro para armados especiales). La longitud se escribe en mm (`1500`; en metros si es menor de 5).
  Como en el add-in de muros, cada bastón va **apilado por dentro** de la capa 1 de su
  cara (tocándola, o con el **hueco** que se indique), en el hueco central que dejan las
  capas 2 y 3 a esa altura
  o **en la misma capa** que ellas, intercalado en los huecos entre las corridas de forma
  simétrica desde el centro con la separación libre mínima.
- Comprobaciones: las capas superiores e inferiores no pueden solaparse en el canto
  **mínimo** de la viga (error); barras a menos de `minClearMm` (25) o de un diámetro
  libres, o bastones que no caben, se avisan (la fila de la viga y el esquema lo dicen).

Las barras iguales alineadas y equiespaciadas a lo largo de `u` (las extremas de una
capa, sus intermedias, las de un bastón) se crean como un solo conjunto de Revit
(array), igual que si se modelaran a mano.

## Barras a lo largo de la viga

- Las corridas van de la cara de inicio a la cara final. Con **prolongación** en un
  extremo sobresalen esa longitud (anclaje en el apoyo); sin prolongación terminan a
  `endCoverMm` de la cara. Las prolongaciones y los anclajes de los bastones son las
  únicas partes de barra que pueden estar fuera del hormigón de la viga: el resto se
  comprueba.
- Con **patilla** llevan una pata a 90° en los extremos elegidos: las superiores
  doblan hacia abajo y las inferiores hacia arriba (gancho estándar). Con prolongación
  queda dentro del apoyo; sin prolongación, dentro de la viga a `endCoverMm` de la cara.
- Cada barra se define por su distancia a la cara del alma de su lado, así en los tramos
  de **canto variable** sigue la cara inclinada (quiebro en cada cambio de tramo) y en un
  **escalón** salva el salto con una **bayoneta a 45°** dentro del lado de más canto,
  metida `recubrimiento + estribo + diámetro` respecto al plano del escalón. La cara que
  no cambia (normalmente la superior) lleva barras rectas.
- Los estribos siguen la sección de cada estación: en los tramos constantes se crean
  como arrays; en las cartelas, uno a uno con su canto.

## Empalmes por longitud comercial (`SpliceLayout`)

En obra las barras vienen de una longitud comercial (normalmente **9 m**), así que una
corrida más larga se hace con dos o más barras **empalmadas por traslape**. El add-in lo
reproduce (ACI 318-19):

- Cada barra corrida (prolongaciones incluidas) más larga que la **longitud comercial**
  se parte en los trozos justos de como mucho esa longitud, solapados la **longitud de
  empalme a tracción** `lst`. Los bastones no se empalman (si uno es más largo que la
  barra comercial, se avisa).
- `lst` se calcula por diámetro: `ld` según 25.4.2.3 (expresiones simplificadas, con
  `ψt = 1.3` si la barra tiene más de 300 mm de hormigón fresco debajo, es decir las
  superiores y las laterales altas; `ψe = λ = 1`; `ψg` según el grado del acero;
  `√f'c ≤ 8.3 MPa`; `ld ≥ 300 mm`) y empalme **clase B = 1.3 ld** (todas las barras de
  una capa se empalman en la misma sección) o clase A = 1.0 ld, mínimo 300 mm,
  redondeado hacia arriba a 50 mm. `f'c` y `fy` se escriben en kg/cm² (210 y 4200 por
  defecto). También se puede dar una **longitud fija** para todos los diámetros (la de la
  tabla del plano). Con f'c 210 y fy 4200: Ø1/2" 750 / 950 mm, Ø5/8" 900 / 1200 mm,
  Ø3/4" 1100 / 1400 mm, Ø1" 1800 / 2300 mm (barra baja / barra alta).
- **Zona de empalme**, por cara: las **superiores en el tercio central** de la luz (ahí
  el momento negativo es pequeño) y las **inferiores en los cuartos extremos**, con el
  empalme acabando en `L/4` desde la cara del apoyo y, si cabe, fuera de `2h` desde esa
  cara (zona de rótula); las laterales van en el tercio central. Cada zona se puede
  cambiar en la ventana. Con un empalme basta hasta una barra de `2·Lc − lst`; una
  inferior más larga lleva un empalme en cada cuarto extremo. Si los empalmes no caben
  en su zona se reparten por igual a lo largo de la barra y la fila de la viga lo avisa.
- Geometría como en obra: el primer trozo sigue la línea de la barra; el siguiente va
  **pegado por dentro** (desplazado un diámetro hacia el interior de la sección) durante
  el solape y vuelve a la línea con una **bayoneta** de pendiente 1:6. Las patillas van
  solo en el primer y en el último trozo. Cada trozo es un conjunto de Revit propio
  (con su array), con el mismo nombre de conjunto que la barra entera.
- El alzado dibuja los trozos con sus bayonetas y una etiqueta `empalme 1100` con la
  longitud, y la fila de cada viga resume los empalmes (`empalmes: superiores 1 x 1400
  mm (3/4) en el tercio central; inferiores 1 x 1100 mm (3/4) cerca de los apoyos`).

Con **Longitud comercial de barra = 0** no se empalma nada y las corridas se crean de una
pieza como antes.

## Distribución de estribos (`StirrupLayout`)

Se escribe como en los planos y **desde cada apoyo**: **`1@50, 8@100, R@200`** = el
primero a 50 mm de la cara del apoyo, ocho más cada 100 mm y el resto cada 200 mm como
máximo (repartidos por igual en el tramo central). Los valores menores de 5 se leen en
metros (`1@.05`). Con **Repetir desde el otro apoyo** (por defecto) los grupos fijos se
colocan también desde el final en espejo. Los desfases de inicio y fin descuentan
longitud (media columna si la viga está modelada de eje a eje).

Cada viga puede tener **su propia distribución** desde la lista de la ventana. Si la viga
es demasiado corta para toda la distribución, los grupos se recortan por la mitad y se
avisa.

## Comprobaciones de seguridad

Igual que en los otros add-ins: **o se arma la viga entera y bien, o no se arma**.

1. **Antes de crear cada barra** se comprueba que su eje, y cuatro fibras desplazadas
   medio diámetro en el plano de la sección (seis en los estribos), quedan dentro del
   sólido del elemento (`Solid.IntersectWithCurve`), en todas las posiciones del array.
   Las longitudinales se recortan a la longitud de la viga para esa comprobación.
2. **Después de crear y regenerar** se lee la geometría real de cada barra de cada
   conjunto (radios de doblado y ganchos incluidos) y se vuelve a comprobar.
3. Cualquier fallo deshace la subtransacción de ese elemento: no queda ni una barra.

El informe final dice, viga a viga, qué se ha creado y por qué se ha rechazado lo que no.

## Interfaz gráfica

- **Vigas seleccionadas**: forma detectada (rectangular, en T, de sección variable con
  sus tramos...), alma, longitud, nota de geometría unida y, en rojo, el motivo del
  rechazo. Cada fila tiene su **distribución de estribos** propia (vacío = general). Clic
  en una fila para verla en los esquemas.
- **Barras longitudinales corridas**: tabla de capas de la cara superior y de la
  inferior (extremas, intermedias, total; añadir / quitar capa), con el resumen de cada
  capa como en los planos; prolongaciones, recubrimiento en extremos, patilla,
  separaciones entre capas y entre barras; **empalmes por traslape** (longitud
  comercial, f'c, fy, clase, longitud fija y zona de empalme de cada cara, con la
  longitud de empalme resultante de cada tipo de barra en uso); cuadro de **barras por
  capa de la viga seleccionada**.
- **Bastones**: tabla con un bastón por fila (cara, posición, tipo, barras, capa,
  longitud, anclaje, desde / hasta) con botones para añadir y quitar; debajo, los avisos.
- **Estribos**: tipo, gancho, giro del gancho, distribución, simetría y desfases.
- **Recubrimiento, geometría unida y partición**: recubrimiento al estribo, cómo tratar
  la geometría unida (al cambiarlo se vuelven a leer todas las vigas) y plantilla del
  parámetro Partición (`{marca}`, `{id}`, `{tipo}`, `{familia}`, `{conjunto}`, `{cara}`).
- **Sección**: hormigón de la sección de referencia (la más cercana al centro del vano),
  el estribo con sus ganchos dibujados con el ángulo del tipo elegido, cada barra a su
  diámetro (rojo oscuro las extremas, naranja las intermedias, morado los bastones) y las
  etiquetas de las capas (S1, S2... arriba; I1, I2... abajo). Rueda: zoom; arrastrar:
  mover; doble clic: encajar. Al pasar el ratón por una barra se ve su capa, tipo,
  posición y distancia a la cara.
- **Alzado**: el perfil del alma tramo a tramo, las barras corridas siguiendo las caras
  (prolongaciones y patillas a trazos), los bastones en su tramo con su etiqueta y cada
  estribo, con la etiqueta de cada tramo de la distribución (`inicio 1@50`,
  `resto R@200 (=187)`). La escala vertical se exagera si hace falta (se indica).
- **Guardar como valores por defecto** escribe `config.json`; **Armar** crea las
  barras; **Cancelar** no toca nada.

Sin tipo de barra elegido el esquema se dibuja con diámetros orientativos y el botón
Armar avisa de qué falta.

## config.json

```jsonc
{
  "coverMm": 40,
  "topBars":    { "layers": [ { "barTypeName": "", "intermediateBarTypeName": "", "count": 2 } ] },
  "bottomBars": { "layers": [ { "barTypeName": "", "intermediateBarTypeName": "", "count": 2 } ] },
  "sideBars": { "barTypeName": "", "pairs": 0 },
  "longitudinal": { "startExtensionMm": 0, "endExtensionMm": 0, "endCoverMm": 40,
                    "legMm": 0, "legAtStart": true, "legAtEnd": true,
                    "layerClearMm": 25, "minClearMm": 25 },
  "bastones": [
    { "face": "top", "position": "both", "barTypeName": "", "count": 1, "stacked": true, "gapMm": 0,
      "length": "1500", "anchorMm": 0, "fromMm": 0, "toMm": 0 }
  ],
  "splices":    { "commercialLengthMm": 9000, "fcKgCm2": 210, "fyKgCm2": 4200, "classB": true,
                  "fixedLengthMm": 0, "topZone": "center", "bottomZone": "ends" },
  "stirrups":   { "barTypeName": "", "hookTypeName": "135", "hookOrientation": "left",
                  "distribution": "1@50, 8@100, R@200", "symmetric": true,
                  "startOffsetMm": 0, "endOffsetMm": 0 },
  "joinedGeometry": "auto",
  "partitionTemplate": "VIG-{marca}",
  "probeSliceMm": 10, "prismCheckStepMm": 250, "prismCheckToleranceMm": 2, "rectilinearAngleDeg": 0.5
}
```

`face`: `top` / `bottom`. `position`: `start`, `end`, `both` o `center` (`fromMm` / `toMm` = longitudes hacia inicio y fin desde el centro).
`stacked`: `true` (apilado por dentro, con `gapMm` de hueco) o `false` (en la misma capa).
`splices`: `commercialLengthMm` 0 = sin empalmes; `fixedLengthMm` 0 = calcular `lst` según ACI 318-19 con `fcKgCm2`, `fyKgCm2` y `classB`;
`topZone` / `bottomZone`: `center` (tercio central) o `ends` (cuartos extremos). Los nombres de tipo de barra y de gancho pueden ser
exactos o un fragmento (`"135"`, `"3/8"`); sin coincidencia no se arma, nunca se
sustituye por otro tipo.

## Compilar e instalar

Requiere el SDK de .NET 10 y Revit 2027 (los paquetes `Nice3point.Revit.Api.*`
traen las DLL de la API; para Revit 2025/2026 cambia el `TargetFramework` a
`net8.0-windows` y la versión del paquete).

```
dotnet build -c Debug
```

En Debug la compilación copia `BeamRebar.dll`, `config.json` y `BeamRebar.addin`
a `%AppData%\Autodesk\Revit\Addins\2027\`. Al abrir Revit aparece la pestaña **ARBA**
con el botón **Vigas** en el desplegable **Acero** (comparte la pestaña con los add-ins
de columnas y muros si están instalados) y el comando queda también en Complementos >
Herramientas externas.

## Estructura del código

| Archivo | Qué hace |
|---------|----------|
| `Rectilinear.cs` | Geometría pura de polígonos rectilíneos: limpieza, comprobación, rectángulos máximos, nombre de la forma. |
| `BeamProfile.cs` | Perfil de la viga a lo largo del eje: tramos constantes, cartelas lineales y escalones a partir de las estaciones. Pura. |
| `BeamPlan.cs` | Armado de la sección (estribo, capas, bastones por capa) y trayectorias de las barras a lo largo de la viga (`BarPaths`). Pura, compartida por ventana y generador. |
| `StirrupLayout.cs` | Lectura de `1@50, 8@100, R@200` desde los dos apoyos, cotas de los estribos y longitudes `L/4`. Pura. |
| `SpliceLayout.cs` | Empalmes por traslape: longitud de desarrollo y de empalme (ACI 318-19) por diámetro, y reparto de los trozos de una barra más larga que la comercial en su zona de empalme. Pura. |
| `BeamSection.cs` | Lectura del sólido de Revit: eje, rebanadas perpendiculares, límites de tramo, geometría completa si está unida. |
| `HostAnalysis.cs` | Resultado por elemento (perfil o motivo de rechazo) y elecciones por viga. |
| `RebarGenerator.cs` | Crea los `Rebar` (corridas, bastones, estribos) con las dos redes de seguridad y la inversión automática de ganchos. |
| `RebarOptionsWindow.cs`, `SectionPreview.cs`, `ElevationPreview.cs`, `RevitTheme.cs` | Ventana y esquemas (WPF en código, sin XAML) con el tema oscuro de Revit. |
| `ArmarVigaCommand.cs`, `RibbonApp.cs` | Comando externo y pestaña de la cinta. |
| `AppConfig.cs`, `PartitionName.cs` | Configuración y plantilla de Partición. |

Las clases puras (`Rectilinear`, `BeamProfile`, `BeamPlan`, `StirrupLayout`, `SpliceLayout`, `AppConfig`)
no dependen de Revit y se pueden probar en un programa de consola.

## Limitaciones conocidas

- Un solo estribo rectangular por sección: sin estribos dobles ni grapas para vigas
  anchas con muchas barras.
- El ancho del alma tiene que ser constante; las cartelas tienen que ser rectas.
- Las barras corridas cruzan los escalones con bayoneta; si el detalle de obra es otro
  (barras ancladas por separado), habrá que retocarlas a mano.
- No hace comprobaciones estructurales: decide las cuantías y las longitudes tú; esto
  solo modela lo que eliges en la ventana. La longitud de empalme usa las expresiones
  simplificadas de ACI 318-19 (sin el término `cb + Ktr`) y no escalona los empalmes de
  una misma capa (de ahí la clase B por defecto): revísala contra tu plano.
