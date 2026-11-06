# SackMix

Mezclador de audio por programa para Windows 10 y 11. Cada programa que suena (Discord, Spotify, el juego,
el navegador…) cae en un **canal con nombre** —Juego, Discord, Música, Otros, o los que crees— y cada canal
tiene su fader, su silencio y su medidor. Dentro del canal, cada programa tiene además su propio volumen y
silencio. Con un botón, cada canal aparece en **OBS** como una fuente de audio con ese mismo nombre.

![SackMix](docs/captura.png)

## Instalar

SackMix es **gratis**. Funciona en Windows 10 y 11 (64 bits) y no necesita .NET instalado.

**[Descarga el instalador](https://github.com/DarkSack/sackmix/releases/latest)** (`SackMix-Setup-<versión>.exe`, en
*Assets*) y ábrelo. No está firmado: si Windows muestra «Windows protegió su PC», pulsa **Más información › Ejecutar de
todas formas**. El asistente muestra las condiciones de uso y
privacidad, que hay que aceptar, y permite elegir la carpeta y marcar el acceso en el escritorio y el inicio con
Windows (arranca oculto en la bandeja). No pide permisos de administrador. Para actualizar, se instala la versión
nueva encima: los ajustes se conservan. Para desinstalar, Configuración › Aplicaciones.

## Uso

- **Abrir:** desde el menú Inicio o el escritorio. Solo se abre una copia: abrirla otra vez muestra la que ya está.
- **Bandeja:** cerrar la ventana deja SackMix funcionando junto al reloj (los volúmenes siguen aplicándose).
  Clic en el icono para abrirlo; clic derecho › Salir para cerrarlo del todo. Al salir, cada programa se queda
  en Windows con el último volumen que tenía.
- **Iniciar con Windows:** el interruptor de la cabecera. Encendido, SackMix arranca solo al encender el PC y
  espera en la bandeja (es el mismo ajuste que marca el instalador y que se ve en Administrador de tareas ›
  Aplicaciones de arranque).
- **Canales:** el nombre se edita encima; el punto de color lo cambia; «⋯» cambia el color o borra el canal
  (sus programas pasan a «Otros», que no se puede borrar y recoge todo lo que nadie colocó). «Nuevo canal» crea otro.
- **Mover un programa:** arrástralo por su icono o su nombre a otro canal, o clic derecho → «Mover a…»
  (con teclado: foco en su barra y Shift+F10). Se recuerda por programa.
- **Primer reparto automático:** Discord/TeamSpeak/Mumble → Discord; Spotify, Apple Music, TIDAL… → Música;
  lo instalado en `steamapps\common`, Epic, Riot, Xbox, GOG…, los juegos de Unreal (`…-Shipping.exe`, estén donde
  estén) y Minecraft (Java y Bedrock) → Juego; el resto → Otros. Lo que nunca colocaste se vuelve a repartir al abrirlo.
- **Normalizar** (el botón del ecualizador junto al silencio del canal): baja en 0,1–0,2 s cada programa del
  canal que suene demasiado fuerte (un grito en Discord, una explosión) y lo devuelve despacio a su volumen; la
  etiqueta «−8 dB» junto al programa muestra lo que le está quitando. Solo baja: Windows no deja subir un programa
  por encima del 100 %, ni separar a cada persona dentro de Discord, ni quitar el ruido de lo que oyes. Para lo
  que te llega, AMD Noise Suppression (salida) o NVIDIA Broadcast quitan el ruido, y la «Ecualización de
  sonoridad» de Windows (Sonido › tu salida › Mejoras), si tu driver la trae, iguala lo bajo y lo alto.
- **Volumen real** = volumen del programa × volumen del canal; silenciado si lo está cualquiera de los dos.
  Si mueves un programa en el mezclador de Windows, SackMix lo toma como el nuevo volumen del programa.
- **Salidas:** sigue a los programas en **todas** las salidas activas (por si Discord usa otra). Los sonidos del
  sistema solo se toman de la salida predeterminada, porque cada salida tiene los suyos a su propio nivel.
