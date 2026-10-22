using System;
using System.Collections.Generic;
using System.Linq;
using OldMaid.Core;
using Renegade.Cards;
using Xunit;

namespace OldMaid.Tests;

public class OldMaidTests
{
	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(5)]
	public void FiftyOneCardsAreDealtAsEvenlyAsTheyGo(int players)
	{
		var random = new Random(2);
		var game = new OldMaidGame(OldMaidGame.ShuffledDeck(random), players, random);
		var sizes = Enumerable.Range(0, players).Select(p => game.Hand(p).Count).ToList();
		Assert.Equal(51, sizes.Sum());
		Assert.True(sizes.Max() - sizes.Min() <= 1);
		Assert.DoesNotContain(OldMaidGame.Removed, Enumerable.Range(0, players).SelectMany(game.Hand));
	}

	[Fact]
	public void PairsAreLaidDownAtTheStartAndNoOneKeepsAPair()
	{
		var random = new Random(3);
		var game = new OldMaidGame(OldMaidGame.ShuffledDeck(random), 3, random);
		var events = game.Opening();
		Assert.NotEmpty(events.OfType<Paired>());
		for (int p = 0; p < 3; p++)
			Assert.All(game.Hand(p).GroupBy(c => c.Rank), g => Assert.Single(g));
		Assert.IsType<TurnTo>(events[^1]);
	}

	[Fact]
	public void YouDrawFromThePlayerBeforeYou()
	{
		var random = new Random(4);
		var game = new OldMaidGame(OldMaidGame.ShuffledDeck(random), 3, random);
		game.Opening();
		int me = game.Current, from = game.Source;
		Assert.NotEqual(me, from);
		var card = game.Hand(from)[0];
		int before = game.Hand(from).Count;
		var events = game.Draw(0);
		Assert.Equal(card, Assert.IsType<Drew>(events[0]).Card);
		Assert.Equal(before - 1, game.Hand(from).Count);
	}

	[Theory]
	[InlineData(2, 1)]
	[InlineData(3, 2)]
	[InlineData(4, 3)]
	[InlineData(5, 4)]
	public void TheLastPlayerHoldsOnlyTheOldMaid(int players, int seed)
	{
		var random = new Random(seed);
		var game = new OldMaidGame(OldMaidGame.ShuffledDeck(random), players, random);
		game.Opening();
		int moves = 0;
		while (!game.IsOver && moves++ < 5000)
			game.Draw(random.Next(game.Hand(game.Source).Count));
		Assert.True(game.IsOver);
		var hand = game.Hand(game.Loser);
		var maid = Assert.Single(hand);
		Assert.Equal(Card.Queen, maid.Rank);
		Assert.Equal(1, Enumerable.Range(0, players).Sum(p => game.Hand(p).Count));
	}
}

public class DrawFromTheLeftTests
{
	[Fact]
	public void EachPlayerDrawsFromThePlayerOnTheirLeft()
	{
		var random = new System.Random(3);
		var game = new OldMaidGame(OldMaidGame.ShuffledDeck(random), 4, random, first: 0);
		var turn = System.Linq.Enumerable.OfType<TurnTo>(game.Opening()).Last();
		// Seat 1 sits on your left; it's the next player still in after the one drawing.
		int expected = System.Linq.Enumerable.First(System.Linq.Enumerable.Range(1, 3).Select(k => (turn.Player + k) % 4), p => !game.IsOut(p));
		Assert.Equal(expected, turn.From);
		Assert.Equal(expected, game.Source);
	}
}
