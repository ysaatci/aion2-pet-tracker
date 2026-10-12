# AION 2 Pet Tracker: plan

An overlay for **AION 2 Global (EU/NA)** that shows:

1. **Your pet progress**: souls collected per pet and level (pets have **3 levels** on Global).
2. **Nearby NPCs that are pets**: which monsters around you drop a pet soul, grouped by genus
   (Cogni / Fera / Natura / Varian / Special), with your progress on each.

It's built on a fork of [cyberbadger6969/aion2-dps-meter](https://github.com/cyberbadger6969/aion2-dps-meter)
(GPL-3.0), which already does passive Npcap capture, TCP reassembly, LZ4 frame decoding, and NPC spawn
decoding for the Global client. This project is therefore GPL-3.0 as well. The `upstream` remote points
at the original repo.

## Ground rules

- **Passive and read-only.** Packet capture only: no memory reads, no injection, nothing sent to the game.
  Only the user's *own* character data is shown. No other players' data is stored or uploaded.
- **Opcodes and data are tables, not code.** They move with game patches.
- SOLID modules, one logical change per commit, and small tests for each step.
- Raw captures (`captures/*.pcap`) contain account traffic: **never commit them** (gitignored).

## Architecture

```
AionMeter.Core (upstream: capture → reassembly → frames → PacketParser → GameEvents)
        │  new events: NpcSpawned / NpcDespawned / PetCollectionSnapshot / PetSoulGained
        ▼
PetTracker.Core (new, no UI)
  ├─ PetCatalog        pet ↔ genus ↔ source NPC codes (data/pets.json)
  ├─ PetProgressStore  souls + level per pet (snapshot from packets, deltas applied)
  ├─ NearbyNpcTracker  spawned/despawned NPCs in view, joined to PetCatalog
  └─ PetTrackerService composes the above into an immutable PetOverlaySnapshot
        ▼
AionMeter.App (WPF): new PetOverlayWindow (nearby pets grouped by genus, progress bars)
```

## Steps

### Step 0: fork setup *(done)*
Clone upstream, rename the remote, write plan.md, gitignore captures.

### Step 1: capture spike (decides the design)
Use `aionmeter-cli live <seconds> --record` (run as admin) to record these scenes, one pcap each:

| Scene | What to do in game | Packets to find |
|---|---|---|
| A login | Log out to character select, then log back in | Pet collection snapshot on login? |
| B pet UI | Open the Pets window, switch genus tabs | Snapshot sent on demand? |
| C kill | Kill 10+ mobs of a pet you're collecting, note the soul count before and after | Soul-gain delta, KillCredit, Death |
| D walk | Walk through a mob area without fighting | Spawn/despawn stream, NPC codes, positions |

Analysis uses `dump`, `hex`, `find`: diff opcode counts across scenes, search for known soul counts
(as u8/u16/u32/varint) and for NPC codes of the mobs killed. Output: `docs/protocol-pets.md`.

**Fallback if there's no snapshot packet:** the user enters a starting snapshot once, and progress is
counted from soul-gain or kill packets.

### Step 2: pet catalog data
`data/pets.json`: pet id, genus, EN name, source NPC codes, level thresholds (3 levels on Global,
values read from captures or game UI, not hardcoded). Build it by joining a community mob → pet list
(names) to `data/npcs/en.json` (NPC code → name). A small script reports unmatched names.

### Step 3: new events in the parser
Add `NpcSpawned`/`NpcDespawned` (code, actor id, position) and the pet events found in step 1 to
`GameEvents.cs` and `PacketParser.cs`. Add replay tests from trimmed, anonymised byte fixtures.

### Step 4: PetTracker.Core
`PetCatalog`, `PetProgressStore` (persisted to `%APPDATA%`), `NearbyNpcTracker`, `PetTrackerService`.
Pure logic, unit tested with synthetic events.

### Step 5: CLI check
`aionmeter-cli pets <file.pcap>` prints the progress and nearby-pet list for a capture, to verify
against the game before any UI.

### Step 6: overlay window
WPF `PetOverlayWindow`: click-through/draggable, nearby pet mobs grouped by genus, souls x/y, level,
"closest to next level" sort, and a highlight for pets not collected yet. Toggle with a hotkey.

### Step 7: trim and package
Hide or remove the DPS-only UI the pet tool doesn't need (or keep both, decided later), rename the
app, and build the installer.

## Open questions
- Exact soul thresholds for Global's 3 levels.
- Whether pet collection is account-wide/server-wide on Global (KR/TW changed this).
- Whether a soul-gain packet exists, or whether souls must be inferred from kills (drops aren't guaranteed).
