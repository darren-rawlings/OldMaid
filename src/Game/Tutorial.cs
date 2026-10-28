using System.Collections.Generic;
using Renegade.Ui;

namespace OldMaid.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("The Old Maid",
			"One queen is taken out of the deck, so one queen can never make a pair: she's the Old Maid. The rest is dealt out, and everyone lays down their pairs.",
			"res://tutorial/pairs.ogv"),
		new TutorialPage("Draw",
			"On your turn, the player on your left holds their cards out to you, spread wide: tap one to draw it, unseen. If it makes a pair, it goes straight down. Then they draw from the player on their left.",
			"res://tutorial/draw.ogv"),
		new TutorialPage("Get out",
			"Run out of cards and you're safe. The last player left, holding the Old Maid, loses. Play with up to five in Settings › Players. Once you're safe, the others play on at double speed.",
			"res://tutorial/pairs.ogv"),
	};
}
