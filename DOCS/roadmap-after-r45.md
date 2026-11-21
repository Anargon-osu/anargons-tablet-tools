# Roadmap after R45: the proposed pull requests

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

**Author:** Gemini Hydrogen 4 (xHigh), Google, relayed by Anargon on 2026-11-15 during R45-rc5. Credited in
`AGENTS.md` section 2 and `GEMINI.md`. This is a **proposal**: nothing in it is built unless a revision's history
says so, and its numbers are Gemini's reading of this repository's records, not new measurements. The text below is
as received, except for the heading levels and its punctuation, turned into plain ASCII (the project's rule for
every file); it ends part-way through PR #6, where the message that carried it ended.

## Claude's notes on the proposal (R45-rc5)

Where the proposal and the measured record differ, the record wins (AGENTS.md section 1). Points to know before
starting any of these:

- **The R45-rc4 (2) result on the tablet** is Anargon's impression after flashing it: the switch steps are "less
  noticable, but still not perfect". The rc4 (2) build has not been measured with the ruler yet; the 0.68-0.79 mm
  ruler numbers are from the rc4sm build before Window Scale.
- **Dynamic Anti-Chatter** (the +0.4 ms filter the proposal advises against): Anargon asked for it in R45-rc5, so
  rc5 builds it as a setting and compares it with the source-level route of PR #1 (see `DOCS/CHANGELOG.md`). The
  proposal's concern is real: offline, on raw positions before ALPI, it triggered in 32-44 % of frames.
