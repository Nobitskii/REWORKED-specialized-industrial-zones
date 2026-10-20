REWORKED SPECIALIZED INDUSTRIAL ZONE

Adds specialized industrial zones to Cities: Skylines II, so you can zone industry for one resource chain instead of all of it.

ZONES
- Forestry
- Agriculture
- Ore
- Oil

Each zone only attracts companies and buildings that work with that industry's resources. Place Agriculture zones near farms, Oil zones near oil fields, and so on. The normal Industrial zone is untouched, so you can still use it for general industry.

HOW IT WORKS
Specialized zones are filtered by the resources a building makes, stores or sells. A company's inputs aren't checked, so a factory that uses imported materials can still appear in a zone that matches its output. The zones are saved in a JSON file you can edit:
ModsData/ImprovedSpecializedZones/SpecializedZones.json

CHANGES FROM THE ORIGINAL
- Based on a rework of the original Specialized Industrial Zones.
- Only the four general zones are created by default. The "Manufacturing only" and "Warehouses only" variants from the original are turned off, which means fewer prefabs for the game to create.
- Faster prefab scan at startup, and the mod writes timings to its log.
- Fixes for a few small bugs, including a crash if the mod's files can't be found and an extra reload after a config upgrade.

IMPORTANT
- This mod REPLACES the original Specialized Industrial Zones. Do not run both at once, because they share the same names.
- Cities that used the original's "Manufacturing only" or "Warehouses only" zones will lose those zones while they are off.
- Back up your saves before adding any mod.
- Tested on Cities: Skylines II version 1.6.2.

CREDITS
Original mod by DillonN (MIT License):
https://github.com/DillonN/specialized-industrial-zones
This version continues his work. His original license and copyright notice are included.

BUG REPORTS
Please include your game version and the mod's log file from Logs/.
