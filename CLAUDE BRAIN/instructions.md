# AI Brain — Core Instructions

## Purpose
This system is designed to help build and maintain high-performance software, creative projects, and technical systems with a focus on speed, clarity, and execution quality.

---

## File Structure Rules

### Root (`~/AI Brain/`)
- `instructions.md` → global rules (this file)
- `context.md` → identity + long-term stable information
- `memory.md` → global session log (append-only)

### Each Project Folder
- `context.md` → project state and current direction
- `memory.md` → project-specific log (append-only)
- `instructions.md` → optional override file ONLY if project needs special rules

---

## Read Order (Every Session)
When working in AI Brain, always read in this order:
1. Root `instructions.md`
2. Root `context.md`
3. Root `memory.md`
4. Project `context.md`
5. Project `memory.md`
6. Project `instructions.md` (only if it exists)

Never skip context files.

---

## Core Operating Rules

### 1. Performance First (Non-Negotiable)
All recommendations, designs, and implementations must prioritize:
- Fast startup time
- Low-latency interaction (UI must feel instant)
- Efficient memory and CPU usage
- Smooth responsiveness under load
- Minimal unnecessary abstraction

If a design choice reduces performance, it must be justified or rejected.

---

### 2. State Persistence
All applications should assume:
- User state must persist between sessions
- Layouts, settings, and preferences should restore exactly as last saved
- Load time for restoring state must be minimal

---

### 3. Simplicity Over Complexity
- Prefer fewer moving parts
- Avoid over-engineering
- Use the simplest architecture that achieves performance goals
- Do not introduce frameworks or systems unless they improve performance or maintainability

---

### 4. Communication Style
- Direct and technical
- No filler language
- Bullet points preferred for structured information
- No motivational or generic AI phrasing
- No assumptions presented as fact

If something is unknown, say so clearly.

---

### 5. Memory System Rules

#### `memory.md` (append-only)
- Log only real outcomes, decisions, or completed work
- Never rewrite past entries
- Always timestamp new entries (YYYY-MM-DD)

#### `context.md`
- Stores stable truth (identity, project direction, constraints)
- Must only be changed when explicitly approved
- Keep minimal and high-value only

---

### 6. Project Handling Rules
Each project is treated as an isolated system:
- No cross-project assumptions unless explicitly stated
- Each project must define its own state clearly in its `context.md`
- Avoid mixing logic between projects

---

### 7. Change Control Rules
- Session events → append to `memory.md` immediately
- Project direction changes → propose edit to `context.md`
- Global rule changes → propose edit to root `instructions.md`
- Identity changes → propose edit to root `context.md`

Nothing structural changes without approval.

---

### 8. Default Engineering Priority Stack
When making decisions, prioritize in this order:
1. Responsiveness / latency
2. Stability
3. Simplicity
4. Maintainability
5. Feature completeness

---

## Projects
- Applications — General software systems focused on speed, responsiveness, and persistent state
- Music — Production, mixing, branding, and distribution workflows
- Games — Gameplay systems, prototypes, and experimental mechanics

---

## End Goal
Build systems that feel:
- instant
- stable
- predictable
- efficient

No wasted cycles, no unnecessary complexity.