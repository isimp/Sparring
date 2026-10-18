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

## When the game moves a hook

A duel's protection is every hook at once, so Sparring never runs with part of it. When the game will not take one of its hooks, as after an update that renamed or moved what it patches, Sparring takes out the hooks that did go in and switches itself off. It writes no lease, so no duel can start with this player, and the panel, the keys and the messages between two games stay quiet. `/duel` answers every word with what the game changed, and the log says the same with the full error. The self-test still runs its checks that only read and names the hooks that are missing; it fights no duel against yourself then, since a fall to zero would be a real death. A challenge from another player gets no answer and runs out as unanswered on their side.

## The ring

The ring is built on each client from the centre and radius already carried in the duel state, so nothing is spawned into the world or left behind. By default posts are placed around the edge with a marker in the middle; `RingStyle` set to `Line` draws a plain circle instead. Marker and sound names are prefab names.

## Sounds

Only a few of the game's sounds are registered with the scene by name. Most are referenced straight from the effect lists of the items, pieces, creatures, status effects and interface that play them, so a name the scene does not know is looked for in those lists. The walk looks for every sound Sparring will need at once, starts with the interface, where most of the duel's own sounds are, and stops as soon as all of them have been met; walking the whole game takes around a tenth of a second, and a name that exists nowhere costs one such walk before misses are answered at once. The duel's own sounds are looked up on entering a world and never during a duel. Sounds that belong only to locations, such as boss altars and waystones, are not indexed, because locations are loaded on demand and walking them would force those loads. Every sound is played locally, from a prefab spawned with its network view disabled and removed on a timer, so nobody else hears it and nothing is left behind.

## Limits

Creatures take no notice of a duel and fight both sides as usual, so a duel is only as safe as the ground it is fought on. Valheim is client authoritative, so a player who edits their own game can make themselves unkillable with or without this mod. Status effects added by other mods are not cleared unless listed in `AlsoClear`. If none of the shaders the ring can use are available, no ring is drawn, and the duel still ends when a fighter leaves the arena.

## Rehearsal commands

With `DebugCommands` on, which is off by default and set by the server, `/duel spar` rehearses a duel alone, and `/duel testwin`, `/duel testyield`, `/duel testmessages`, `/duel testannounce`, `/duel testsound` and `/duel panels` show the end of a duel, the messages and the panels.

## Tests

`tests/Sparring.Tests` covers what needs no game. The duel itself, from the challenge to the card, runs in `DuelFlow`, which reaches the game only through `IDuelWorld` (`GameWorld` is the game's side). The tests give each simulated player a game of their own on a simulated server, with one clock, the leases judged by the same rule the game uses, and the messages between games arriving when a test lets them, and play whole duels through the entry points the game calls: challenges and their answers, every refusal, withdrawing, walking away and the pause after a challenge came to nothing; the countdown and its sounds; yields, deaths and every other ending, who wins and who is lifted to what, and both halves of the card; a game that stops, a result that arrives after the lease is gone, and an accept that never pairs; practice duels, death and respawn, and rematches. Beside that they hold the rules the flow and the hooks ask: when two leases make a duel and when one runs out, and when the countdown has finished; who is free to duel; which killing blows become a yield and which still kill; what the hooks into the game decide (when a death check becomes a yield, which skills a duel teaches and at what rate, which blows spare built pieces, which blows count on the card); the ring's sizes and how far apart the fighters may get; the extra effects named in `AlsoClear`; reasons and names arriving from another game, and the limit on announcements; the yield key's hold, the words `/duel` takes and who you are looking at; the announcement's line and when the card is shown; what `/duel` says while Sparring is off; and the self-test's report. Tests state what has to hold and are written before the code that makes it hold. What needs the game itself is checked by the self-test, or by hand where two players are needed: reading the player's state, the hooks, the real messages between two games, and everything drawn or played. The tests run on .NET 8 against the real `BepInEx.dll` of a local profile, which is not part of the repository, so they run locally rather than on the build server:

```
dotnet test tests/Sparring.Tests
```

The build itself runs every .NET code analysis rule and fails on any finding (`BuildChecks.props`), but for the few that misread a Unity plugin, which `Analysis.globalconfig` turns off with the reason for each. It also compiles the doc comments, so a link to something renamed fails it. `src/ServerSync` is marked as generated code, which the analysis leaves alone, so it stays a plain copy of upstream's file. `tests/Sparring.Standards` holds what the compiler cannot. It compiles Sparring's source as the build does, against the stubs in `lib/`, and reads it with the compiler's own types; ServerSync is compiled with it but held to none of these. Text is compared and sorted ordinally, never in the PC's language, and a fraction or a time becomes text the same way on every PC. A Unity object is checked for none by Unity's own test. A failure caught is told in the log, or the catch says what the failure means, and a patch that fails lets the game's own method run, so a death Sparring could not judge stands. Every patch is in `Patches` and all of them go in together, a hook the game will not take switches Sparring off as a whole, every part of a duel checks for that first, and `Character.RPC_Damage` is never patched. The rules are handed to ServerSync and the preferences stay local. The server's clock is read only in `Lease.Now`, and local timers keep real time. Every game field reached by name is one the self-test looks for. Sparring writes no file and opens no program or website, every type but the plugin is internal, and comments carry no dates. Every file under `src/` is compiled into the unit tests or named in `GameOnlyFiles.cs` with why it needs the game, `Duel.Reset` forgets all it holds but what it keeps on purpose (the last opponent for a rematch, and how the last duel ended), and the plugin, the project and `manifest.json` name one version. These tests need no `BepInEx.dll`, so the build workflow runs them on every push:

```
dotnet test tests/Sparring.Standards
```

`.githooks/pre-push` runs both sets before every push and stops the push when one fails; without a local `BepInEx.dll` it leaves out the unit tests with a warning. It also runs `tools/check-shared.sh`, which compares the files listed in `tools/shared-files.txt` with Bindrune's copies beside this repository (the same but for the mod's name) and stops the push where one changed without the other; without a Bindrune checkout it says so and compares nothing. Git uses the hooks once told to, per clone: `git config core.hooksPath .githooks`.

## Self-test

The self-test is a plugin of its own, built from `selftest/` with `dotnet build selftest` and deployed beside Sparring. A release packages `Sparring.dll` alone, so players never have it. Installed, it answers `/duel selftest`, which checks Sparring against the running game and reports how it went: a top line with the verdict, then what failed, what needs a look, what passed and what is only information. The report goes to the console and to `Sparring-selftest.txt` in the BepInEx folder.

Most of it only reads: whether Sparring is on and each hook went in and `Character.RPC_Damage` is left to the game, whether the fields Sparring reads by name still exist, whether the duel's sounds, the challenge sound, the ring's shader and its markers resolve, how a hit with nobody named on it is judged, and whether you are read as free to duel, which you are when standing out of combat with PvP off. The rest fights three duels against yourself, each ending in a fall to zero by a blow, a burn and poison, and checks that each comes out as a yield through the game's own death check, along with the countdown, the card and what is left once a duel is over. Those falls are real, so a broken yield is a real death; run it near a bed with nothing hostile about, which it checks for before fighting.

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
