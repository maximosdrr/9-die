using Godot;
using Godot.Collections;
using Pool.Simulation;

/// <summary>
/// Loads the scenes touched by the physics rewrite and drives a real shot through
/// PoolSimulationRunner against real Ball nodes. The pure-simulation self-test proves the
/// physics; this proves the wiring — that the scenes still parse after the RigidBody3D removal,
/// that the runner finds its balls, and that the rack no longer spawns overlapping.
/// </summary>
public partial class SceneLoadTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste de integração de cena ===");

        var ballScene = TestSceneLoads("res://Features/Games/Pool/Components/Balls/Ball.tscn");
        var tableScene = TestSceneLoads("res://Features/Games/Pool/Components/Tables/Table2.tscn");
        if (tableScene != null)
            TestGeometryComesFromScene(tableScene);
        TestSceneLoads("res://Features/Games/Pool/Pool.tscn");
        TestSceneLoads("res://Features/Games/Pool/GameController/PoolController.tscn");

        if (ballScene != null)
        {
            TestRackSpacing(ballScene);
            TestShotRunsThroughRunner(ballScene);
            TestPottedBallFalls(ballScene);
        }

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de integração falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private PackedScene TestSceneLoads(string path)
    {
        var scene = GD.Load<PackedScene>(path);
        var ok = scene != null && scene.CanInstantiate();
        Check($"carrega {path.GetFile()}", ok);
        return ok ? scene : null;
    }

    // The rack used to be laid out on a 0.032 pitch while the balls are 0.0572 across, so every
    // neighbour started 44% inside its neighbour.
    private void TestRackSpacing(PackedScene ballScene)
    {
        var holder = new Node3D();
        AddChild(holder);

        var respawn = new PoolBallRespawn { BallsHolder = holder };
        AddChild(respawn);
        respawn.StartGame();

        var balls = new Array<Ball>();
        foreach (var child in holder.GetChildren())
        {
            if (child is Ball ball)
                balls.Add(ball);
        }

        Check("rack gera as bolas esperadas", balls.Count >= 10);

        var diameter = (float)(2.0 * BilliardConstants.Radius);
        var worstOverlap = 0.0f;

        for (var i = 0; i < balls.Count; i++)
        {
            for (var j = i + 1; j < balls.Count; j++)
            {
                var gap = balls[i].Position.DistanceTo(balls[j].Position);
                var overlap = diameter - gap;
                if (overlap > worstOverlap)
                    worstOverlap = overlap;
            }
        }

        Check($"nenhuma bola nasce sobreposta (pior caso {worstOverlap * 1000.0f:F2} mm)", worstOverlap <= 0.0f);

        var restingHeight = balls.Count > 0 ? balls[0].Position.Y : -1.0f;
        Check($"bolas nascem apoiadas no pano (y={restingHeight:F5})",
            Mathf.Abs(restingHeight - (float)BilliardConstants.Radius) < 1e-4f);

        respawn.QueueFree();
        holder.QueueFree();
    }

    private void TestShotRunsThroughRunner(PackedScene ballScene)
    {
        var holder = new Node3D();
        AddChild(holder);

        var cueBall = ballScene.Instantiate<Ball>();
        cueBall.Index = 0;
        cueBall.TextureId = 0;
        holder.AddChild(cueBall);
        cueBall.Position = new Vector3(0.0f, (float)BilliardConstants.Radius, -0.5f);

        var target = ballScene.Instantiate<Ball>();
        target.Index = 1;
        target.TextureId = 1;
        holder.AddChild(target);
        target.Position = new Vector3(0.0f, (float)BilliardConstants.Radius, 0.3f);

        var runner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(runner);
        runner.Setup(cueBall, new Array<Ball> { target });

        var contacted = false;
        cueBall.BallContacted += _ => contacted = true;

        var struck = false;
        cueBall.Striked += () => struck = true;

        var fired = runner.ExecuteShot(new ShotInput(0.0, 0.0, 4.0, 0.0, 0.0));

        Check("runner aceita a tacada", fired);
        Check("bola branca emite Striked", struck);
        Check("tacada em andamento é detectada", runner.IsPlaying);

        // A zero-power shot must be refused outright rather than starting a turn that can never
        // finish — the old Ball.Strike emitted its signal before checking the force.
        var idleRunner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(idleRunner);
        idleRunner.Setup(cueBall, new Array<Ball> { target });
        Check("tacada de força zero é recusada",
            !idleRunner.ExecuteShot(new ShotInput(0.0, 0.0, 0.0, 0.0, 0.0)));

        // Drive playback to completion the way _Process would.
        for (var i = 0; i < 4000 && runner.IsPlaying; i++)
            runner._Process(1.0 / 120.0);

        Check("reprodução termina", !runner.IsPlaying);
        Check("bola branca contatou a bola objeto", contacted);
        Check("bola objeto se moveu", Mathf.Abs(target.Position.Z - 0.3f) > 0.01f);
        Check("bolas continuam na altura de repouso",
            Mathf.Abs(cueBall.Position.Y - (float)BilliardConstants.Radius) < 1e-3f);
    }

    // A potted ball has to be seen dropping into the hole. The first version of this hid the ball
    // the instant the simulation captured it, so it blinked out of existence at the pocket mouth.
    private void TestPottedBallFalls(PackedScene ballScene)
    {
        var holder = new Node3D();
        AddChild(holder);

        var cueBall = ballScene.Instantiate<Ball>();
        cueBall.Index = 0;
        cueBall.TextureId = 0;
        holder.AddChild(cueBall);

        var runner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(runner);
        runner.Setup(cueBall, new Array<Ball>());

        // Straight at the far corner pocket from alongside the cushion.
        var startZ = -0.9f;
        cueBall.Position = new Vector3(
            (float)runner.Table.HalfWidth - 0.03f,
            (float)BilliardConstants.Radius,
            startZ);

        var pocketed = false;
        runner.BallPocketed += _ => pocketed = true;
        runner.ExecuteShot(new ShotInput(0.0, 0.0, 3.0, 0.0, 0.0));

        for (var i = 0; i < 4000 && runner.IsPlaying; i++)
            runner._Process(1.0 / 120.0);

        if (!pocketed)
        {
            Check("bola foi encaçapada (pré-requisito do teste de queda)", false);
            return;
        }

        Check("bola encaçapada sai de jogo", !cueBall.InPlay);
        Check("bola encaçapada continua visível no instante da captura", cueBall.Visible);

        var heightAtCapture = cueBall.Position.Y;
        var lowest = heightAtCapture;

        for (var i = 0; i < 600 && cueBall.Visible; i++)
        {
            runner._Process(1.0 / 120.0);
            lowest = Mathf.Min(lowest, cueBall.Position.Y);
        }

        var fell = heightAtCapture - lowest;
        Check($"bola cai dentro da caçapa (desceu {fell * 100.0f:F1} cm)", fell > 0.25f);
        Check("bola some só depois de cair", !cueBall.Visible);

        var pocket = runner.Table.Pockets[0];
        var settledOverPocket = new Vector2(cueBall.Position.X, cueBall.Position.Z)
            .DistanceTo(new Vector2((float)pocket.Center.X, (float)pocket.Center.Z));
        Check($"queda converge para o centro de alguma caçapa", settledOverPocket < 0.02f || CloseToAnyPocket(runner, cueBall));
    }

    private static bool CloseToAnyPocket(PoolSimulationRunner runner, Ball ball)
    {
        var here = new Vector2(ball.Position.X, ball.Position.Z);
        foreach (var pocket in runner.Table.Pockets)
        {
            var centre = new Vector2((float)pocket.Center.X, (float)pocket.Center.Z);
            if (here.DistanceTo(centre) < 0.02f)
                return true;
        }

        return false;
    }

    // The physics geometry has to track what the artist placed in the table scene, otherwise
    // dragging a pocket in the viewport silently does nothing. This also pins the asymmetry:
    // Table2's pockets differ by ~2 cm in position and have different radii, which the old
    // symmetric two-number spec could not represent.
    private void TestGeometryComesFromScene(PackedScene tableScene)
    {
        var table = tableScene.Instantiate<PoolGameTable>();
        AddChild(table);

        Check("mesa expõe seu nó de geometria", table.Geometry != null);
        if (table.Geometry == null)
            return;

        var spec = table.Geometry.BuildSpec();

        Check($"pano lido do marcador ({spec.HalfWidth * 2.0:F4} x {spec.HalfLength * 2.0:F4} m)",
            spec.HalfWidth > 0.4 && spec.HalfWidth < 0.6 && spec.HalfLength > 0.9 && spec.HalfLength < 1.2);

        Check($"seis caçapas lidas dos marcadores (achou {spec.Pockets.Count})", spec.Pockets.Count == 6);
        Check($"seis tabelas lidas dos marcadores (achou {spec.Cushions.Count})", spec.Cushions.Count == 6);

        // Pocket radii come straight from each marker, so the two side pockets being smaller than
        // the four corners has to survive the read.
        var distinctRadii = new System.Collections.Generic.HashSet<int>();
        foreach (var pocket in spec.Pockets)
            distinctRadii.Add(Mathf.RoundToInt((float)pocket.Radius * 10000.0f));

        Check($"caçapas mantêm raios individuais ({distinctRadii.Count} raios distintos)",
            distinctRadii.Count > 1);

        var allInward = true;
        foreach (var cushion in spec.Cushions)
        {
            var midpoint = (cushion.Start + cushion.End) * 0.5;
            if (midpoint.Dot(cushion.Normal) > 0.0)
                allInward = false;
        }

        Check("normais das tabelas apontam para dentro da mesa", allInward);

        // The cushion line must land on the rail's INNER face, not its centre — that is what makes
        // rail thickness the thing you adjust to move where balls bounce.
        var narrowest = double.MaxValue;
        foreach (var cushion in spec.Cushions)
        {
            var midpoint = (cushion.Start + cushion.End) * 0.5;
            narrowest = Mathf.Min(narrowest, Mathf.Abs(midpoint.Dot(cushion.Normal)));
        }

        Check($"linha de quique fica na face interna do rail (mais próxima: {narrowest:F4} m)",
            narrowest > 0.4 && narrowest < 0.55);

        TestEditsPropagate(table);

        table.QueueFree();
    }

    // The point of reading geometry from the scene is that dragging things in the viewport
    // changes the physics. This moves and resizes the real nodes and checks the spec follows —
    // the first cut read the bed's SIZE but not its POSITION, so moving the cloth did nothing.
    private void TestEditsPropagate(PoolGameTable table)
    {
        var geometry = table.Geometry;

        // Cloth: resizing the marker resizes the playing surface, moving it moves it.
        var cloth = geometry.ClothMarker;
        var clothBox = cloth.Shape as BoxShape3D;
        if (clothBox == null)
        {
            Check("marcador de pano é uma BoxShape3D (pré-requisito)", false);
            return;
        }

        var clothSize = clothBox.Size;
        var clothPos = cloth.Position;

        clothBox.Size = new Vector3(clothSize.X * 0.5f, clothSize.Y, clothSize.Z);
        Check($"redimensionar o pano muda a área de jogo ({geometry.BuildSpec().HalfWidth * 2.0:F4} m)",
            Mathf.Abs((float)geometry.BuildSpec().HalfWidth - clothSize.X * 0.25f) < 1e-3f);
        clothBox.Size = clothSize;

        cloth.Position = clothPos + new Vector3(0.2f, 0.0f, 0.0f);
        Check($"mover o pano move a área de jogo (x={geometry.BuildSpec().PlayCentre.X:F4})",
            Mathf.Abs((float)geometry.BuildSpec().PlayCentre.X - (clothPos.X + 0.2f)) < 1e-3f);
        cloth.Position = clothPos;

        // Rail: making the rail thicker must push the bounce line inward by half the growth,
        // because the line sits on the inner face.
        var rail = geometry.RailMarkers.GetChild<CollisionShape3D>(0);
        var railBox = rail.Shape as BoxShape3D;
        var railSize = railBox.Size;

        var lineBefore = RailLineDistance(geometry, rail);
        railBox.Size = railSize + new Vector3(0.0f, 0.0f, 0.1f);
        var lineAfter = RailLineDistance(geometry, rail);

        Check($"engrossar o rail move a linha de quique (Δ={(lineBefore - lineAfter) * 100.0:F1} cm)",
            Mathf.Abs((float)(lineBefore - lineAfter) - 0.05f) < 1e-3f);
        railBox.Size = railSize;

        // Rail: moving it moves the bounce line the same amount.
        var railPos = rail.Position;
        rail.Position = railPos - new Vector3(0.0f, 0.0f, 0.08f);
        var movedLine = RailLineDistance(geometry, rail);
        Check($"mover o rail move a linha de quique (Δ={(lineBefore - movedLine) * 100.0:F1} cm)",
            Mathf.Abs((float)(lineBefore - movedLine) - 0.08f) < 1e-3f);
        rail.Position = railPos;

        // Pocket: moving the marker moves the capture circle.
        var pocket = geometry.PocketMarkers.GetChild<CollisionShape3D>(0);
        var pocketPos = pocket.Position;
        var beforeZ = NearestPocketZ(geometry.BuildSpec(), pocket, geometry);

        pocket.Position = pocketPos + new Vector3(0.0f, 0.0f, -0.1f);
        var afterZ = NearestPocketZ(geometry.BuildSpec(), pocket, geometry);

        Check($"mover uma caçapa move a caçapa da física (Δ={(afterZ - beforeZ) * 100.0:F1} cm)",
            Mathf.Abs((float)(afterZ - beforeZ) + 0.1f) < 1e-3f);
        pocket.Position = pocketPos;
    }

    /// <summary>Perpendicular distance from the table centre to the cushion line of one rail marker.</summary>
    private static double RailLineDistance(PoolTableGeometry geometry, CollisionShape3D rail)
    {
        var spec = geometry.BuildSpec();
        var railHere = new Vector2(rail.Position.X, rail.Position.Z);

        var best = 0.0;
        var bestDistance = double.MaxValue;

        foreach (var cushion in spec.Cushions)
        {
            var midpoint = (cushion.Start + cushion.End) * 0.5;
            var here = new Vector2((float)midpoint.X, (float)midpoint.Z);
            var distance = here.DistanceTo(railHere);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = Mathf.Abs(midpoint.Dot(cushion.Normal));
        }

        return best;
    }

    private static double NearestPocketZ(TableSpec spec, CollisionShape3D shape, PoolTableGeometry geometry)
    {
        var local = geometry.ToLocal(shape.GlobalPosition);
        var bestZ = 0.0;
        var bestDistance = double.MaxValue;

        foreach (var pocket in spec.Pockets)
        {
            var dx = pocket.Center.X - local.X;
            var dz = pocket.Center.Z - local.Z;
            var distance = dx * dx + dz * dz;
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestZ = pocket.Center.Z;
        }

        return bestZ;
    }

    private void Check(string label, bool condition)
    {
        if (condition)
        {
            _passed++;
            GD.Print($"  OK   {label}");
        }
        else
        {
            _failed++;
            GD.Print($"  FALHA {label}");
        }
    }
}
