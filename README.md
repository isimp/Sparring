# Sparring

Duel another player without either of you dying.

Look at another player and type /duel in chat. They get a prompt that shows which keys accept and decline the challenge. A ring is marked out on the ground, a short countdown runs, and the fight begins. Whoever runs out of health yields instead of dying. When the duel ends, both fighters get some health back and their harmful effects are cleared, and a card shows damage dealt and taken, hits and how long it lasted. Hold Backspace to give up.

Only your opponent's hits are made safe. Creatures, falls and drowning can still kill you. While the duel lasts, creatures ignore both fighters unless you attack them. Leaving the ring ends the duel, and a disconnect or crash ends every protection on its own within a few seconds.

![A Sparring duel in progress](https://raw.githubusercontent.com/isimp/Sparring/main/docs/images/screenshot.webp)

## AI notice

Most of Sparring was written by Claude Code (Anthropic), which did the heavy lifting on implementation and design. Heads-up so you can judge for yourself.

## Commands

Add a number to /duel to set the ring size in metres. /duel rematch challenges your last opponent, /duel preview draws a ring where you stand, and /duel status shows what is going on. Accepting, declining and yielding also work from chat with /duel accept, /duel decline and /duel yield.

## Multiplayer

Both fighters need Sparring. Players standing nearby need it too for creatures to leave the fighters alone. On a server running Sparring, the server's duel rules apply to everyone, while keys and display options stay your own. Players without the mod can still join.

## Settings

All settings are in BepInEx/config/isimp.Sparring.cfg, each with a description.

## More

Technical notes and build instructions are on GitHub at https://github.com/isimp/Sparring
