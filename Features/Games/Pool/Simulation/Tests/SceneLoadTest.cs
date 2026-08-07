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
        var hudScene = TestSceneLoads("res://Features/Player/Components/UI/PlayerHud.tscn");
        if (hudScene != null)
            TestPushOutControlsLoad(hudScene);

        if (ballScene != null)
        {
            TestRackSpacing(ballScene);
            TestShotRunsThroughRunner(ballScene);
            TestPottedBallFalls(ballScene);
            TestTurnFactsComeFromTimeline(ballScene);
            TestServerValidatesBallInHand(ballScene);
            TestPlacementGhostIsPresentationOnly(ballScene);
        }

        TestDisposedTurnOwnerIsIgnored();

        TestRulerHandlesEmptyRack();
        TestGoldenNineBreakRules();

        if (ballScene != null)
            TestLockstepReproducesShot(ballScene);

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

    private void TestPushOutControlsLoad(PackedScene hudScene)
    {
        var hud = hudScene.Instantiate<PlayerHud>();
        AddChild(hud);
        Check("HUD expõe declaração e escolha de push-out",
            hud.PushOutButton != null && hud.AcceptPushOutButton != null && hud.PassBackButton != null);
        hud.QueueFree();
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

        Ball one = null;
        Ball nine = null;
        foreach (var ball in balls)
        {
            if (ball.Index == 1) one = ball;
            if (ball.Index == 9) nine = ball;
        }

        Check("bola 1 fica no ápice voltado para a cabeça da mesa",
            one != null && Mathf.Abs(one.Position.X - respawn.FootSpot.X) < 1e-5f
                        && one.Position.Z < respawn.FootSpot.Z);
        Check("bola 9 fica no centro do diamante sobre o foot spot",
            nine != null && new Vector2(nine.Position.X, nine.Position.Z)
                .DistanceTo(new Vector2(respawn.FootSpot.X, respawn.FootSpot.Z)) < 1e-5f);

        var runner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(runner);
        runner.Setup(respawn.CueBall, respawn.Balls);
        var cueScene = GD.Load<PackedScene>("res://Features/Games/Pool/Components/Cue/Cue.tscn");
        var configuredCue = cueScene?.Instantiate<Cue>();
        var fullBreakSpeed = configuredCue != null
            ? Cue.PowerToCueSpeed(1.0f, configuredCue.NormalCueSpeed, configuredCue.MaxCueSpeed)
            : 0.0f;
        configuredCue?.Free();
        var breakAccepted = runner.ExecuteShot(new ShotInput(0.0, 0.0, fullBreakSpeed, 0.0, 0.0));
        Check("quebra do rack apertado termina sem timeout",
            breakAccepted && runner.LastShot != null && !runner.LastShot.TimedOut);
        Check("quebra toca primeiro a bola 1",
            runner.LastShot != null && runner.LastShot.FirstBallContacted(0) == 1);

        var breakObjectBallsPocketed = 0;
        if (runner.LastShot != null)
        {
            foreach (var shotEvent in runner.LastShot.Events)
            {
                if (shotEvent.Type == ShotEventType.BallPocketed && shotEvent.BallId != 0)
                    breakObjectBallsPocketed++;
            }
        }

        var breakBallsAtRail = runner.LastShot?.CountDistinctBallsAtCushion(0) ?? 0;
        Check($"quebra máxima pode cumprir a regra (encaçapadas={breakObjectBallsPocketed}, tabelas={breakBallsAtRail})",
            breakObjectBallsPocketed > 0 || breakBallsAtRail >= 4);

        runner.QueueFree();
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

        var started = false;
        runner.ShotStarted += () => started = true;

        var fired = runner.ExecuteShot(new ShotInput(0.0, 0.0, 4.0, 0.0, 0.0));

        Check("runner aceita a tacada", fired);
        Check("runner anuncia o início da tacada", started);
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
        Check($"seis tabelas e doze faces de caçapa lidas dos marcadores (achou {spec.Cushions.Count})",
            spec.Cushions.Count == 18);

        // Pocket radii come straight from each marker, so the two side pockets being smaller than
        // the four corners has to survive the read.
        var distinctRadii = new System.Collections.Generic.HashSet<int>();
        foreach (var pocket in spec.Pockets)
            distinctRadii.Add(Mathf.RoundToInt((float)pocket.Radius * 10000.0f));

        Check($"caçapas mantêm raios individuais ({distinctRadii.Count} raios distintos)",
            distinctRadii.Count > 1);

        // The cloth marker remains a rectangle for easy editing, but each real Table2 pocket
        // must carve its overlapping mouth out of that rectangle. Some asymmetrical markers sit
        // just beyond the cloth edge and need no subtraction; immediately beyond every capture
        // circle the bed must still support a ball.
        var allPocketOverlapsAreCutOut = true;
        var allPocketApproachesAreSupported = true;
        foreach (var pocket in spec.Pockets)
        {
            var nearestBedPoint = new Vec3d(
                System.Math.Clamp(pocket.Center.X,
                    spec.PlayCentre.X - spec.HalfWidth,
                    spec.PlayCentre.X + spec.HalfWidth),
                0.0,
                System.Math.Clamp(pocket.Center.Z,
                    spec.PlayCentre.Z - spec.HalfLength,
                    spec.PlayCentre.Z + spec.HalfLength));
            var towardBed = (nearestBedPoint - pocket.Center).Flat;
            var distanceToBed = towardBed.FlatLength;
            var inward = towardBed.Normalized();

            var overlapsCloth = distanceToBed <= pocket.Radius;
            allPocketOverlapsAreCutOut &= !overlapsCloth
                                           || !spec.HasClothSupport(nearestBedPoint);

            var beforeMouth = pocket.Center
                              + inward * (System.Math.Max(distanceToBed, pocket.Radius) + 0.002);
            allPocketApproachesAreSupported &= spec.HasClothSupport(beforeMouth);
        }

        Check("as seis caçapas reais recortam o pano onde os marcadores se sobrepõem",
            allPocketOverlapsAreCutOut);
        Check("a aproximação das seis caçapas reais continua apoiada no pano",
            allPocketApproachesAreSupported);

        var allInward = true;
        foreach (var cushion in spec.Cushions)
        {
            // Pocket facings can legitimately override the centre-based heuristic through their
            // manual flip. The six long rails, however, must always face the playing surface.
            if ((cushion.End - cushion.Start).FlatLength < 0.4)
                continue;

            var midpoint = (cushion.Start + cushion.End) * 0.5;
            if (midpoint.Dot(cushion.Normal) > 0.0)
                allInward = false;
        }

        Check("normais das tabelas principais apontam para dentro da mesa", allInward);

        // The six main cushion lines must land on each rail's INNER face, not its centre — that is
        // what makes rail thickness the thing you adjust to move where balls bounce. The short
        // pocket facings deliberately sit nearer the corners and side-pocket throats.
        var narrowest = double.MaxValue;
        foreach (var cushion in spec.Cushions)
        {
            if ((cushion.End - cushion.Start).FlatLength < 0.4)
                continue;

            var midpoint = (cushion.Start + cushion.End) * 0.5;
            narrowest = Mathf.Min(narrowest, Mathf.Abs(midpoint.Dot(cushion.Normal)));
        }

        Check($"linha de quique principal fica na face interna do rail (mais próxima: {narrowest:F4} m)",
            narrowest > 0.4 && narrowest < 0.55);

        // Every short facing must overlap a pocket's capture envelope once the ball radius is
        // considered. Otherwise a ball centre can thread between the jaw and the capture circle.
        var facingCount = 0;
        var allFacingsMeetCapture = true;
        foreach (var cushion in spec.Cushions)
        {
            if ((cushion.End - cushion.Start).FlatLength >= 0.4)
                continue;

            facingCount++;
            var nearestEndpointToCapture = double.MaxValue;
            foreach (var pocket in spec.Pockets)
            {
                nearestEndpointToCapture = System.Math.Min(nearestEndpointToCapture,
                    System.Math.Min((cushion.Start - pocket.Center).FlatLength,
                                    (cushion.End - pocket.Center).FlatLength) - pocket.Radius);
            }

            if (nearestEndpointToCapture > BilliardConstants.Radius)
                allFacingsMeetCapture = false;
        }

        Check($"doze faces auxiliares fecham os corredores das caçapas (achou {facingCount})",
            facingCount == 12 && allFacingsMeetCapture);

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

        // Diagonal pocket facings can make automatic side selection ambiguous. Their marker
        // exposes an explicit override that must select the opposite box face and normal.
        var cushionMarker = rail as PoolCushionMarker;
        Check("marcador de tabela expõe o flip manual da normal", cushionMarker != null);
        if (cushionMarker != null)
        {
            var normalBefore = RailNormal(geometry, rail);
            cushionMarker.FlipNormal = true;
            var flippedLine = RailLineDistance(geometry, rail);
            var normalAfter = RailNormal(geometry, rail);

            Check("flip manual troca a face e inverte a normal",
                normalBefore.Dot(normalAfter) < -0.999
                && Mathf.Abs((float)(flippedLine - lineBefore) - railSize.Z) < 1e-3f);

            cushionMarker.FlipNormal = false;
        }

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

    private static Vec3d RailNormal(PoolTableGeometry geometry, CollisionShape3D rail)
    {
        var spec = geometry.BuildSpec();
        var railHere = new Vector2(rail.Position.X, rail.Position.Z);

        var best = Vec3d.Zero;
        var bestDistance = double.MaxValue;

        foreach (var cushion in spec.Cushions)
        {
            var midpoint = (cushion.Start + cushion.End) * 0.5;
            var here = new Vector2((float)midpoint.X, (float)midpoint.Z);
            var distance = here.DistanceTo(railHere);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = cushion.Normal;
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

    // The turn used to be reconstructed from physics callbacks while waiting on "all balls
    // stopped". These check the facts the ruler needs now come out of the event timeline, which
    // is complete and ordered by construction.
    private void TestTurnFactsComeFromTimeline(PackedScene ballScene)
    {
        var holder = new Node3D();
        AddChild(holder);

        var runner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(runner);

        var cueBall = Spawn(ballScene, holder, 0, 0.0f, -0.6f);
        var near = Spawn(ballScene, holder, 1, 0.0f, -0.2f);
        var far = Spawn(ballScene, holder, 2, 0.0f, 0.4f);
        runner.Setup(cueBall, new Array<Ball> { near, far });

        runner.ExecuteShot(new ShotInput(0.0, 0.0, 3.0, 0.0, 0.0));
        var result = runner.LastShot;

        Check("primeiro contato é a bola mais próxima na linha",
            result.FirstBallContacted(0) == 1);
        Check("bola distante não é confundida com o primeiro contato",
            result.FirstBallContacted(0) != 2);

        var events = result.Events;
        var ordered = true;
        for (var i = 1; i < events.Count; i++)
        {
            if (events[i].Time < events[i - 1].Time)
                ordered = false;
        }

        Check($"linha do tempo sai ordenada ({events.Count} eventos)", ordered);

        // A ball already touching the cue ball used to read as "no contact" — the Area3D never
        // fired a fresh body_entered — and the turn was scored as a foul.
        var touchingShot = RunIsolatedShot(ballScene, 0.5f, -0.6f, new ShotInput(0.0, 0.0, 2.0, 0.0, 0.0),
            objectBallOffsetZ: (float)(2.0 * BilliardConstants.Radius));

        if (touchingShot == null)
            Check("bola encostada na branca conta como primeiro contato (tacada recusada)", false);
        else
            Check("bola encostada na branca conta como primeiro contato",
                touchingShot.FirstBallContacted(0) == 1);

        // A firm shot down the table must register the cushion it reaches.
        var railShot = RunIsolatedShot(ballScene, -0.3f, -0.9f, new ShotInput(0.0, 0.0, 3.0, 0.0, 0.0));
        Check("contato com tabela é registrado", railShot != null && railShot.AnyCushionContact());

        // And a shot that touches nothing must still produce a resolvable turn rather than an
        // await that never completes.
        var softShot = RunIsolatedShot(ballScene, 0.2f, 0.0f, new ShotInput(0.0, 0.0, 0.2, 0.0, 0.0));
        Check("tacada fraca produz resultado resolvível", softShot != null);
        Check("tacada sem contato reporta ausência de contato",
            softShot != null && softShot.FirstBallContacted(0) == -1);
    }

    private void TestServerValidatesBallInHand(PackedScene ballScene)
    {
        var holder = new Node3D
        {
            Position = new Vector3(2.0f, 0.8f, -1.0f),
            Rotation = new Vector3(0.0f, 0.35f, 0.0f),
        };
        AddChild(holder);

        var cueBall = Spawn(ballScene, holder, 0, 0.0f, -0.4f);
        var obstacle = Spawn(ballScene, holder, 1, 0.0f, 0.1f);
        var runner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(runner);
        runner.Setup(cueBall, new Array<Ball> { obstacle });

        var legal = runner.TableToGlobalPosition(new Vector2(0.2f, -0.2f));
        Check("servidor aceita ball-in-hand legal em mesa transformada",
            runner.TryValidatePlacement(legal, cueBall, out var validated)
            && validated.DistanceTo(legal) < 1e-5f);

        var outside = runner.TableToGlobalPosition(new Vector2(5.0f, 5.0f));
        Check("servidor rejeita ball-in-hand fora da mesa",
            !runner.TryValidatePlacement(outside, cueBall, out _));

        Check("servidor rejeita ball-in-hand sobre outra bola",
            !runner.TryValidatePlacement(obstacle.GlobalPosition, cueBall, out _));

        var pocket = runner.Table.Pockets[0].Center;
        var overPocket = runner.TableToGlobalPosition(new Vector2((float)pocket.X, (float)pocket.Z));
        Check("servidor rejeita ball-in-hand dentro da caçapa",
            !runner.TryValidatePlacement(overPocket, cueBall, out _));

        Check("servidor rejeita coordenadas não finitas no ball-in-hand",
            !runner.TryValidatePlacement(new Vector3(float.NaN, 0.0f, 0.0f), cueBall, out _));

        var placementManager = new BallPlacementManager { SimulationRunner = runner };
        const float headStringZ = -0.5f;
        var behindHeadString = runner.TableToGlobalPosition(new Vector2(0.2f, -0.7f));
        var beyondHeadString = runner.TableToGlobalPosition(new Vector2(0.2f, -0.3f));
        Check("posicionamento inicial aceita a branca atrás da linha de cabeça",
            placementManager.TryValidatePlacement(behindHeadString, cueBall,
                BallPlacementManager.PlacementRegion.BehindHeadString, headStringZ, out _));
        Check("posicionamento inicial rejeita a branca depois da linha de cabeça",
            !placementManager.TryValidatePlacement(beyondHeadString, cueBall,
                BallPlacementManager.PlacementRegion.BehindHeadString, headStringZ, out _));
        Check("ball-in-hand após falta continua aceitando a mesa inteira",
            placementManager.TryValidatePlacement(beyondHeadString, cueBall,
                BallPlacementManager.PlacementRegion.FullTable, headStringZ, out _));
        placementManager.Free();

        runner.QueueFree();
        holder.QueueFree();
    }

    /// <summary>
    /// Runs one shot on its own runner. The cue ball must carry Index 0 — that is the convention
    /// the whole feature uses to identify it, from the rack spawner through to the scratch rule.
    /// </summary>
    private ShotResult RunIsolatedShot(
        PackedScene ballScene,
        float x,
        float z,
        ShotInput shot,
        float objectBallOffsetZ = 0.0f)
    {
        var holder = new Node3D();
        AddChild(holder);

        var cueBall = Spawn(ballScene, holder, 0, x, z);
        var objectBalls = new Array<Ball>();

        if (objectBallOffsetZ != 0.0f)
            objectBalls.Add(Spawn(ballScene, holder, 1, x, z + objectBallOffsetZ));

        var runner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(runner);
        runner.Setup(cueBall, objectBalls);

        return runner.ExecuteShot(shot) ? runner.LastShot : null;
    }

    // The ruler dereferenced the target ball unguarded; with the rack exhausted that was a crash.
    private void TestRulerHandlesEmptyRack()
    {
        var ruler = new GoldenNineTurnRuler();

        var context = new TurnContext
        {
            BallsScored = new Dictionary<int, Ball>(),
            FirstBallTouched = null,
            BallsOffTable = new Array<Ball>(),
            TargetBall = null,
            CurrentBallsRemaining = new Dictionary<int, Ball>(),
            AnyRailContact = false,
        };

        var threw = false;
        try
        {
            ruler.Rule(context);
        }
        catch (System.Exception)
        {
            threw = true;
        }

        Check("regra não estoura com o rack vazio", !threw);
        ruler.Free();
    }

    private void TestGoldenNineBreakRules()
    {
        var ruler = new GoldenNineTurnRuler();
        var one = new Ball { Index = 1 };
        var nine = new Ball { Index = 9 };

        var illegalBreak = new TurnContext
        {
            BallsScored = new Dictionary<int, Ball>(),
            FirstBallTouched = one,
            BallsOffTable = new Array<Ball>(),
            TargetBall = one,
            CurrentBallsRemaining = new Dictionary<int, Ball>(),
            AnyRailContact = true,
            IsBreakShot = true,
            IsLegalBreak = false,
            ObjectBallsDrivenToRail = 3,
        };
        Check("quebra seca com menos de quatro bolas no rail é falta",
            ruler.Rule(illegalBreak) == TurnRuler.Actions.CallCueBallReplacement);
        Check("modo solo ignora falta na quebra e mantém a vez",
            PoolTurnResolver.AdaptActionForSolo(ruler.Rule(illegalBreak), illegalBreak)
                == TurnRuler.Actions.ExtendTurn);
        Check("quatro faltas ainda não encerram a partida",
            !PoolTurnResolver.IsFatalFoulCount(4));
        Check("a quinta falta encerra a partida competitiva",
            PoolTurnResolver.IsFatalFoulCount(5));

        var nineOnBreak = new TurnContext
        {
            BallsScored = new Dictionary<int, Ball> { [9] = nine },
            FirstBallTouched = one,
            BallsOffTable = new Array<Ball>(),
            TargetBall = one,
            CurrentBallsRemaining = new Dictionary<int, Ball>(),
            AnyRailContact = true,
            IsBreakShot = true,
            IsLegalBreak = true,
            ObjectBallsDrivenToRail = 4,
        };
        Check("bola 9 na quebra legal é recolocada e o turno continua",
            ruler.Rule(nineOnBreak) == TurnRuler.Actions.ExtendTurn);

        nineOnBreak.IsBreakShot = false;
        Check("bola 9 em uma tacada normal encerra a partida",
            ruler.Rule(nineOnBreak) == TurnRuler.Actions.EndGamePlayerWin);
        Check("modo solo preserva a vitória ao encaçapar a bola 9",
            PoolTurnResolver.AdaptActionForSolo(ruler.Rule(nineOnBreak), nineOnBreak)
                == TurnRuler.Actions.EndGamePlayerWin);

        var legalMiss = new TurnContext
        {
            BallsScored = new Dictionary<int, Ball>(),
            FirstBallTouched = one,
            BallsOffTable = new Array<Ball>(),
            TargetBall = one,
            CurrentBallsRemaining = new Dictionary<int, Ball> { [1] = one },
            AnyRailContact = true,
            IsBreakShot = false,
            IsLegalBreak = true,
        };
        Check("modo solo não troca de jogador depois de uma tacada sem pontuar",
            PoolTurnResolver.AdaptActionForSolo(ruler.Rule(legalMiss), legalMiss)
                == TurnRuler.Actions.ExtendTurn);

        var pushOutWithoutContact = new TurnContext
        {
            BallsScored = new Dictionary<int, Ball>(),
            FirstBallTouched = null,
            BallsOffTable = new Array<Ball>(),
            TargetBall = one,
            CurrentBallsRemaining = new Dictionary<int, Ball>(),
            AnyRailContact = false,
            IsBreakShot = false,
            IsLegalBreak = true,
            IsPushOut = true,
        };
        Check("push-out declarado permite tacada sem contato e pede escolha do adversário",
            ruler.Rule(pushOutWithoutContact) == TurnRuler.Actions.CallPushOutChoice);
        Check("modo solo não abre a escolha de push-out",
            PoolTurnResolver.AdaptActionForSolo(
                ruler.Rule(pushOutWithoutContact), pushOutWithoutContact)
                == TurnRuler.Actions.ExtendTurn);

        pushOutWithoutContact.BallsScored[0] = new Ball { Index = 0 };
        Check("scratch durante push-out continua sendo falta",
            ruler.Rule(pushOutWithoutContact) == TurnRuler.Actions.CallCueBallReplacement);
        Check("modo solo repõe a branca encaçapada sem aplicar penalidade",
            PoolTurnResolver.AdaptActionForSolo(
                ruler.Rule(pushOutWithoutContact), pushOutWithoutContact)
                == TurnRuler.Actions.CallCueBallReplacement);
        pushOutWithoutContact.BallsScored[0].Free();

        one.Free();
        nine.Free();
        ruler.Free();
    }

    private void TestPlacementGhostIsPresentationOnly(PackedScene ballScene)
    {
        var holder = new Node3D();
        AddChild(holder);
        var ball = Spawn(ballScene, holder, 0, 0.0f, 0.0f);
        var ghost = ball.CreatePlacementGhost();
        holder.AddChild(ghost);

        Check("bola fantasma não possui identidade de bola de jogo", ghost is not Ball);
        Check("bola fantasma não possui sincronizador próprio",
            ghost.FindChild("MultiplayerSynchronizer", true, false) == null);

        var translucent = false;
        foreach (var child in ghost.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (child is GeometryInstance3D geometry && geometry.Transparency > 0.0f)
            {
                translucent = true;
                break;
            }
        }
        Check("bola fantasma é visualmente translúcida", translucent);

        ball.SetPlacementPreviewActive(true);
        ball.SetInPlay(true); // simulates a late network state update during placement preview
        Check("atualização atrasada não revela a bola real durante a prévia", !ball.Visible);

        ball.SetPlacementPreviewActive(false);
        ball.SetInPlay(true);
        Check("bola real reaparece quando a prévia termina", ball.Visible);

        holder.QueueFree();
    }

    private void TestDisposedTurnOwnerIsIgnored()
    {
        var table = new Table { DebugLabel = new Label3D() };
        var game = new TableGame();
        var player = new Player { Name = "peer" };
        table.CurrentTableGame = game;
        game.TurnOwner = player;
        player.Free();

        var safe = true;
        try
        {
            table._Process(0.0);
        }
        catch (System.ObjectDisposedException)
        {
            safe = false;
        }

        Check("mesa ignora o jogador do turno depois que ele é liberado", safe);
        game.Free();
        table.DebugLabel.Free();
        table.Free();
    }

    // Lockstep's promise: given the same starting layout and the same ShotInput, two independent
    // runners — standing in for two peers — must land every ball in the same place and produce
    // the same event timeline. That is what lets a shot travel as ~100 bytes instead of a
    // per-frame position stream.
    private void TestLockstepReproducesShot(PackedScene ballScene)
    {
        var shot = new ShotInput(0.35, 0.0, 3.2, 0.2, -0.15);

        var host = BuildRack(ballScene, out var hostBalls);
        var peer = BuildRack(ballScene, out var peerBalls);

        // The snapshot the server would broadcast, applied to the peer before it simulates.
        host.CaptureState(out var ids, out var positions);
        peer.ApplyState(ids, positions);

        Check($"snapshot cobre a mesa inteira ({ids.Length} bolas, {positions.Length * 4} bytes)",
            ids.Length == hostBalls.Count);

        host.ExecuteShot(shot);
        peer.ExecuteShot(shot);

        var hostResult = host.LastShot;
        var peerResult = peer.LastShot;

        Check("ambos os peers aceitam a tacada", hostResult != null && peerResult != null);
        if (hostResult == null || peerResult == null)
            return;

        Check($"mesma quantidade de eventos ({hostResult.Events.Count})",
            hostResult.Events.Count == peerResult.Events.Count);

        var sameEvents = hostResult.Events.Count == peerResult.Events.Count;
        for (var i = 0; sameEvents && i < hostResult.Events.Count; i++)
        {
            var a = hostResult.Events[i];
            var b = peerResult.Events[i];
            if (a.Type != b.Type || a.BallId != b.BallId || a.OtherId != b.OtherId)
                sameEvents = false;
        }

        Check("linhas do tempo são idênticas", sameEvents);

        var worstDrift = 0.0;
        for (var i = 0; i < hostResult.FinalStates.Count; i++)
        {
            var offset = hostResult.FinalStates[i].Position - peerResult.FinalStates[i].Position;
            worstDrift = Mathf.Max(worstDrift, offset.Length);
        }

        Check($"posições finais coincidem (desvio {worstDrift * 1000.0:F6} mm)", worstDrift < 1e-9);

        // And a peer that fell behind is put right by the next snapshot, which is what makes
        // exact cross-machine determinism an optimisation rather than a correctness requirement.
        peerBalls[1].Position = new Vector3(0.3f, (float)BilliardConstants.Radius, 0.7f);
        peerBalls[2].SetInPlay(false);

        host.CaptureState(out var freshIds, out var freshPositions);
        peer.ApplyState(freshIds, freshPositions);

        var healed = true;
        for (var i = 0; i < hostBalls.Count; i++)
        {
            if (hostBalls[i].Position.DistanceTo(peerBalls[i].Position) > 1e-6f
                || hostBalls[i].InPlay != peerBalls[i].InPlay)
                healed = false;
        }

        Check("snapshot corrige um peer divergente", healed);
    }

    private PoolSimulationRunner BuildRack(PackedScene ballScene, out Array<Ball> balls)
    {
        var holder = new Node3D();
        AddChild(holder);

        var cueBall = Spawn(ballScene, holder, 0, 0.0f, -0.55f);
        balls = new Array<Ball> { cueBall };

        var objectBalls = new Array<Ball>();
        var layout = new[]
        {
            new Vector2(0.0f, 0.45f),
            new Vector2(-0.03f, 0.51f),
            new Vector2(0.03f, 0.51f),
            new Vector2(-0.06f, 0.57f),
            new Vector2(0.06f, 0.57f),
        };

        for (var i = 0; i < layout.Length; i++)
        {
            var ball = Spawn(ballScene, holder, i + 1, layout[i].X, layout[i].Y);
            objectBalls.Add(ball);
            balls.Add(ball);
        }

        var runner = new PoolSimulationRunner { TableAnchor = holder };
        AddChild(runner);
        runner.Setup(cueBall, objectBalls);

        return runner;
    }

    private static Ball Spawn(PackedScene scene, Node3D holder, int index, float x, float z)
    {
        var ball = scene.Instantiate<Ball>();
        ball.Index = index;
        ball.TextureId = index;
        holder.AddChild(ball);
        ball.Position = new Vector3(x, (float)BilliardConstants.Radius, z);
        return ball;
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
