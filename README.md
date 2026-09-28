# Raid Recovery

A mod for SPT 4.1 (Single Player Tushonka) that lets you resume a raid after the game crashes.

## Why

When the game crashes or freezes in the middle of a raid, the raid is never saved: your profile goes back to
what it was before the raid started. You keep the gear you brought in, but everything that happened during
the raid is gone: the loot you found, your quest progress, and the time you spent. That loss comes from a
technical failure, not from a mistake you made.

The goal is to get close to what the live version of the game does. There, when you disconnect or crash, the
game tells you that you are still in a raid when you come back, and you choose between rejoining or leaving
and losing all your gear.

## How does it work?

Think of it as an automatic save, like in any other single-player game.

1. **During the raid, the mod takes snapshots.** Every 30 seconds, it writes down where you are, what you
   carry and how healthy you are. You do not notice it while playing.
2. **Each snapshot replaces the previous one.** Only the latest is kept, plus the one before as a spare in
   case the game crashes right in the middle of a save.
3. **If the raid ends normally, the snapshot is thrown away.** Extracting, dying or leaving the raid are
   normal endings: there is nothing to recover.
4. **If the game crashes, the snapshot stays.** The next time you reach the main menu, the mod finds it and
   opens a window with two buttons: Resume or Discard.
5. **If you resume, the mod puts everything back.** It gives your character the gear and health from the
   snapshot, starts a raid on the same map, and moves you to the spot where you were.

A snapshot that is more than 24 hours old is ignored and deleted.

## What is restored

- Inventory, including the loot found during the raid
- Health, hydration and energy
- Position and view direction
- Remaining raid time and time of day
- Quest progress and examined items
- Map loot: the resumed raid gets the loot of the interrupted raid, without the items you already took
- Opened doors, used switches, open or closed extractions, containers already searched
- Bots that were alive, with their gear, health and position
- Bodies left on the map, with what they carried

## Known bugs and limitations

These are not handled yet and are planned for later versions.

- **A restored bot forgets what it was doing.** It comes back where it stood, with its gear and health, but
  without its target, its alert state or its group.
- **A half-searched container comes back fully searched.**
- **Items dropped on the ground are lost.** An item you picked up and then dropped is in neither your
  inventory nor the map.
- **Items merged into a stack come back.** Rounds picked up into a stack you already carried show up again
  where you found them.
- **An extraction countdown is not restored.** Only the open or closed state of an extraction is.
- **A door caught in the middle of its movement is not restored.**
- **Weather is not restored.** The server generates new weather for the resumed raid.
- **Discarding a raid costs nothing.** Unlike the live version, choosing not to resume does not take your
  gear away: you go back to the profile you had before the raid.
- **Scav raids cannot be resumed.** The snapshot is detected, then discarded.
- **Progress since the last snapshot is lost.** A snapshot is taken every 30 seconds by default, so up to
  that much progress can be missing.
- **Closing the game on purpose also triggers a recovery.** Alt+F4 just before dying brings back the state of
  the last snapshot. There is no limit yet on how many times a raid can be resumed.
- **A crash while the resumed raid is loading loses the recovery.** The snapshot is consumed as soon as it is
  applied to the profile.
- **The raid preparation screen is skipped on relaunch.** Mods that hook into that screen do not run for the
  resumed raid.
