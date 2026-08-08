using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// The player's hand as tiles held in front of the camera, replacing the panel of buttons.
///
/// Holds the whole selection/aim/rotate model; the states drive it and decide what input means
/// right now, exactly the way the cue's states drive the cue. Nothing here talks to the network —
/// it emits the same four intents the abstraction has always had, and the controller forwards them.
///
/// The placeholder rig is a flat palm plus the player's real tiles fanned out. When the animated
/// first-person hand arrives, replace the HandRig subtree and assign AnimationPlayer: the state
/// names and clip names below are already the contract.
/// </summary>
[GlobalClass]
public partial class DominoHand3DView : DominoHandView
{
	[Export] public Node3D HandRig;
	[Export] public Node3D TileSlots;
	[Export] public Node3D GhostSlot;
	[Export] public PackedScene TileScene;

	/// <summary>The aiming dot. The only screen-space element left in the game.</summary>
	[Export] public DominoHandCrosshair Crosshair;

	/// <summary>
	/// Everything the game needs to say in words, in world space rather than on a panel: a refused
	/// move, or how far along leaving the table is.
	/// </summary>
	[Export] public Label3D MessageLabel;

	[Export] public Vector3 MessageOffset = new(0.0f, -0.05f, -0.55f);

	/// <summary>Optional. Each state plays its clip here when a real rig is wired up.</summary>
	[Export] public AnimationPlayer AnimationPlayer;

	[ExportGroup("Fan")]
	[Export] public Vector3 HandOffset = new(0.03f, -0.075f, -0.32f);
	[Export] public float FanSpreadDeg = 42.0f;
	[Export] public float FanRadius = 0.13f;
	[Export] public float SelectedLift = 0.03f;

	/// <summary>Backward lean of the held tiles, so the faces angle toward the player's eyes.</summary>
	[Export] public float TileTiltDeg = -22.0f;

	/// <summary>Tiles the player cannot play are still shown, just dimmed — you hold your whole hand.</summary>
	[Export] public float UnplayableAlpha = 0.35f;

	private readonly List<DominoTile> _fan = new();
	private int[] _hand = System.Array.Empty<int>();
	private IReadOnlyList<MoveOption> _moves = new List<MoveOption>();
	private DominoTile _ghost;
	private StandardMaterial3D _ghostMaterial;
	private float _messageSeconds;

	public bool IsYourTurn { get; private set; }
	public bool CanDraw { get; private set; }
	public bool MustPass { get; private set; }

	/// <summary>Index into the hand, or -1. Always a playable tile while it is the player's turn.</summary>
	public int SelectedIndex { get; private set; } = -1;

	public int SelectedTileId =>
		SelectedIndex >= 0 && SelectedIndex < _hand.Length ? _hand[SelectedIndex] : DominoTileId.NoEnd;

	/// <summary>The half the player is presenting to the chain. This is what A/D turns.</summary>
	public int LeadingPips { get; private set; } = DominoTileId.NoEnd;

	/// <summary>The end being aimed at. Phase 3 takes the first legal one; the crosshair replaces this.</summary>
	public ChainEnd AimedEnd { get; private set; } = ChainEnd.Right;

	/// <summary>The stock place under the aim, or NoEnd. Phase 3 takes the first available.</summary>
	public int AimedSlot { get; private set; } = DominoTileId.NoEnd;

	public bool HasPlayableTile => _moves.Count > 0;

