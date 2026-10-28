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
/// unseen, from the hand of the player on their left. On your turn that player holds their cards out
/// to you, spread wide: tap the one you want. Once you're safe, the rest plays out at double speed.
/// Don't be the one left holding the Old Maid.
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private OldMaidGame? _game;
	private int _first;
	private int _pairsDown;
	private int _presented = -1;

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
		_presented = -1;
		SpeedBoost = 1;
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
					News(drew.Player == 0
						? $"You drew the {Describe(drew.Card)} from {Names[drew.From]}."
						: $"{Names[drew.Player]} draws a card from {from}.");
					await Pause(0.35f);
					break;
				}
				case Paired paired:
					await LayDown(paired.Player, paired.Cards);
					News(paired.Player == 0 ? $"You lay down a pair of {Card.RankName(paired.Cards[0].Rank, plural: true).ToLowerInvariant()}." : $"{Names[paired.Player]} lays down a pair of {Card.RankName(paired.Cards[0].Rank, plural: true).ToLowerInvariant()}.");
					break;
				case Shuffled:
					SyncFans();
					await Pause(0.2f);
					break;
				case Out @out:
					Say(@out.Player, "Safe!");
					Hud.SetStatus(@out.Player == 0 ? "No cards left: you're safe! The others play on at double speed." : $"{Names[@out.Player]} has no cards left: safe!", Bright);
					await Pause(0.9f);
					// Only the computers are left: no need to make you wait.
					if (@out.Player == 0)
						SpeedBoost = 2;
					break;
				case TurnTo turn:
					SetScore();
					ShowTurn(turn.Player);
					if (turn.Player != 0 || Demo || Flag("autodraw"))
					{
						Hud.SetStatus(turn.Player == 0 ? $"You draw from {Names[turn.From]}." : $"{Names[turn.Player]} draws from {Who(turn.From)}.", Muted);
						await Pause(0.7f);
						if (round != Round)
							return;
						await Act(game.Draw(Random.Next(game.Hand(turn.From).Count)));
						return;
					}
					await Present(turn.From);
					if (round != Round)
						return;
					Busy = false;
					Say(turn.From, "Take one!", 0);
					Hud.SetStatus($"Your turn: tap one of {Names[turn.From]}'s cards.", Bright);
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
						over.Loser == 0 ? 1 : 0, over.Loser == 0 ? null : new[] { maid }, show.Origin, winningSeats: Enumerable.Range(0, Players).Where(p => p != over.Loser));
					return;
				}
			}
		}
	}


	/// <summary>
	/// The player you draw from holds their cards out to you: the fan comes up in front of you,
	/// backs towards you and spread wide, so every card is easy to tap. Their hand comes with it.
	/// </summary>
	private async Task Present(int seat)
	{
		_presented = seat;
		var eye = Table.Camera.GlobalPosition;
		var at = new Vector3(0, 1.45f, 0.85f);
		var forward = (at - eye).Normalized();
		var pivot = new Transform3D(Basis.LookingAt(-forward, Vector3.Up), at);
		var fan = Fans[seat];
		var hold = Hands[seat].Holder;
		if (hold != null)
			CardMotion.Fly(hold, pivot * (fan.Pivot.AffineInverse() * hold.GlobalTransform), 0.35f, lift: 0.1f);
		fan.Pivot = pivot;
		fan.Radius = 0.75f;
		fan.Spread = 15f;
		fan.MaxArc = 150f;
		await Await(fan.Settle(0.35f));
	}

	/// <summary>The presented fan goes back to its player's seat.</summary>
	private void Unpresent()
	{
		if (_presented < 0)
			return;
		int seat = _presented;
		_presented = -1;
		var fan = Fans[seat];
		var hold = Hands[seat].Holder;
		if (hold != null)
			CardMotion.Fly(hold, Table.FanPivot[seat] * (fan.Pivot.AffineInverse() * hold.GlobalTransform), 0.35f, lift: 0.1f);
		fan.Pivot = Table.FanPivot[seat];
		fan.Radius = 0.5f;
		fan.Spread = 9f;
		fan.MaxArc = 80f;
		fan.Settle(0.35f);
	}

	/// <summary>You take the card you tapped from the presented fan, which then goes back.</summary>
	private async Task DrawPresented(int index)
	{
		Busy = true;
		var events = _game!.Draw(index);
		Unpresent();
		await Act(events);
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
		Run(DrawPresented(index));
	}
}
