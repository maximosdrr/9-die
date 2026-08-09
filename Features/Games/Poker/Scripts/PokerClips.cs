/// <summary>
/// The animation clips the poker hand asks for, named once.
///
/// These ARE the contract with the art: when the rigged first-person hand arrives, it needs an
/// AnimationPlayer carrying these four names and nothing else changes — not a state, not a rule,
/// not an RPC. <see cref="PokerHand3DView.PlayClip"/> is a no-op for a name that does not exist, so
/// a partially animated rig degrades a clip at a time instead of erroring.
///
/// Third person is deliberately not here. A seated body plays whatever
/// <see cref="SeatedTableController.SeatedAnimationName"/> asks for, and the character rig currently
/// carries only "Idle" and "Walk" — so every seated player is idle, which is the agreed placeholder
/// until the seated clips exist.
/// </summary>
public static class PokerClips
{
	/// <summary>Holding the cards, doing nothing.</summary>
	public const string Idle = "HandIdle";

	/// <summary>Reaching down and taking the dealt pair off the cloth. Plays once per hand.</summary>
	public const string PickUpCards = "HandPickUpCards";

	/// <summary>Pushing chips in: a bet, a call or a raise.</summary>
	public const string ThrowChips = "HandThrowChips";

	/// <summary>Laying the cards down on the cloth — folding.</summary>
	public const string PlaceCards = "HandPlaceCards";

	/// <summary>Turning the cards face up at a showdown.</summary>
	public const string RevealCards = "HandRevealCards";
}
