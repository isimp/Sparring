# Changelog

## 0.1.1

The card shown when a duel ends now counts every blow it should. Damage is read in a way that survives another mod throwing an exception partway through the game's own damage path, which otherwise left the card with nothing on it and so hidden, looking no different from a duel in which nobody landed a hit.

A duel that somebody wins names the winner on both screens. How a fight ended is now sent to the other fighter before this side packs the duel away, so they learn who won rather than only that it is over, and a duel that loses sight of its opponent waits a moment for that word to arrive before settling for no winner at all.

## 0.1.0

First release.
