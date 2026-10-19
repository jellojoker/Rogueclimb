# Changelog

## 0.12.20
- Binoculars and Compass relics no longer drop a world ping. A label floats over the nearest target instead ("Crate 42m", "Granola Bar 18m"), visible through terrain and fog, pinned to the screen edge when it's behind you. Range 10m to 250m.
- Binoculars only find helpful things: food, healing, Magic Beans and fungi. Never explosives.

## 0.12.19
- Starting relics: the Bugle, Binoculars, Compass, Frisbee and Bing Bong at the crash site each offer a relic. Every scout binds one, and the curio stays put so everyone can choose from all of them. They are normal relics: they never raise or add to a blood moon, and they last the whole run.
  - Bugle: your helping hand reaches twice as far, scouts you carry weigh half as much, and more healing turns up in crates you open.
  - Binoculars: every 10 seconds the nearest item lying out in the world is pinged for you, and more food turns up in your crates.
  - Compass (the Pirate Compass too): every 10 seconds the nearest unopened crate is pinged for you, and more tools turn up in your crates.
  - Frisbee: anything you throw knocks back whatever it hits, scouts and creatures alike.
  - Bing Bong: he wants to play.
- Your starting relic shows in the relic tray in a silver ring.

## 0.12.18
- Flare drops float down on balloons too (no more crate slamming down on your head), and land a few steps further in front of you.
- A plane passes overhead before every supply drop: the loading screen's plane, heard crossing the sky as the crate is let go.
- The Lantern shows a card the first time you pick one up, and the Flare's card says what it's good for.

## 0.12.17
- Supply drops: every 2.5 minutes out on the mountain (not at a campfire, the crash site or during a blood moon), a crate floats down on balloons near the scout carrying the least. It lands on flat ground with open sky above it and smokes until you open it.
- Open a supply crate with empty pockets and you take two cards.
- A lit flare now slams down a big crate, and 1 in 5 times an Ancient Luggage that glows instead of smoking.
- Flare, Bing Bong, Binoculars, Frisbee and Compass show a card the first time you pick each one up in a run (a placeholder for powers to come).
- Relics are now either cursed or normal. Only cursed relics raise blood moons and count toward their tier; every statue relic is cursed.

## 0.12.16
- Flares: carry an unlit flare and crate screens get a REROLL button (or press R). The flare is spent and the whole crate is dealt again, for everyone still to pick.
- Light a flare anywhere but the peak and a supply crate falls from the sky beside you, with that biome's loot. At the peak a flare still calls the helicopter.
- The log now records when storms (rain, snow, wind) start and stop.

## 0.12.15
- Small gold "+XP" pop-ups above the XP bar whenever you earn XP from anything but climbing: crates, crate items you use up (or share with a friend), the fast-clear bonus and the campfire. Gains close together share one pop-up; a faint "XP maxed" shows once when a biome's crate or item XP runs out.
- The fast-clear toast in the middle of the screen is gone (the pop-ups replace it).

## 0.12.14
- Special cards sparkle: level-up picks glow gold with a gold edge and gold sparkles, evolutions sparkle white, fusions purple, relics in their gem colour, cursed copies dark and slow. Plain item cards stay plain.
- Picks from a level-up get a gold banner, and the level-up cards open with a whoosh.

## 0.12.13
- Bug reports: press F7 to save one on every modded player's PC (screenshot, the last 5 minutes of log, the run's state and your config) in BepInEx/BugReports. Whoever pressed it can type what happened.
- The first mod error in a run saves a report by itself.
- The host collects everyone's unsent reports from the F8 panel, anywhere; other players can send theirs from the airport.

## 0.12.12
- Fixed: merging a third copy (including the Rope Cannon evolution) ate your stack and handed out nothing.
- Fixed: a picked item joining a lower-level stack now raises the stack to the level you were offered.
- Green sun: crate cards no longer promise stacks or merges.
- Blood moons: zombies rise at tier 1, the Scoutmaster joins from tier 2 (hunts at tiers 2-3, only watches at 4).
- The Scoutmaster only grabs scouts on his level, and never appears above you.
- Crate screens close if you're moved 5m away or caught by a tornado.
- The fast-clear clock starts once the campfire is lit, and stops when you reach the next one.
- Scouts without Rogue Climb are named in a toast (with the Thunderstore package), can't spill crates open for everyone, and are left out of crate picks, relic sharing and zombies.
- Joining a run late: crates opened before you came keep their cards and picks (no rerolls).
- Relics dropped on death land where the scout last stood on solid ground; the host drops them if the scout's game doesn't.
- Shared relic pickups are on by default (everyone gets a relic choice when one is bound).

## 0.12.11
- Safe ground holds: every campfire counts, lit or not. Blood moon zombies won't come for anyone resting there, and the Scoutmaster won't grab them.
- The hunting Scoutmaster freezes whenever anyone looks at him. Look away and he comes - running from afar, walking the last stretch. Right on you, he waits a moment... then a second after you turn your back.

