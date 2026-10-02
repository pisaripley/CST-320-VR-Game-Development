# CST-320 VR Game Development

Unity VR puzzle project with controller and hand-tracking support.

## Open the project

1. Clone this repository.
2. In Unity Hub, add the repository folder as a project.
3. Open it with **Unity 6.3 LTS (6000.3.25f1)** and allow Unity to import assets and restore packages.
4. Open `Assets/Scenes/SampleScene.unity`.

The repository includes the complete `Assets`, `Packages`, and `ProjectSettings` folders. Unity regenerates its `Library`, `Temp`, and other local cache folders.

## Play

The project uses OpenXR and is configured for Meta Quest, including hand tracking tested through Meta Quest Link. Connect the headset through Link before starting Play in the editor. Read the welcome instructions and select **BEGIN**.

- **Controllers:** left thumbstick moves; right thumbstick snap-turns. Aim the left controller at the floor and click its thumbstick to teleport. Press the left X button for settings. Use the trigger to select and drag.
- **Hands:** aim the left hand at the floor and pinch to teleport. Hold the left palm open, flat, and facing up for settings. Point and pinch with the right hand to select and drag.
- **Settings:** master-volume and puzzle-tone sliders, plus return-to-start and close buttons.

## Puzzles

- **Echo Orbs:** repeat the flashing colour pattern.
- **Lights Out:** follow the blue path to the second station. Selecting a tile changes it and its adjacent neighbours; turn all nine tiles blue.

Both puzzles support controllers and hand tracking.
