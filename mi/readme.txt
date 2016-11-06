This is my modified version of https://github.com/irungentoo/Xiaomi_gamepad

Modifications made:
- Pushed left limits for left/right stick to -32768 (was -32767);
- Rewritten main app workflow, so it can be run on system start with controller auto-detection and auto-mapping.
Enjoy!

Original description:
Xiaomi Gamepad to Xbox 360 controller Mapper using SCPbus.

Features:
-Rumble support
-Actually works well
-Tested on windows 10 by an actual gamer (not me) and he said there was no delays and everything is good.



Libs used:
https://github.com/mogzol/ScpDriverInterface
https://github.com/mikeobrien/HidLibrary/ (modified it a bit)


This helped a bit:
https://github.com/aadfPT/MiControllerPluginForInputMapper
https://github.com/aadfPT/EAll4Windows
