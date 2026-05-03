# AGENTS.md

## Work Decision Rules

- Do not remove UI options, menus, buttons, or visible fields unless the user explicitly asks for that change.
- If a device cannot be handled by the normal read/write path, do not remove it from the choices by default. Prefer a dedicated path, read-only handling, disabled handling, or a clear error message first.
- "Cannot read", "cannot write", or "the PLC rejects it" is not enough reason to change the UI specification. Ask the user before changing the UI specification.
- Add, remove, or reorder device choices only within the scope explicitly requested by the user.
- Device values already present in existing project files or settings JSON should be handled safely when possible, even if the device no longer appears in the current choices. If the value cannot be handled, show the reason instead of crashing.

