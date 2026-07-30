using Godot;
using Godot.Collections;
using System.Threading.Tasks;

[GlobalClass]
public partial class PoolBallRespawn : Node
{
    [Signal]
    public delegate void TableReadyEventHandler(Ball cueBall, Array<Ball> balls);

    private static readonly PackedScene BallScene = GD.Load<PackedScene>("uid://cqwu27wd0ddmr");

    [Export] public Vector3 HeadSpot = new Vector3(0, 0.17f, -0.5f);
    [Export] public Vector3 FootSpot = new Vector3(0, 0.17f, 0.5f);
    [Export] public int BallsQuantity = 9;
    [Export] public Node3D BallsHolder;

    public float BallDiameter = 0.032f;
    public Array<Ball> Balls = new();
    public Ball CueBall = null;

    private bool _readyEmitted = false;

    public override void _Ready()
    {
        if (BallsHolder == null)
            return;
        BallsHolder.ChildEnteredTree += OnHolderChanged;
        BallsHolder.ChildExitingTree += OnHolderChanged;
    }

    public void StartGame()
    {
        ResetReadyState();

        if (Multiplayer.IsServer())
        {
            ClearTable();
            SpawnCueBall();
            SpawnTriangle();
        }

        RefreshAndMaybeEmit();
    }

    public void ClearTable()
    {
        if (BallsHolder == null)
            return;
        foreach (var child in BallsHolder.GetChildren())
            child.QueueFree();
    }

    private void ResetReadyState()
    {
        _readyEmitted = false;
        CueBall = null;
        Balls.Clear();
    }

    private void OnHolderChanged(Node child = null)
    {
        RefreshAndMaybeEmit();
    }

    private void RefreshAndMaybeEmit()
    {
        if (_readyEmitted || BallsHolder == null)
            return;

        Ball foundCue = null;
        var foundBalls = new Array<Ball>();

        foreach (var child in BallsHolder.GetChildren())
        {
            if (child is not Ball b)
                continue;

            if (b.TextureId == 0)
                foundCue = b;
            else
                foundBalls.Add(b);
        }

        if (foundCue == null || foundBalls.Count < BallsQuantity)
            return;

        CueBall = foundCue;
        Balls = foundBalls;

        _readyEmitted = true;
        EmitSignal(SignalName.TableReady, CueBall, Balls);
    }

    public Vector3 GetFootSpotGlobalPosition()
    {
        return BallsHolder.ToGlobal(FootSpot);
    }

    public async Task<(Ball CueBall, Array<Ball> Balls)> WaitTableReady()
    {
        if (_readyEmitted && CueBall != null && Balls.Count >= BallsQuantity)
            return (CueBall, Balls);

        var result = await ToSignal(this, SignalName.TableReady);
        return ((Ball)result[0], (Array<Ball>)result[1]);
    }

    private void SpawnCueBall()
    {
        var ball = (Ball)BallScene.Instantiate();
        ball.Position = HeadSpot;
        ball.TextureId = 0;
        ball.Name = "CueBall";
        BallsHolder.AddChild(ball, true);
    }

    private void SpawnTriangle()
    {
        var index = 0;
        var rows = 5;

        for (var row = 0; row < rows; row++)
        {
            var zOffset = row * (BallDiameter * 0.866f);
            var startX = -(row * BallDiameter) / 2.0f;

            for (var col = 0; col <= row; col++)
            {
                if (index >= BallsQuantity)
                    return;

                var xPos = startX + (col * BallDiameter);
                var pos = new Vector3(xPos, FootSpot.Y, FootSpot.Z + zOffset);

                CreateColoredBall(index, pos);
                index += 1;
            }
        }
    }

    private void CreateColoredBall(int index, Vector3 pos)
    {
        var ball = (Ball)BallScene.Instantiate();
        ball.Position = pos;

        ball.TextureId = index + 1;
        ball.Index = index + 1;

        ball.Name = $"Ball_{index + 1}";

        BallsHolder.AddChild(ball, true);
    }
}
