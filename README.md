# Jagti Outbreak

An original battle royale (with the earlier zombie survival mode kept as a second mode) set on the IIM Jammu campus at Jagti, built in Unity 6 (Universal Render Pipeline) and tuned to run on a laptop with Intel Iris Xe graphics.

- Plan: https://claude.ai/artifact/7T3xj4y35T2h2AhZ9Ptzqj
- Battle royale phase plan: https://claude.ai/artifact/QAzo7ieAC4QWkLMhU9QijF
- 2D vision map (layout reference): https://claude.ai/artifact/HmWJswKQuVajR4iMNxvyMN

## Current phase: 5E, map for battle (untested in Unity)

- **Cover on open ground:** about 170 pieces of works-site cover across the campus lawns and open slopes: stacks of concrete drain pipes, pallets of bricks, black water tanks on a plinth, cable drums, cement bags and broken lengths of sandstone garden wall. `tools/royale/build_cover.py` measured how far every 4 m of open ground is from anything that stops a bullet (buildings, tree trunks, solid street furniture) and placed small sites at the most exposed spots until almost nothing is more than 20 m from cover (1,512 exposed spots before, 102 after, mostly on slopes too steep to build on). They are built before the NavMesh bake, so bots walk round them and use them in their cover search. The athletics field, courts and roads stay clear, and the main roads already have avenue trees.
- **Landing spots:** every named place is rated by the loot around it (light, good or rich), shown as dots on the full map. Walking, gliding or driving into a place shows its name and rating.
- **Full map (M):** bold place names, a 100 m grid with lettered columns and numbered rows for calling out positions, and a marker you place with a click (right-click clears it). The marker shows on the minimap, on a compass strip under the top bar with its distance, and over the world where it stands. The mouse is free while the map is open; you can still move.
- **Compass:** a heading strip under the top bar (N, E, S, W and every 15 degrees).
- **Performance:** small outdoor things are hidden past a set distance (bollards 90 m, bins and benches about 110 to 120 m, signs and railings 140 to 200 m, lamp posts 260 m, sign text 60 m, cover 450 m), checked a slice at a time each frame, with a few metres of give so nothing flickers. This cuts the draw calls when looking across the campus. Loot was already drawn only within 55 m and far bots already update less often.

`tools/royale/build_cover.py --preview` draws the open-ground map before and after; `--render` renders the cover models.

## Phase 5D: getting around

- **Scooters and utility carts** parked along the campus roads and in the car park, a fresh random set each match (about nine scooters and five carts, in random colours). Press **E** beside one to ride: **W/S** throttle and reverse, **A/D** steer, **Space** brake, **E** to get off (below about 30 km/h). The scooter is quick (about 80 km/h) and leans into turns; the cart is slower (about 55 km/h) but tougher. You sit on them with your hands on the bars or wheel and your feet on the board or pedals.
- **Vehicle damage:** bullets, grenades, hard landings and crashes wear them down; the panel at the bottom shows health and speed. A wrecked vehicle blows up (hurting anyone close), burns and smokes. Riding into people hurts them, and engines are loud enough for bots to hear. You can't shoot while riding, and your rider is still hittable.
- **Zip lines:** steel towers on the steeper slopes (up to five lines, placed where the cable clears the ground, buildings and trees), shown as orange dotted lines on the map. Press **E** at either tower: downhill is fast, uphill a small motor pulls you; **S** slows you, **Space** lets go (you drop and take fall damage if high).
- **Vaulting and climbing:** **Space** in front of a low wall, railing, sill or crate vaults over it if there is room beyond, or climbs on top of anything up to about 1.9 m.
- **Water:** the campus has no ponds, only the water channels, the cascade basins and the fountain, so there is no swimming; you wade through them more slowly, and the splashing can be heard.
- Vehicles show on the minimap and the full map within range.
- Bots don't use vehicles, zip lines or vaulting yet (they walk the NavMesh); they do get out of the way of parked vehicles.

