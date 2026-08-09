using Godot;

[GlobalClass]
public partial class StatesRef : RefCounted
{
    // CUE
    public const string CueCharging = "CUE_CHARGING";
    public const string CueIdle = "CUE_IDLE";
    public const string CueSpinning = "CUE_SPINNING";
    public const string CueJumping = "CUE_JUMPING";
    public const string CueLocked = "CUE_LOCKED";
    public const string CueRecover = "CUE_RECOVER";

    // TABLE
    public const string GameStarting = "GAME_STARTING";
    public const string GameFinished = "GAME_FINISHED";
    public const string GameStarted = "GAME_STARTED";
    public const string GameWaitingStart = "GAME_WAITING_START";

    // DOMINO HAND
    public const string DominoHandIdle = "DOMINO_HAND_IDLE";
    public const string DominoHandLooking = "DOMINO_HAND_LOOKING";
    public const string DominoHandAiming = "DOMINO_HAND_AIMING";
    public const string DominoHandDrawing = "DOMINO_HAND_DRAWING";
    public const string DominoHandPlacing = "DOMINO_HAND_PLACING";

    // POKER HAND
    public const string PokerHandIdle = "POKER_HAND_IDLE";
    public const string PokerHandLooking = "POKER_HAND_LOOKING";
    public const string PokerHandActing = "POKER_HAND_ACTING";

    // PLAYER
    public const string PlayerIdle = "PLAYER_IDLE";
    public const string PlayerWalking = "PLAYER_WALKING";
    public const string PlayerStrike = "PLAYER_STRIKE";
}
