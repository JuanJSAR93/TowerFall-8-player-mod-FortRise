[Read in English](README.md)

# TF8Player

Port de **TF-8-Player** para **FortRise**. Permite partidas de hasta 8 jugadores en TowerFall mediante parches en tiempo de ejecución con Harmony, sin necesidad de usar `Patcher.exe` ni modificar los binarios originales de `TowerFall.exe`.

## Características

- **Hasta 8 jugadores reales:** Soporta Versus Todos contra Todos (FFA) y Combate por Equipos (Team Deathmatch) para 8 jugadores (P1 a P8).
- **Compatibilidad de mandos:** Admite hasta 8 mandos físicos mediante el lanzador incluido, o combinación de mandos y teclado para los slots restantes.
- **16 Torres adaptadas y corregidas:** Basadas en los niveles del mod original de Jonesey13 (12 torres clásicas y 4 de la expansión *Dark World*), retocadas y corregidas para solucionar problemas en puntos de aparición (*spawns* rotos o demasiado juntos) y garantizar un balance adecuado para 8 arqueros.
- **Carga dinámica y segura:** Se instala como un mod estándar de FortRise sin sobrescribir archivos del juego.

---

## Requisitos

- TowerFall instalado en PC.
- [FortRise](https://github.com/FortRise/FortRise) instalado y funcionando (versión 5.5.0 o superior).
- .NET SDK `10.x` *(únicamente necesario si deseas compilar el mod desde el código fuente)*.

---

## Instalación

1. Asegúrate de que TowerFall esté cerrado.
2. Copia la carpeta `TF8Player` dentro del directorio `Mods/` de tu instalación de TowerFall:
   ```text
   <Ruta-de-TowerFall>\Mods\TF8Player
   ```
   *(Ruta habitual en Steam: `C:\Program Files (x86)\Steam\steamapps\common\TowerFall - FortRise\Mods\TF8Player`)*
3. La carpeta instalada debe contener:
   * `TF8PlayerFortRise.dll`
   * `meta.json`
   * `Launch-8Players.cmd`
   * Carpeta `Content/` con los niveles adaptados.

> [!IMPORTANT]
> No instales el mod antes de haber instalado FortRise en tu juego.

---

## Cómo jugar (Ejecución)

- **Para 1 a 4 jugadores:** Inicia TowerFall normalmente desde Steam o mediante `FortRise.exe`.
- **Para 5 a 8 jugadores:** Ejecuta `Mods\TF8Player\Launch-8Players.cmd`. Define `FNA_GAMEPAD_NUM_GAMEPADS=8` y las opciones SDL DirectInput/RawInput antes de iniciar el proceso. Si hay menos de 8 mandos, los jugadores restantes pueden unirse utilizando el teclado.

---

## Compilar desde el código fuente

Si eres desarrollador o deseas compilar la DLL por tu cuenta:

1. Asegúrate de tener instalado el **.NET SDK 10.x**.
2. Ejecuta `Compile-DLL.cmd`. El script detectará el compilador Roslyn y generará `TF8PlayerFortRise.dll`.
3. Por defecto, el script busca las dependencias en:
   ```text
   C:\Program Files (x86)\Steam\steamapps\common\TowerFall - FortRise
   ```
4. Si tu juego está en otra ruta o unidad, puedes definirla antes de compilar:
   ```cmd
   set "TF8_GAME_DIR=D:\Juegos\TowerFall - FortRise"
   Compile-DLL.cmd
   ```

---

## Estructura del proyecto

```text
TF8Player/
├─ src/
│  ├─ TF8PlayerFortRiseModule.cs   # Inicialización del módulo FortRise y hooks de entrada
│  └─ GameplayPatches.cs           # Parches de Harmony (menús, HUD, spawns y resultados)
├─ Content/
│  └─ Levels/
│     └─ Versus/                   # 16 torres con mapas adaptados para 8 jugadores
├─ tools/                          # Herramientas de validación
│  ├─ Validate-Harmony.cs
│  └─ Validate-Harmony.runtimeconfig.json
├─ Launch-8Players.cmd             # Configura ocho mandos antes de iniciar
├─ Compile-DLL.cmd                 # Script de compilación con .NET 10
├─ TF8PlayerFortRise.csproj        # Proyecto de C#
├─ meta.json                       # Manifiesto de mod para FortRise
├─ README.md                       # Documentación en inglés
└─ README_ES.md                    # Documentación en español
```

---

## Mecanismo técnico

A diferencia del mod original que requería un parcheador estático con Mono.Cecil, este port opera en memoria:
- **Intercepción con Harmony:** Modifica en tiempo de ejecución las clases `MainMenu.CreateRollcall`, `MainMenu.CreateTeamSelect`, `HUD`, `VersusRoundResults`, `SessionStats` y `TreasureSpawner` para expandir los límites nativos de 4 a 8 jugadores.
- **Gestión de Spawns:** Cuando un mapa no incluye 8 puntos de equipo (`TeamSpawn`), el mod utiliza automáticamente los `PlayerSpawn` libres como reserva para garantizar que los 8 arqueros aparezcan correctamente en la arena.

---

## Créditos y Agradecimientos

- **[Jonesey13 / TF-8-Player](https://github.com/Jonesey13/TF-8-Player):** Autor del mod original de 8 jugadores para TowerFall, cuyo diseño e investigación de mapas e interfaz sirvieron como base directa e inspiración fundamental para este port. Los mapas de niveles incluidos se basan en su mod original, habiendo sido retocados y ajustados para solucionar problemas específicos de aparición (*spawns*) presentes en dicha versión.
- **[FortRise](https://github.com/FortRise/FortRise):** El framework y cargador de mods oficial de la comunidad para TowerFall.
