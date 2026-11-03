# CONTENT HOUSE

**A 24-Hour Subathon Gone Wrong** — a first-person horror / stealth / black-comedy game inspired by *Fears to Fathom*. You play the overworked assistant of a mega-streamer. Chores, a phone, a blackout, an intruder in a stolen mascot suit, and a double-twist ending.

- **Genre:** First-person horror / narrative thriller / stealth
- **Target playtime:** 18–22 minutes
- **Engine:** Unity 6000.3.27f1 (Built-in render pipeline)

---

## Status

**Early playable prototype.** Core systems are implemented and a playable house scene exists; the game is in active bug-fixing and art-migration phase.

### Working
- Procedural scene generation (menu: `Content House → Generate House Scene`) — full two-story house + basement, furniture, lights, interactables, NavMesh bake, all gameplay wiring
- Main menu with player-name customization (defaults to **Steve**) and mic on/off toggle
- First-person controller: walk / run (`Shift`) / crouch (`C`), mouse look
- Interactive phone (`Tab` or `P`): Flashlight app, Messages, CCTV feed, battery drain across Acts I–II
- Microphone noise detection while hiding (RMS threshold 0.15, green/yellow/red meter) with a **hold-breath fallback** (`Space`) when no mic is selected
- Enemy AI state machine (NavMesh): Patrol 1.8 m/s / Investigate 2.5 m/s / Pursue 4.2 m/s, 0.5 s line-of-sight buffer, hearing via a global noise event bus, force-opens doors, scripted closet beat
- All 4 acts: chores (delivery → mini-fridge → drink delivery, router + breakers, laptop waveform minigame, window/patio locks), blackout, kitchen false alarm, glass break, stealth climb, double-twist cutscene, desk-shove + window-smash QTE escape, epilogue
- All sound effects loaded from free CC0 packs; rain/thunder/siren/heartbeat are synthesized at runtime (no audio files needed)

### In progress / known issues
- **House art migration:** the current cube-shell house is being replaced by Loafbrr's CC0 *House & Props* pack. 192 parts are already converted to `Assets/Loafbrr/OBJ/` (see `Tools` below); the builder that composes the chosen house + re-wires gameplay is the next milestone.
- Furniture from the Kenney furniture kit is placed with an auto-scale/auto-recenter pass — spot-check proportions after each regeneration.
- Balance pass pending (enemy detection ranges, phone battery curve).

## Versions

| Component | Version |
|---|---|
| Unity Editor | 6000.3.27f1 (6.3 LTS stream) |
| Render pipeline | Built-in |
| com.unity.ugui | 2.0.0 (built-in package) |
| com.unity.ai.navigation | 2.0.16 |
| Active input handling | Input Manager (Old) |
| Color space | Gamma |

## Getting started

1. Open the project in **Unity 6000.3.27f1** (exact version recommended).
2. If `Assets/Scenes/Main.unity` is missing or you changed layout code, run the menu item **`Content House → Generate House Scene`**. This rebuilds the whole level, wires every component and bakes the NavMesh (takes ~30–60 s).
3. Open `Assets/Scenes/Main.unity` and press **Play**.
4. In the main menu, enter your name (or leave Steve), choose whether to use your microphone, and start the subathon.

### Controls
| Input | Action |
|---|---|
| WASD | Move |
| Mouse | Look |
| `E` | Interact / leave hiding spot |
| `C` | Crouch |
| `Shift` | Run (works in any direction, cancels crouch) |
| `Tab` / `P` | Phone |
| `Space` | Loud breathing (mic-off fallback while hiding) |

## Architecture

Everything is code-built; there are no serialized scene UI or prefabs to inspect — the scene is generated and wired by the editor tooling.

