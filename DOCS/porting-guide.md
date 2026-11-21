# Porting guide: studying another tablet's firmware

<img src="../misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

This guide explains how to study the firmware of a pen tablet this tool does not support yet, so that you can decide
whether it can be supported and what that would take. It is written for people and for AI agents, and assumes the
reader can program in an ordinary language (C, C#, Python) but has never read assembly or worked with a
microcontroller. Every term is explained where it first appears; `DOCS/FIRMWARE_DESIGN.md` section 0 explains the
basics (registers, instructions, interrupts, peripherals) at more length, and it is worth reading first.

**Three tablets are used as examples throughout:**

| Tablet | Status | Why it is here |
|---|---|---|
| **Deco 640** | supported | the reference: everything about it is known and checked on real hardware |
| **G640 Rev B** | not supported | the same kind of processor (Arm), but a different chip family |
| **G430S** | not supported | a different kind of processor entirely (MIPS): the case where nothing of the Arm tooling applies |

The G640 Rev B and G430S dumps come from the maintainer's backup set (folders `G640 Rev B/` with `ic903-*.bin`, and
`G430S/` with `ic901-*.bin`). Everything here is **read-only** until step 9, and nothing in this guide needs a tablet
connected.

---

## Ground rules, and why each exists

1. **Work on copies; never write into the folder a dump came from.** A dump may be the only complete copy of a
   tablet's firmware. On the Deco 640, the files an older xppen-tools saved next to each dump were cut short, and
   flashing one damaged tablets (`CLAUDE.md` section 8, vector 17). An accidental overwrite cannot be undone.
2. **No firmware bytes in source or docs** (AGENTS.md rule 15). The firmware is the vendor's copyrighted work. Write
   down addresses, sizes, counts and register values, never runs of the vendor's bytes. Locations the tool needs go
   into the code as *fingerprints* (masked hashes, `tools/mksig.py`), never as bytes. The tools below print addresses
   and values only.
3. **Label every result** with how it was established: *code analysis* (reading the machine code), *twin* (running
   it in the emulator), *model* (a calculation) or *tablet* (seen on real hardware). A reader must be able to tell a
   measurement from an inference. A model number is never a measurement.
4. **Build and run a limit before you quote it.** The Deco 640's timing model projected about 2000 frames per second
   without pressure and tilt; built and run in the twin, it reached 1545 (R45-rc3). The model had left out costs that
   only appeared in the running code.
5. **Never send a real tablet a request you do not understand.** On the Deco 640, merely reading USB string
   descriptor 200 (a harmless-looking text query) restarts the tablet into its update mode. Try every new request in
   the emulator first.
6. **Identify the processor and the chip before anything else.** Every conclusion about timing, interrupts and
   hardware depends on them, and the emulator models exactly one chip family (GigaDevice GD32F3x0, `DOCS/EMULATOR.md`).

---

## Background: what you are looking at

