using Godot;

[GlobalClass]
public partial class StatesRef : RefCounted
{
    // BALL
    public const string BallIdle = "BALL_IDLE";
    public const string BallMoving = "BALL_MOVING";

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

    // PLAYER
    public const string PlayerIdle = "PLAYER_IDLE";
    public const string PlayerWalking = "PLAYER_WALKING";
    public const string PlayerStrike = "PLAYER_STRIKE";
}
