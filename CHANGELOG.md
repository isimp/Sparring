# Changelog

## 0.2.3

When a game update changes something Sparring hooks into, Sparring now switches itself off as a whole and says so, rather than `/duel` simply going missing. `/duel` answers that Sparring is off and names what the game changed, the log has the full error, and no duel can start with you until Sparring is updated. Any hook that did go in comes out again, so no duel is ever fought with part of its protection missing.

Felling a tree during a duel raises WoodCutting as it does outside one. `SkillGain` and `SkillGainRate` are meant for weapon skills, Blocking and Dodge, and also caught WoodCutting, which only ever comes from chopping trees.

## 0.2.2

The Staff of Protection's shield, and any other area effect meant for friendly players, reaches them again. Those effects carry the same mark a duel blow uses to get past the PvP check, and the `RefusePvpBypass` setting treated every such hit from a player you were not duelling as forged and stripped the mark. The setting is gone, and Sparring now only marks its own duel blows and leaves every other hit as the game sent it. An older entry left behind in a config file is ignored.

## 0.2.1

A duel has sounds of its own. Accepting a challenge answers the one who sent it, the countdown ticks each second and a bell marks the moment blows start counting, being knocked down has a sound, and a challenge turned down or withdrawn, or a duel called off, has another. All of them are the game's own, only you hear them, and `DuelSounds` turns them off.

`ChallengeSoundPrefab` accepts far more of the game's sounds than before. Most sounds are kept with the item, piece or creature that plays them rather than registered by name, and those are now found as well.

## 0.2.0

Creatures no longer ignore the fighters. Going unnoticed by the wildlife made a challenge worth sending in bad faith, which is not what duelling is for. Duels are fought in the world as it stands, and the rule that none can be started with anything hostile around the ring is what keeps them safe. The `NeutralToCreatures` setting is gone, and an older entry left behind in a config file is ignored. Only the two fighters need the mod now.

The card shown when a duel ends now counts every blow it should. Damage is read in a way that survives another mod throwing an exception partway through the game's own damage path, which otherwise left the card with nothing on it and so hidden, looking no different from a duel in which nobody landed a hit.

Fire and poison from an opponent count as theirs. Neither lands with the blow that carried it, arriving instead as damage over time with nobody named on it, so an arrow that set a fighter alight and then killed them used to be a real death. It now ends in a yield like any other blow of theirs, and it shows on the card.

A duel that somebody wins names the winner on both screens. How a fight ended is now sent to the other fighter before this side packs the duel away, so they learn who won rather than only that it is over, and a duel that loses sight of its opponent waits a moment for that word to arrive before settling for no winner at all.

## 0.1.0

First release.
