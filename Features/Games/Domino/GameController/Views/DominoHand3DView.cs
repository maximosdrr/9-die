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
	/// <summary>
	/// Pushed further out than it looks like it should be: the tiles grew 1.25x and the seat FOV
	/// tightened from 55 to 45 degrees, which together magnify the hand about 1.6x.
	/// </summary>
	[Export] public Vector3 HandOffset = new(0.04f, -0.105f, -0.46f);

	/// <summary>Angle between neighbouring tiles. Fixed per tile, so a bigger hand simply fans wider.</summary>
	[Export] public float FanStepDeg = 8.0f;

	/// <summary>Distance from the pivot the tiles hang off, which sets how flat the fan is.</summary>
	[Export] public float FanRadius = 0.42f;

	[Export] public float SelectedLift = 0.028f;

	/// <summary>Backward lean of the held tiles, so the faces angle toward the player's eyes.</summary>
	[Export] public float TileTiltDeg = -22.0f;


	private readonly List<DominoTile> _fan = new();
	private int[] _hand = System.Array.Empty<int>();
	private IReadOnlyList<MoveOption> _moves = new List<MoveOption>();
	private DominoTile _ghost;
	private readonly MeshInstance3D[] _frame = new MeshInstance3D[4];
	private StandardMaterial3D _outlineMaterial;
	private float _messageSeconds;

	[ExportGroup("Ghost")]
	/// <summary>How much of the real face shows through the preview.</summary>
	[Export] public float GhostOpacity = 0.55f;

	/// <summary>Thickness of the bars that box the preview in, in metres.</summary>
	[Export] public float OutlineThickness = 0.005f;

	[Export] public Color ValidColor = new(0.45f, 0.90f, 0.55f, 0.82f);
	[Export] public Color InvalidColor = new(0.93f, 0.42f, 0.38f, 0.82f);

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

	/// <summary>
	/// Whether the hand should be listening at all. Godot delivers unhandled input to children
	/// before parents, so without this the very click that takes the cursor back after Escape
	/// would reach the hand first and lay a tile the player never meant to play.
	/// </summary>
	public static bool InputIsLive => InputFocus.IsCaptured;

	public override void _Ready()
	{
		// A hand only exists for the peer holding it.
		if (!IsMultiplayerAuthority())
		{
			Hide();
			SetProcess(false);
			return;
		}

		// Softened: slightly transparent and lit rather than flat, so the frame sits in the scene
		// instead of glowing on top of it.
		_outlineMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = ValidColor,
			EmissionEnabled = true,
			Emission = ValidColor,
			EmissionEnergyMultiplier = 0.6f,
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

		// Anchored to the TILE, not to its slot in the hand. The player browses their tiles while
		// waiting for their turn, and re-anchoring on every refresh — which is what "reselect
		// whenever the pick is not playable" amounted to — would yank the selection away from them
		// mid-thought. It only moves when the tile they were holding is genuinely gone.
		var previous = SelectedTileId;

		RebuildFan();

		var stillHeld = System.Array.IndexOf(_hand, previous);
		if (stillHeld >= 0)
		{
			SelectedIndex = stillHeld;
			ApplyFanHighlight();
		}
		else
		{
			SelectFirstPlayable();
		}
	}

	// ---------------------------------------------------------------- selection

	/// <summary>
	/// Starts on a tile that can actually go down when there is one, so the common case needs no
	/// hunting — but every tile stays selectable.
	/// </summary>
	public void SelectFirstPlayable()
	{
		SelectedIndex = _hand.Length > 0 ? 0 : -1;

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

	/// <summary>
	/// Steps through the WHOLE hand, wrapping.
	///
	/// Unplayable tiles used to be skipped and faded out, which read as the game taking tiles away
	/// from the player. They are all there and all pickable now; trying one and seeing the preview
	/// turn red is the feedback, and it says something the fade never did — WHY it does not fit.
	/// </summary>
	public void SelectStep(int direction)
	{
		if (_hand.Length == 0)
			return;

		var start = SelectedIndex < 0 ? 0 : SelectedIndex;
		SelectedIndex = ((start + direction) % _hand.Length + _hand.Length) % _hand.Length;

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
	/// Picks the tile up: aims at wherever the crosshair already is and turns the tile to suit.
	///
	/// The end comes from the crosshair rather than from the first legal move, so the preview
	/// appears where the player was already looking instead of jumping somewhere else the instant
	/// they click.
	/// </summary>
	public void BeginAiming()
	{
		if (Game?.ChainPresenter != null && DominoTileId.IsValid(SelectedTileId)
			&& TryAimPoint(out var aim))
		{
			AimedEnd = DominoAim.NearestEnd(Game.Plays, Game.ChainPresenter.Spec, SelectedTileId, aim);
		}

		ResetLeadingPips();
		UpdateGhost();
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
	/// Starts the tile already turned for the end being aimed at.
	///
	/// Starting on a fixed half instead would mean roughly half of all placements begin invalid and
	/// need a turn for no reason the player can see. Turning still matters, and still means what it
	/// should: swinging the crosshair to the OTHER end leaves the tile facing the wrong way, and
	/// putting it right is a deliberate act.
	/// </summary>
	private void ResetLeadingPips()
	{
		var tileId = SelectedTileId;
		if (!DominoTileId.IsValid(tileId))
		{
			LeadingPips = DominoTileId.NoEnd;
			return;
		}

		var required = Game != null
			? DominoAim.RequiredLeadingPips(Game.Plays, AimedEnd)
			: DominoTileId.NoEnd;

		LeadingPips = required != DominoTileId.NoEnd && DominoTileId.Matches(tileId, required)
			? required
			: DominoTileId.Low(tileId);
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
	/// Splays the tiles like a hand of cards: all of them hang off one pivot below the hand at a
	/// fixed radius, evenly spaced by angle, each rolled by its own angle.
	///
	/// The first version placed them around an arc and swung them about the vertical, which put
	/// each tile at a different distance from the eye — under perspective they came out at mismatched
	/// sizes and angles and looked spilled rather than held. Rolling about a shared pivot keeps every
	/// tile the same distance away, so the overlap is even and the fan reads as one object.
	/// </summary>
	private Transform3D FanTransform(int index, int count)
	{
		var step = Mathf.DegToRad(FanStepDeg);
		var angle = (index - (count - 1) * 0.5f) * step;

		var selected = index == SelectedIndex;

		// Hanging off a pivot below: at angle zero the tile sits at the hand's origin.
		var position = new Vector3(
			Mathf.Sin(angle) * FanRadius,
			Mathf.Cos(angle) * FanRadius - FanRadius + (selected ? SelectedLift : 0.0f),
			// Each tile a hair nearer than the one before, so they always layer the same way
			// instead of fighting over which is in front.
			index * 0.0015f + (selected ? 0.012f : 0.0f));

		var lean = Basis.FromEuler(new Vector3(Mathf.DegToRad(TileTiltDeg), 0.0f, 0.0f));
		var roll = Basis.FromEuler(new Vector3(0.0f, 0.0f, -angle));

		return new Transform3D(lean * roll * Upright, position);
	}

	/// <summary>
	/// Only the lift marks the selection. Fading the rest broke the illusion of holding a real hand
	/// of tiles, and nothing here touches the preview: while the player is still choosing there is
	/// deliberately no preview at all, so where a tile fits is something they find out by trying it.
	/// </summary>
	private void ApplyFanHighlight()
	{
		for (var i = 0; i < _fan.Count && i < _hand.Length; i++)
			_fan[i].Transform = FanTransform(i, _hand.Length);
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

		// The slot, not just the legal placement: an incompatible tile still has to be shown in
		// the spot with a red frame, or picking it would look like the game ignored the input.
		if (!DominoAim.TryPreviewSlot(Game.Plays, spec, SelectedTileId, AimedEnd, out var placement))
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

		var colour = CanPlaceNow() ? ValidColor : InvalidColor;
		_outlineMaterial.AlbedoColor = colour;
		_outlineMaterial.Emission = colour;
	}

	public void HideGhost()
	{
		if (GhostSlot != null)
			GhostSlot.Visible = false;
	}

	/// <summary>
	/// The preview is the real tile — real face, real pips, turned exactly as it would land —
	/// made see-through, wearing a coloured border. Two separate readings for the two questions
	/// the rotation mechanic asks: which half is leading, and whether that is accepted.
	/// </summary>
	private void EnsureGhost(LayoutSpec spec, int tileId)
	{
		if (GhostSlot == null || TileScene == null)
			return;

		if (_ghost == null)
		{
			_ghost = TileScene.Instantiate<DominoTile>();
			GhostSlot.AddChild(_ghost);

			for (var i = 0; i < _frame.Length; i++)
			{
				_frame[i] = new MeshInstance3D
				{
					Mesh = new CapsuleMesh { RadialSegments = 12, Rings = 4 },
					MaterialOverride = _outlineMaterial,
				};

				GhostSlot.AddChild(_frame[i]);
			}
		}

		if (_ghost.TileId != tileId)
		{
			_ghost.Configure(tileId, spec);
			// Faded rather than repainted, so the pips still read through the preview.
			_ghost.SetDimmed(true, GhostOpacity);
		}

		BuildFrame(spec);
	}

	/// <summary>
	/// Four rounded bars boxing the tile in, at the tile's own height.
	///
	/// Capsules rather than boxes: their hemispherical ends meet at the corners and close them off
	/// as rounded joins, so the whole thing reads as a soft rounded rectangle instead of a hard
	/// sharp-cornered crate, which is closer to how the rest of the game looks.
	///
	/// A flat mat under the tile was the first attempt and looked like a coloured sticker on the
	/// cloth — worse at a shallow angle, which is exactly how a seated player sees the table. A
	/// grown-shell outline would be the usual answer and does not work here at all: the tile is
	/// see-through, so the shell's back faces show straight through it and flood the face.
	/// </summary>
	private void BuildFrame(LayoutSpec spec)
	{
		var radius = OutlineThickness * 0.5f;
		var halfWidth = spec.TileWidth * 0.5f + radius;
		var halfLength = spec.TileLength * 0.5f + radius;

		// A capsule's height is its whole length, caps included, so the mid-section is what is
		// left after them. Sized to reach the corners, where the caps overlap and round the join.
		SetBar(0, spec.TileLength + OutlineThickness, radius, Vector3.Right * -halfWidth, true);
		SetBar(1, spec.TileLength + OutlineThickness, radius, Vector3.Right * halfWidth, true);
		SetBar(2, spec.TileWidth + OutlineThickness, radius, Vector3.Back * -halfLength, false);
		SetBar(3, spec.TileWidth + OutlineThickness, radius, Vector3.Back * halfLength, false);
	}

	private void SetBar(int index, float length, float radius, Vector3 position, bool alongLength)
	{
		if (_frame[index] == null)
			return;

		var capsule = (CapsuleMesh)_frame[index].Mesh;
		capsule.Radius = radius;
		capsule.Height = Mathf.Max(length, radius * 2.0f + 0.0001f);

		// Capsules stand on Y by default; these have to lie flat along the tile's own axes.
		_frame[index].Position = position;
		_frame[index].Rotation = alongLength
			? new Vector3(Mathf.Pi * 0.5f, 0.0f, 0.0f)
			: new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f);
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
