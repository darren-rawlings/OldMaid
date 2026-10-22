using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace OldMaid.Core;

/// <summary>Something that happened, in order, for the table to act out.</summary>
public abstract record OldMaidEvent;

/// <summary>A player laid down a pair, face up.</summary>
public sealed record Paired(int Player, IReadOnlyList<Card> Cards) : OldMaidEvent;

/// <summary>A player drew a card, unseen, from the hand of the player before them.</summary>
public sealed record Drew(int Player, int From, int Index, Card Card) : OldMaidEvent;

/// <summary>A player has no cards left: they're safe.</summary>
public sealed record Out(int Player) : OldMaidEvent;

/// <summary>A player mixed up their hand (after drawing), so no one knows where anything is.</summary>
public sealed record Shuffled(int Player) : OldMaidEvent;

/// <summary>Whose turn it is to draw, and from whom.</summary>
public sealed record TurnTo(int Player, int From) : OldMaidEvent;

/// <summary>Everyone else is out: the last player holds the Old Maid.</summary>
public sealed record GameOver(int Loser) : OldMaidEvent;

/// <summary>
/// Old Maid for 2 to 5 (the Bicycle rules): one queen is taken out of the deck, so one queen can
/// never be paired: she's the Old Maid. All 51 cards are dealt out, as evenly as they go. Everyone
/// lays down the pairs they hold. Then, in turn, each player draws a card, unseen, from the hand
/// of the player on their left (the next player still in); a pair is laid down at once, and the
/// player drawn from goes next. A player with no cards is out (safe).
/// Whoever is left holding the Old Maid loses.
/// </summary>
public sealed class OldMaidGame
{
	public const int MinPlayers = 2, MaxPlayers = 5;

	/// <summary>The queen taken out before the deal.</summary>
	public static readonly Card Removed = new(Card.Queen, Suit.Clubs);

	private readonly List<Card>[] _hands;
	private readonly bool[] _out;
	private readonly Random _random;
	private bool _opened;

	/// <summary>Deals from a deck of the 51 cards (top card first), round the table from the first player.</summary>
	public OldMaidGame(IReadOnlyList<Card> deck, int players, Random random, int first = 0)
	{
		if (deck.Count != 51 || deck.Distinct().Count() != 51 || deck.Contains(Removed))
			throw new ArgumentException("Old Maid needs the deck without one queen.", nameof(deck));
		if (players is < MinPlayers or > MaxPlayers)
			throw new ArgumentOutOfRangeException(nameof(players), "Old Maid is for 2 to 5 players.");
		Players = players;
		_random = random;
		_hands = Enumerable.Range(0, players).Select(_ => new List<Card>()).ToArray();
		_out = new bool[players];
		Dealt = deck.ToList();
		for (int i = 0; i < deck.Count; i++)
			_hands[(first + i) % players].Add(deck[i]);
		Current = first;
	}

	/// <summary>The 51-card deck, shuffled.</summary>
	public static List<Card> ShuffledDeck(Random random)
	{
		var deck = Deck.Standard().Where(c => c != Removed).ToList();
		Deck.Shuffle(deck, random);
		return deck;
	}

	public int Players { get; }

	/// <summary>The cards dealt, in dealing order (round the table, starting with the first player).</summary>
	public IReadOnlyList<Card> Dealt { get; }

	public int Current { get; private set; }

	/// <summary>A player's hand, in the order it's held (draws are by position in this order).</summary>
	public IReadOnlyList<Card> Hand(int player) => _hands[player];

	public bool IsOut(int player) => _out[player];
	public bool IsOver { get; private set; }
	public int Loser { get; private set; } = -1;

	/// <summary>The player the current player draws from: the nearest one on their left (after them) still in.</summary>
	public int Source => After(Current);

	private int Before(int player)
	{
		for (int k = 1; k < Players; k++)
		{
			int p = (player - k + Players) % Players;
			if (!_out[p])
				return p;
		}
		return player;
	}

	private int After(int player)
	{
		for (int k = 1; k < Players; k++)
		{
			int p = (player + k) % Players;
			if (!_out[p])
				return p;
		}
		return player;
	}

	/// <summary>After the deal: everyone lays down their pairs; anyone left with nothing is out. Call once.</summary>
	public List<OldMaidEvent> Opening()
	{
		if (_opened)
			throw new InvalidOperationException("The game has already opened.");
		_opened = true;
		var events = new List<OldMaidEvent>();
		for (int i = 0; i < Players; i++)
		{
			int p = (Current + i) % Players;
			LayPairs(p, events);
		}
		for (int p = 0; p < Players; p++)
			CheckOut(p, events);
		if (_out[Current])
			Current = After(Current);
		Next(events, Current);
		return events;
	}

	/// <summary>The current player draws the card at a position in the source player's hand.</summary>
	public List<OldMaidEvent> Draw(int index)
	{
		if (!_opened)
			throw new InvalidOperationException("Call Opening() after the deal first.");
		if (IsOver)
			throw new InvalidOperationException("The game is over.");
		int from = Source;
		var card = _hands[from][index];
		_hands[from].RemoveAt(index);
		_hands[Current].Add(card);
		var events = new List<OldMaidEvent> { new Drew(Current, from, index, card) };
		LayPairs(Current, events);
		CheckOut(from, events);
		CheckOut(Current, events);
		// The one who just drew mixes their hand up before offering it on.
		if (!_out[Current] && _hands[Current].Count > 1)
		{
			Deck.Shuffle(_hands[Current], _random);
			events.Add(new Shuffled(Current));
		}
		Next(events, After(Current));
		return events;
	}

	private void Next(List<OldMaidEvent> events, int player)
	{
		int stillIn = Enumerable.Range(0, Players).Count(p => !_out[p]);
		if (stillIn <= 1)
		{
			IsOver = true;
			Loser = Enumerable.Range(0, Players).First(p => !_out[p] || _hands[p].Count > 0);
			events.Add(new GameOver(Loser));
			return;
		}
		Current = player;
		events.Add(new TurnTo(Current, Source));
	}

	private void LayPairs(int player, List<OldMaidEvent> events)
	{
		foreach (var group in _hands[player].GroupBy(c => c.Rank).Where(g => g.Count() >= 2).ToList())
		{
			var cards = group.ToList();
			for (int i = 0; i + 1 < cards.Count; i += 2)
			{
				_hands[player].Remove(cards[i]);
				_hands[player].Remove(cards[i + 1]);
				events.Add(new Paired(player, new[] { cards[i], cards[i + 1] }));
			}
		}
	}

	private void CheckOut(int player, List<OldMaidEvent> events)
	{
		if (!_out[player] && _hands[player].Count == 0)
		{
			_out[player] = true;
			events.Add(new Out(player));
		}
	}
}
