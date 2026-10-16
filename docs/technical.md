# Technical notes

## Duel state

Duel state is a lease rather than a flag. Each fighter's own ZDO holds the opponent and an expiry in server time, renewed every second and valid for five. A disconnect, a crash or an error simply stops the renewal, and every protection switches itself off. Nothing needs cleaning up.

Both sides have to name each other with a live lease. A one sided claim grants nothing, and anything unreadable counts as not duelling. Death protection covers only the opponent's hits, and the fire and poison they leave behind, which arrive later as damage over time with nobody named on them. No duel damage is enabled until the countdown, which runs on the server's clock, has finished.

A duel can only be started when both players are free: out of combat by the game's own ten second PvP rule, with PvP off, not sitting, sleeping or otherwise occupied, and with nothing hostile around the ring. Most of that is only known on the player's own machine, so the challenged player's game checks it when the challenge arrives and turns down one it cannot take without showing it, telling the challenger why. The challenged player's game also refuses a challenge from further away than the proposed ring.

Withdrawing a challenge, or walking away from one, clears it on both sides. After an invitation ends without a duel, the same two players cannot exchange another for fifteen seconds. Both sides enforce this, so a declined challenge cannot be repeated as fast as it can be typed.

Leaving the ring after the fight has begun counts as yielding, and the opponent wins. Leaving it during the countdown only calls the duel off.

End of duel announcements are rate limited per sender, since they are a broadcast any client can send.

## Server rules

The config is grouped so every section is either synchronised or local. `1 - Duel` and `2 - Rules` are synchronised from a server running Sparring. `3 - Controls` and `4 - Display` stay local. Admins are exempt from the lock, and `LockConfiguration` turns it off.

A client whose Sparring speaks a different message protocol than the server's is refused with a reason. This is tied to the protocol rather than the version number, so releases that do not change the protocol stay compatible. On a server without Sparring, a protocol mismatch declines the duel instead.

## PvP

Neither fighter turns PvP on. A blow between the two of them carries the flag the game's own area effects use to reach friendly players, which tells the victim's game to skip its PvP check, and it is only set while both leases agree the fight is under way. Sparring leaves the flag alone on every other hit, so a friendly area effect such as the Staff of Protection's shield reaches other players exactly as it does without the mod. The victim's game takes the flag on trust, as it does for any hit, so a client modified to set it can reach a player with PvP off with or without Sparring installed.

## The ring

The ring is built on each client from the centre and radius already carried in the duel state, so nothing is spawned into the world or left behind. By default posts are placed around the edge with a marker in the middle; `RingStyle` set to `Line` draws a plain circle instead. Marker and sound names are prefab names.

## Sounds

Only a few of the game's sounds are registered with the scene by name. Most are referenced straight from the effect lists of the items, pieces, creatures, status effects and interface that play them, so a name the scene does not know is looked for in those lists. The walk looks for every sound Sparring will need at once, starts with the interface, where most of the duel's own sounds are, and stops as soon as all of them have been met; walking the whole game takes around a tenth of a second, and a name that exists nowhere costs one such walk before misses are answered at once. The duel's own sounds are looked up on entering a world and never during a duel. Sounds that belong only to locations, such as boss altars and waystones, are not indexed, because locations are loaded on demand and walking them would force those loads. Every sound is played locally, from a prefab spawned with its network view disabled and removed on a timer, so nobody else hears it and nothing is left behind.

## Limits

Creatures take no notice of a duel and fight both sides as usual, so a duel is only as safe as the ground it is fought on. Valheim is client authoritative, so a player who edits their own game can make themselves unkillable with or without this mod. Status effects added by other mods are not cleared unless listed in `AlsoClear`. If none of the shaders the ring can use are available, no ring is drawn, and the duel still ends when a fighter leaves the arena.

## Rehearsal commands

With `DebugCommands` on, which is off by default and set by the server, `/duel spar` rehearses a duel alone, and `/duel testwin`, `/duel testyield`, `/duel testmessages`, `/duel testannounce`, `/duel testsound` and `/duel panels` show the end of a duel, the messages and the panels.

## Self-test

The self-test is a plugin of its own, built from `selftest/` with `dotnet build selftest` and deployed beside Sparring. A release packages `Sparring.dll` alone, so players never have it. Installed, it answers `/duel selftest`, which checks Sparring against the running game and reports how it went: a top line with the verdict, then what failed, what needs a look, what passed and what is only information. The report goes to the console and to `Sparring-selftest.txt` in the BepInEx folder.

Most of it only reads: whether each hook went in and `Character.RPC_Damage` is left to the game, whether the fields Sparring reads by name still exist, whether the duel's sounds, the challenge sound, the ring's shader and its markers resolve, and how a hit with nobody named on it is judged. The rest fights three duels against yourself, each ending in a fall to zero by a blow, a burn and poison, and checks that each comes out as a yield through the game's own death check, along with the countdown, the card and what is left once a duel is over. Those falls are real, so a broken yield is a real death; run it near a bed with nothing hostile about, which it checks for before fighting.

It also answers `/duel practice`, which starts a duel against yourself, and `/duel hit`, `/duel burn` and `/duel poison` with an amount, which land a blow, set you alight or poison you within one. The last two go through the game's own effects, which is what turns them into the damage over time a duel has to recognise. Nothing that needs a second player can be checked alone: the handshake, refusals on the other side, the order the ending messages arrive in, and announcements reaching others.

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