| Area | Files | Notes |
|---|---|---|
| Core | `Assets/Scripts/Core/` | `GameConfig.cs` (**all names & tuning — change these any time**), `GameManager`, `StoryManager` (act-by-act sequencer), `PowerSystem` (blackout), `NoiseSystem` (event bus) |
| Player | `Assets/Scripts/Player/` | `PlayerController`, `PlayerInteraction` (raycast `[E]`), `FlashlightController` |
| Mic | `Assets/Scripts/Mic/MicLoudnessDetection.cs` | RMS loudness, thresholds, zones |
| Phone | `Assets/Scripts/Phone/PhoneController.cs` | Builds its whole canvas in code |
| Enemy | `Assets/Scripts/Enemy/` | `EnemyAI` (Patrol/Investigate/Pursue + scripted closet guard), `PatrolWaypoint` |
| Interaction | `Assets/Scripts/Interaction/` | Doors (hinged, lockable, slam, ajar-peek while hiding), windows (lock/shatter), pickups, router/breakers/fridge/laptop/knock, `HideSpot` |
| CCTV | `Assets/Scripts/CCTV/CCTVCameraFeed.cs` | RenderTexture feeds + smash-to-static |
| UI | `Assets/Scripts/UI/` | `HUDController`, `MainMenuUI`, `EpilogueUI`, `QTEController`, `UIFactory` (code-built uGUI) |
| Audio | `Assets/Scripts/Audio/` | Hybrid `ProceduralAudio`: real CC0 clips first, synthesis fallback; `AmbienceController` (rain, thunder, heartbeat, sirens) |
| Editor tools | `Assets/Editor/` | `HouseBuilder.cs` (level generator), `SceneDiagnostics.cs`, `PackageAdder.cs` |
| Tools (repo root) | `Tools/gltf_to_obj.ps1` | glTF → OBJ converter used for the Loafbrr pack |

## Resources used (and their licenses)

| Asset | Author / Source | License | Used for |
|---|---|---|---|
| Furniture Kit (140 FBX models) | Kenney — https://kenney.nl/assets/furniture-kit | CC0 | Furniture throughout the house |
| Impact Sounds (130 SFX) | Kenney — https://kenney.nl/assets/impact-sounds | CC0 | Footsteps, glass, wood/metal impacts |
| RPG Audio (51 SFX) | Kenney — https://kenney.nl/assets/rpg-audio | CC0 | Door creaks/slam, latch, knife draw |
| Interface Sounds (100 SFX) | Kenney — https://kenney.nl/assets/interface-sounds | CC0 | Phone UI, clicks, glitches, errors |
| House & Props pack (gltf/blend) | Loafbrr — https://loafbrr.itch.io/ | CC0 | (superseded by the house below) |
| House with rounded neighbourhood | user-provided download | verify before distribution | The house, doors, windows, collider model |
| Creepy Horror UI Sounds (10 SFX) | walrussoundqueen (itch.io free) | Free royalty-free (no resale as pack) | Narration typewriter, locked-door rattle, game-over stinger, CCTV static, phone whoosh, dread confirm, knife reveal |
| Rain / door slam / fridge / doorbell / chime | "Kaino" (user-provided, likely Pixabay) | verify before distribution | Rain ambience, closet slam, fridge chore, knock, message chime |
| Dark Tension Rising Music | FreeHorrorGameKit (Asset Store freebie) | Asset Store terms | Act III+ stealth tension bed |
| Flashlight switch | Rivgo Flashlight System (MIT, © 2025 Pavlo Panko) | MIT | Phone flashlight toggle |
| Peeing sound (`freesound_community-peeing-88718.mp3`, trimmed to `Resources/Sfx/peeing.wav`) | freesound_community (Pixabay/Freesound) | verify before distribution | Hold-to-pee toilet task |
| Gamer setup models (monitor, PC, keyboard...) | user-provided download (itch.io freebie) | verify before distribution | Mr. IG's battlestation |
| Special Elite font | Google Fonts (Apache 2.0) | Apache 2.0 | Narration screens |

Attribution is not required for any of the above (CC0) but is provided here gladly. Rain, thunder, siren and heartbeat sounds are synthesized in code (`ProceduralAudio`) — no third-party audio for those.

## License

- **Project code & docs:** MIT — see [LICENSE](LICENSE).
- **Third-party assets:** remain under their own licenses (all CC0), credited above.
