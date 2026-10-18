# Shadow Ruins — Unity project (Legacy Input)

This is the native Unity/C# starter port of Shadow Ruins. It uses Unity's built-in Legacy Input Manager and deliberately does **not** depend on the `com.unity.inputsystem` package, which was throwing an editor-initialization NullReferenceException in the supplied log.

## Open the project

1. Open Unity Hub and choose **Add project from disk**.
2. Select this `Shadow_Ruins_Unity_Project_Legacy_Input` folder.
3. Wait for Unity to import and compile.
4. Open `Assets/Scenes/Main.unity` and press **Play**. Play mode will continue until you stop it manually. If the scene isn't generated on first import, use **Shadow Ruins → Create or Repair Main Scene** from the Unity menu.

## Controls

- WASD / arrow keys: move
- Mouse: look
- Space: jump
- Left Shift: sprint
- C / Ctrl: crouch
- Left mouse: sword swing
- Q: spend 14 mana to reveal numbered spectral side ledges for 8.5 seconds; collect sigils in number order (wrong order resets progress)
- E: use arcane bolts on nearby bridge sockets and glyphs in sequence
- R: cast a temporary Stone Step; on crystal trials, land on it and jump again to reach the crystal
- Z: stand on the PAST seal and anchor an echo, then reach PRESENT before the timer runs out
- T: bind time while standing on the orange crumbling ledge
- F3: toggle FPS display
- Escape: pause / resume

## Included

- Procedural 100-chamber platforming campaign foundation
- Main menu, pause/options screens, and persistent settings
- Procedural castle/platform geometry, hidden spectral side ledges, numbered sigils, checkpoints, and spells
- Restored puzzle trials: ordered glyphs, arcane bridges, a timed PAST-to-PRESENT echo, high crystals requiring Stone Step, and crumbling ledges requiring Chrono Bind
- The ten-chamber puzzle pattern repeats through the 100-chamber campaign, with puzzle objectives shown in the HUD and gate feedback
- Articulated first-person arms, sword model, and basic animations
- Five bundled soundtrack WAV files with the grouped-repeat playlist rule
- Audio, physics, and IMGUI built-in modules enabled in `Packages/manifest.json`

## Why the Input System package was removed

Your error log showed the Input System package throwing a `NullReferenceException` in `InputSystem.InitializeInEditor`, repeated throughout the Editor log. This project avoids that package entirely:

- `Packages/manifest.json` does not list `com.unity.inputsystem` or the invalid `com.unity.modules.input` ID.
- `ProjectSettings/ProjectSettings.asset` sets `activeInputHandler: 0` (Legacy Input Manager).
- Keyboard and mouse controls use `UnityEngine.Input`.
- Mouse X/Y axes are defined in `ProjectSettings/InputManager.asset`.

Please open this copy in a **new folder**, rather than copying only the scripts into the previous project. Unity may otherwise retain the old package resolution in its existing `Library` cache. The project has been checked structurally, but it has not been launched in Unity in this environment.

Input settings fix (revision): `ProjectSettings/ProjectSettings.asset` uses Unity's actual serialized `activeInputHandler: 0` property to select Input Manager (Old). The earlier file used an invalid `m_ActiveInputHandler` name, which Unity ignored. The project manifest intentionally does not include `com.unity.inputsystem`. If you previously opened this project, close Unity and import this extracted revision into a new folder without copying the old `Library` folder.

## Puzzle progression

The gate now remains locked until the current chamber's required trials are complete. Sigil chambers require spending 14 mana on Q to reveal spectral side ledges for 8.5 seconds; collect the numbered sigils in order. The reveal can be cast again after the timer ends, and collected sigils stay collected unless you touch one out of sequence. Required objectives vary by chamber: collect the indicated sigils, activate and cross marked bridges, strike glyphs in the sequence shown in the HUD, link the PAST and PRESENT seals, claim the high crystal using Stone Step when present, and bind the orange ledge with Chrono Bind when present. The glowing portal changes to green when every required objective is complete.
