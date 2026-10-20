REWORKED SPECIALIZED INDUSTRIAL ZONES (BETA)

*** BETA: this mod needs more testing. Please back up your saves and report any problems. ***

Adds specialized industrial zones to Cities: Skylines II, so you can zone industry for one resource chain instead of all of it.

ZONES
- Forestry
- Agriculture
- Ore
- Oil

Each zone only attracts companies and buildings that work with that industry's resources. Place Agriculture zones near farms, Oil zones near oil fields, and so on. The normal Industrial zone is untouched, so you can still use it for general industry.

HOW IT WORKS
Zones are filtered by the resources a building makes, stores or sells. A company's inputs aren't checked, so a factory that uses imported materials can still appear in a zone that matches its output. The zones are saved in a JSON file you can edit:
ModsData/ImprovedSpecializedZones/SpecializedZones.json

CHANGES FROM THE ORIGINAL
- Based on a rework of the original Specialized Industrial Zones.
- Only the four general zones are created by default. The "Manufacturing only" and "Warehouses only" variants are turned off, which means fewer prefabs for the game to create.
- Faster prefab scan at startup, and the mod writes timings to its log.
- Fixes for a few small bugs, including a crash if the mod's files can't be found and an extra reload after a config upgrade.

HELP ME TEST
This is a beta, and I've only tested it on my own setup (game version 1.6.2). I'd love to hear from you, especially if you have:
- Saves made with the original Specialized Industrial Zones mod
- Lots of mods or industrial asset packs
- A big city with slow load times (I'm still checking whether this version helps)

Things I want to know:
1. Do all four zones grow buildings?
2. Does your save load, and do the buildings stay after you save and reload?
3. How long does your save take to load, compared to the original mod?
4. Did anything break, vanish or show errors?

HOW TO REPORT A PROBLEM
Please include:
- Your game version
- A list of your other mods, if you can
- The mod's log file from Logs/ (the file for this mod)
- What you were doing when it happened

Report it on the mod's comments page or on GitHub: (https://github.com/Nobitskii/improved-specialized-industrial-zones)

IMPORTANT
- This mod REPLACES the original Specialized Industrial Zones. Do not run both at once, because they share the same names.
- Cities that used the original's "Manufacturing only" or "Warehouses only" zones will lose those zones while they are off.
- Back up your saves before adding any mod.

CREDITS
Original mod by DillonN (MIT License):
https://github.com/DillonN/specialized-industrial-zones
This version continues his work. His original license and copyright notice are included.
