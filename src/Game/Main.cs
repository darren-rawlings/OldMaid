using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OldMaid.Core;
using Renegade.Cards;
using Renegade.Play;
using Renegade.Ui;

namespace OldMaid.Game;

/// <summary>
/// Old Maid with one to four computer players: one queen is taken out, the rest is shuffled and
/// dealt round, everyone lays down their pairs on the felt, and then each in turn draws a card,
/// unseen, from the hand of the player before them. On your turn, tap a card in their fan. Don't
/// be the one left holding the Old Maid.
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private OldMaidGame? _game;
	private int _first;
	private int _pairsDown;

	protected override string Title => "Old Maid";
	protected override string Hint => "Tap a card in their hand to draw it";
	protected override bool HasLevels => false;
	protected override int MinPlayers => 2;
	protected override int MaxPlayers => 5;
	protected override int DefaultPlayers => 3;
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => _game is { IsOver: false };

	protected override void AddSettings()
	{
		Hud.AddToggle("Draw automatically", Flag("autodraw"), on =>
		{
			SetFlag("autodraw", on);
			if (on && !Busy && _game is { IsOver: false, Current: 0 })
				Run(Act(_game.Draw(Random.Next(_game.Hand(_game.Source).Count))));
		});
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_nodes.Clear();
		_game = null;
		_pairsDown = 0;
		SetScore();
		var order = OldMaidGame.ShuffledDeck(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		int first = _first;
		_first = (_first + 1) % Players;
		_game = new OldMaidGame(order, Players, Random, first);

		// Deal all 51 round the table, starting with the first player.
		await DealRound(nodes, Enumerable.Range(0, order.Count).Select(i => (first + i) % Players).ToList(), spacing: 0.06f);
		if (round != Round)
			return;
		await PickUpAll(p => _game.Hand(p).Select(c => _nodes[c]));
		SyncFans();
		if (round != Round)
			return;
		Hud.SetRestartVisible(true);
		Hud.SetStatus("Everyone lays down their pairs…", Muted);
		var opening = _game.Opening();
		// Everyone lays their pairs down at once, then the first turn begins.
		var pairs = opening.OfType<Paired>().GroupBy(e => e.Player)
			.Select(g => LayDown(g.Key, g.SelectMany(e => e.Cards).ToList())).ToList();
		await Task.WhenAll(pairs);
		SetScore();
		await Act(opening.Where(e => e is not Paired).ToList());
	}

	/// <summary>Keeps every fan in the order its player holds the cards (yours sorted).</summary>
	private void SyncFans()
	{
		for (int p = 1; p < Players; p++)
		{
			Fans[p].Reorder(_game!.Hand(p).Select(c => _nodes[c]));
			Fans[p].Settle(0.2f);
		}
	}

	/// <summary>A player's hand lays pairs face up on the heap in the middle of the table.</summary>
	private async Task LayDown(int p, IReadOnlyList<Card> cards)
	{
		var nodes = cards.Select(c => _nodes[c]).ToList();
		var targets = new List<Transform3D>();
		for (int i = 0; i < nodes.Count; i += 2)
		{
			// Each pair a little apart and askew, so the heap looks dropped, not stacked.
			float angle = (float)Random.NextDouble() * Mathf.Tau;
			float r = 0.15f + (float)Random.NextDouble() * 0.55f;
			var at = Table.Centre + new Vector3(Mathf.Cos(angle) * r * 1.3f, 0.003f * (_pairsDown + 1), Mathf.Sin(angle) * r * 0.7f);
			float yaw = (float)Random.NextDouble() * Mathf.Tau;
			targets.Add(FaceUpAt(at, yaw));
			targets.Add(FaceUpAt(at + new Vector3(0.12f, 0.003f, 0.05f), yaw + 0.25f));
			_pairsDown++;
		}
		foreach (var node in nodes)
			Fans[p].Remove(node);
		Fans[p].Settle();
		await Hands[p].Carry(nodes, targets, seconds: 0.45f, lift: 0.35f);
	}

	/// <summary>Acts out what happened, then hands over to whoever draws next.</summary>
	private async Task Act(List<OldMaidEvent> events)
	{
		var game = _game!;
		int round = Round;
		Busy = true;
		foreach (var e in events)
		{
			if (round != Round)
				return;
			switch (e)
			{
				case Drew drew:
				{
					var node = _nodes[drew.Card];
					await IntoFan(drew.Player, new[] { node });
					string from = drew.From == 0 ? "you" : Names[drew.From];
					Hud.SetStatus(drew.Player == 0
						? $"You drew the {Describe(drew.Card)} from {Names[drew.From]}."
						: $"{Names[drew.Player]} draws a card from {from}.", Muted);
					await Pause(0.35f);
					break;
				}
				case Paired paired:
					await LayDown(paired.Player, paired.Cards);
					Hud.SetStatus(paired.Player == 0 ? $"A pair of {Card.RankName(paired.Cards[0].Rank, plural: true).ToLowerInvariant()}!" : $"{Names[paired.Player]} lays down a pair.", Bright);
					break;
				case Shuffled:
					SyncFans();
					await Pause(0.2f);
					break;
				case Out @out:
					Say(@out.Player, "Safe!");
					Hud.SetStatus(@out.Player == 0 ? "No cards left: you're safe!" : $"{Names[@out.Player]} has no cards left: safe!", Bright);
					await Pause(0.9f);
					Say(@out.Player, null);
					break;
				case TurnTo turn:
					SetScore();
					if (turn.Player != 0 || Demo || Flag("autodraw"))
					{
						Hud.SetStatus(turn.Player == 0 ? $"You draw from {Names[turn.From]}." : $"{Names[turn.Player]} draws from {Who(turn.From)}.", Muted);
						await Pause(0.7f);
						if (round != Round)
							return;
						await Act(game.Draw(Random.Next(game.Hand(turn.From).Count)));
						return;
					}
					Busy = false;
					Hud.SetStatus($"Your turn: tap a card in {Names[turn.From]}'s hand.", Bright);
					return;
				case GameOver over:
				{
					SetScore();
					var maid = _nodes[game.Hand(over.Loser).Single()];
					// The Old Maid is shown to the table.
					Fans[over.Loser].Remove(maid);
					var show = FaceUpAt(Table.Centre + new Vector3(0, 0.08f, 0.55f), 0);
					await PlayToTable(over.Loser, maid, show);
					Sparkle.Burst(this, show.Origin + Vector3.Up * 0.2f);
					Finish(over.Loser == 0 ? "Oh no! You're left holding the Old Maid." : $"{Names[over.Loser]} is left holding the Old Maid. You're safe!",
						over.Loser == 0 ? 1 : 0, over.Loser == 0 ? null : new[] { maid }, show.Origin);
					return;
				}
			}
		}
	}

	private static string Describe(Card card) => $"{Card.RankName(card.Rank).ToLowerInvariant()} of {card.Suit.ToString().ToLowerInvariant()}";

	private void SetScore()
	{
		var game = _game;
		if (game == null)
		{
			Hud.SetScore($"Safe {Wins.First} of {Wins.First + Wins.Second}");
			return;
		}
		var counts = Enumerable.Range(0, Players).Select(p => $"{(p == 0 ? "you" : Names[p])} {game.Hand(p).Count}");
		Hud.SetScore($"Cards: {string.Join(" · ", counts)}   ·   Safe {Wins.First} of {Wins.First + Wins.Second}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_game is not { IsOver: false, Current: 0 } game)
			return;
		var from = game.Source;
		var hit = HitCard(Fans[from].Cards, origin, direction);
		if (hit == null)
			return;
		int index = game.Hand(from).ToList().IndexOf(hit.Card);
		if (index < 0)
			return;
		Hud.HideHint();
		Run(Act(game.Draw(index)));
	}
}
