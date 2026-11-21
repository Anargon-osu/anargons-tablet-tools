# Historical R36 reference preset verification

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Five reference images were generated from the complete `2025-02-14_release001` firmware for revision 36. Each passed boot, USB, gain-control, pen-detection and required model measurements in that historical run. R37 source handoffs contain no prebuilt firmware images or executables. Build all five presets from your own full dump (stock, or with another mod's known changes, which are undone first) with `AnargonsTabletTools main.bin output/ --all`. See [verification](verification.md) for fixture provenance and current coverage.

Startup frequency calibration and bootloader writes are off. Position interpolation is on. STOCK is a scan-rate preset with the tool's normal fixes; it is not a factory-firmware restoration image.

Use your tablet's own original stock dump when building firmware. These reference images were not tested on a physical tablet in the recorded run. A passing simulation is not a hover-height measurement.

Generated non-STOCK firmware contains a timestamp-derived build identifier. Rebuilding the same defaults can change that four-byte field without changing instructions or scan parameters. It is unused with Bootloader Changes off; counter-enabled builds use it to recognize new firmware and require separate comparison checks.