`tools/royale/preview_vehicles.py` renders the scooter, cart and a zip line from the C# model code; `tools/royale/preview_getting_around.py` maps where the zip lines and parking spots should go.

## Phase 5C: arsenal and feel

- **Ten guns**, all original, named after places in Jammu: Ravi P9 and Basohli .45 (sidearms), Chenab SMG, Banihal 12 pump and Bhaderwah Double (shotguns), Tawi AR, the three-round-burst Ujh Carbine and the 75-round Udhampur LMG (rifles), Jhelum DMR and the bolt-action Pir Panjal (marksman). Each has its own model, sound, handling and aim-in time. The LMG, the DMR and the bolt-action rifle are rarer, and turn up more in better tiers and in supply crates.
- **Recoil patterns:** each gun climbs and drifts the same way every spray, so you can learn it and pull against it. Crouching, aiming, a fore grip and a compensator all tame it.
- **Attachments**, fitted automatically to the right gun when you pick them up and shown on the gun in your hands and on the bots: red dot, 2x sight, 4x and 8x scopes, compensator, suppressor (quieter, fewer tracers, heard from closer), fore grip and extended magazine. They drop beside a gun when you drop or swap it.
- **Camera:** over the right shoulder by default with your own body visible, and first person when you aim (V switches). Shots in third person go from your head to what the crosshair covers, and the camera pulls in rather than going through walls. **C** or **Ctrl** crouches (steadier aim, smaller target, lower view).
- **Smoke grenades (4):** a thick screen for about twenty seconds that bots can't see or shoot through. Bots throw them too when caught hurt in the open.
- **Feedback:** damage numbers over the people you hit (yellow for headshots, blue when armour soaked it), an armour-broken call-out and sound, an elimination banner, a crosshair that opens as you fire, red dot reticles, spent cases flicking out, a red frame and heartbeat at low health.

`tools/royale/preview_guns.py` renders the gun and attachment models straight from the C# model code.

## Phase 5B: opponents

The bots are now people worth fighting.

- **Bodies:** each bot is a jointed person (16 bones, one skinned mesh, a few draw calls) with a face, hair, short sleeves, belt and shoes, and a mix of skin tones, shirts and hair. Feet are placed by leg IK as they walk, run, crouch and crouch-walk; both hands hold the real gun model (arm IK), low at the ready or up at the shoulder when aiming. They flinch when hit and fall when killed. Vests, helmets and backpacks show their level (khaki, olive, black; pouches, brims, visors, bigger packs). Far bodies update less often and stop casting shadows.
- **Cover and corners:** in a fight a bot searches the walkable edges around it (walls, low walls, buses, furniture) for a spot the enemy can't see. Behind low cover it crouches to hide and stands to shoot; behind a corner it hides, then steps out to fire and back. It reloads and heals in cover, and moves on if it is flanked.
- **Reacting to gunfire:** shots passing close make a bot duck, turn towards the shooter and spoil its aim. It hears fights from further away at higher difficulty, creeps in crouched when close, crouches for long rifle shots, and lobs grenades (with the right arc, flatter indoors) at anyone hiding from it.
- **Difficulty:** Recruit, Regular or Veteran, chosen in the pause menu or on the results screen (applies from the next match). It sets skill spread, reaction time, aim, how often bots use cover and grenades, and how far they see and hear.
- **Budget:** thinking is shared out (six bots think per frame, two cover searches per frame), bots far from you think less often and skip obstacle avoidance.

`tools/royale/preview_bodies.py` renders the bodies in Blender (a port of the body code) to check them without Unity.

## Phase 5A: battle royale match core

The game is now a last-one-standing match on the whole campus, against 29 bots. Build it with **Jagti > Build Campus** (the layout version rose to 15, so the scene rebuilds); **Jagti > Game Mode** switches between Battle Royale (default) and Zombie Survival before a build.

