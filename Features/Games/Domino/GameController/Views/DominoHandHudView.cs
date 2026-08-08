using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// The shipping hand view: the player's tiles as buttons along the bottom of the screen.
///
/// Clicking a tile that fits only one end plays it straight away; a tile that fits both opens a
/// small chooser, because those are two different places on the table. Nothing here knows what a
/// legal move is — the controller hands the list in.
/// </summary>
[GlobalClass]
public partial class DominoHandHudView : DominoHandView
{
	[Export] public CanvasLayer Canvas;
	[Export] public Label TurnLabel;
	[Export] public Label EndsLabel;
	[Export] public Label BoneyardLabel;
	[Export] public Label OpponentsLabel;
	[Export] public Label RejectionLabel;
	[Export] public Label ControlsLabel;
	[Export] public HBoxContainer HandBox;
	[Export] public Button DrawButton;
	[Export] public Button PassButton;
	[Export] public Button SurrenderButton;
	[Export] public Control EndPicker;
	[Export] public Button PlayLeftButton;
	[Export] public Button PlayRightButton;
	[Export] public Button CancelEndButton;

	private static readonly Color YourTurnColor = new(1.0f, 0.478431f, 0.2f);
	private static readonly Color NormalTextColor = new(0.933333f, 0.956863f, 0.984314f);
	private static readonly Color DimTextColor = new(0.678431f, 0.752941f, 0.839216f);

	private readonly List<Button> _tileButtons = new();
	private int _pendingTileId = DominoTileId.NoEnd;
	private bool _interactive = true;
	private float _rejectionSeconds;

	public override void _Ready()
	{
		// A CanvasLayer is not hidden by its parent's Hide(), so without this guard every peer
		// would draw every other peer's hand. CuePowerBar guards the same way.
		if (!IsMultiplayerAuthority())
		{
			Canvas?.Hide();
			SetProcess(false);
			return;
		}

		DrawButton.Pressed += () => EmitSignal(SignalName.DrawRequested);
		PassButton.Pressed += () => EmitSignal(SignalName.PassRequested);
		SurrenderButton.Pressed += () => EmitSignal(SignalName.SurrenderRequested);

		PlayLeftButton.Pressed += () => PlayPendingOn(ChainEnd.Left);
		PlayRightButton.Pressed += () => PlayPendingOn(ChainEnd.Right);
		CancelEndButton.Pressed += CloseEndPicker;

		EndPicker.Hide();
		RejectionLabel.Text = "";
		SetTopViewActive(false);
	}

	public override void SetTopViewActive(bool active)
	{
		if (ControlsLabel == null)
			return;

		ControlsLabel.Text = active
			? "T: voltar para a cadeira"
			: "Segure o botão direito para olhar ao redor   •   T: ver a mesa de cima";
	}

	public override void _Process(double delta)
	{
		if (_rejectionSeconds <= 0.0f)
			return;

		_rejectionSeconds -= (float)delta;
		if (_rejectionSeconds <= 0.0f)
			RejectionLabel.Text = "";
	}

	public override void Refresh(
		int[] hand,
		IReadOnlyList<MoveOption> playableMoves,
		bool isYourTurn,
		bool canDraw,
		bool mustPass)
	{
		if (!IsMultiplayerAuthority() || Game == null)
			return;

		RebuildHand(hand, playableMoves, isYourTurn);
		RefreshStatus(isYourTurn);

		DrawButton.Visible = isYourTurn && canDraw && _interactive;
		PassButton.Visible = isYourTurn && mustPass && _interactive;
		SurrenderButton.Visible = _interactive;

		// The chain moved under an open chooser, so whatever it was offering is stale.
		if (EndPicker.Visible && !isYourTurn)
			CloseEndPicker();
	}

	private void RefreshStatus(bool isYourTurn)
	{
		var turnOwnerId = Game.TurnOwner != null ? (string)Game.TurnOwner.Name : null;
		TurnLabel.Text = isYourTurn ? "Sua vez!" : $"Vez de {NameOf(turnOwnerId)}";
		TurnLabel.AddThemeColorOverride("font_color", isYourTurn ? YourTurnColor : NormalTextColor);

		EndsLabel.Text = Game.LeftEnd == DominoTileId.NoEnd
			? "Mesa vazia"
			: $"Pontas: {Game.LeftEnd}  e  {Game.RightEnd}";

		BoneyardLabel.Text = $"Monte: {Game.BoneyardCount}";

		var others = new List<string>();
		foreach (var playerIdVariant in Game.TurnOrder)
		{
			var playerId = (string)playerIdVariant;
			if (playerId == (string)Player.Name)
				continue;

			var count = Game.HandCounts.TryGetValue(playerId, out var held) ? held : 0;
			others.Add($"{NameOf(playerId)}: {count}");
		}

		OpponentsLabel.Text = string.Join("   •   ", others);
		OpponentsLabel.AddThemeColorOverride("font_color", DimTextColor);
	}

