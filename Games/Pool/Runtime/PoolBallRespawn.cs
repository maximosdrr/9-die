using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PoolBallRespawn : Node
{
    [Signal]
    public delegate void TableReadyEventHandler(Ball cueBall, Array<Ball> balls);

    private static readonly PackedScene BallScene = GD.Load<PackedScene>("uid://cqwu27wd0ddmr");

    // Table-local spots, in the same frame the simulation uses: origin at the centre of the
    // cloth, +Z along the long axis. Y is the resting ball-centre height, so a ball spawns
    // sitting on the cloth rather than being dropped onto it — the old spots were 18 cm above
    // the bed, which meant every rack began as a bounce test.
    [Export] public Vector3 HeadSpot = new Vector3(0, BallCentreHeight, -0.5f);
    [Export] public Vector3 FootSpot = new Vector3(0, BallCentreHeight, 0.5f);
    [Export] public int BallsQuantity = 9;
    [Export] public Node3D BallsHolder;

    private const float BallCentreHeight = 0.028575f;

    // Rack spacing is the ball DIAMETER — the old 0.032 was barely half of it, so adjacent balls
    // were born overlapping by 44% and the solver's penetration recovery blew the rack apart on
    // the first tick. A touch of slack keeps neighbours from counting as already in contact.
    private const float BallDiameter = 2.0f * BallCentreHeight;
    private const float RackPitch = BallDiameter + 0.00001f;
    public Array<Ball> Balls = new();
    public Ball CueBall = null;

    private bool _readyEmitted = false;
    private bool _rebuildingTable;

    public override void _Ready()
    {
        if (BallsHolder == null)
            return;
        BallsHolder.ChildEnteredTree += OnHolderChanged;
        BallsHolder.ChildExitingTree += OnHolderChanged;
    }

    public override void _ExitTree()
    {
        if (!IsInstanceValid(BallsHolder))
            return;

        BallsHolder.ChildEnteredTree -= OnHolderChanged;
        BallsHolder.ChildExitingTree -= OnHolderChanged;
    }

    public void StartGame()
    {
        ResetReadyState();

        if (Multiplayer.IsServer())
        {
            _rebuildingTable = true;
            try
            {
                ClearTable();
                SpawnCueBall();
                SpawnNineBallDiamond();
            }
            finally
            {
                _rebuildingTable = false;
            }
        }

        RefreshAndMaybeEmit();
    }

    public void ClearTable()
    {
        if (BallsHolder == null)
            return;
        foreach (var child in BallsHolder.GetChildren())
        {
            // QueueFree alone leaves the old rack in GetChildren until the end of the frame. A
            // restart in that window used to publish a mixed old/new rack to the simulation.
            BallsHolder.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ResetReadyState()
    {
        _readyEmitted = false;
        CueBall = null;
        Balls.Clear();
    }

    private void OnHolderChanged(Node child = null)
    {
        if (_rebuildingTable)
            return;

        RefreshAndMaybeEmit();
    }

    private void RefreshAndMaybeEmit()
    {
        if (_readyEmitted || BallsHolder == null)
            return;

        Ball foundCue = null;
        var foundBalls = new List<Ball>();

        foreach (var child in BallsHolder.GetChildren())
        {
            if (child is not Ball b)
                continue;

            if (b.TextureId == 0)
                foundCue = b;
            else
                foundBalls.Add(b);
        }

        if (!IsInstanceValid(foundCue) || foundBalls.Count < BallsQuantity)
            return;

        CueBall = foundCue;
        Balls.Clear();
        foreach (var ball in foundBalls)
            Balls.Add(ball);

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

    private void SpawnNineBallDiamond()
    {
        // WPA nine-ball: 1 at the apex toward the head, 9 in the middle on the foot spot.
        var rows = new[] { 1, 2, 3, 2, 1 };
        var rackOrder = new[] { 1, 2, 3, 4, 9, 5, 6, 7, 8 };
        var rowSpacing = RackPitch * Mathf.Sqrt(3.0f) * 0.5f;
        var slot = 0;

        for (var row = 0; row < rows.Length; row++)
        {
            var count = rows[row];
            var z = FootSpot.Z + (row - 2) * rowSpacing;
            var startX = -((count - 1) * RackPitch) * 0.5f;

            for (var col = 0; col < count; col++)
            {
                if (slot >= rackOrder.Length || slot >= BallsQuantity)
                    return;

                var xPos = startX + (col * RackPitch);
                var pos = new Vector3(FootSpot.X + xPos, FootSpot.Y, z);

                CreateColoredBall(rackOrder[slot], pos);
                slot += 1;
            }
        }
    }

    private void CreateColoredBall(int ballNumber, Vector3 pos)
    {
        var ball = (Ball)BallScene.Instantiate();
        ball.Position = pos;

        ball.TextureId = ballNumber;
        ball.Index = ballNumber;

        ball.Name = $"Ball_{ballNumber}";

        BallsHolder.AddChild(ball, true);
    }
}