- **Drop:** a helicopter crosses the campus on a random line. Space jumps (you are pushed out at the far edge), W dives, S slows; the glider opens with Space below 500 m or by itself at 130 m. Steer with the mouse and W/S onto a roof, lawn or doorway. The camera follows from behind until you land.
- **Loot:** 3,374 loot spots on free floor in every room (`tools/royale/build_loot_points.py`), about half filled each match. What a room holds depends on what it is: hostel rooms, classrooms, offices, the mess, the medical centre, the sports complex, stores and the bank strong room. Items come in four tiers (grey, green, blue, gold rings).
- **Arsenal:** five original guns (Ravi P9 sidearm, Chenab SMG, Banihal 12 shotgun, Tawi AR, Jhelum DMR with a scope), four ammo types, vests and helmets at levels 1 to 3, backpacks, bandages, medkits, a chai flask that heals over time, and grenades. 1 to 3 or the wheel select, LMB fires, RMB aims, R reloads, E picks up, X drops, Q heals, Z drinks chai, G throws.
- **The haze:** six rounds close a brown haze onto a random safe circle on walkable ground. Outside it you lose health every second, more each round, and armour does not help.
- **Bots:** 29 bots drop with you, loot the nearest rooms, move with the haze, hear shots and fight with reaction time and aim error by skill.
- **Supply drops:** two crates fall by parachute with red smoke during rounds 2 and 4, holding gold gear.
- **HUD:** players left, kills, haze timer, kill feed, minimap and a full map (M) drawn from the terrain and layout, health and armour, slots, prompts, and a results screen with your placement.

Everything is made in code (bodies, helicopter, guns, sounds), as before. Nothing in this phase has been run in Unity yet.

## Phase 4D: shops, medical centre, sports complex and outdoor detail

The last group of buildings can now be entered, and the campus outside has street furniture.

