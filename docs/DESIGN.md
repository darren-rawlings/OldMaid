# Old Maid: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)).

## The game
Old Maid for 2 to 5: you and up to four computer players (the Bicycle rules). One queen (clubs)
is taken out, so one queen can never pair: the Old Maid. All 51 cards are dealt round. Everyone
lays down their pairs. Then each in turn draws a card, unseen, from the hand of the player
before them; a pair goes straight down, and the drawer mixes their hand before it's offered on.
Run out of cards and you're safe; whoever is left holding the Old Maid loses. Rules in
`src/Core/OldMaidGame.cs` (tested).

## Feel
- Shuffle (riffles and a bridge) and a brisk deal; glass hands fan the cards.
- Everyone lays their pairs on a loose heap in the middle at once.
- On your turn, you tap a card in the fan of the player before you; your hand pinches it out.
- At the end the Old Maid is shown to the table with a burst of sparks (and fire, if it isn't you).

## Settings
Players (2–5), Draw automatically.

## Scope for version 1
Everything above. Draws are blind, so there are no computer levels. Later: tutorial clips,
a "tease" (the computer fanning the Old Maid up a little to tempt you).
