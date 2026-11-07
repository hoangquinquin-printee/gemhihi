# GEMINI Agent Desktop test

Date: 2026-11-06

## Result

- Node.js: `v24.22.1`
- npm: `11.19.1`
- agent-desktop: `1.2.0` (Windows x64)
- `agent-desktop status`: PASS; Windows adapter healthy, Accessibility `granted`, supported surfaces include `window`, `menu`, `sheet`.
- `agent-desktop skills get desktop`: PASS.
- `agent-desktop skills get platform`: PASS (Windows UI Automation guide).

## Codex MCP

Added a new `[mcp_servers.agent_desktop]` entry to `C:\Users\ASROCK\.codex\config.toml` without changing the existing `node_repl` or `rea` entries. It invokes the npm shim through `cmd.exe`:

`C:\Users\ASROCK\AppData\Roaming\npm\agent-desktop.cmd mcp`

Codex must be restarted/reconnected before the new MCP tools can appear in the current session. This turn verified the underlying CLI, not a post-restart Codex tool inventory.

## MCP stdio verification

The configured command was started directly with an MCP `tools/list` request. It returned the expected tools `desktop_snapshot`, `desktop_click`, and `desktop_screenshot` (plus the remaining desktop tools). Codex itself still needs a restart/reconnect to load this newly added server entry.

## Privacy

No screenshots or desktop data were uploaded. The screenshot and snapshot in this folder were written locally only.