- **Shopping and medical blocks:** the plaza fronts of the three blocks by Annapurna Mess now have shop fronts: wide glazed openings with rolled-up shutters, a concrete canopy and a blue fascia board with each shop's name. A paved walk with planters and garden lights runs along them to the mess. Each block has a stair in a back corner (with a back door) up to two more floors.
- **Medical centre (block 1):** a waiting hall with a reception counter, rows of steel waiting chairs and a TV; two consulting rooms (doctor's desk, examination couch behind a folding screen, eye chart, medicine cabinet), a dressing room and toilets. Upstairs a seven-bed ward and an observation room with hospital beds, lockers, drips and screens, the duty doctor's room, the nurses' station and a linen store; on top medical records, physiotherapy, the medical officer's room and a staff room. On the plaza front: a pharmacy (counters, shelves of medicines, a vaccine fridge) and an ATM vestibule with two cash machines.
- **Shops (blocks 2 and 3):** stationery and photocopy shop, mobile repair counter, general store (stocked shelving, drinks coolers, checkout), barber (mirror stations and barber chairs), fruit and juice bar, and a cafe with tables. Upstairs: a bank branch with tellers' counters and a strong room, a bookshop, a laundry and press with a tailor's shop, and a computer and reading room.
- **Sports complex:** a full-height hall with four marked badminton courts, nets, benches, a scoreboard and high-bay lamps; a lobby with reception and a trophy cabinet, men's and women's changing rooms (lockers, benches, showers, WCs), an equipment store and first aid. Upstairs a gym (treadmills, bikes, power racks, benches, dumbbells) that looks down into the hall through a long window, and on top an indoor games room (table tennis, carrom, chess) and a yoga room.
- **Outdoors:** lamp posts along the roads, benches and twin bins along the paths, round the lawns and outside the hostels, cycle stands, a name board for every named place, finger signposts at the main junctions, steel railings where the ground drops steeply beside a road, chain-link fences round the hostel courts with basketball hoops and volleyball nets.

## Phase 4C: walk-in hostels and Annapurna Mess

Every hostel can now be entered and explored on all seven floors: Brahmaputra, Hostel U, the three chain blocks and Sindhu.

- **Entrances:** each wing has a front door with a concrete canopy and steps, in the bay beside a jali stair slot. Glass doors stand open inside it, and the security desk and notice board are in the entrance lobby. The warden's office is next door.
- **Floors:** one room behind every window pair, so each room has its window and curtains. Wide wings have a corridor running round a central core of washrooms, bathing areas, a pantry and a common room; narrow wings have a corridor down the middle. A dog-leg stair rises behind the jali screens (you can see the lattice from the landings) to the top floor. Where two wings meet, a passage joins their corridors, so you can walk right through Sindhu or round Hostel U without going outside.
- **Rooms:** a single room has a cot with a checked sheet, a pillow and folded blanket and a steel trunk underneath, a study table with a shelf of books, a lamp and a laptop, a chair, a steel almirah with a suitcase on top, a bookshelf or pinboard, a ceiling fan and a tube light. The corner rooms are double rooms, study rooms or common rooms, and on the ground floor the common room (TV, sofas, carrom), the games room (table tennis) and the reading room.
- **Washrooms and services:** WC cubicles and basin counters, shower stalls with a geyser, bucket and mug, white wall tiles; pantries with a kitchenette, fridge and water cooler; laundries with washing machines and clothes horses hung with washing; stores; jali sit-outs with benches and plants.
- **Annapurna Mess:** a double-height dining hall with rows of long steel tables and benches laid with thalis, hand-wash troughs by the west door, water coolers, a plate return, and serving counters (rice, dal, sabzi, curry) under hatches through the kitchen wall. Doors on the west and south fronts. Behind, the kitchen has cooking ranges with big vessels under steel hoods, prep tables, double sinks, racks and fridges, and a service door at the back.

As in the academic block, only your storey and the ones just above and below are drawn, and only nearby room lights are on.

## Phase 4B: upper floors, stairs and the Mandapam auditorium

The whole academic block can now be explored, not just the ground floor.

- **Stairs and lifts:** a dog-leg stair in each of the four corner pavilions climbs from the ground to the pavilion's glazed top floor (level 4), with lift doors beside it on every floor. The pavilions' walls are solid stone below the window row, as on the model, so they make natural stair cores.
- **Floors 1 to 3:** the same ring corridor and partitions as the ground floor, with each floor's own windows (two slender windows per bay, the blue glass ribbon right round the top floor). The rooms change from floor to floor: more case rooms, classrooms and seminar rooms, a library reading hall and library stacks, a finance lab, a board room, the faculty lounge, programme, accounts, placement, alumni and dean's offices, a club room, a music room, an exhibition hall and an archive. Toilets stack on every floor. Over 200 rooms in all.
- **Top floor of the pavilions:** a glass-walled lounge in each, looking out over the roofs.
- **The Mandapam auditorium:** 474 maroon upholstered seats in a fan of curved rows round a curved stage, raked up to a balcony at first-floor level. Ground-floor doors from the gallery lead to the side aisles; first-floor doors from the auditorium foyer open onto the balcony. The stage has a dais table, a lectern, speakers, a big projection screen and velvet drapes; timber acoustic panels line the walls, and a clerestory glass band runs round the curved wall under the ceiling.

Only the floor you are on and the floors just above and below it are drawn, so being inside the building costs about as much as it did with one floor.

## Phase 4A: walk-in academic block

The academic block's ground floor is now a real building you can walk into. Doors open at the glass bay under the porch (into a double-height vestibule and the entrance lobby), in the middle of each courtyard side (out to the garden paths), and in the curved east front (into the double-height "block 1" foyer facing the amphitheatre).

Inside, a 3 m corridor rings the courtyard, with a second corridor in the deep south wing. There are 55 furnished rooms:

- **Teaching:** six tiered horseshoe case rooms with curved desk rows, three classrooms, two seminar rooms and eight syndicate rooms.
- **Offices and labs:** faculty cabins, the admin office, a meeting room, two computer labs, a server room and a research room.
- **Shared spaces:** the reading room lined with bookshelves, the cafeteria, student lounges in the corner pavilions, the activity room, a gallery, toilets and stores.

Each room has its own furniture, about 4,100 pieces in all: desks, office chairs, lecterns, whiteboards, projectors and screens, ceiling fans, AC units, bookshelves, sofas, plants, notice boards, doors and toilet cubicles. Windows have clear glass on the inside and sills.

Room lights switch on around you as you walk (within about 24 m), and the furniture is only drawn when you are in or near the building, so the rest of the campus runs as before. The renderer switches to Forward+ so a room can have more than eight lights. Zombies can follow you in: the navigation mesh now covers the corridors and rooms.


## Phase 3: survival gameplay

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

### Campus bus

The white shuttle bus runs a loop on its own: Brahmaputra Hostel, Academic Block, Main Gate (via the serpentine drive), back past the Academic Block to Annapurna Mess and the hostels and sports courts, and round again. It waits at each stop. It starts at the stop beside where you spawn.

| Key | Action |
| --- | --- |
| E (at the door, left side behind the front wheel) | Get on while it is stopped, or drive it if you parked it |
| E (inside) | Get off |
| T (inside) | Swap between riding and driving |
| W / S | Accelerate / reverse |
| A / D | Steer |
| Space | Brake |
| C | Swap between the driver's seat view and a view from behind |
| G | Horn |

When you get off after driving, the bus stays parked where you left it. Get back on and press T to hand it back to its driver, who carries on along the route. Weapons and searching are put away while you are aboard. Zombies in the road are knocked down if the bus is moving faster than walking pace; slower than that, they stop it.

The overlay in the top-left shows your frame rate and the nearest place. Please send the frame rate you see when walking around outdoors.

## How the project is built

- `tools/layout/generate_layout.py` holds the campus geometry traced from the vision map and writes `Assets/Data/campus_layout.json` (1 unit = 1 metre, origin at the central roundabout).
- `tools/layout/generate_terrain.py` shapes the hilltop, flattens roads and building plots into it, paints the ground, and places grass and trees. It writes `Assets/Data/terrain_*.bytes` and `campus_terrain.json`.
- `Assets/Editor/CampusGreyboxBuilder.cs` turns that data into the scene; `CampusTerrainBuilder.cs` makes the Unity Terrain; `ProceduralAssets.cs` makes the ground textures and tree models from code, saved under `Assets/Generated` (rebuilt each time, not stored in Git).
- `tools/blender/build_buildings.py` builds the landmark buildings in headless Blender from the same layout data and exports `Assets/Models/Buildings/*.fbx` (small files, kept in plain Git) plus `Assets/Data/building_models.json`. `BuildingModels.cs` places them and `BuildingMaterials.cs` paints their facade textures in code.
- `tools/blender/build_interiors.py` (run after `build_buildings.py`) builds the walk-in interiors: the interior shell (floors, ceilings, walls with window openings, partitions with doorways, tiered case rooms) as `Assets/Models/Buildings/*_Interior.fbx`, one small FBX per kind of furniture in `Assets/Models/Props/`, and `Assets/Data/interiors.json`, which lists every room, piece of furniture and light. `InteriorBuilder.cs` places them and `InteriorMaterials.cs` paints floors, walls and furniture in code; `InteriorZone.cs` hides the interior and switches off its lights when you're away.
- `tools/blender/build_outdoor.py` (run after `build_buildings.py` and the terrain) makes the street furniture models and `Assets/Data/outdoor_props.json`, which says where each lamp post, bench, sign and railing stands and what each sign says. `OutdoorBuilder.cs` sets them out on the terrain and writes the sign text.
- `Assets/Editor/JagtiProjectSetup.cs` sets up URP for integrated graphics and rebuilds the scene whenever the layout data is newer than the last build.
- `Assets/Scripts/` holds runtime code: the first-person controller, labels and the test overlay, and `Gameplay/` (zombie AI and spawner, weapons, health and stamina, inventory, loot crates, HUD, synthesized sounds). `Assets/Editor/GameplayBuilder.cs` adds these to the scene and places the supply crates.
- Models, textures and audio are stored with Git LFS (see `.gitattributes`).

To change the layout, edit `generate_layout.py` (raise `VERSION`), run `python3 tools/layout/generate_layout.py` and then `python3 tools/layout/generate_terrain.py`, and open Unity.
