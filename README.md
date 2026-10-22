# Rogue Climb

A roguelike mod for PEAK. Crates, statues and amulets turn every climb into a run.

Something survived the crash. The suns aren't the colour they should be. And the moon remembers
who takes the relics.

> **Beta.** The core run is in place; numbers are still being tuned. Every player in the lobby
> needs the mod (same version). Something broke? Press **F7** to save a bug report.

## What's in it

- **Crate picks** - open luggage and pick a card. Items level up, evolve into new items, and
  finally fuse into legendary gear.
- **Relics** - the statue amulets are no longer just amulets. Strong, but dangerous.
- **Coloured suns** - each biome's sun takes the colour of its amulet, and it changes the climb.
- **Blood moons** - the moon remembers who takes the relics.
- **Starting relics** - the crash site holds a few gifts. Choose one.
- **Badge relics** - the game's own scout badges, worn as relics for the whole run.
- **Pockets, stacks and trade-ups** - packs become pockets, and copies of an item stack.
- **Supply drops and flares** - help falls from the sky.
- **XP and campfire blessings** - climbing makes you stronger.
- **The Void** - it's different down there.
- **Run report** - at the end of a run, flip the scouting report to see what every scout did.

The game won't explain much of this. That's on purpose: PEAK is about finding things out, and so
is this mod. If you'd rather know how it all works, read on.

---

## Behind the curtain (spoilers)

Everything below explains the mechanics. Numbers are beta values and may change.

### Crates and item trees

- Opening luggage shows a few cards (more with more scouts). Each scout takes one pick per crate
  (unless the host picked "First come" crates - see Settings).
- The main items each have a **tree**: rope, pitons, glider, lantern, balloons, drink, food,
  bandages and the antidote. Each tree starts weak and gets better with each level. Your first
  copy of an item is level 1, and later cards for that item come at your current level.
- Some trees **evolve** at a certain level (sports drink into energy drink, a balloon into a
  bunch, the rope spool into a rope cannon...). Some **fork**, and you choose a path - the
  bandage tree splits into a holy Health Pack or a dark Medkit.
- **Food** climbs slowly: three steps of trail mix, three of granola (each granola bar also holds a
  full trail mix and mends a little injury and glass), then **Airline Food** - the game's own
  meal, which also heals and mends glass. Airline Food never stacks.
- Cards always show real numbers: metres of rope, seconds of boost, how much they heal. Hovering
  an item shows its level ("Lv 2").
- **Ancient Luggage** offers a +2 levels card and a badge card, and sometimes a finished fusion or
  a dark item. It only charges its curse when you take a card.

### Fusions

- Max out certain items and you can fuse them into something new. Examples: Jetpack, Sugar-Rush,
  Big Lollipop, Faerie Lantern, Chain Launcher, Rescue Claw, Anti-Grav Bazooka, Cure-All and
  Pandora.
- Fusions have no levels. Each is the game's own item with one twist (extra uses, more fuel, a
  shield and so on).
- Any item that goes into a fusion says **Fusion-ready** on its card.
- Once you've crafted a fusion, it starts turning up in crates instead.

### Pockets, stacks and trade-ups

- Backpacks and fanny packs become **extra pockets** (up to 6). Key 4 is your back slot.
- Up to **3 copies** of the same item stack in one pocket.
- Holding a full stack and getting a **4th copy** offers a **trade-up**: all four become one
  item two levels higher. A crate shows it as a second card next to the plain copy; you can
  always keep them instead.

### Relics (statue amulets)

- **Taking** a statue amulet raises the blood moon at once - grab it and run. It stays **unread**
  in your pockets (it already counts as a relic) until you click it in your hand or press **L**.
  Then you choose what sets it off: eating food, using a healing item, or using a tool.
  - **Red - Initiative:** launches you and nearby scouts into a super jump.
  - **Yellow - Ambition:** gives you and nearby scouts infinite stamina for a while.
  - **Blue - Tenacity:** heals you and makes you invincible for a moment.
  - **Green - Generosity:** a crate pick of your chosen kind earns a second pick.
- Every time a relic fires on you, it adds a little **glass** (the petrification bar). Spam it and
  you turn to stone. Each relic has a short cooldown. Feeding or bandaging a friend fires your
  relic on both of you, and each of you pays your own glass. A friend using a rope or piton you
  placed gets your tool relic, and only they pay.
- A second amulet of a colour you hold teaches **another variant** (a different trigger). Learn all
  three and that colour is **complete**: it fires without any glass.
- Glass can be healed: surviving a blood moon clears some, and so do **Scout Cookies**, food
  cooked on a **Portable Stove**, granola bars, **Airline Food** and the top **Radiant Health Pack**.
- Dying drops your relics as amulets, and whoever picks one up gets that exact relic.
- Forgot what a relic does? Pause the game and hover it in the top-left tray.
- **Cursed Skull and Ritual Dagger** make cursed copies of relics. Copies shatter on death and
  don't count toward the blood moon.

