# Jagti Outbreak

An original zombie survival game set on the IIM Jammu campus at Jagti, built in Unity 6 (Universal Render Pipeline) and tuned to run on a laptop with Intel Iris Xe graphics.

- Plan: https://claude.ai/artifact/7T3xj4y35T2h2AhZ9Ptzqj
- 2D vision map (layout reference): https://claude.ai/artifact/HmWJswKQuVajR4iMNxvyMN

## Current phase: 3, survival gameplay

The campus is now a game. Zombies roam it: 18 at the start, rising to 30 the longer you survive, kept in a ring around you and spawned out of sight. They wander, come to investigate noise (footsteps carry 7 m, sprinting 18 m, a pistol shot 65 m, the shotgun 95 m), chase you when they see you (further when your torch is on), and claw at you up close. About one in seven is a runner. Headshots do 2.5x damage.

You start with a cricket bat, a pistol with 36 rounds, a bandage and a spare torch battery. Supply crates sit outside every building (about 100 in all); hold E to search one. What is inside depends on the building: medicine at the medical shops, ammunition at the guard posts, batteries and bandages in the hostels and the mess. The guard's locker at the main gate has a shotgun. Your torch battery runs down, sprinting and swinging use stamina, and Q uses a bandage or medkit. Esc pauses; when you die you can try again.

Zombies, weapons and sounds are placeholders made in code (blocky bodies, synthesized audio) until the art pass in Phase 6.

### Phase 2: landmark buildings

The main landmarks are now real models instead of grey blocks: the academic block (four wings around the courtyard, the Mandapam Auditorium drum and the east drum, a glass entrance with a portico facing the clock tower), the clock tower, the main gate (you can now drive or walk through it), Annapurna Mess, Brahmaputra Hostel, Sindhu Hostel and the other hostel blocks. They have window facades, terracotta brick, white concrete bands and sun fins, and you can't walk through them. Other buildings are still grey blocks. The boundary wall now has gaps where roads pass through.

The look (warm plaster, terracotta brick, white trim) is a best guess, because little detail about the real facades is public. Photos of the real buildings would make the next pass much closer.

The terrain is a foothill ridge. Campus levels follow the owner's description: the sports complex, courts, track and both hostels are the low part (about 29 m above the highway), the ground climbs to the mess (about 40 m) with uphill ground ringing the girls' hostel, the academic block sits a little higher (about 45 m), and a steep pine-wooded hill rises behind the basketball courts. Roads are light concrete with black-and-white kerbs, and the hostels are off-white with box-framed windows, jali stair towers and roof pergolas, as in the photos. Elsewhere the serpentine drive climbs about 30 m from the gate, steep wooded ravines drop 40 to 55 m outside the wall, and ridges up to about 175 m line the horizon (fog now starts at 150 m so you can see them). About 17,000 trees, ground paint, grass and the zombie navigation mesh came with Phase 1. The shape is a design, not survey data.

**Updating:** pull the new version in GitHub Desktop, then open the project. It notices the new data and rebuilds the scene by itself (a progress bar shows; the navigation mesh step can take a minute or two). If it doesn't, use **Jagti > Build Campus**.

## How to open it (first time)

1. Install **Unity Hub** from unity.com, then use it to install **Unity 6 LTS** (any 6000.0.x version). Tick "Windows Build Support" if it's offered.
2. Get the project onto your laptop. The easiest way is **GitHub Desktop**: File > Clone repository > `hclaude194-arch/jagti-outbreak`. You can also use the green "Code > Download ZIP" button on GitHub and unzip it.
3. In Unity Hub: **Add > Add project from disk**, and pick the `jagti-outbreak` folder. If Hub asks which editor version to use, pick your Unity 6 LTS.
4. Open it. The first open takes a few minutes while Unity downloads packages.
5. When it finishes, the project sets itself up: it switches on the laptop graphics settings and builds `Assets/Scenes/Campus_Greybox.unity`, then opens it. Watch the Console for `[Jagti] Built campus`.
6. Press **Play** (the triangle at the top).

If the scene doesn't appear, use the menu **Jagti > Build Campus**. If everything looks pink, use **Jagti > Set Up Laptop Graphics (URP)**.

## Controls

| Key | Action |
| --- | --- |
| WASD | Walk |
| Mouse | Look |
| Shift | Sprint (uses stamina) |
| Space | Jump |
| Left click | Swing or shoot |
| R | Reload |
| 1 / 2 / 3, mouse wheel | Cricket bat / pistol / shotgun (once found) |
| E (hold) | Search a crate |
| Q | Use a bandage or medkit |
| F | Torch (battery runs down) |
| Esc | Pause menu |
| [ and ] | Fast travel between campus places (for testing) |
| H | Hide or show the help overlay |

The overlay in the top-left shows your frame rate and the nearest place. Please send the frame rate you see when walking around outdoors.

## How the project is built

- `tools/layout/generate_layout.py` holds the campus geometry traced from the vision map and writes `Assets/Data/campus_layout.json` (1 unit = 1 metre, origin at the central roundabout).
- `tools/layout/generate_terrain.py` shapes the hilltop, flattens roads and building plots into it, paints the ground, and places grass and trees. It writes `Assets/Data/terrain_*.bytes` and `campus_terrain.json`.
- `Assets/Editor/CampusGreyboxBuilder.cs` turns that data into the scene; `CampusTerrainBuilder.cs` makes the Unity Terrain; `ProceduralAssets.cs` makes the ground textures and tree models from code, saved under `Assets/Generated` (rebuilt each time, not stored in Git).
- `tools/blender/build_buildings.py` builds the landmark buildings in headless Blender from the same layout data and exports `Assets/Models/Buildings/*.fbx` (small files, kept in plain Git) plus `Assets/Data/building_models.json`. `BuildingModels.cs` places them and `BuildingMaterials.cs` paints their facade textures in code.
- `Assets/Editor/JagtiProjectSetup.cs` sets up URP for integrated graphics and rebuilds the scene whenever the layout data is newer than the last build.
- `Assets/Scripts/` holds runtime code: the first-person controller, labels and the test overlay, and `Gameplay/` (zombie AI and spawner, weapons, health and stamina, inventory, loot crates, HUD, synthesized sounds). `Assets/Editor/GameplayBuilder.cs` adds these to the scene and places the supply crates.
- Models, textures and audio are stored with Git LFS (see `.gitattributes`).

To change the layout, edit `generate_layout.py` (raise `VERSION`), run `python3 tools/layout/generate_layout.py` and then `python3 tools/layout/generate_terrain.py`, and open Unity.