- **Mezcla por espacio de [SackSpace](https://github.com/DarkSack/sackspace):** al cambiar de espacio, SackSpace avisa a
  SackMix y este pone la mezcla guardada para ese espacio (volumen, silencio y «Normalizar» de cada canal). Para
  guardarla, deja los canales como quieras y en el menú de la bandeja elige «Guardar esta mezcla para «Espacio»»;
  «Olvidar…» la quita. Un espacio sin mezcla guardada no cambia nada. El aviso va por una tubería local de tu usuario.
- **Sonar por** (menú «⋯» del canal): manda los programas del canal a un altavoz o audífonos concretos, por ejemplo
  Discord a los audífonos y la música a los altavoces. Es la misma preferencia por programa que Windows tiene en
  Configuración › Sonido › Mezclador de volumen, y la aplica al momento; «La salida de Windows» la quita. Un programa
  que nunca moviste conserva lo que elegiste en Windows. Debajo del nombre del canal se ve su salida.
- **Ajustes:** `%LOCALAPPDATA%\SackMix\config.json` (se guarda 0,8 s después del último cambio). Errores en `sackmix.log`.

## OBS

«Enviar a OBS» da a **cada programa de tus canales su propia Captura de audio de aplicación** (la de OBS 28+),
con su nombre («Discord», «Spotify»): cada sonido tiene su fader en OBS. Lo que OBS ya captura, se llame como se
llame tu fuente, se deja como está (nada se duplica). Una fuente nueva va a la escena donde están los otros programas
de su canal, o a una escena llamada como el canal («[AUD] Música»), o a la escena actual. Los programas del último
canal («Otros») y el propio OBS no se envían: arrastra a un canal lo que quieras en OBS. No pide nada: lee el puerto y
la contraseña del propio OBS
(`%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json`). OBS tiene que estar abierto, con el servidor
WebSocket activado (Herramientas → Ajustes del servidor WebSocket). También quita los filtros «SackMix · …» que el
antiguo botón «Mejorar micro» (hasta la 0.3.0) puso en el micrófono; tus filtros no se tocan.

**Escuchar OBS en…** elige por qué altavoz o audífonos te deja escuchar OBS las fuentes que monitorizas (OBS ›
Ajustes › Audio › Avanzado › Dispositivo de monitorización). Se escribe en tu perfil de OBS, así que OBS tiene que
estar cerrado. Qué fuentes se escuchan se elige en OBS, en Propiedades de audio avanzadas › Monitorización; las
capturas de programas conviene dejarlas sin monitorizar, porque ese sonido ya lo oyes.

Cosas de OBS que conviene saber:

- La captura sigue al volumen por programa de Windows: lo que silencias en SackMix también se silencia en OBS.
- Si tienes activado el «Audio del escritorio» de OBS, captura otra vez todo lo que suena: con una fuente por
  programa, desactívalo (o silencialo) para no oír cada cosa dos veces en el directo.
- OBS busca el programa por una ventana visible: un Discord minimizado a la bandeja no se captura hasta que su
  ventana existe (OBS reintenta solo).
- Los «Sonidos del sistema» no se envían (no tienen programa ni ventana).

## Por qué no hay «dispositivos virtuales»

Programas como SteelSeries Sonar crean salidas de audio falsas («Sonar - Chat», «Sonar - Media») con un driver
de núcleo. Un driver así necesita firma de Microsoft (certificado EV), así que SackMix no instala ninguno:
controla el volumen de cada sesión de Windows y usa la captura por programa que OBS ya trae.

## Consumo (Ryzen 5 5600G, versión publicada, medido el 2026-10-26)

| Estado | CPU del equipo | Memoria privada |
|---|---|---|
| Visible, medidores en marcha | 0,13 % | 50 MB |
| Minimizada | 0,00 % | 42 MB |

Abre en 0,8 s. Cómo lo consigue: todo el audio va en un hilo propio que duerme si no hay trabajo; los medidores
se leen a 30 fps solo con la ventana visible; la ventana se dibuja por CPU (con la GPU integrada eran 147 MB y
más CPU, porque solo cambian los medidores); sin efectos de desenfoque; ejecutable precompilado (ReadyToRun) y
sin comprimir.

## Desarrollo

- `dotnet test SackMix.slnx` — pruebas: reparto, mezcla, archivo de ajustes y OBS (contra un servidor
  falso que habla obs-websocket 5, incluida la contraseña).
- `.\publish.ps1` — pruebas, `dist\SackMix.exe` (autocontenido, ~166 MB en disco) y el instalador
  `dist\SackMix-Setup-<versión>.exe`, con [Inno Setup 6](https://jrsoftware.org/isinfo.php)
  (`winget install JRSoftware.InnoSetup`); su script es [installer/SackMix.iss](installer/SackMix.iss).
- `tools\generar-icono.ps1` — regenera `Assets\sackmix.ico`.

Estructura: `src/SackMix.Core` (canales, reglas, ajustes y OBS, sin WPF), `src/SackMix` (WPF: `Audio/` es la
API Core Audio de Windows sin librerías externas, `ViewModels.cs`, `MainWindow.xaml`, `Theme.xaml`),
`tests/SackMix.Tests`.

## Licencia

SackMix es gratis: puedes usarlo en todos tus equipos, pero no venderlo ni distribuir versiones modificadas. Las
condiciones completas y la privacidad están en [installer/LICENCIA.txt](installer/LICENCIA.txt), el mismo texto que
muestra el instalador.
