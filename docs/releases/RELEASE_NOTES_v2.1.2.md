# MystMC v2.1.2 — Runtime Resource Compatibility Fix

## Fixed
- Added a dedicated `Themes/Compatibility.xaml` dictionary for native/legacy functional pages.
- Fixed runtime `'{resource}' resource not found` errors such as `DangerButton` on Scheduler.
- Added theme-aware compatibility styles for action buttons, secondary buttons, section headers, field captions, navigation styles, quick-tool styles, semantic brushes, and legacy pixel-icon resources.
- Kept the v1.8.8 MystUI/theme layer authoritative; the old `DesignSystem.xaml` is not loaded wholesale.
- Compatibility styles use the active MystUI theme resources so Dark/Light/Minecraft/Underworld/Ender/Aether theme switching remains intact.

## Why this occurred
The v2.1 native pages retained semantic resource names from the proven v1.5.x functional UI. The new v1.8.8 shell did not load the old design dictionary, so those keys were only requested when individual pages were opened. That allowed the application to compile and start while failing at runtime on tabs such as Scheduler.