	public override void _Ready()
	{
		// A hand only exists for the peer holding it.
		if (!IsMultiplayerAuthority())
		{
			Hide();
			SetProcess(false);
			return;
		}

		_ghostMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.4f, 1.0f, 0.5f, 0.45f),
		};
	}

	public override void Setup(DominoGame game, Player player)
	{
		base.Setup(game, player);

		if (HandRig != null)
			HandRig.TopLevel = true;

		if (GhostSlot != null)
			GhostSlot.TopLevel = true;

		HideGhost();
	}

	public override void _Process(double delta)
	{
		if (!IsMultiplayerAuthority() || HandRig == null || Game?.Camera == null)
			return;

		// Rides the live camera rather than being parented to it: the camera is a single shared
		// node that moves between rigs, so the hand follows whichever view is driving it. Read in
		// _Process, after the look rig has already pushed this frame's transform through its
		// RemoteTransform3D, so the hand does not swim a frame behind the view.
		var eye = Game.Camera.GlobalTransform;
		HandRig.GlobalTransform = eye.TranslatedLocal(HandOffset);

		if (MessageLabel != null && MessageLabel.Visible)
		{
			MessageLabel.GlobalTransform = eye.TranslatedLocal(MessageOffset);

			_messageSeconds -= (float)delta;
			if (_messageSeconds <= 0.0f)
				MessageLabel.Hide();
		}
	}

	// ---------------------------------------------------------------- aiming from the crosshair

	/// <summary>
	/// Where the crosshair meets the cloth, in table-local coordinates.
	///
	/// A ray against the table PLANE rather than a physics cast: the tiles are not colliders and
	/// the table's own collider is a fat cylinder, so a plane through the tile surface is both
	/// cheaper and exactly the surface the chain is laid out on. Same technique the pool game uses
	/// for ball-in-hand, aimed at the screen centre instead of the cursor.
	/// </summary>
	public bool TryAimPoint(out Vector2 tableLocal)
	{
		tableLocal = Vector2.Zero;

		var camera = Game?.Camera;
		var cloth = Game?.ChainPresenter;
		if (camera == null || cloth == null || !IsInsideTree())
			return false;

		var centre = GetViewport().GetVisibleRect().Size * 0.5f;
		var plane = new Plane(cloth.GlobalBasis.Y.Normalized(), cloth.GlobalPosition);

		var hit = plane.IntersectsRay(camera.ProjectRayOrigin(centre), camera.ProjectRayNormal(centre));
		if (hit == null)
			return false;

		var local = cloth.ToLocal(hit.Value);
		tableLocal = new Vector2(local.X, local.Z);
		return true;
	}

	/// <summary>Points the placement at whichever open end the crosshair is nearest.</summary>
	public void UpdateAimFromCrosshair()
	{
		if (Game?.ChainPresenter == null || !DominoTileId.IsValid(SelectedTileId))
			return;

		if (TryAimPoint(out var aim))
			AimedEnd = DominoAim.NearestEnd(Game.Plays, Game.ChainPresenter.Spec, SelectedTileId, aim);

		UpdateGhost();
	}

	/// <summary>Points the draw at whichever face-down tile the crosshair is nearest.</summary>
	public void UpdateStockAimFromCrosshair()
	{
		if (Game == null || Game.BoneyardSlots.Length == 0)
		{
			AimedSlot = DominoTileId.NoEnd;
			return;
		}

		if (!TryAimPoint(out var aim))
			return;

		// The layout comes off the game, which is also what the seat presenter draws the stock
		// from — one source, so the aim can never be pointing at where the tiles are not.
		AimedSlot = DominoBoneyardLayout.NearestSlot(Game.BoneyardSlots, Game.StockSpec, aim);
	}

	public void SetCrosshairVisible(bool visible)
	{
		if (Crosshair == null)
			return;

		Crosshair.Visible = visible;
	}

	public override void ShowNotice(string text, float seconds = 2.5f)
	{
		if (MessageLabel == null || !IsMultiplayerAuthority())
			return;

		MessageLabel.Text = text;
		MessageLabel.Show();
		_messageSeconds = seconds;
	}

	public void HideMessage()
	{
		MessageLabel?.Hide();
		_messageSeconds = 0.0f;
	}

	// ---------------------------------------------------------------- state fed in by the controller

	public override void Refresh(
		int[] hand,
		IReadOnlyList<MoveOption> playableMoves,
		bool isYourTurn,
		bool canDraw,
		bool mustPass)
	{
		if (!IsMultiplayerAuthority())
			return;

		_hand = hand ?? System.Array.Empty<int>();
		_moves = playableMoves ?? new List<MoveOption>();
		IsYourTurn = isYourTurn;
		CanDraw = canDraw;
		MustPass = mustPass;

		AimedSlot = Game != null && Game.BoneyardSlots.Length > 0
			? Game.BoneyardSlots[0]
			: DominoTileId.NoEnd;

		RebuildFan();

		// The hand changed under whatever the player had picked, so re-anchor the selection rather
		// than leaving it pointing at a tile that moved or was played.
		if (!IsPlayable(SelectedTileId))
			SelectFirstPlayable();
		else
			ApplyFanHighlight();
	}

	// ---------------------------------------------------------------- selection

	public void SelectFirstPlayable()
	{
		SelectedIndex = -1;

		for (var i = 0; i < _hand.Length; i++)
		{
			if (!IsPlayable(_hand[i]))
				continue;

			SelectedIndex = i;
			break;
		}

		ResetLeadingPips();
		ApplyFanHighlight();
	}

	/// <summary>Steps to the next playable tile, wrapping. Unplayable tiles are skipped entirely.</summary>
	public void SelectStep(int direction)
	{
		if (_hand.Length == 0 || _moves.Count == 0)
			return;

		var start = SelectedIndex < 0 ? 0 : SelectedIndex;
		for (var step = 1; step <= _hand.Length; step++)
		{
			var index = ((start + direction * step) % _hand.Length + _hand.Length) % _hand.Length;
			if (!IsPlayable(_hand[index]))
				continue;

			SelectedIndex = index;
			break;
		}

		ResetLeadingPips();
		ApplyFanHighlight();
	}

	public bool IsPlayable(int tileId)
	{
		if (!DominoTileId.IsValid(tileId))
			return false;

		foreach (var move in _moves)
		{
			if (move.TileId == tileId)
				return true;
		}

		return false;
	}

	// ---------------------------------------------------------------- aiming and rotation

	/// <summary>
	/// Picks the end to aim at. Phase 3 takes the first end the selected tile can legally take;
	/// the crosshair replaces this with whatever the player is pointing at.
	/// </summary>
	public void AimAtDefaultEnd()
	{
		foreach (var move in _moves)
		{
			if (move.TileId != SelectedTileId)
				continue;

			AimedEnd = move.End;
			break;
		}

		ResetLeadingPips();
	}

	public void AimAtEnd(ChainEnd end) => AimedEnd = end;

	/// <summary>
	/// Turns the tile over. The player has to present the right half to the chain — that is the
	/// mechanic — so this simply swaps which half leads and lets the ghost report the result.
	/// </summary>
	public void RotateSelected()
	{
		var tileId = SelectedTileId;
		if (!DominoTileId.IsValid(tileId))
			return;

		LeadingPips = LeadingPips == DominoTileId.Low(tileId)
			? DominoTileId.High(tileId)
			: DominoTileId.Low(tileId);

		UpdateGhost();
	}

	/// <summary>
	/// Starts the tile on its low half, deliberately without correcting it for the aimed end: the
	/// player is meant to turn it themselves, and the ghost tells them when it is right.
	/// </summary>
	private void ResetLeadingPips()
	{
		var tileId = SelectedTileId;
		LeadingPips = DominoTileId.IsValid(tileId) ? DominoTileId.Low(tileId) : DominoTileId.NoEnd;
	}

	public bool CanPlaceNow() =>
		Game != null
		&& DominoRules.CanPlaceOriented(SelectedTileId, LeadingPips, AimedEnd, Game.LeftEnd, Game.RightEnd);

	// ---------------------------------------------------------------- intents

	public void RequestPlaySelected()
	{
		if (!CanPlaceNow())
			return;

		EmitSignal(SignalName.TilePlayRequested, SelectedTileId, (int)AimedEnd);
	}

	public void RequestDrawAimed()
	{
		if (AimedSlot == DominoTileId.NoEnd)
			return;

		EmitSignal(SignalName.DrawRequested, AimedSlot);
	}

	public void RequestPassTurn() => EmitSignal(SignalName.PassRequested);

	public void RequestLeaveTable() => EmitSignal(SignalName.SurrenderRequested);

	// ---------------------------------------------------------------- presentation

	public void SetHandVisible(bool visible)
	{
		if (HandRig != null)
			HandRig.Visible = visible;
	}

	/// <summary>Plays a clip if a real rig is wired up; silently does nothing while it is not.</summary>
	public void PlayClip(string clipName)
	{
		if (AnimationPlayer == null || string.IsNullOrEmpty(clipName))
			return;

		if (AnimationPlayer.HasAnimation(clipName))
			AnimationPlayer.Play(clipName);
	}

	private void RebuildFan()
	{
		if (TileSlots == null || TileScene == null)
			return;

		while (_fan.Count > _hand.Length)
		{
			var last = _fan[^1];
			_fan.RemoveAt(_fan.Count - 1);
			last.QueueFree();
		}

		var spec = Game?.ChainPresenter?.Spec ?? LayoutSpec.Default;

		for (var i = 0; i < _hand.Length; i++)
		{
			if (i == _fan.Count)
			{
				var tile = TileScene.Instantiate<DominoTile>();
				TileSlots.AddChild(tile);
				_fan.Add(tile);
			}

			var node = _fan[i];
			if (node.TileId != _hand[i])
				node.Configure(_hand[i], spec);

			node.Transform = FanTransform(i, _hand.Length);
		}
	}

	/// <summary>
	/// A tile lies face-up with its long axis on +Z. Held in a hand it stands upright with the face
	/// toward the player, so the long axis goes up and the face swings back — written as explicit
	/// axis images because composing this out of Euler angles is how it ended up edge-on.
	/// </summary>
	private static readonly Basis Upright = new(
		new Vector3(-1.0f, 0.0f, 0.0f),
		new Vector3(0.0f, 0.0f, 1.0f),
		new Vector3(0.0f, 1.0f, 0.0f));

	/// <summary>
	/// Spreads the tiles along an arc in front of the player, standing them up and leaning them
	/// back so the faces read from the seat camera.
	/// </summary>
	private Transform3D FanTransform(int index, int count)
	{
		// Widened for a big hand: a player who has drawn a lot ends up with ten tiles, and at a
		// fixed spread they crowd into an unreadable stripe.
		var spread = Mathf.DegToRad(FanSpreadDeg) * Mathf.Clamp(count / 7.0f, 0.7f, 1.5f);
		var t = count <= 1 ? 0.0f : index / (float)(count - 1) - 0.5f;
		var angle = t * spread;

		var position = new Vector3(
			Mathf.Sin(angle) * FanRadius,
			index == SelectedIndex ? SelectedLift : 0.0f,
			FanRadius - Mathf.Cos(angle) * FanRadius);

		var lean = Basis.FromEuler(new Vector3(Mathf.DegToRad(TileTiltDeg), 0.0f, 0.0f));
		var swing = Basis.FromEuler(new Vector3(0.0f, angle, 0.0f));

		return new Transform3D(swing * lean * Upright, position);
	}

	private void ApplyFanHighlight()
	{
		for (var i = 0; i < _fan.Count && i < _hand.Length; i++)
		{
			var playable = IsPlayable(_hand[i]);
			_fan[i].SetDimmed(!playable, UnplayableAlpha);
			_fan[i].Transform = FanTransform(i, _hand.Length);
		}

		UpdateGhost();
	}

	// ---------------------------------------------------------------- the ghost

	/// <summary>
	/// Shows where the tile would land and whether it would be accepted. Green when the aimed end
	/// takes the half the player is presenting, red when it does not — which is the only feedback
	/// telling them to turn the tile round.
	/// </summary>
	public void UpdateGhost()
	{
		if (Game?.ChainPresenter == null || !DominoTileId.IsValid(SelectedTileId))
		{
			HideGhost();
			return;
		}

		var spec = Game.ChainPresenter.Spec;
		if (!DominoAim.TryPreviewPlacement(Game.Plays, spec, SelectedTileId, AimedEnd, out var placement))
		{
			HideGhost();
			return;
		}

		EnsureGhost(spec, placement.TileId);

		// A tile held the wrong way round is drawn the wrong way round, so the mistake is visible
		// and not just colour-coded.
		var yaw = LeadingPips == placement.Incoming || placement.IsDouble
			? placement.Yaw
			: placement.Yaw + Mathf.Pi;

		var local = new Transform3D(
			Basis.FromEuler(new Vector3(0.0f, yaw, 0.0f)),
			new Vector3(placement.Center.X, spec.TileThickness * 0.5f, placement.Center.Y));

		GhostSlot.GlobalTransform = Game.ChainPresenter.GlobalTransform * local;
		GhostSlot.Visible = true;

		_ghostMaterial.AlbedoColor = CanPlaceNow()
			? new Color(0.4f, 1.0f, 0.5f, 0.45f)
			: new Color(1.0f, 0.35f, 0.3f, 0.45f);
	}

	public void HideGhost()
	{
		if (GhostSlot != null)
			GhostSlot.Visible = false;
	}

	private void EnsureGhost(LayoutSpec spec, int tileId)
	{
		if (GhostSlot == null || TileScene == null)
			return;

		if (_ghost == null)
		{
			_ghost = TileScene.Instantiate<DominoTile>();
			GhostSlot.AddChild(_ghost);
		}

		if (_ghost.TileId != tileId)
			_ghost.Configure(tileId, spec);

		// Set after Configure, which clears the override so the art pack's own atlas shows.
		if (_ghost.Body != null)
			_ghost.Body.MaterialOverride = _ghostMaterial;
	}

	// ---------------------------------------------------------------- abstraction surface

	public override void SetInteractive(bool interactive)
	{
		if (interactive)
			return;

		SetHandVisible(false);
		SetCrosshairVisible(false);
		HideGhost();
		HideMessage();
	}

	public override void SetTopViewActive(bool active)
	{
		// From above, a hand pinned to the camera would fill the shot with tiles. Aiming still
		// works — looking down at the board is a good way to choose an end.
		SetHandVisible(!active);
	}

	public override void ShowRejection(string reason)
	{
		if (!IsMultiplayerAuthority())
			return;

		ShowNotice(Describe(reason));
	}

	private static string Describe(string reason) => reason switch
	{
		"not_your_turn" => "Não é a sua vez",
		"stale_turn" => "A vez já mudou",
		"tile_not_in_hand" => "Você não tem essa peça",
		"tile_does_not_match" => "A peça não encaixa aí",
		"has_legal_move" => "Você ainda tem jogada",
		"boneyard_empty" => "O monte acabou",
		"invalid_slot" => "Não há peça nesse lugar",
		"must_draw" => "Compre antes de passar",
		"match_not_running" => "A partida não está em andamento",
		_ => $"Jogada recusada ({reason})",
	};

	public override void Clear()
	{
		foreach (var tile in _fan)
			tile.QueueFree();

		_fan.Clear();
		_hand = System.Array.Empty<int>();
		SelectedIndex = -1;
		HideGhost();
		SetHandVisible(false);
	}
}
