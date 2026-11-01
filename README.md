# Jagti Outbreak

An original zombie survival game set on the IIM Jammu campus at Jagti, built in Unity 6 (Universal Render Pipeline) and tuned to run on a laptop with Intel Iris Xe graphics.

- Plan: https://claude.ai/artifact/7T3xj4y35T2h2AhZ9Ptzqj
- 2D vision map (layout reference): https://claude.ai/artifact/HmWJswKQuVajR4iMNxvyMN

## Current phase: 0, grey-box campus

You can walk the whole campus from the gate to Sindhu Hostel. Every building is a grey block placed at its traced footprint and storey count, with roads, lawns, the track, courts and the boundary wall. Floating labels name the main places.

## How to open it (first time)

1. Install **Unity Hub** from unity.com, then use it to install **Unity 6 LTS** (any 6000.0.x version). Tick "Windows Build Support" if it's offered.
2. Get the project onto your laptop. The easiest way is **GitHub Desktop**: File > Clone repository > `hclaude194-arch/jagti-outbreak`. You can also use the green "Code > Download ZIP" button on GitHub and unzip it.
3. In Unity Hub: **Add > Add project from disk**, and pick the `jagti-outbreak` folder. If Hub asks which editor version to use, pick your Unity 6 LTS.
4. Open it. The first open takes a few minutes while Unity downloads packages.
5. When it finishes, the project sets itself up: it switches on the laptop graphics settings and builds `Assets/Scenes/Campus_Greybox.unity`, then opens it. Watch the Console for `[Jagti] Built grey-box campus`.
6. Press **Play** (the triangle at the top).

If the scene doesn't appear, use the menu **Jagti > Build Grey-box Campus**. If everything looks pink, use **Jagti > Set Up Laptop Graphics (URP)**.

## Controls

| Key | Action |
| --- | --- |
| WASD | Walk |
| Mouse | Look |
| Shift | Sprint |
| Space | Jump |
| F | Flashlight |
| [ and ] | Fast travel between campus places |
| H | Hide or show the help overlay |
| Esc | Free the mouse (click to capture it again) |

The overlay in the top-left shows your frame rate and the nearest place. Please send the frame rate you see when walking around outdoors.

## How the project is built

- `tools/layout/generate_layout.py` holds the campus geometry traced from the vision map and writes `Assets/Data/campus_layout.json` (1 unit = 1 metre, origin at the central roundabout).
- `Assets/Editor/CampusGreyboxBuilder.cs` turns that JSON into the scene.
- `Assets/Editor/JagtiProjectSetup.cs` sets up URP for integrated graphics and builds the scene on first open.
- `Assets/Scripts/` holds runtime code: the first-person controller, labels and the test overlay.
- Models, textures and audio are stored with Git LFS (see `.gitattributes`).

To change the layout, edit `generate_layout.py`, run `python3 tools/layout/generate_layout.py`, then use **Jagti > Build Grey-box Campus** in Unity.
