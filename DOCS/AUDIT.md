# Release verification

<img src="../misc/AIdisclosure-all_docs.png" align="right" width="280" alt="Note: This document is ENTIRELY AI generated and has received NO human input or review.">

The maintained verification scope, fixture provenance and ordered hardware checks are in [verification guide](verification.md). The corrected handoff retains the two sibling project directories. Its accompanying verification records identify source hashes and the checks performed; do not interpret earlier consolidated-package paths as the current repository layout.

The current candidate corrects calibration report routing, fresh telemetry validation, conservative restoration after failed measurement updates, and measurement-only bootloader deferral. Host checks cover complete backup retention, dry-run isolation, process failures and quoted recovery paths. Firmware checks cover layout guards, strict input, branch bounds, isolated flash, missing measurements and verified cleanup.

The final pass adds shared tablet USB validation and descriptor-driven host polling. Release checks bind runtime notices to the verified version, resolve staging boundaries and require all regression receipts to match the current invocation, captured output, hashes and counts. Documentation uses complete paragraphs and list items without arbitrary hard wrapping.

The release record distinguishes checks actually run from missing fixtures, unexecuted native platforms and physical hardware work. A passing local or simulated check does not establish those remaining behaviors.