## 0.12.10
- Relics: "Movement" is now "Tools". Letting go of any rope counts (the mountain's own too), pitons count even on a quick hold, and the Chain Launcher, Rescue Claw and anti-ropes are tools now.
- Blood moons come for everyone, relic carriers first. Tier 1 brings a zombie for every scout; tier 2 brings more for relic carriers, and the Scoutmaster doesn't stop at one throw.
- The Scoutmaster's static is gone. No more red screen filter: a blood moon lights the mountain red.
- Level-ups offer up to 4 cards, from anything you've found this run - pick one you're not carrying and you get one.
- Climbing pays more, and the fast-clear bonus lasts longer. The Kiln now counts. Caldera and Gloom reward exploring: new ground pays.

## 0.12.9
- The XP bar is now Climber XP, and every level makes something you carry stronger.
- Crates hand out items. Find a third of something you're stacking and they merge into one, a level higher.
- A relic taken in the morning still brings its moon that night.
- The fast-clear bonus moved up beside the build number.
- The higher you climb, the luckier the mountain gets.

## 0.12.8
- The Lantern answers to healing relics.

## 0.12.4 - 0.12.7
- XP: a gold bar beside your stamina. Each biome, climbing higher, opening crates and using up what crates give you each fill up to 2 levels; reaching the campfire fast adds a bonus. Every level is another crate pick, offered once you stand on flat ground.
- The Scoutmaster changes with the moon. Tier 1: he hunts, and throws each scout once. Tier 2: he watches - don't stare back. Tier 3 and up: he only watches. He is always somewhere you can see him, and his throws carry less far.
- Blue sun: everything that hurts you hurts 1.5x more. No more cold sunlight.
- Green sun: items wear out twice as fast, ropes fray and pitons crack (was blue). No more food storms.
- No sun changes hunger any more.
- Yellow sun on the Shore: pushing on with no stamina makes you drowsy (no more slips).

## 0.12.3
- Green sun: nothing new stacks while it burns (stacks you have stay); singles merge back at sunset or when its relic is taken. Generosity carriers are spared.
- Green sun on the Shore: everything you carry weighs 1 more.
- Green sun food storms every 2 minutes (was 50s); rained food vanishes after 20s. Existing configs are updated once.
- Only crate and luggage items stack - wild food (berries, coconuts, mushrooms) never does.

## 0.12.2
- The sun is named the first time it reaches you - awake, off the crash site or a campfire, no sunscreen.
- Blood moon: two zombies rise beside each relic, skull or dagger carrier, on opposite sides.
- Cursed Skull: every other scout also gets a random cursed relic (a colour they lack).
- Ritual Dagger: a kill where neither side carries a relic gives the killer a random cursed relic. Its heal is never cut by a sun.
- Raised by the Book of Bones after a skull or dagger death: a random cursed relic.

## 0.12.1
- Blood moons rise every night once the squad holds a relic, and at once when a relic is taken.
- Brighter, deeper sun colours: sunlight colour 100%, sky colour 80%. Existing configs are updated once.
- The airport ticket's relic order picker matches the ticket's own lettering and arrows.
- Shuffled statues show the amulet, not a giant hand.
- Relic order stays Off by default (the game's own order).

## 0.12.0
- Health tree reworked: Bandages cleanse ailments; the holy path becomes a Health Pack that heals now, the dark path a Medkit that turns injury and glass into poison or sleep.
- New fusions: Cure-All (thrown, it doses everyone close) and Pandora's Lunchbox.
- Antidote turns up in every biome's crates; the Book of Bones turns up in cursed luggage.
- Rope tree is a straight line; anti-ropes are fusions with the Balloon Bunch.
- Crafting a Skull or Dagger never raises a moon.

## 0.11.10
- Red sun away from the Shore cuts sprint and lunge only; heavy gravity stays on the Shore.
- Yellow sun on the Shore: slips last 1 second.

## 0.11.9
- Relic order on the airport ticket: Off (the game's), Daily or Every run.

## 0.11.5 - 0.11.8
- Item stacking works (up to 3, by level).
- Blood moon waves come all at once; a zombie rises beside every relic, Skull or Dagger carrier.
- The Scoutmaster walks while you can see him, sprints when you can't, stares before he strikes, and never grabs inside the tornado.
- Packs at full pockets stay on the ground.

## 0.11.4
- Stacking actually works now: fresh items (crate picks, most ground items) were never allowed to join a stack.
- Sugar-Rush is marked by the host before pickup, so it never merges into a stack of plain Sports Drinks.

## 0.11.3
- Stacking: every stackable item holds at least 2 (level 1-2 = 2, level 3+ = 3); snacks Trail Mix and Granola stack to 2.
- F8 > World: Sunlight colour goes to 150% (past 100% the light is deeper than the sun's colour).

## 0.10.9
- Item names show their level on hover, pick-up and the backpack wheel ("Rope Spool 3", "Rope Cannon 5"); snacks show their food level.
- Taking a relic raises a blood eclipse by day or a blood moon by night at once; nightfall alone is calm.
- Green sun: every bite of food that fills hunger fills exactly 10% of the bar (GreenFoodBite).
- Coloured suns no longer dim the world: screen wash removed, brighter sky and sun colours.
- Log line whenever Tenacity fires (healing, invincibility, sun) to track the lava report.

## 0.10.8
- Blue sun on the Shore: sunlight cold now fills as fast as a high ascent's night at dusk (much gentler); shade still thaws it.
- Lantern levels shrink its glow (warmth, protection and light reach together) instead of its brightness.
- Balloons fly: level 2+ lifts you at one steady speed, a max bunch rises exactly one max rope; level 3 ties two balloons; jump in the air to pop one for a hop.

## 0.10.7
- First Thunderstore test release as Rogue Climb.
- Item cards show real numbers (metres, seconds, easy fractions); items do exactly what their card says.
- One PEAK font across every screen; calm level-up banner with the campfire chime.
- Blood moon text uses the game's own lettering, in red.
- Dev tools are off by default.
