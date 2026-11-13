# Cloth Preview 3D

Visor 3D de ropa de GTA V / FiveM (.ydd + .ytd). Solo previsualización, sin exportar.

## Requisitos
- Windows + Visual Studio 2022 (carga de trabajo "Desarrollo de escritorio con .NET") o .NET SDK con soporte net48.
- Git.

## Montaje
1. Clona CodeWalker dentro de `libs`:
   `git clone https://github.com/dexyfex/CodeWalker libs/CodeWalker`
2. Compila: `dotnet build -c Release` (o abre la carpeta en Visual Studio).
3. Ejecuta, arrastra una carpeta con `.ydd` y `.ytd` y elige una prenda.

## Nombres que reconoce
- Modelo: `jbib_000_u.ydd` (también con prefijo FiveM `pack^jbib_000_u.ydd`)
- Textura: `jbib_diff_000_a_uni.ytd`
Se emparejan por componente + número. Las variaciones (a, b, c…) salen en el desplegable.

## Estructura
- `Services/ClothPairing.cs`: empareja YDD con YTD.
- `Services/RageMeshLoader.cs`: lee la malla con CodeWalker.Core (**único sitio, junto a TextureLoader, que depende de la API de CodeWalker**).
- `Services/TextureLoader.cs`: YTD -> DDS -> bitmap (Pfim).
- `MainWindow.xaml(.cs)`: interfaz y visor (HelixToolkit).

## Limitaciones conocidas
- Se aplica una sola textura difusa a toda la prenda (no se leen los shaders por geometría).
- Pfim decodifica DXT1/3/5; texturas BC7 u otros formatos no se muestran (la prenda sale gris). Solución: añadir un decodificador BC7 en `TextureLoader`.
- No incluye cuerpo base (ped); la prenda se ve flotando.
- Solo ropa (componentes), no props (`p_head`, etc.).
- Licencia: CodeWalker es GPL-3.0; si distribuyes este programa, revisa esa licencia.

## Cuerpo hombre/mujer
1. Pulsa **Importar cuerpo desde GTA V…** y elige `GTA5.exe` (solo una vez, tarda unos minutos).
2. Se crean `body_male.bin` y `body_female.bin` junto al programa; no se redistribuyen modelos de Rockstar.
3. El género se detecta por el nombre de la carpeta/archivo (mp_m_, mp_f_, male, female, mujer…) o se elige a mano.

## Interfaz
Diseño negro/rojo de 420 Tool´s. Las 'Partes de la prenda' permiten ocultar geometrías que se vean mal.
