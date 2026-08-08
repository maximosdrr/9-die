using Godot;
using Godot.Collections;

/// <summary>
/// What a Player equips to play a table game: the per-player input, aiming and camera surface for
/// one game mode. PlayerGameHandler spawns one of these per participant and hands control to it
/// while the match runs, so the Player body itself stays game-agnostic.
///
/// Each game mode subclasses this (PoolController, DominoController) and the handler only ever
/// talks to this base — adding a mode must not require touching Features/Player.
/// </summary>
[GlobalClass]
public partial class GameController : Node3D
{
	/// <summary>
	/// Whether the player may currently pull control back into the game (their turn, nothing
	/// pending). ControlSwitch reads it; subclasses write it directly from ApplyControl.
	/// </summary>
	public bool CanTakeControl = false;

	/// <summary>
	/// Whether "switch_control" (E) toggles between walking and playing. A mode can turn this off
	/// while it owns the player continuously, or leave it on when returning is safe.
	/// </summary>
	public virtual bool AllowsControlSwitch => true;

	public virtual void Setup(Player parent, TableGame tableGame, GlobalCamera camera) { }

	public virtual void TakeControl() { }

	public virtual void GiveControl() { }

	/// <summary>
	/// Reacts to the turn moving to <paramref name="turnOwnerId"/>. Called on every peer that owns
	/// a controller, so implementations must check authority and identity before touching input,
	/// the camera or the mouse.
	/// </summary>
	public virtual void ApplyControl(string turnOwnerId, Dictionary context) { }
}
