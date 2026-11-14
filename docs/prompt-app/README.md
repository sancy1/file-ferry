<!--
FILE:     docs/prompt-app/README.md
PURPOSE:  Placeholder entry point for Prompt App documentation. The
          architecture, phase-map and user-guide documents land here in
          Phase 5; a claims test will assert doc claims against code.
RELATION: Part of the docs tree referenced by Documentation claims tests.
-->
# Prompt App

A local prompt and document manager inside File-Ferry. In development -
see the phased plan (Phase 0: foundations, Phase 1: SQLite persistence,
Phase 2: shell integration and UI, Phase 3: import/export, Phase 4: safety
and history, Phase 5: polish and documentation).

v1 scope: local only. No LLM submission, no cloud pipeline. Reuses the
harvester format engine (IFormatWriter implementations) for export.
