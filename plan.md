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
AionMeter.Core (upstream: capture â†’ reassembly â†’ frames â†’ PacketParser â†’ GameEvents)
        â”‚  new events: NpcSpawned / NpcDespawned / PetCollectionSnapshot / PetSoulGained
        â–¼
PetTracker.Core (new, no UI)
  â”œâ”€ PetCatalog        pet â†” genus â†” source NPC codes (data/pets.json)
  â”œâ”€ PetProgressStore  souls + level per pet (snapshot from packets, deltas applied)
  â”œâ”€ NearbyNpcTracker  spawned/despawned NPCs in view, joined to PetCatalog
  â””â”€ PetTrackerService composes the above into an immutable PetOverlaySnapshot
        â–¼
AionMeter.App (WPF): new PetOverlayWindow (nearby pets grouped by genus, progress bars)
```

## Steps

### Step 0: fork setup *(done)*
Clone upstream, rename the remote, write plan.md, gitignore captures.

### Step 1: capture spike *(done, see docs/protocol-pets.md)*
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

### Step 2: pet catalog data *(done: 207 pets, tools/build-pet-catalog.ps1)*
`data/pets.json`: pet id, genus, EN name, source NPC codes, level thresholds (3 levels on Global,
values read from captures or game UI, not hardcoded). Build it by joining a community mob â†’ pet list
(names) to `data/npcs/en.json` (NPC code â†’ name). A small script reports unmatched names.

### Step 3: new events in the parser *(done)*
Add `NpcSpawned`/`NpcDespawned` (code, actor id, position) and the pet events found in step 1 to
`GameEvents.cs` and `PacketParser.cs`. Add replay tests from trimmed, anonymised byte fixtures.

### Step 4: PetTracker.Core *(done; learned links are not saved yet)*
`PetCatalog`, `PetProgressStore` (persisted to `%APPDATA%`), `NearbyNpcTracker`, `PetTrackerService`.
Pure logic, unit tested with synthetic events.

### Step 5: CLI check *(done; verified 15/75 against the game)*
`aionmeter-cli pets <file.pcap>` prints the progress and nearby-pet list for a capture, to verify
against the game before any UI.

### Step 6: overlay window
WPF `PetOverlayWindow`: click-through/draggable, nearby pet mobs grouped by genus, souls x/y, level,
"closest to next level" sort, and a highlight for pets not collected yet. Toggle with a hotkey.

### Step 7: trim and package
Hide or remove the DPS-only UI the pet tool doesn't need (or keep both, decided later), rename the
app, and build the installer.

## Open questions
- ~~Thresholds~~: unlock 5 (per pet in the catalog), then 25, then 75 (confirmed in game).
- Whether pet collection is account-wide/server-wide on Global.
- Learned NPC → pet links live in memory only: persist them next to the state file (step 6).
- 44 catalog pets have no monster source (chests, shops, events); they never show as nearby.