	private void RebuildHand(int[] hand, IReadOnlyList<MoveOption> playableMoves, bool isYourTurn)
	{
		foreach (var button in _tileButtons)
			button.QueueFree();

		_tileButtons.Clear();

		if (hand == null)
			return;

		foreach (var tileId in hand)
		{
			var ends = EndsFor(playableMoves, tileId);

			var button = new Button
			{
				Text = DominoTileId.Label(tileId),
				Disabled = !_interactive || !isYourTurn || ends.Count == 0,
				CustomMinimumSize = new Vector2(56.0f, 64.0f),
			};
			button.AddThemeFontSizeOverride("font_size", 20);

			var captured = tileId;
			button.Pressed += () => OnTilePressed(captured, ends);

			HandBox.AddChild(button);
			_tileButtons.Add(button);
		}
	}

	private static List<ChainEnd> EndsFor(IReadOnlyList<MoveOption> playableMoves, int tileId)
	{
		var ends = new List<ChainEnd>();
		if (playableMoves == null)
			return ends;

		foreach (var move in playableMoves)
		{
			if (move.TileId == tileId && !ends.Contains(move.End))
				ends.Add(move.End);
		}

		return ends;
	}

	private void OnTilePressed(int tileId, List<ChainEnd> ends)
	{
		if (ends.Count == 0)
			return;

		// Only ask when there is something to ask about.
		if (ends.Count == 1)
		{
			EmitSignal(SignalName.TilePlayRequested, tileId, (int)ends[0]);
			return;
		}

		_pendingTileId = tileId;
		PlayLeftButton.Text = $"◀  na ponta {Game.LeftEnd}";
		PlayRightButton.Text = $"na ponta {Game.RightEnd}  ▶";
		EndPicker.Show();
	}

	private void PlayPendingOn(ChainEnd end)
	{
		if (_pendingTileId == DominoTileId.NoEnd)
			return;

		var tileId = _pendingTileId;
		CloseEndPicker();
		EmitSignal(SignalName.TilePlayRequested, tileId, (int)end);
	}

	private void CloseEndPicker()
	{
		_pendingTileId = DominoTileId.NoEnd;
		EndPicker.Hide();
	}

	public override void SetInteractive(bool interactive)
	{
		_interactive = interactive;

		if (!interactive)
		{
			CloseEndPicker();
			DrawButton.Visible = false;
			PassButton.Visible = false;
			SurrenderButton.Visible = false;

			foreach (var button in _tileButtons)
				button.Disabled = true;
		}
	}

	public override void ShowRejection(string reason)
	{
		if (!IsMultiplayerAuthority())
			return;

		RejectionLabel.Text = DescribeRejection(reason);
		_rejectionSeconds = 3.0f;
	}

	private static string DescribeRejection(string reason) => reason switch
	{
		"not_your_turn" => "Não é a sua vez.",
		"stale_turn" => "A vez já mudou.",
		"tile_not_in_hand" => "Você não tem essa peça.",
		"tile_does_not_match" => "A peça não encaixa nessa ponta.",
		"has_legal_move" => "Você ainda tem jogada.",
		"boneyard_empty" => "O monte acabou.",
		"must_draw" => "Compre antes de passar.",
		"match_not_running" => "A partida não está em andamento.",
		_ => $"Jogada recusada ({reason}).",
	};

	public override void Clear()
	{
		foreach (var button in _tileButtons)
			button.QueueFree();

		_tileButtons.Clear();
		CloseEndPicker();
		Canvas?.Hide();
	}

	private static string NameOf(string playerId)
	{
		if (string.IsNullOrEmpty(playerId))
			return "—";

		var player = PlayerRegistry.Instance.GetPlayerById(playerId);
		return player != null && !string.IsNullOrWhiteSpace(player.Nickname)
			? player.Nickname
			: $"Jogador {playerId}";
	}
}
