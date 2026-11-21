# Model provenance: R36 and R37

<img src="../misc/AIdisclosure-most_docs.png" align="right" width="280" alt="Note: This document is mostly AI generated, with only minor corrections done by a human (stated to the AI).">

| Revision / work | OpenAI Codex model | Reasoning effort | Evidence |
|---|---|---|---|
| R36 audit, calibration, firmware/emulation, host and release work | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]) | Ultra (Max thinking with multi-agent capability) | User confirmation on 2026-11-09 (replaces the earlier 2026-11-04 confirmation) |
| R36 delegates and later follow-ups | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), user-identified; no separate per-delegate manifest retained here | Ultra for R36; separate delegate settings unrecorded | User confirmation and preserved R36 notes |
| R37 coordinator | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), `gpt-6-astra` | Ultra | Local session `turn_context`: `model=gpt-6-astra`, `effort=ultra` |
| R37 console/process investigation and implementation delegates | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), `gpt-6-astra` | Ultra | Each original delegate's local session `turn_context` |
| R37 architecture and naming/calibration delegates | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), `gpt-6-astra` | Ultra | Each original delegate's local session `turn_context` |
| R37 documentation and independent review delegates | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), `gpt-6-astra` | Ultra | Each original delegate's local session `turn_context` |
| R37 structural audit and correction | Codex (GPT 6 Astra Ultra [Max thinking with Multiagentic Capability]), `gpt-6-astra` | Ultra | Parent session settings inherited without a model or effort override |
| R42 (edited vs damaged firmware, cut restore images) | Claude Opus 5.5 (Anthropic), `claude-opus-5-5`; not an OpenAI model | Not recorded | `history/claude-rev42.md` |
| R41 (Windows reader: Raw Input for the pen collection) | Claude Opus 5.5 (Anthropic), `claude-opus-5-5`; not an OpenAI model | Not recorded | `history/claude-rev41.md` |
| R40 (calibration readiness retries) | Claude (Anthropic); not an OpenAI model | Exact model version not recorded in the repository | `history/claude-rev40.md` |
| R39 (Step 1 backup option, test fix) | Claude (Anthropic); not an OpenAI model | Exact model version not recorded in the repository | `history/claude-rev39.md` |
| R38 (disclosure, background dump, one backup per run, interactive helper input) | Claude (Anthropic); not an OpenAI model | Exact model version not recorded in the repository | `history/claude-rev38.md` |

The earlier R37 statement that exact model and effort were unavailable was incorrect. The coordinator and original delegate session records were inspected after the user corrected that claim. R36's user-confirmed attribution supersedes older generic GPT-family wording; historical notes remain preserved with correction banners.

R37 retains the reviewed console/input, subprocess ownership, native HID, naming, and resonance/threshold wording changes. The corrected ZIP restores the approved sibling repositories: `anargons-tablet-tools/` and `gd32emu/`, plus the handoff README. Guides live inside each project's DOCS folder; root CLAUDE.md/CODEX.md and the tablet repository's root history/ preserve the contributor handoff. The intermediate consolidated `app/` layout is not the source-repository contract.

Model identity is attribution, not hardware verification. No real tablet was flashed or calibrated in this session. Historical model findings are preserved under [models](../CODEX.md) and in the [original contributor record](models/AGENTS-R36.md).
