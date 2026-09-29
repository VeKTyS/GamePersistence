# Raid Recovery

A mod for SPT 4.1 (Single Player Tarkov) that lets you resume a raid after the game crashes.

> **Beta.** The mod was played on Factory and Interchange, on an installation without other mods. It has not
> been tried yet next to a large mod list, nor on every map. Back up your profile before you rely on it.

## Why

When the game crashes or freezes in the middle of a raid, the raid is never saved: your profile goes back to
what it was before the raid started. You keep the gear you brought in, but everything that happened during
the raid is gone: the loot you found, your kills, your quest progress, and the time you spent. That loss
comes from a technical failure, not from a mistake you made.

The mod does what the live version of the game does: when you come back after a crash, the game tells you
that you are still in a raid, and you choose between going back to it or leaving it.

## Installation

Extract the archive into the root of your SPT 4.1 folder, the one that holds `BepInEx` and `SPT_Runtime`.
It adds two folders:

```
BepInEx/plugins/RaidRecovery/          the plugin of the game
SPT_Runtime/user/mods/RaidRecovery/    the mod of the server
```

Both are needed. To remove the mod, delete those two folders.

## How does it work?

Think of it as an automatic save, like in any other single-player game.

1. **During the raid, the mod takes snapshots.** Every 30 seconds, it writes down the state of the raid:
   your character, the map around you, the bots. The reading is spread over several frames, a few
   milliseconds each, so that you do not feel it.
2. **Each snapshot replaces the previous one.** Only the latest is kept, plus the one before as a spare in
   case the game crashes right in the middle of a save.
3. **If the raid ends normally, the snapshot is thrown away.** Extracting, dying or leaving the raid are
   normal endings: there is nothing to recover.
4. **If the game crashes, the snapshot stays.** The next time you reach the main menu, the game opens its
   own "Return to raid" screen, the one of the live version, with two buttons: Reconnect or Confirm leave.
5. **If you reconnect, the mod puts the raid back.** It restores your character, starts a raid on the same
   map with the loot, the weather and the time that raid had, moves you to the spot where you were, then
   puts the doors, the bots and the bodies back in place.
6. **If you leave, nothing is lost.** You go back to the profile you had before the raid, gear included.
   There is no penalty.

A snapshot that is more than 24 hours old is ignored and deleted.

## What is restored

**Your character**

- Inventory, including the loot found during the raid
- Health, hydration and energy
- Position and view direction
- Posture (standing, crouched, lying down), stamina, and the item you held
- Skills, achievements, trader standing, wish list
- Quest progress and examined items
- Kills, experience and counters of the raid so far: the end-of-raid screen counts the whole raid

**The raid**

- Remaining raid time and time of day
- Weather
- Extractions: the ones you had, open or closed as they were
- Opened doors and used switches
- Containers you already searched

**The loot**

- The resumed raid gets the loot of the interrupted raid, not a new draw
- Items that left the map stay gone: taken, used up, or merged into a stack you carried
- Items you dropped or moved are where you left them

**The bots**

- Bots that were alive come back with their gear, their health and their position
- A bot comes back unaware of you, so that you have time to settle in. A setting makes it chase you
  again if it was doing so
- Bodies are where they fell, with what they carried
- Bots that already spawned do not spawn a second time

**Scav raids** are resumed too, with the scav.

## Settings in the game

Press F12 in the game and look for Raid Recovery.

| Setting | Default | Meaning |
| --- | --- | --- |
| Enabled | on | Turns the snapshots on or off |
| Interval (seconds) | 30 | Time between two snapshots, from 15 to 120. Applies at once |
| Save now | | A button that takes a snapshot right away, and shows when the last one was saved |
| Save now (shortcut) | none | A key that does the same without opening the menu |
| Relaunch the raid automatically | on | When off, you start the raid yourself after reconnecting |
| Use the return-to-raid screen of the game | on | When off, a plain window is shown instead |
| Keep the loot as the game sees it | on | When off, only the items you carry are removed from the loot |
| Keep bots and bodies | on | When off, the resumed raid spawns new bots |
| Bots that were after you still are | off | When on, a bot that was chasing you chases you again as soon as it is back |
| Log measurements | on | Writes the cost and size of each snapshot to the log |

The last four are also a way out: if a resumed raid misbehaves, turning one of them off tells which part is
at fault, and the rest of the mod keeps working.

## Settings of the server

In `SPT_Runtime/user/mods/RaidRecovery/config.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `maxAgeHours` | 24 | A snapshot older than this is ignored and deleted |
| `maxResumesPerRaid` | 0 | How many times one raid may be resumed. 0: no limit |
| `blockResumeUnderVitalHealthPercent` | 0 | A raid cut with the head or thorax under this share of its health cannot be resumed. 0: never blocked |

A raid that is refused by one of these rules is discarded like a raid you chose to leave: you go back to
the profile you had before it.

## Known bugs and limitations

- **Progress since the last snapshot is lost.** A snapshot is taken every 30 seconds by default, so up to
  that much progress can be missing. The "Save now" button takes one on demand.
- **Closing the game on purpose also triggers a recovery.** Alt+F4 just before dying brings back the state
  of the last snapshot. This is accepted: the mod protects from a crash, it does not try to prevent
  cheating in a single-player game.
- **A crash while the resumed raid is loading loses the recovery.** The snapshot is consumed as soon as it
  is applied to the profile.
- **The raid preparation screen is skipped on relaunch.** Mods that hook into that screen do not run for
  the resumed raid.
- **A restored bot loses its alert state and its group.** It comes back where it stood, and behaves like a
  bot that just spawned.
- **What was in motion is not restored, on purpose.** A grenade in the air, a bot in the middle of an
  action: the raid resumes calm.
- **A stack you only took part of comes back whole.** Take 20 rounds out of a stack of 60 and the 60 are
  there again.
- **A half-searched container comes back fully searched.**
- **An extraction countdown is not restored.** Only the open or closed state of an extraction is.
- **A door caught in the middle of its movement is not restored.**
- **After a server restart, the weather of a resumed raid no longer moves on.** It stays as it was when the
  raid was cut.
- **The reward of the Lightkeeper quests is not handed out at recovery**, only at the true end of the raid.
- **The warning of the return-to-raid screen is only rewritten in English and French.** In another language
  it may still mention a penalty: there is none.

## Where the files are

Snapshots are written by the server in `SPT_Runtime/user/raid-recovery/`, a few files per profile. They are
deleted when the raid ends. Nothing is sent anywhere else.

The log of the plugin is in `BepInEx/LogOutput.log`, on the lines that start with `Raid Recovery`. It says
what each recovery put back, and what each raid added up to.
