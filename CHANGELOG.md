# Changelog

## 0.2.0

Creatures no longer ignore the fighters. A duel used to leave both of them unnoticed by the wildlife, which turned a challenge into a way of putting someone where they could not be helped and nothing would come for whoever arranged it. Duels are now fought in the world as it is, and the standing rule that a duel cannot be started with anything hostile around the ring is what keeps them honest. The `NeutralToCreatures` setting is gone; an older entry left in your config file is ignored.

The card shown when a duel ends now counts every blow it should. Damage is read in a way that survives another mod throwing an exception partway through the game's own damage path, which otherwise left the card with nothing on it and so hidden, looking no different from a duel in which nobody landed a hit.

Fire and poison from an opponent count as theirs. Neither lands with the blow that carried it, arriving instead as damage over time with nobody named on it, so an arrow that set a fighter alight and then killed them used to be a real death. It now ends in a yield like any other blow of theirs, and it shows on the card.

A duel that somebody wins names the winner on both screens. How a fight ended is now sent to the other fighter before this side packs the duel away, so they learn who won rather than only that it is over, and a duel that loses sight of its opponent waits a moment for that word to arrive before settling for no winner at all.

## 0.1.0

First release.