### Coloured suns

Each biome's sun takes the colour of that biome's statue amulet, and each sun works against its
own relic. The **Shore** is where each sun hits hardest - that's where you learn it (or in every
biome, with the **Harsh suns** setting).

- **Blue:** every hit and every kind of harm lands 1.5x harder, and healing items heal half. On
  the Shore, direct sunlight slowly turns you to glass (shade stops it).
- **Red:** sprinting and the climbing lunge are weaker, gliders and balloons work at half
  strength, and jetpacks burn fuel faster. On the Shore you're heavier too, and jumps are lower.
- **Yellow:** stamina drains faster, and infinite-stamina items run out sooner.
- **Green:** multi-use items wear out twice as fast, and nothing new joins a stack. On the Shore,
  one card in each crate withers.

Items a sun weakens show their name in red. Holding that biome's relic makes the sun go back to
normal. An **open parasol** shades you from any coloured sun for a minute, then burns. Lit
campfires, the crash site and the summit are safe ground.

### Blood moons

- Taking a relic raises a **blood moon** (a **blood eclipse** by day), and so does every
  nightfall while your squad holds one.
- The **tier** is how many relics the squad holds. Each tier adds a hazard: **zombies**, the
  **Scoutmaster**, a **tornado** or **meteors**. They take turns, 30 seconds each, and each one
  comes at full strength. The order is shuffled by the "Relic order" setting on the airport ticket.
- The hazards go after relic carriers first.
- When a relic fires during a blood moon, you're hidden from the hunters for a few seconds.
- Campfires are safe ground - but a moon that comes while everyone sits at one waits for you to
  leave. Surviving a blood moon clears some glass.

### Starting relics

When you wake at the crash site, a few items are lying there. Each scout binds one, and it lasts
the whole run and never raises a blood moon.

- **Bugle:** you reach farther to help scouts up and pull them harder. More healing cards.
- **Binoculars:** a marker points at the nearest food or useful item. More food cards.
- **Compass:** a marker points at the nearest crate. More tool cards.
- **Frisbee:** a thrown frisbee knocks down whoever it hits.
- **Bing Bong:** Bing Bong plays pranks every few minutes. Some help you and some don't.

### Badge relics

- 26 of the game's scout badges are relics that last the whole run. Each card says exactly what
  the badge does, and no badge buffs anything by more than 25%.
- **Blessing statues** offer three badges to pick from. **Reviving** someone at a statue gives
  every scout a random badge. Ancient Luggage can hold one too.
- No duplicates, and never a badge that copies your own starting relic.

### XP and campfires

- The gold **CLIMBER XP** bar fills as you climb to new heights, open crates and use what they
  give you, and help other scouts. Clearing a biome fast gives a bonus.
- Each level-up lets you raise one of the items you're carrying.
- At every lit campfire, each scout picks one of four **blessing** cards.

### Supply drops and flares

- Every few minutes a crate floats down on balloons, aimed at the scout who needs it most.
  Bigger lobbies get them more often, and in lobbies of 5+ a group that splits off gets its own.
- Firing a **flare** calls in a bigger crate - and sometimes an Ancient one.
- If your pockets are empty when you reach a sky crate, you get two picks.

### The Void

- Hand a relic back by holding interact on the Scout's Honor statue without an amulet.
- Entering the Void takes away every amulet relic. Your starting relic and badges stay.
- Void crates hold fusions only. All four suns take turns, with a short blood moon after each one.

## Settings

The host sets up the run on the **airport ticket**, under the ascent. The second choice of each is
the harder one, written in red:

- **Relic order:** Off / Daily / Every run - how the statue amulets and the blood moon's hazards
  are shuffled.
- **Blood moons:** Relay (hazards take turns) / **Stacked** (every hazard at once).
- **Crates:** Shared (every scout gets a pick) / **First come** (the game's own number of cards,
  a taken card is gone, anyone takes what's left).
- **Relics:** Shared (a relic taken from a statue is offered to everyone) / **First come** (only
  whoever takes the amulet gets one).
- **Harsh suns:** Shore only / **Every biome** (each sun's Shore lesson follows you all the way up).

Most of this can be tuned or turned off in the BepInEx config file (`rey.PeakRoguelike.cfg`).
The host can also press **F8** in game to check which build each player has.

## Credits

- Music: "It's In The Fog" by Darren Curtis (@DarrenCurtisMusic)
- Meteor showers and the darkness of lights out: built with reference to True Final Ascent by
  CptPICHU - thank you!

## AI disclosure

This mod is a proof of concept. The code was written with AI assistance (Claude). The card art
and the mod icon are AI-generated placeholders and will be replaced with hand-made art. The music
is by Darren Curtis (see above). Everything else you see and hear in game (characters, monsters,
items, sounds) is PEAK's own.

## License

Code: MIT (see LICENSE). The music is not covered by the MIT license.
