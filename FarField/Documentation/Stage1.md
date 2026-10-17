# FARFIELD — Stage 1 editor world

## Get the world into your saved scene

1. Open this project in Unity **6000.6.6f1** and wait for script compilation.
2. Open `Assets/Scenes/SampleScene.unity`. Keep the original `FarFieldStage1Backup` scene as a fallback.
3. Exit Play mode. Choose **FARFIELD > Build World In Editor** from Unity's top menu.
4. The menu adds a bootstrap if needed and builds **FARFIELD Generated World** in the active scene. Expand that object in the Hierarchy to edit the terrain, roads, buildings, five dishes, plants, rocks and lamps.
5. Select `Open Plateau Terrain` and press **F** with the pointer over Scene view to frame it. The existing Main Camera is preserved; Game view is not automatically reframed during editing.
6. Press **Ctrl+S** (Cmd+S on macOS) to save the scene. Generated meshes/materials are stored under `Assets/FarFieldGenerated`; commit these assets and their meta files along with the scene.
7. Press Play. Runtime initialization reuses saved scenery and starts the existing player, sky, dish and console systems. Stop Play: the scenery remains.

Subsequent builds and **Clear Generated World** are buttons on the **FARFIELD Bootstrap** Inspector. Rebuilding replaces manual edits inside the owned generated root; move objects outside it if you want them to survive. Every build first saves a uniquely named `_BeforeWorldBuild` scene copy, including unsaved scene edits. Both build and clear support Undo. Clear removes only roots carrying a marker owned by that bootstrap. Asset files are deliberately retained for Undo and backup scenes; do not delete assets still referenced by a scene.

No generation runs on Inspector refresh or script recompilation. Editor generation does not create a player, camera, listener, sky system, volume or EventSystem, and does not change the existing camera or directional light. The original runtime camera/light handoff remains Play-only. `Build On Start` must remain checked for normal runtime initialization.

## Verification checklist in Unity — still required

- Console has no compile errors after import.
- Build twice: exactly one owned generated root, five dishes, and no extra editor cameras/listeners.
- Clear then Undo: manual scene objects remain and generated scenery returns.
- Save, close and reopen the scene, then restart Unity: meshes, materials, dish pivots and console link remain intact.
- Play: WASD/Shift/Space/mouse work; approach the main console and press E; W/A/S/D slews, Q scans, R captures, N opens the existing notebook, Esc exits.
- Stop and Play again: scenery is unchanged and runtime objects do not accumulate.
- Walk the facility and remote roads; no oversized-triangle warnings or falling through terrain.
- Confirm the active quality tier uses its existing URP asset and objects render without pink materials.

## What changed and what was found

- `FarFieldBootstrap.cs`: explicit scenery build separated from runtime initialization; saved-world reference; unit-scale world root independent of bootstrap transform; editor-safe decorative collider removal; runtime resource cleanup.
- `Editor/FarFieldBootstrapEditor.cs`: explicit build/clear UI and menu, backup before build, resource persistence, transactional replacement, ownership-based deletion, Undo and collision-edge validation.
- `FarFieldGeneratedWorld.cs`: ownership marker.
- `DishController.cs`: serialized pivots/angles and Play initialization from saved transforms; preserves remote automatic scans.
- `ConsoleOperator.cs`: serialized dish link; runtime-only notebook/readout initialization with duplicate readout protection.
- `Scenes/FarFieldStage1Backup.unity`: unchanged copy of the uploaded starting scene with its own GUID.

`CreateTerrainMesh` uses 144 cells per axis across 1,200 metres. The defect was in `BaseTerrainHeight` and `AccumulateTerrainFlattening`: `Mathf.SmoothStep(from, to, t)` interpolates between values; it does not normalize a world distance. Normalizing with `InverseLerp` before smoothing fixes the enormous rim-height step and restores flat facility pads. A numerical reproduction of all 41,472 grid triangles found maximum edge length reduced from 2,160.671 m to 12.680 m; spawn terrain changed from 7,087.457 m to 0 m. The editor additionally rejects nonfinite or >100 m collision edges before replacing the old world.

The uploaded scene contains only Main Camera, Directional Light and Global Volume. The original bootstrap was auto-created in Play. No PSXShaderKit or alternate terrain-collider implementation is present in this upload. Existing signal/notebook functionality and render pipeline assets were preserved. Remote consoles and later visual/gameplay enhancements belong to later stages.

Unity is unavailable in the coding sandbox: compilation, actual mesh cooking, scene serialization/reload, visual appearance and Play-mode behavior remain unverified. Do not treat Stage 1 as verified until the checklist passes. If anything fails, keep the backup and provide the first Unity Console error.