- **Processing inside the scan steps:** Anargon pointed out that anything switching during a scan step adds noise
  (R45: switching the LED mid-scan raised ULTRA's noise 1.2-1.4x). R45-rc5 only measures how much CPU time is free
  inside a frame; nothing is moved into the scan.
- **Clock:** the GD32F350 is rated to 108 MHz, so the proposal's 96 / 108 MHz limits (and leaving out 120 MHz) match
  the datasheet. Anargon is interested in 96 MHz for later.
- **3 + 3 lines:** the 1545 frames/s figure was built without pressure, buttons or tilt (R45-rc3 Part 14).

---

## The proposal, as received

This is a verbose breakdown of the remaining work required organized into **major pull requests** following the project's release. `PR #X` convention (`README.md`, `AGENTS.md` section 4) and the two-repository split (`anargons-tablet-tools` vs. `gd32emu`, `AGENTS.md` Rule 9).

> **Note on PR numbering:** Per `AGENTS.md` section 4, replace `PR #1`..`PR #7` with the actual GitHub pull request numbers on `Anargon-osu/anargons-tablet-tools` and `Anargon-osu/GD32EMU` when opened, and add a `history/<model>-prX.md` entry + update `CLAUDE.md` in each.
>
> **Scope excluded intentionally:** Per the analysis above, **binary "+0.4 ms Dynamic Anti-Chatter" (`noise > 0.10 mm`)**, **mid-scan CPU task offloading during coil bursts**, and **120 MHz overclocking** are left out of these PRs - reasons are noted under the relevant PRs so the next contributor doesn't re-try them blindly.

---

### PR #1 (`anargons-tablet-tools`): Source-Level Cold-Read & Coil-Wave Position Calibration (Replace / Retire `Switch Match`)

#### Goal & Motivation
Your `R45-rc4 (2)` hardware check confirmed that `Window Scale` + gated `Switch Match` only smooths out the step at coil transitions while leaving the underlying spatial position distortion untouched. The `15 cm` ruler test (`history/claude-rev45.md` Part 16.7) showed **0.68-0.79 mm peak-to-peak repeatable error** (`0.25-0.32 mm` slow warp + **0.33-0.35 mm** coil-scale wave across every 5.08 mm pitch), well above your `0.1 mm` target. Meanwhile, `Switch Match` (`atf_switchmatch.cs`) only hides switch steps behind a 2.5 mm / 500 ms ramp and previously caused slow still-pen drift at coil midpoints (`0.30 mm` sd, `2.7 mm` p-p in `rc4 (1)`).

#### Root Cause Being Addressed
1. **Cold first reading (`history/claude-rev45.md` Part 16.3):** Every line in a 5-coil window except the first read is measured while the pen still rings from the previous step. On ULTRA fast stage, the five slots read off by `X: -0.44 / -0.05 / +0.03 / +0.07 / +0.08` and `Y: -0.16 / -0.06 / +0.01 / +0.06 / +0.06`, varying with read length, drive step (`C[3]`), and pen frequency (pressure).
2. **`Window Scale` (`atf_winscale.cs`) is currently ULTRA fast-stage only:** Its 9 pressure knots (`KX`/`KY`) were fitted solely from Anargon's 6 ULTRA drags in stage 0, leaving `RAPID`, `FAST`, and Steady Hover ladder steps (`VB[0] != 0`) uncorrected - and it does not fix the intra-coil wave (`0.16 mm X / 0.22 mm Y` on drags, `0.35 mm` on the ruler).
3. **Tilt-frame cold-line offset (`DOCS/CHANGELOG.md`):** On tilt frames, the outer line is read first, giving tilt frames their own uncalibrated bias when hovering/touching.

#### Proposed Changes
1. **Hardware A/B baseline (`rc4 (2)` check before changing code):**
   * Compare `Window Scale` ON + `Switch Match` ON vs. **`Window Scale` ON + `--no-switch-match`** on 45 degrees ruler drags and on the noisy midpoint on hardware (`experiments/noise45/ruler.py`, `vendorlog.exe`).
2. **Extend & generalize `Window Scale` (`atf_winscale.cs`) + intra-coil correction:**
   * Record `slotfit` / drag / ruler datasets using the silent line recorder across **FAST**, **RAPID**, **ULTRA**, and key **Steady Hover ladder steps** (and across `C[3]` drive steps and tilt frames).
   * Test two source-level fixes offline on the recorded line data first (never guessing gains - remember `gainfix.py` showed `1%` gain error moves a switch jump `0.15 mm`):
     * **Option A:** Multi-stage `Window Scale` tables indexed by `(axis, ladder_step_or_read_length, pressure)` + a memoryless **1D coil-phase correction table** (correcting the repeatable intra-coil S-curve wave inside `[0, 1018)`).
     * **Option B:** Revisit `Multi-coil Read` (`atf_multicoil.cs`) with **per-slot cold-read calibration** measured directly per read length/pressure/drive step, instead of assuming equal response across all 5 read slots.
3. **Retire or demote `Switch Match` (`atf_switchmatch.cs`):**
   * Once source correction reduces switch jumps below `~0.05-0.08 mm` across presets, remove `Switch Match` from default-ON (`smatch` default `false` or removed), eliminating any risk of midpoint drift or velocity-line estimation glitches.
   * **Note for contributors:** Do **not** replace `Switch Match` with a hard-switched `+0.4 ms` moving average (`noise > 0.10 mm`): at 1000 Hz even an unweighted 2-sample average is `+0.50 ms`, raw ULTRA touching noise already sits near `0.116 mm X` (causing constant threshold toggling and `speed * 0.4 ms` transition snaps), and a 0.4 ms filter cannot remove persistent window offsets or spatial waves.

#### Presets & Settings Affected
* Affects `FAST`, `RAPID`, `ULTRA` (especially `wscale`, `smatch`, `multicoil`, and Steady Hover ladder steps).
* Requires running `tools/mksig.py` if any new firmware hook addresses are introduced.

#### Verification & Risks
* **Twin:** Bit-exact C# comparison (`McTwinBoard.ws` extended across stages/tables); note that `gd32emu` has no physical cold-reading ring-down carryover across separate line steps, so twin tests prove arithmetic equivalence and safety, **not** jump removal.
* **Hardware:** Must be validated on a real tablet using straight-edge ruler passes (`experiments/noise45/ruler.py`) and 45 degrees drags before declaring the `0.1 mm` target met or missed.
* **Risk:** Very low (application-only position post-math or table lookups; zero bootloader changes).

---

### PR #2 (`anargons-tablet-tools`): `3+3` Fast-Stage Scan Mode & ALPI Dynamic-Rate Rescaling

#### Goal & Motivation
Unlock **`3+3` line scanning** (`6` scan steps/frame instead of `10`) exclusively when **ALPI** is active, while keeping **`5+5` lines** for non-ALPI calibrated builds, pen search, and fallback/Steady Hover stages. Even at stock `72 MHz`, `3+3` drops frame scan work from `850 us` (`1,140 Hz`) to **`620 us` (`1,545 Hz`)** (`history/claude-rev45.md` Part 14), creating massive CPU/frame headroom inside 1,000 Hz ULTRA *and* laying the scan foundation for >1,000 Hz OTD batching later.

#### Root Cause & Architectural Details
1. **Why `3+3` works with ALPI (`atf_apip.cs`, `atf_multicoil.cs`):**
   * Stock `0x08005432` computes `(P1 + P2) / 2`, where `P2` is the 3-line parabola (`c-1, c, c+1`) and `P1` is the 4-line ratio (`c-2` or `c+2`).
   * Dropping `c-2` and `c+2` leaves `P2`, which has a repeatable periodic S-curve error across each `5.08 mm` coil pitch - **and ALPI's 16-segment coil-distance warp table (`W[0..16]`) is already designed to measure and flatten periodic coil-pitch distortion at zero latency.**
   * In the twin (`speed-limit.txt`), `3+3` preserved hover range (`25.7 mm` vs `25.8 mm` for `5+5`) because outer coils sit near the `~72/conversion` floor anyway.
2. **Firmware Changes (`atf_sched.cs`, `atf_build.cs`, `atf_apip.cs`, `atf_cli.cs`):**
   * **Scan gating (`start_x`, `start_y`, `inc_x`, `inc_y`):** Skip lines `0, 1, 5, 6` (scan only `2, 3, 4` = `c-1, c, c+1`) in stage 0 when `3+3` mode is enabled, while keeping `5+5` during search, Steady Hover / Reach stages (optional toggle), and near edge coils (`coil <= 2` or `>= N-1`) where `P2` needs margin.
   * **Position call (`0x08005432` or lightweight `P2` wrapper):** Return `P2` directly (or guard against `c-2`/`c+2` garbage affecting `P1`) when in `3+3` mode so `P1` never reads stale buffer entries from previous frames!
   * **Re-tune `Coil Hold` (`atf_sched.cs` lines 290-308):** Currently `Coil Hold` checks `min(L1..L5)` (`c-2..c+2`). With 3 lines, `min(L1..L3)` is `~0.70x` peak instead of `~0.115x` peak. Update `Coil Hold` to compute margin against either the known floor (`Floor(reads, fullRes)`) or an adjusted 3-line threshold so re-centering doesn't stick too far past midpoints.
   * **Sparse / interleaved frequency sweep:** Instead of removing pressure and buttons completely (like Part 14's `speed-limit.txt`), schedule **1 sweep step every 4-8 frames** (`~10-21 us/frame` amortized instead of `84 us/frame`) so tip pressure and barrel buttons stay active while freeing ~`65-75 us/frame`.
   * **ALPI training alignment (`o.apipTrain`):** Ensure the ALPI training build uses the exact same `3+3` fast-stage scan and `P2` math so `W[]` and `N0[]` learn the true `3+3` distortion and noise profile.
   * **ALPI dynamic-ladder velocity & gain rescaling (`atf_apip.cs`):** In `apfilt`/`apx`, whenever the dynamic Steady Hover ladder (`VBX+8`) changes step (`dt_old -> dt_new`), rescale the velocity accumulator **`v = v * dt_new / dt_old`** (and scale step gains across ladder rungs) so lifting off a fast `3+3` stage into a slower `Steady Hover` rung never creates a cursor velocity spike/lag glitch.
   * **UI/Settings (`atf_build.cs`):** Add setting (e.g. `scan3x3` / `Compact 3-Line Scan`, unlocked only when `ALPI` is active and calibrated/trained on `3+3`, keeping `5+5` for non-ALPI calibrated builds).

#### Presets & Settings Affected
* `ULTRA`, `RAPID`, `FAST` with `ALPI` enabled (standard non-ALPI builds stay `5+5`).
* Affects `Coil Hold`, `Window Scale` (disabled or re-fitted for `3+3`), `Multi-coil Read` (mutually exclusive with `3+3`), `ALPI` training/filtering, and the dynamic Steady Hover ladder.

#### Verification & Risks
* **Twin:** Verify `3+3` builds pass `Cli.Gate` on both February and December dumps, verify `P1` stale-buffer isolation (`XBUF`/`YBUF` poisoned with old values doesn't leak into `P2`), verify `Coil Hold` mid-point stability, and verify ALPI `v` rescaling bit-for-bit against updated `Apip.Axis` in C# across ladder transitions.
* **Hardware:** Check `3+3` + ALPI training on tablet vs `5+5` + ALPI (`ruler.py`, still midpoint, fast flicks across multiple coils, edge coils, and barrel button latency with sparse sweep).
* **Risk:** Low-moderate (application flash only, no bootloader changes; main risk is edge-coil accuracy or missed re-centering during ultra-fast flicks if `Coil Hold` margin is too sticky).

---

### PR #3 (`gd32emu`  -  Prerequisite in Sibling Repository): RCU Clock-Tree, `FMC_WS` Wait States, `PMU` Power, and Dynamic Cycle/Timer Modeling

#### Goal & Motivation
Per `AGENTS.md` Rule 9 (`anargons-tablet-tools`), changes to the chip core belong in **`Anargon-osu/GD32EMU`**. Right now `gd32emu.cs` hard-codes `CLK = 72000000L`, `adcEocAt = now + 100`, `sofNext += 72000`, and ignores `FMC_WS` wait states, `PMU` high-driver mode, `RCU_CFG0` PLL multipliers, and bus/VCO frequency limits. Without modeling those in `gd32emu`, any overclocked firmware build would pass the digital twin falsely - even if its PLL config killed USB, broke `CK_TIMER`, or violated silicon specs.

#### Proposed Changes (`gd32emu/gd32emu.cs`, `gd32emu/tests/EmulatorTests.cs`)
1. **Model the `GD32F3x0` `RCU` clock tree (`0x40021000`-`0x400210C4`):**
   * Decode `HXTAL` crystal frequency (configurable per `Machine` / board, default `8` or `12 MHz` matching Deco 640), `RCU_CFG1` `PREDV[3:0]`, `RCU_CFG0` `PLLSEL` + `PLLMF[5:0]` (both low bits `18..21`, bit `27`, and `RCU_CFG0`/GigaDevice extended multiplier bits), `AHBPSC`, `APB1PSC`, `APB2PSC`, `USBFSPSC` (`/1, /1.5, /2, /2.5, /3, /3.5`), and `RCU_ADDCTL` (`0x400210C0` `CK48MSEL` / `IRC48M` + `CTC`).
   * Compute live derived frequencies: `fHCLK`, `fAPB1`, `fAPB2`, `fTimer1`/`fTimer2`, `fUSBFS`, `fADC`, and `fVCO`.
   * **Enforce GD32F350 datasheet limits (Rule 2: "Model what the hardware does, including the bad parts"):**
     * Raise a `Fault` (or explicit clock-validation error) if `fHCLK > 108 MHz`, `fVCO > 108 MHz`, `fPLLOUT > 108 MHz`, `fAPB1 > 54 MHz`, `fAPB2 > 54 MHz`, `fADC > 40 MHz`, or if `USBFS` is enabled with `fUSBFS != 48 MHz` (+/-tolerance).
     * Keep an explicit `allowOutOfSpec` test flag only if needed for negative-test harnesses, **off** in the gate.
2. **Model `PMU_CTL` (`0x40007000`) & `FMC_WS` (`0x40022000`):**
   * Model `FMC_WS` wait-state penalty (`0` WS vs `1` WS cycle stalls on flash fetches) and enforce `0` WS limit `<= 108 MHz`.
   * Track `PMU_CTL` `LDOVS` / High-driver (`HDEN` / `HDS`) state when switching above 72 MHz.
3. **Decouple wall/USB/ADC time from CPU cycles (`Machine`):**
   * Replace `72000000.0` constants in `RunMs`, `Pace`, `UsbFrame` (`sofNext`), and `adcEocAt` with `fHCLK`-scaled timing so **USB SOF always stays locked at exactly 1.000 ms real time**, while `TIMERS` tick at `CK_TIMER` (`APB` or `2*APB`) and `ADC` conversion scales with `fADC` (allowing `atf_twin.cs` to detect if an overclock changed the integrating ADC window!).
   * Reset all `RCU`/`FMC_WS`/`PMU` registers back to stock 72 MHz defaults on `SystemReset()` (`SYSRESETREQ`).
4. **Update `gd32emu/tests/EmulatorTests.cs`:**
   * Add unit tests verifying `72 MHz`, `96 MHz` (`USB=/2`), `108 MHz` (`IRC48M`), `120 MHz` fault/rejection (`>108 MHz` VCO/AHB or `>54 MHz` APB), bad USB divider detection, `SystemReset()` clock restoration, and exact `1.000 ms` USB SOF pacing across CPU frequencies.

#### Verification & Risks
* **Verification:** `build.cmd` / `build.sh`, `EmulatorTests`, and verifying all existing `anargons-tablet-tools` `72 MHz` regression suites produce **bit-for-bit identical** cycle counts and outputs before moving to PR #4.
* **Risk:** Low in `gd32emu`, provided `72 MHz` defaults match existing cycle accounting (`1` cycle = `1/72 MHz` when configured to stock 72 MHz).

---

### PR #4 (`anargons-tablet-tools`): Safe In-Spec `96 MHz` (and Optional `108 MHz`) Clock Option + `apipr3` / `apipr4` Unlock

#### Goal & Motivation
Increase MCU execution speed by **+33% (`96 MHz`)** - or up to **+50% (`108 MHz`)** if stock `IRC48M + CTC` USB clocking is confirmed - so that heavier on-chip filters (`apipr3` = 3-tracker Robust ALPI, `apipr4` = 4-tracker) comfortably fit inside ULTRA's 1,000 Hz frame budget at 0 wait states without OTD plugins, and to provide the CPU headroom needed for >1,000 Hz scanning in PR #5.

#### Why `96 MHz` (and `108 MHz` max), NOT `120 MHz`
* `GD32F350C8T6` is rated to **108 MHz** (`fHCLK`, `fPLLOUT`, `fVCO`, and 0-wait-state gFlash) and **54 MHz** (`APB1`/`APB2`).
* **`96 MHz`** keeps 12 MHz headroom under 108 MHz, runs `APB1/2` at `48 MHz` (`< 54 MHz`), produces **`48.000 MHz` USB exact (`96 / 2`)** off the quartz crystal (`HXTAL`), gives exact `1.000 us` timer ticks (`PSC = 95`), and exact `500.000 kHz` coil drive (`96 / 192`).
* **`108 MHz`** gives exact `1.000 us` ticks (`PSC = 107`) and `500.000 kHz` drive (`108 / 216`), but requires `USBFS` on `IRC48M + CTC` (`0x400210C0`).
* **`120 MHz`** is excluded because it breaks every GD32F350 spec (`>108 MHz` VCO/AHB/0-WS Flash, `>54 MHz` APB at `/2`), and adding `1` flash wait state erases most of the 11% gain over `108 MHz`.

#### Proposed Changes
1. **Disassemble & fingerprint stock clock init (`tools/dis.py`, `tools/mksig.py`, `atf_symbols.cs`):**
   * Inspect `SystemInit` / `rcu_config` in both verified stock dumps (`2025-02-14` and `2025-12-23`) to record exact `HXTAL` frequency (`8` vs `12 MHz`), `APB1`/`APB2` dividers, `ADCPSC`, and whether `0x400210C0` (`IRC48M` + `CTC`) is already active.
2. **Safe Clock Switching & Flash-Safety Gate (`atf_sched.cs`, `atf_patcher.cs`):**
   * Add `clock` setting (`72 MHz` stock default / `96 MHz` experimental / optionally `108 MHz` if `IRC48M` verified; `--clock 72|96|108`).
   * **Mandatory flash/bootloader safety rule:**
     * Keep CPU at stock **72 MHz** during startup until **after** `blinst` (`0x08000000`/`0x08000C00`/`0x08002800`), `fcount` (`0x08002C00`), and `blclean` complete in `search`!
     * Drop clock back to **72 MHz** before any runtime flash write (`0x080090C4` DFU entry writing `0x08003400`).
     * *(Note: `xppen-tools` DFU `dump`/`update` resets via `SYSRESETREQ` and always executes inside the stock bootloader at 72 MHz.)*
3. **Rescale every 72 MHz hardware dependency (`atf_build.cs`, `atf_sched.cs`):**
   * Recompute and patch the **40-entry coil drive frequency table** (`72 MHz / N` -> `96 MHz / N` or `108 MHz / N`) and verify `RESONANCE` (`0x20000410`) and pressure lookup (`0x08007604`) remain aligned to `457-540 kHz`.
   * Rescale `TIMER15` (`step tick`/`poll`), `TIMER16` (`EmitT16Init` `PSC = 95` or `107`), `TIMER2`/`13`/`14`, and `MS_COUNTER` (`0x20000408`  -  or its tick timer) so millisecond timings (Startup Calibration 3 s window, LED 360 ms fade, Switch Match 500 ms fade) stay exact.
   * Set `ADCPSC` (`RCU_CFG0` / `RCU_CFG2`) to preserve the **exact ADC conversion/integration window** (~14-28 MHz) so pick-up/drop/boost thresholds and `Window Scale` / `Multi-coil` floors do not shift!
4. **Unlock `apipr3` / `apipr4` on ULTRA (`atf_build.cs`, `atf_apip.cs`, `atf_apipr.cs`):**
   * Scale `Apip.ProcUs` (`ROB_EXTRA_US`) and `Build.FrameNeedUs` by `72.0 / clockMHz`. At `96 MHz`, `apipr3` drops from `~28.2 us` to `~21.1 us` (`apipr4` from `~35.1 us` to `~26.4 us`), cleanly passing `Build.ApipFits` on ULTRA with `~50-60 us` saved across frame overhead/processing.
   * Update `atf_twin.cs` (`Twin.Ms`, `MeasureFrames`, `TabletBoard`) to use the build's configured `clockMHz` (`gd32emu` PR #3) and verify `USBFS` stays 48.000 MHz and `ADC` integration stays within spec.
5. **User Warnings & Disclosure (`atf_build.cs`, `atf_cli.cs`, `DOCS/`):**
   * Clearly mark `96/108 MHz` as **Experimental** with an explicit warning: if a newer/unverified `"UG902C"` tablet happens to house non-gFlash clone silicon or lower-binned `72/84 MHz` silicon, overclocking can cause an immediate **`Tier B` soft-brick** requiring opening the case (`PA15` DFU pad), even though `gd32emu` passes.

#### Verification & Risks
* **Twin:** Pass `Cli.Gate` across all presets at `96 MHz` (and `108 MHz`) on both February and December dumps; verify `500 kHz` `TIMER0` frequency, `1.000 ms` `MS_COUNTER` rate, `ADC` signal strength equivalence vs `72 MHz`, `apipr3`/`apipr4` bit-exact C# matching, and `Bootloader Counter` cold-boot install + `Return to stock` at `72 MHz` before PLL boost.
* **Hardware:** Flash `96 MHz` build on your tablet (keeping `PA15`/backup ready); verify USB enumeration stability, thermal behavior over extended play, `Calibration` / `ALPI` signal invariance, and run an **A/B test of `apipr3` vs `ALPI: Tracker`** at `96 MHz` (important because `atf_build.cs` notes `apipr3` had `1.3x` sideways shake / `1.7-4x` more jitter than `Tracker` on earlier `72 MHz` centre-hold data).
* **Risk:** Moderate (`Tier B` soft-brick on non-GD32F350 / low-bin `"UG902C"` clones; mitigated by signature checks, keeping boot/flash writes at 72 MHz, and making it opt-in).

---

### PR #5 (`anargons-tablet-tools`): Inline Scan-Step Chain, Height-Adaptive Touch Pulse Scaling, and Multi-Report Batched USB EP82 Mode (`2,000-2,600 Hz`)

#### Goal & Motivation
Provide the firmware side of the unpromoted >1,000 Hz OTD pipeline: push actual scan rate past `2,000 Hz` (up to **~2,400-2,600 Hz** at touch/very low hover while dynamically stepping down as the pen lifts) and transmit every sub-millisecond sample to the PC without breaking Full-Speed USB's 1 ms polling rule.

#### Why These Specific Limits Apply
* At `96 MHz` + `3+3` lines (`PR #2` + `PR #4`) with the existing `TIMER15` inter-step interrupt gap (`~11 us`/step), frame time is `~550-585 us` (**~1,700-1,820 Hz**).
* To break **2,000 Hz** at `96 MHz`, the firmware must reclaim the `~11 us` inter-step gap (`~50-65 us` across 6 steps).
* To reach **~2,350-2,600 Hz** at touch/very low hover, the fast ladder rungs can shorten excitation pulses from `16` down to **`10-12` pulses** (keeping `20-40` pulses for Steady Hover / Reach so tracking never drops higher up).
* **Hard floor (`>= 10` pulses, NOT `3,000+ Hz`):** `3,000 Hz` (`<= 333 us`) would leave `<= 7-8` pulses per step after fixed `15 us` settle + `~6 us` ADC + `~8 us` CPU overhead, hitting the exact **8-pulse dropout wall** discovered on your hardware in rev 16-17 (`CLAUDE.md`).

#### Proposed Changes (`atf_sched.cs`, `atf_build.cs`, `atf_twin.cs`)
1. **Inline deterministic `3+3` step loop:**
   * Replace per-step `TIMER15` IRQ teardown/re-arming during the 6 fast-stage scan steps with a cycle-deterministic inline scan chain (keeping `TIMER0` burst, fixed `15 us` settle, integrator release `PB8`/`PB9`, and 4 ADC reads per line), saving `~50-65 us/frame` at `96 MHz` (**~2,020-2,150 Hz** with `16` pulses).
   * Re-measure cold-read biases (`Window Scale` / `W[]`) because reducing the inter-step gap alters line-to-line pen ringing carryover.
2. **Height-adaptive pulse ladder (`10-12` pulses at touch -> `20-40` pulses on hover):**
   * Extend `Build.BuildLadder` (`atf_build.cs`) with optional fast touch/very-low-hover rungs (`10-12` pulses, `3+3` lines, `~2,350-2,600 Hz`), stepping smoothly down to `16` pulses (`~2,000 Hz`) and then to standard `Steady Hover` (`930 -> 426 Hz`, `5+5` lines, `20-40` pulses) and `Reach` (`60 Hz`, `24x254`) as signal weakens with height.
   * Keep `APIP` velocity rescaling (`PR #2`) active across every rung.
3. **Batched multi-sample USB HID packet mode on EP82 (`64` bytes max):**
   * Keep EP82's `64`-byte `wMaxPacketSize` (`bInterval = 1`).
   * Add a dedicated opt-in report mode / packed HID frame format (activated via a custom USB feature/string trigger or explicit firmware build flag so normal OS pen drivers ignore or stay safe, and standard mode remains default):
     * **Compact position batch format:** `2`-byte header (`report ID`, `sample count`, `flags`) + `N` samples x `6` bytes (`X 15b`, `Y 15b`, `pressure 14b`, `tip/buttons/step 4b`) -> fits up to **10 samples per 1 ms USB poll** (covering up to 10,000 Hz with zero overflow), OR
     * **Raw line-sum batch format (`DOCS/otd-plugin.md` section 5):** `3+3` line sums (`6 x 12 bits = 9 bytes` + `3 bytes` sweep/stage/seq = `12 bytes/scan`) -> fits up to **5 raw frames per 1 ms USB packet**, letting the PC run all filtering and coil fitting.
   * **Expected latency note:** Batching introduces `+0.5-1.0 ms` waiting-for-SOF packing latency (increasing end-to-end delay by `~0.5-0.9 ms` over single-frame SOF-aligned `1,000 Hz`, as expected).

#### Presets & Settings Affected
* New experimental preset/mode (e.g., `HYPER` / `--batched-otd` or experimental `ULTRA+` option, strictly hidden/unpromoted or clearly tagged as requiring the OTD unpacker plugin).

#### Verification & Risks
* **Twin:** Verify inline `3+3` step loop matches `TIMER0` / `PB8` / `ADC` timing cleanly, verify multi-sample EP82 packets decode back every scan in order with zero sequence gaps at `2,000-2,500 Hz`, and verify ladder step-down/step-up with `10-12` pulse touch rungs.
* **Hardware:** Requires real-tablet line-recorder / noise check at `10-12` pulses to verify touch SNR and dynamic transition height before relying on it; requires OTD plugin (`PR #6`) to actually deliver >1,000 Hz to applications.
* **Risk:** Moderate (inline scan timing bugs or step overruns can hang scanning - `Tier A/B` recoverable; requires careful `gd32emu` step-cycle verification and hardware check).

---

### PR #6 (`anargons-tablet-tools`  -  Separate `otd-plugin/` Subproject): Shared Pure-C# Filter Library & Unpromoted OpenTabletDriver Batch-Unpacker Plugin

#### Goal & Motivation
Build the PC-side companion described in `DOCS/otd-plugin.md` without

*(The message ended here.)*
