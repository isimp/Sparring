# Changelog

## 0.1.1

The summary shown at the end of a duel now counts every blow it should. It listens for damage in a way that survives another mod throwing an exception partway through the game's damage path, which otherwise left the tally empty and the summary hidden with nothing to say why.

Leaving the ring once a duel is under way now yields to your opponent rather than simply calling the fight off, so walking away is a way of losing rather than a way of avoiding a loss. Withdrawing a challenge reaches the other player, who can no longer accept one that has already been taken back. A challenge is refused up front when either fighter is in combat, has PvP on, is otherwise occupied, or has creatures too close, and repeat challenges to the same player are spaced out so a declined one cannot be used to pester them. The yield key is shown under the health bar while you fight, a rematch is offered in the ring the last duel was fought in, and the first `/duel` no longer stutters.

## 0.1.0

First release.