A **firmware dump** is a file holding a copy of the tablet chip's permanent memory (its **flash**), byte for byte.
It contains two kinds of thing mixed together: **machine code** (the processor's instructions, encoded as numbers)
and **data** (tables, text, USB descriptors). Nothing in the file says which is which, or at which address the chip
sees each byte; you work that out from patterns.

Three facts make that possible:

- **Each processor family has its own instruction set**, the encoding of its instructions. Arm Cortex-M chips use
  **Thumb-2** (instructions of 2 or 4 bytes); MIPS chips use **MIPS32** (always 4 bytes). The same bytes mean
  completely different things in the two, so step 0 is to find out which one you have.
- **Numbers are stored little-endian** on all three examples: the lowest byte comes first. The 32-bit value
  `0x08000165` is stored as the bytes `65 01 00 08`. Keep this in mind whenever you search a file for a value.
- **Code contains addresses.** Calls, jumps and pointers hold the addresses the chip will use at run time. They tell
  you where the image is loaded (its **image base**) and where the hardware registers are (which identifies the chip).

---

## Tools

| Tool | What it does | Limits |
|---|---|---|
| `tools/survey.py` | steps 0-5 in one run: size and SHA-256, instruction set, image base, vector tables (Arm), blank areas, where each part file sits, peripheral constants, USB descriptors | prints addresses and values only; `--isa` and `--base` override its guesses |
| `tools/dis.py` | a Thumb-2 disassembler: turns machine code back into readable instructions | Arm Thumb only; knows the Deco 640's address ranges best |
| `tools/mksig.py` | makes the fingerprints the tool uses to find code in any build | Arm Thumb only (it knows which bits of Thumb instructions change when code moves) |

A typical first run:

```text
python -I tools/survey.py MAIN.bin --part code=CODE.bin --part bootloader=BOOT.bin
```

(`-I` runs Python in isolated mode, so nothing from the current folder can be imported by accident; the dumps are
untrusted input.)

---

## Step 0: which instruction set?

**Why first:** every later step reads the code, and reading it in the wrong instruction set gives nonsense that can
look plausible. The tablets an XP-Pen tool can talk to are built on at least two processor families.

**How `survey.py` decides.** Almost every function in a compiled program starts by saving some registers and ends
by returning, and each instruction set encodes those two steps in a fixed, recognisable way:

| Instruction set | Function return | Function start |
|---|---|---|
| Arm Thumb | `bx lr` ("jump to the return address": the 16-bit value `0x4770`) | `push {..., lr}` (save registers and the return address: 16-bit values `0xB5xx`) |
| MIPS32 | `jr ra` (the 32-bit value `0x03E00008`) | `addiu sp, sp, -n` (make room on the stack: 32-bit values starting `0x27BD` with a negative `n`) |

(These are encodings defined by Arm and MIPS for every program, not anyone's firmware bytes.) The tool counts both
kinds per kilobyte of non-blank data. Real code shows several per kilobyte of one kind and almost none of the other;
random data shows few of either.

| Dump | Thumb markers per KB | MIPS32 markers per KB | Verdict |
|---|---|---|---|
| Deco 640 (`ic902-main.bin`, 64 KB) | 9.4 | 0.0 | Arm Thumb |
| G640 Rev B (`ic903-main.bin`, 128 KB) | 6.9 | 0.0 | Arm Thumb |
| G430S (`ic901-main.bin`, 128 KB) | 0.0 | 3.8 | MIPS32, little-endian |

*(code analysis, `survey.py`)*

**A small part can be too small to recognise.** The G430S's 3 KB `ic901-boot-flash.bin` holds about 115
instructions of start-up code with no ordinary functions, so it shows no markers of either kind. Run it with
`--isa mips32 --base 0xBFC00000` once the larger dump has told you the processor (step 2 explains the address).

**If the result is Thumb,** steps 1-10 apply as written, and the tooling (disassembler, fingerprints, emulator core)
is usable. **If it is anything else,** the steps still apply, but every tool that reads code must be extended first;
see the G430S worked example at the end.

---

## Step 1: what does the dump cover?

**Why:** a dump that misses part of the flash can mislead you (no bootloader means you cannot see how the firmware is
started) or, worse, be flashed back and damage a tablet (a cut-short image left the Deco 640's start-up data erased).

A useful dump is the **whole flash**: the **bootloader** (the small program that starts first and handles updates),
the **application** (the firmware proper) and any settings or state pages. xppen-tools also saves the parts as
separate files. **Check:** every part file must be found, whole, inside the full image; record the size and SHA-256
(a fingerprint of the file's contents) of each.

| | Deco 640 | G640 Rev B | G430S |
|---|---|---|---|
| full image | 64 KB | 128 KB | 128 KB program flash, plus a separate 3 KB boot flash |
| bootloader part | 0x08000000, 14 KB | 0x00000000, 12 KB | "loader" 0x9D000000, 24 KB; start-up code in the boot flash |
| application part | from 0x08003800 | 0x00006000-0x0001F7FF (104448 bytes, 50 KB of it blank) | "code" 0x9D006000, 100 KB |
| other parts | settings page 0x08003400 | state page 0x0001F800, the last 2 KB | "vectors" 0x9D01F000, 4 KB (the loader's exception handlers, step 2) |
| blank areas | 0x08002800-0x08002FFF | 0x00002C00-0x00005FFF | 0x9D003800-0x9D0057FF and five smaller ones |

*(code analysis, `survey.py`)*

---

## Step 2: where is the image loaded, and where does it start?

**Why:** an address in the code only means something once you know where the file's first byte sits in the chip's
address space (the **image base**). Get it wrong and every pointer, call and table points at the wrong place.

### On Arm (Cortex-M): the vector table

A Cortex-M program starts with its **vector table**, an array of 32-bit addresses (see `FIRMWARE_DESIGN.md` 0.4).
Word 0 is the initial stack pointer (the top of the RAM the program uses), word 1 the **reset handler** (the first
instruction to run), then one address per exception and interrupt. Thumb code addresses are odd by convention: the
lowest bit says "Thumb", and the code itself starts one byte lower.

- **The reset address gives the image base.** Deco 640: reset `0x08000165`, so flash sits at `0x08000000`. G640 Rev B:
  reset `0x0000027D`, so its flash sits at address 0. Many Arm chips place flash at `0x08000000` and mirror it at 0;
  an image that uses address 0 directly is a first sign of a different chip family.
- **A second table marks where the application starts.** Look for a RAM address followed by odd addresses inside the
  image, on a 256-byte boundary. Deco 640: `0x08003800`. G640 Rev B: `0x00006000`.
- **The stack tops show how much RAM each program uses:** Deco 640 application `0x20000E70` (3.6 KB of its 8 KB),
  G640 Rev B application `0x20001250`.
- **The table's length gives the number of interrupts:** Deco 640 84 entries (68 interrupts), G640 Rev B 75 (59).
  A different count means a different chip.

### On MIPS: fixed reset address, exception base, and address segments

A MIPS processor has no vector table at the start of its image. It always starts at the fixed address
`0xBFC00000`, and finds its exception and interrupt handlers through a register called **EBase** (exception base),
which the start-up code sets. MIPS also splits the address space into **segments** that show the same physical memory
at different addresses:

| Segment | Addresses | What it is |
|---|---|---|
| KSEG0 | `0x80000000`-`0x9FFFFFFF` | physical memory, read through the processor's cache (normal for code) |
| KSEG1 | `0xA0000000`-`0xBFFFFFFF` | the same physical memory, uncached (normal for hardware registers, and for code that runs before the cache is set up) |

So `0x9D000000` and `0xBD000000` are the same flash byte, seen two ways, and both are physical address `0x1D000000`.

**How the G430S's addresses were established** *(code analysis)*:

- **Jumps.** A MIPS `jal` (call) instruction holds only the lower 28 bits of its target; the top 4 come from where
  the code runs. Nearly all of the G430S's calls point into the `0x0D000000` region, so the code runs at
  `0x9D000000` (KSEG0). `survey.py` reports this.
- **Constants.** MIPS builds a 32-bit constant in two instructions: `lui` ("load upper immediate") sets the upper 16
  bits, and `addiu` adds a signed 16-bit lower half. Counting the upper halves shows which regions the code points
  into: `0xA000....` (991 times: RAM, uncached), `0xBF80....`-`0xBF88....` (440: hardware registers), `0x9D0x....`
  (program flash).
- **Sign extension, a trap worth knowing.** Because `addiu` adds a *signed* value, the upper half you see is not
  always the upper half of the result. The boot code builds its exception base as `lui t1, 0x9D02` followed by
  `addiu t1, t1, -4096`, which in C is `t1 = 0x9D020000 + (-4096)`, so `0x9D01F000`, not `0x9D02....`. A tool that
  only reads the `lui` gets it wrong by 64 KB.
- **The boot flash.** The 3 KB boot flash sits at `0xBFC00000`, the MIPS reset address. Its first instruction is a
  call into the start-up routine at `0xBFC00008`.

**What the G430S's start-up code does, in C terms** *(code analysis of the boot flash; the sequence matches the
standard start-up code of Microchip's XC32 compiler for PIC32 chips)*:

```c
if (status_register_says_NMI) goto nmi_handler;   // 0x9D0036E8
sp = 0xA0003FF8;                // stack at the top of RAM: 16 KB of RAM from 0xA0000000
gp = 0xA0007FF0;                // "global pointer", the compiler's base for small variables
on_reset();                     // 0x9D003718, a hook the program may fill
memset((void *)0xA0000000, 0, 932);   // .bss: variables that start at zero
copy_initial_values(table_at_0x9D00360C);   // .data: variables with stored first values
bus_matrix_ram_partitions = 16 KB;          // three hardware registers at 0xBF882010-0xBF882030
cp0_count = 0; cp0_compare = -1;            // the core's own timer, stopped from interrupting
cp0_ebase = 0x9D01F000;                     // exception handlers live in the "vectors" part
/* interrupt vector spacing and mode set, status register adjusted */
on_bootstrap();                 // 0x9D003720
main();                         // 0x9D0034EC: the loader's main program
```

(`.data` and `.bss` are explained in `FIRMWARE_DESIGN.md` section 5.) The application sets its own exception base,
`0x9D006000`, the start of its part, with the same sequence at `0x9D007150`. So the loader's handlers live at the
end of flash and the application's at its own start: an update can replace the application without touching the
loader's handlers.

---

## Step 3: which interrupts matter?

**Why:** an interrupt that runs during the pen scan steals time from it, and the scan is timing-sensitive. On the
Deco 640, one interrupt nobody needed was the cause of bursts of cursor shake (below).

On Arm, most entries of a vector table point to a **stub**. A handler is one of three kinds, which `survey.py` reports:

| Kind | First instruction | What it means |
|---|---|---|
| stub | loops on itself (`b .`, "goto here") | the program never expects this interrupt; if it fires, the chip hangs |
| empty | returns at once (`bx lr`) | allowed but does nothing; if it runs, it costs time nobody sees |
| code | anything else | this interrupt matters: find out what triggers it |

| | bootloader | application |
|---|---|---|
| Deco 640 | SysTick has code; IRQ 13, 42, 67 | SysTick **empty**; IRQ 5, 6, 7, 9, 13, 16, 19, 20, 21, 27, 42, 67 |
| G640 Rev B | SysTick stub; IRQ 32, 53 | SysTick stub; HardFault; IRQ 32, 33, 35, 37, 40, 42, 53 |

*(code analysis)*

An interrupt both programs handle is usually USB, since both talk to the PC. Look up each number in the chip's
reference manual once the chip is known (step 4).

**Why it matters, from the Deco 640:** its bootloader uses SysTick (the Arm core's built-in 1 ms timer) and leaves it
running when it starts the application, whose SysTick handler only returns. Each tick stole a little time from the
coil scan, and in the fastest build that showed as bursts of noise at coil midpoints about once a minute. It was found
on the tablet, explained from this table and the bootloader's code, and fixed by stopping SysTick (R45-rc2,
`FIRMWARE_DESIGN.md` 3.3). On the G640 Rev B both SysTick handlers are stubs, so a SysTick left running would hang it;
there, this cannot be the cause of anything.

**On MIPS** the handlers sit at fixed offsets from EBase instead of in a table of addresses, so this step means
reading the code at the exception base (`0x9D01F000` and `0x9D006000` on the G430S) and the interrupt controller's
setup. The G430S's start-up code sets the core's own timer to compare against the largest possible value, the
standard start-up step that keeps it from interrupting early; whether anything uses it later, and the interrupt
list, have not been read yet.

---

## Step 4: identify the chip

**Why:** the processor family (step 0) says how to read the code; the chip says what the hardware is: which timers,
which converter, which USB block, at which addresses. Two chips with the same processor can differ in everything else.

**How:** hardware registers have fixed addresses (memory-mapped I/O, `FIRMWARE_DESIGN.md` 0.2), and every chip family
has its own map. Collect the constants that point at the hardware range, then find the reference manual whose map has
peripherals at exactly those addresses. Check two or three of them against the code that uses them (a clock enable
before a peripheral is first touched; a USB register written in the USB interrupt).

- **Deco 640:** timers at `0x40012C00`, `0x40000400`, `0x40002000` and `0x40014000`-`0x40014800`, the ADC at
  `0x40012400`, DMA at `0x40020000`, the clock unit at `0x40021000`, the flash unit at `0x40022000`, USB at
  `0x50000000`, pin ports at `0x48000000`-`0x48000800`. With the flash-size register it reads, that is a GigaDevice
  GD32F3x0 (GD32F350C8T6), not an STM32, although the two are similar. *(code analysis)*
- **G640 Rev B:** none of those. Its bootloader uses `0x40000100`, `0x40000210`, `0x4000C000`, `0x40050000`,
  `0x400C0000`, `0x400C0100` and `0x400C0508`; its application adds `0x40000240`, `0x400040C0`, `0x4000480C`,
  `0x40004840`, `0x4000487C`, `0x40008000`, `0x40050020`, `0x40059000`, `0x40071000` and others. Flash at address 0,
  59 interrupts. **A different chip family, not identified here.** *(code analysis)*
- **G430S:** hardware registers at `0xBF800000`-`0xBF88FFFF` (KSEG1), a boot flash at `0xBFC00000` with its
  configuration words in its last 16 bytes, program flash at physical `0x1D000000`, RAM at physical `0x00000000`, the
  bus-matrix RAM-partition registers at `0xBF882010`-`0xBF882030`, the start-up code of Microchip's XC32 compiler, and
  the flash-unlock key pair Microchip's PIC32 chips use (`0xAA996655` and `0x556699AA`, built with `lui`, 4 times).
  That is **consistent with a Microchip PIC32MX** (a MIPS32 M4K-class core). **Not confirmed:** the configuration
  words select a 12 MHz crystal (the USB clock needs a 4 MHz input, and the USB divider is 3) and a main clock of
  12 / 3 x 15 = 60 MHz, above the 40-50 MHz rating of the PIC32MX1xx/2xx parts this memory size suggests, so the exact
  part, or a compatible chip from another maker, is still open. *(code analysis)*

**Limits of a constant search:**

- Registers reached as "base + offset" show only the base. Neither Arm dump holds the SysTick register's address
  (`0xE000E010`) as a constant; both reach it another way. Confirm what the code writes by disassembling it, or by
  running it in an emulator and logging every hardware write.
- Many values in that range are pieces of instructions, not addresses. A value used once, or with odd low bits, is
  probably noise. Trust a value only when you have found the instruction that loads it.
- On MIPS, remember sign extension (step 2): an upper half of `0xBF81` can belong to a `0xBF80F...` address.

**If the chip is not the emulated family, stop here** for anything that needs the twin. The emulator (`gd32emu`,
[Anargon-osu/GD32EMU](https://github.com/Anargon-osu/GD32EMU)) models the GD32F3x0 only, with 64 KB
flash and 8 KB RAM, and refuses anything else. Another chip needs its own memory map, peripherals and, for another
processor, a new instruction core. That is work in the emulator project, to be agreed with its maintainer.

---

## Step 5: USB, what the PC sees

**Why:** the USB descriptors (`FIRMWARE_DESIGN.md` 6.1) tell you the device's identity, how it reports, and the hard
limit on its report rate.

`survey.py` finds the **device descriptors** (18 bytes, type 1: vendor and product ID) and **configuration
descriptors** (type 2) with their **endpoints** (one-way channels; `0x8n` is device-to-PC, `0x0n` PC-to-device), each
with its packet size and polling interval (**bInterval**, in 1 ms USB frames at full speed).

| | device | endpoints: size and interval |
|---|---|---|
| Deco 640 update mode | USB 2.00, PID FFF1 | 0x81 8 bytes / 4 ms; 0x82, 0x02 64 bytes / 2 ms |
| Deco 640 application | USB 2.00, PID 2904 | in its compressed start-up data, so not found by a search (below) |
| G640 Rev B update mode | USB 1.10, PID FFF2 | 0x81, 0x01 64 / 1 ms; 0x82 10 / 3 ms; 0x02 / 10 ms |
| G640 Rev B application | USB 1.10, PID 0914 | 0x81 8, 0x82 64, 0x83 14 bytes / 1 ms; 0x03 14 / 10 ms |
| G430S update mode | USB 1.10, PID 1227 | 0x81, 0x01 64 / 1 ms; 0x82 8 / 4 ms; 0x83 64 / 3 ms; 0x84 10 / 3 ms; 0x04 8 / 10 ms |
| G430S application | USB 1.10, PID 0913 | 0x81 8 / 4 ms; 0x82 64 / 3 ms; 0x83 12 / 3 ms; 0x03 12 / 10 ms |

*(code analysis; vendor ID 28BD = XP-Pen on all of them)*

- **Full-speed USB carries at most 1000 reports per second per endpoint,** because the PC asks an interrupt endpoint
  for data at most once per 1 ms frame. However fast the scan, that is the ceiling, unless the firmware packs several
  readings into one report.
- **An interval of 3 ms means about 500 reports per second** (hosts round it down to 2 ms). The G430S's pen endpoints
  (`0x82`, `0x83`) say 3 ms, as the Deco 640's did before this tool changed them.
- **The interval may not be where a search finds it.** The Deco 640 application's descriptors are inside its
  compressed start-up data, so a plain search finds only the update-mode ones. They were found in the code that
  unpacks that data and confirmed by running the firmware in the twin and reading the descriptors back. *(code
  analysis, twin)*
- **List every string-descriptor index the firmware answers before asking a real tablet for one** (ground rule 5).

---

## Step 6: the bootloader's hand-over

**Why:** the bootloader decides whether the application runs at all, and whatever it leaves switched on is still on
when the application starts. Both are classic sources of bricks and of noise.

- **The decision.** It may be a boot-state word in a settings page, a reset-cause flag, or a pin held at power-on.
  Deco 640: word 0 of `0x08003400` plus the software-reset flag (`FIRMWARE_DESIGN.md` 3.1). G640 Rev B: its state
  page is the last 2 KB of flash; nothing is concluded about it yet. G430S: the loader's `main()` (`0x9D0034EC`)
  decides; not traced yet.
- **What stays on.** Clocks, timers, the core timer, USB, interrupt priorities. Compare with what the application
  sets up itself: anything it relies on but never sets came from the bootloader.
- **How the application gets its interrupts.** Deco 640: the bootloader writes the application's table address into
  the vector table register (`0xE000ED08`). G640 Rev B: neither program holds that address as a constant, so find how
  its application's interrupts are reached (a remap, an address built another way, or a bootloader that forwards
  them). G430S: the application sets EBase itself (step 2).
- **Never move or reformat a page the bootloader owns.** This tool's flash counter page on the Deco 640 is fixed for
  good (AGENTS.md rule 11): tablets in the field carry it.

---

## Step 7: the scan, from coil to report

**Why:** this is what a mod changes. Read `FIRMWARE_DESIGN.md` sections 7-9 first; they explain coils, readings and
frames on the Deco 640, and most coil tablets work the same way.

Find, in this order, and confirm each in the code that uses it:

1. **The scan timer and its interrupt,** often the handler with the most code.
2. **The coil select outputs** (which pins choose a coil) and how many coils share a switch. On the Deco 640 two coils
   can only be on together if they are exactly 8 apart, because of 8-way switches.
3. **The drive output** (the pulses that make the pen ring) and **the receive path** (the ADC, maybe DMA).
4. **The cost of one reading:** a fixed part, plus a part per drive pulse, plus a part per ADC sample. Deco 640: about
   24.7 + 1.94 x pulses + 1.9 x samples microseconds *(model, fitted to the twin)*; the code's own timing gives
   2 + 2 x (pulses + 2) + 15 + 2.15 x samples *(code analysis)*. Measure it in the twin, then check one value on a
   tablet.
5. **The frame:** sweep steps (the pen's frequency carries pressure and buttons) + X lines + Y lines + processing.
   Write the budget down before changing the frame period. Deco 640 fastest build: a scan of 861 us (1 sweep step of
   84 us, 10 readings of 76 us each, small extras) + 82 us processing = 943 us of 1000. *(twin)*
6. **Frames that differ from the others.** The Deco 640 measures tilt on 4 frames of every 16, with an extra outer
   line; while hovering close, those frames' positions came out 0.2-0.45 mm off, a 60 Hz shake (fixed by Tilt Frame
   Skip, R45-rc3). Look for any frame type with a different set of lines and compare its position with the normal
   frames, pen still.

---

## Step 8: hover stages and thresholds

If the firmware switches between scan settings by signal strength (longer, stronger readings when the pen is far):

- **Compare a stage's signal with a level for that stage.** The receiver adds up the pen's ringing, and more drive
  pulses ring the pen up further: about `1 - exp(-n x pi / Q)` of full after `n` pulses, with `Q` about 300 for the
  Deco 640's pen. A slow stage compared against the fast stage's level reads "strong" too early, steps up, loses the
  pen, steps down: the Deco 640 did this about 37 times a second at low hover (fixed in R45-rc3,
  `Build.StepUpLevel`). *(tablet recordings; the fix twin-tested only)*
- **Keep a tracked pen down to a lower level than it takes to pick one up.** Deco 640 reach stage: drop at 0.6 of its
  old level; hover held in the twin 22 -> 26 mm. *(twin)*
- **Hold what is settled.** Re-centring on every frame with no margin was the main still-pen noise on the Deco 640
  (drive-frequency hops, coil-window flips); Frequency Hold and Coil Hold fixed it (`FIRMWARE_DESIGN.md` 8.2, 8.3).
  *(tablet)*

---

## Step 9: noise and limits

- **Noise first, and the cause before a filter.** List what changes while the pen is still: the drive frequency, the
  coil window, background interrupts, an LED switched in the middle of a scan (Deco 640: 1.2-2.2 times the noise at
  the fast presets). Each has a fix at its source; a filter only hides it and adds delay.
- **Build, then quote.** Every limit (rate, hover height, delay) is built and run in the twin before it is written
  down, and labelled *twin*. Model numbers are labelled *model*.
- **One change per tablet test,** and the person with the tablet runs it.

---

## Step 10: two builds of the same firmware

When a second dump of the same tablet has another firmware date:

1. **Map what moved.** Compare function by function and write the shifted ranges down (Deco 640 December:
   -2 / -4 / +12 bytes over three ranges, `FIRMWARE_VERSIONS.md`).
2. **Give every location the tool uses a fingerprint** that matches exactly once in each build, at the right place
   (`tools/mksig.py`, `DESIGN.md` "Firmware layout", `FIRMWARE_DESIGN.md` 14.1). A location without one is a build
   error, never a guess.
3. **Run every preset of both builds in the twin.**

A different tablet with the same chip follows the same steps with new fingerprints. A different chip (the G640 Rev B)
starts at the emulator; a different processor (the G430S) starts at the emulator's instruction core.

---

## Worked example: the G430S

This section puts the steps together for the hardest case: a tablet whose processor is not the one the tooling was
built for. Everything here is *code analysis* of the backup set's `G430S/` files; nothing was run.

### What is known

| Question | Answer | How |
|---|---|---|
| Processor | MIPS32, little-endian | step 0: 3.8 MIPS markers per KB, no Thumb ones |
| Chip | consistent with a Microchip PIC32MX; exact part open | step 4: register map, XC32 start-up code, PIC32 flash-unlock keys; the 60 MHz clock does not fit the obvious parts |
| Memory | 128 KB program flash at `0x9D000000`, 3 KB boot flash at `0xBFC00000`, 16 KB RAM at `0xA0000000` | steps 1-2: jumps, constants, the start-up code's stack pointer |
| Flash layout | loader 24 KB, application 100 KB from `0x9D006000`, loader's exception handlers 4 KB at `0x9D01F000` | step 1: the part files, found whole inside the image |
| Start-up | boot flash start-up code -> loader `main()` at `0x9D0034EC` | step 2: the start-up sequence, read in full |
| Exception bases | loader `0x9D01F000`, application `0x9D006000` | step 2: the EBase writes, with sign extension |
| USB | update mode PID 1227, application PID 0913, pen endpoints at 3 ms (about 500 reports/s) | step 5 |
| Clock | 12 MHz crystal (inferred), 60 MHz core | step 4: the configuration words |

### What is not known yet

- how the loader decides between update mode and the application (step 6);
- which interrupts the application uses, and what the loader leaves running (steps 3 and 6);
- the coil grid, the drive and receive hardware, and the frame (step 7);
- whether the configuration words also protect the flash against an external programmer (they appear to set the
  code-protect bit, read with the PIC32MX layout). If so, an external programmer can erase the chip but not read it,
  so **a full backup of all three regions (loader, application, boot flash) is the only way back** from a failed
  experiment. xppen-tools can read them because the tablet's own loader reads its flash.

### What a G430S port would need

Every part of this tool that touches machine code is written for Arm Thumb:

| Part | Today | For the G430S |
|---|---|---|
| Emulator core (`gd32emu`) | Thumb-2 instructions, GD32F3x0 memory map | a MIPS32 core (with its coprocessor 0: status, cause, EBase, the core timer) and the PIC32 memory map |
| Emulated hardware | GD32 timers, ADC, DMA, USB, GPIO | the PIC32 equivalents: the interrupt controller, timers, ADC, the USB module, ports, the flash unit |
| Assembler (`Asm` in the patcher) | Thumb-2 encodings | MIPS32 encodings, including branch delay slots (on MIPS the instruction after a jump runs before the jump takes effect) |
| Fingerprints (`tools/mksig.py`) | masks the Thumb bits that change when code moves (call offsets, literal offsets) | masks for MIPS: the 26-bit `j`/`jal` targets, `lui`/`addiu` address pairs, branch offsets |
| Patches | the Deco 640's patch sites, by fingerprint | the G430S's own sites, found with steps 7-8 |
| Flashing | xppen-tools with the Deco 640's IDs | xppen-tools' support for this tablet, checked against its own image format first |

In practice that is a second emulator core, a second assembler and a new set of patches: a project of its own, best
started in the emulator repository. The patching side would then ship as a patcher package with its own checks
("Shipping a port", below). Until then the realistic route for a tablet like this is the PC-side one: run the
filters on the PC rather than in the tablet (see `DOCS/otd-plugin.md`).

---

## Shipping a port: a patcher package

A port does not need its own copy of this tool. It ships as a patcher package (`patcher-packages.md`): raw C# source
that holds the tablet's patching steps and the options that make sense for that tablet. The package's developer
writes both; this tool compiles the package and runs it.

- **A tablet whose firmware this tool already patches** (another build of the Deco 640's firmware): the package adds
  its changes and options to the tool's own wizard: `IPatch` for each change, `Build.AddSetting` for its options,
  `Build.HideSetting` for the tool's options its tablet does not have, `Build.Option` for other defaults, and
  `Layout.AddSignatures` for new firmware locations. The wizard's twin check, backup and flashing steps then cover its
  builds. `tutorial-patcherpkg.md` walks through one.
- **Another tablet** (the G640 Rev B, the G430S): the package runs its own steps from start to end: read the dump,
  offer its options, patch, write the file. This tool's twin, backup and flashing steps are built for the Deco 640
  (its chip, its board model, its USB IDs), so they do not check another tablet's image: the package brings its own
  checks, and says plainly what it does not check. It can reuse the screen helpers (`Screen`), the file and hex
  helpers (`P`, `Build.WriteHex`) and, for a Cortex-M tablet, the Thumb-2 assembler (`Asm`).

Either way the ground rules above still hold: every limit is built and run before it is quoted, and the person who
owns the tablet decides what gets flashed.

---

## For AI agents

- Read `AGENTS.md`, every model file (`CLAUDE.md`, `CODEX.md`, ...) and the latest file in `history/` first.
- **Revisions after R45 are named after their pull request.** A revision needs a GitHub account linked to a person
  (for identity). Working with that account, look up its pull requests in `Anargon-osu/anargons-tablet-tools` when you
  start and name the revision `PR #X` (X = the number of the pull request it goes into). R45 was the last revision
  with numbered release candidates.
- Do not push, post or open anything on GitHub unless your partner asks you to; GitHub work is theirs.
- Do not flash a tablet. Your partner flashes, after the twin gate passes, and runs the hands-on tests.
- Keep this guide general. Tablet-specific findings go into your model file and `history/`.

## Checklist

| # | Question | Done when |
|---|---|---|
| 0 | Which instruction set? | markers counted; the verdict matches what the code reads like |
| 1 | Is the dump whole? | every part found inside the full image; sizes and SHA-256 written down |
| 2 | Where do the programs start? | image base; vector tables or reset address and exception bases; stack tops |
| 3 | Which interrupts matter? | handlers with code listed for both programs; what the core timer does |
| 4 | Which chip? | peripheral bases match one reference manual; two uses checked in code |
| 5 | What does the PC see? | descriptors, endpoint sizes and intervals; string indexes answered |
| 6 | What does the bootloader leave on? | boot decision found; leftovers listed |
| 7 | What does a frame cost? | per-reading model fitted in the twin; frame budget written |
| 8 | Do stages compare like with like? | per-stage levels; drop below pick-up |
| 9 | Which limits are real? | each built and run in the twin, labelled |
| 10 | How does the port ship? | a patcher package with the tablet's patching steps, its options and its own checks |
