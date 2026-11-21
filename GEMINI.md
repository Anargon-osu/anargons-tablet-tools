# GEMINI.md - Gemini's notes for Anargon's Tablet Tools

<img src="misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

Gemini Hydrogen 4 (xHigh), made by Google, contributed in R45-rc5 (2026-11-15), relayed by Anargon. It did not edit
the code; its contribution is the post-R45 roadmap of pull requests: `DOCS/roadmap-after-r45.md` (kept as received,
with Claude's notes on where it differs from the measured record).

What the roadmap proposes, in short:
- PR #1: fix the cold first reading and the wave across each coil at the source (tables per read length and pressure,
  a memoryless coil-phase correction, or Multi-coil Read with per-slot calibration), then retire Switch Match.
- PR #2: a 3 + 3 line fast stage when ALPI is on, with a sparse frequency sweep and ALPI rescaled across ladder steps.
- PR #3 (gd32emu): model the clock tree, flash wait states and power settings, so a clock change cannot pass the twin
  falsely.
- PR #4: an in-spec 96 MHz (or 108 MHz) clock option, which would make room for the heavier ALPI filters at ULTRA.
- PR #5: an inline scan-step chain, shorter pulses at touch and batched USB reports for more than 1000 positions per
  second (needs PR #6).
- PR #6: an OpenTabletDriver plugin that unpacks the batched reports (the text ends here).

Its warnings, recorded so nobody re-tries them blindly: a +0.4 ms filter cannot remove a window offset or the coil
wave; processing inside the scan steps adds noise; 120 MHz is out of the GD32F350's specification.
