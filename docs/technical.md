# Technical notes

## Duel state

Duel state is a lease rather than a flag. Each fighter's own ZDO holds the opponent and an expiry in server time, renewed every second and valid for five. A disconnect, a crash or an error simply stops the renewal, and every protection switches itself off. Nothing needs cleaning up.

Both sides have to name each other with a live lease. A one sided claim grants nothing, and anything unreadable counts as not duelling. Death protection covers only the opponent's hits, and the fire and poison they leave behind, which arrive later as damage over time with nobody named on them. No duel damage is enabled until the countdown, which runs on the server's clock, has finished.

A duel can only be started when both players are free: out of combat by the game's own ten second PvP rule, with PvP off, not sitting, sleeping or otherwise occupied, and with nothing hostile around the ring. Most of that is only known on the player's own machine, so the challenged player's game checks it when the challenge arrives and turns down one it cannot take without showing it, telling the challenger why. The challenged player's game also refuses a challenge from further away than the proposed ring.

Withdrawing a challenge, or walking away from one, clears it on both sides. After an invitation ends without a duel, the same two players cannot exchange another for fifteen seconds. Both sides enforce this, so a declined challenge cannot be repeated as fast as it can be typed.

Leaving the ring after the fight has begun counts as yielding, and the opponent wins. Leaving it during the countdown only calls the duel off.

End of duel announcements are rate limited per sender, since they are a broadcast any client can send.

## Server rules

The config is grouped so every section is either synchronised or local. `1 - Duel` and `2 - Rules` are synchronised from a server running Sparring. `3 - Controls`, `4 - Display` and `5 - Safety` stay local. Admins are exempt from the lock, and `LockConfiguration` turns it off.

A client whose Sparring speaks a different message protocol than the server's is refused with a reason. This is tied to the protocol rather than the version number, so releases that do not change the protocol stay compatible. On a server without Sparring, a protocol mismatch declines the duel instead.

## RefusePvpBypass

Hits can carry a flag that skips the victim's PvP check. The unmodified game never sets it, so such a hit did not come from an unmodified client. With `RefusePvpBypass` on, which is the default, those hits are only accepted from an opponent you agreed to duel, once the duel is under way. It is a local setting, so no server can switch it off for you.

## The ring

The ring is built on each client from the centre and radius already carried in the duel state, so nothing is spawned into the world or left behind. By default posts are placed around the edge with a marker in the middle; `RingStyle` set to `Line` draws a plain circle instead. Marker and sound names are prefab names. With `DebugCommands` on, `/duel prefabs <text>` lists prefabs, `/duel sounds <text>` lists sounds, and `/duel playsound <name>` plays one.

## Limits

Creatures take no notice of a duel and fight both sides as usual, so a duel is only as safe as the ground it is fought on. Valheim is client authoritative, so a player who edits their own game can make themselves unkillable with or without this mod. Status effects added by other mods are not cleared unless listed in `AlsoClear`. If none of the shaders the ring can use are available, no ring is drawn, and the duel still ends when a fighter leaves the arena.

## Rehearsal commands

With `DebugCommands` on, which is off by default and set by the server, `/duel spar` rehearses a duel alone, and `/duel testwin`, `/duel testyield`, `/duel testmessages`, `/duel testannounce`, `/duel testsound` and `/duel panels` show the end of a duel, the messages and the panels.

`/duel practice` fights a whole duel against yourself, with the real ring, countdown, tally, ending and card, so everything that does not need a second player can be tried alone. `/duel hit`, `/duel burn` and `/duel poison` land a blow, set you alight or poison you, so there is something for it to record; the last two go through the game's own effects, which is what turns them into the damage over time a duel has to recognise. Nothing protects you from a creature while you practise. What a rehearsal did is written to the log.

## Building

Requires the .NET SDK 8 or newer. To build against the reference stubs in `lib/`, with no game installation needed:

```
dotnet build -c Release -p:LibsDir=lib
```

To build against a local installation and copy the result into a BepInEx profile:

```
dotnet build -c Release -p:ValheimDir="<Valheim folder>" -p:ProfileDir="<profile folder>"
```

The `VALHEIM_DIR` environment variable can be used instead of `ValheimDir`. Without these, a default Steam installation and a default Gale profile are assumed.

The files in `lib/` contain metadata only: every method body is replaced and resources are removed, so they can be compiled against but not run. Regenerate them after a game update with `tools/strip-references.ps1`. Releasing is described in [releasing.md](releasing.md).

ServerSync by blaxxun is included in `src/ServerSync` under MIT-0.
