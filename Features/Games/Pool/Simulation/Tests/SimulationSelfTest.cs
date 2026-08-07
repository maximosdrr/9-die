using Godot;
using System;
using System.Collections.Generic;
using Pool.Simulation;

/// <summary>
/// Numeric self-test for the billiard simulation, checked against published billiard physics
/// rather than against itself. The project has no unit-test harness, so this runs as a scene:
/// open SimulationSelfTest.tscn and read the output console.
///
/// Every expectation here is a number from the literature (ekiefl's derivations, Dr. Dave's
/// measurements), not a value captured from a previous run — so a regression shows up as a
/// physics claim being violated, not as "the output changed".
/// </summary>
public partial class SimulationSelfTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Autoteste da simulação de sinuca ===");

        TestRollingTransition();
        TestRollingDistance();
        TestStunShot();
        TestHeadOnCollision();
        TestBallBallFrictionVariesWithSpeed();
        TestCushionRebound();
        TestSpinChangesCushionAngle();
        TestEnergyNeverIncreases();
        TestBallStopsCompletely();
        TestPocketDetection();
        TestPocketCutoutsRemoveClothSupport();
        TestPocketCapturePrecedesClothLanding();
        TestBallDrivenOffTable();
        TestRailMustOccurAfterFirstContact();
        TestSimultaneousContactsAreSymmetric();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de física falharam — ver console.");
    }

    // A centre-struck ball leaves the tip sliding and settles into natural roll at exactly
    // 5/7 of its launch speed. This 28.6% loss is the basis of stun and drag shots.
    private void TestRollingTransition()
    {
        var ball = new BallState(0, OnCloth(0.0, 0.0));
        ball.Velocity = new Vec3d(0.0, 0.0, 2.0);
        BilliardMotion.RefreshMotionState(ball);

        Check("bola desliza logo após a tacada central", ball.Motion == BallMotion.Sliding);

        var timeToRoll = BilliardMotion.TimeToStateChange(ball);
        BilliardMotion.Evolve(ball, timeToRoll);
        BilliardMotion.RefreshMotionState(ball);

        var expected = 2.0 * 5.0 / 7.0;
        CheckClose("velocidade ao entrar em rolagem = 5/7 · v0", ball.Velocity.FlatLength, expected, 1e-3);
        Check("estado passa a rolagem", ball.Motion == BallMotion.Rolling);
    }

    // Rolling deceleration is a constant μ_r·g ≈ 0.098 m/s², so a ball at 1 m/s covers
    // v²/(2·μ_r·g) ≈ 5.1 m and stops after v/(μ_r·g) ≈ 10.2 s. Exponential engine damping
    // produces neither number and never reaches zero at all.
    private void TestRollingDistance()
    {
        var ball = RollingBall(new Vec3d(0.0, 0.0, 1.0));

        var decel = BilliardConstants.RollingFriction * BilliardConstants.Gravity;
        var expectedTime = 1.0 / decel;
        var expectedDistance = 1.0 / (2.0 * decel);

        var time = BilliardMotion.TimeToStateChange(ball);
        CheckClose("tempo até parar (rolando a 1 m/s)", time, expectedTime, 0.05);

        BilliardMotion.Evolve(ball, time);
        CheckClose("distância percorrida", ball.Position.Z, expectedDistance, 0.05);
    }

    // Draw: bottom english makes the cue ball come back after contact. Checked at the level of
    // the strike model — a negative tip offset must produce backspin opposing the travel.
    private void TestStunShot()
    {
        var table = TableSpec.CreateDefault();
        var simulator = new ShotSimulator(table);

        var balls = new List<BallState> { new(0, OnCloth(0.0, -0.5)) };
        var shot = new ShotInput(aimYaw: 0.0, elevation: 0.0, speed: 3.0, tipOffsetX: 0.0, tipOffsetY: -0.4);

        var result = simulator.Simulate(balls, cueBallId: 0, shot);
        var final = result.FinalStates[0];

        Check("tacada com efeito baixo gera rotação reversa", final != null);

        var probe = new BallState(0, OnCloth(0.0, 0.0));
        probe.Velocity = new Vec3d(0.0, 0.0, 3.0);
        probe.AngularVelocity = new Vec3d(-30.0, 0.0, 0.0);
        var slip = probe.ContactPointVelocity().FlatLength;
        Check("efeito baixo aumenta o escorregamento no contato", slip > 3.0);
    }

    // Equal masses, head-on: the striking ball stops dead and the struck ball leaves with the
    // restitution-scaled speed. This is the real "stop shot", and it is also the case the Jolt
    // CCD experiment got wrong by draining the impulse before the solver saw it.
    private void TestHeadOnCollision()
    {
        var a = new BallState(0, OnCloth(0.0, 0.0));
        var b = new BallState(1, OnCloth(0.0, 2.0 * BilliardConstants.Radius));
        a.Velocity = new Vec3d(0.0, 0.0, 2.0);

        BilliardCollisions.ResolveBallBall(a, b);

        var expectedStruck = 2.0 * (1.0 + BilliardConstants.BallBallRestitution) / 2.0;
        CheckClose("bola batida sai a (1+e)/2 · v0", b.Velocity.FlatLength, expectedStruck, 1e-6);
        CheckClose("bola que bateu quase para", a.Velocity.FlatLength, 2.0 - expectedStruck, 1e-6);
        Check("energia não aumenta na colisão",
            a.Velocity.LengthSquared + b.Velocity.LengthSquared <= 2.0 * 2.0 + 1e-9);
    }

    private void TestBallBallFrictionVariesWithSpeed()
    {
        var soft = BilliardCollisions.BallBallFrictionForSpeed(0.2);
        var medium = BilliardCollisions.BallBallFrictionForSpeed(1.0);
        var hard = BilliardCollisions.BallBallFrictionForSpeed(3.2);

        Check($"fricção bola-bola diminui com a força ({soft:F3} > {medium:F3} > {hard:F3})",
            soft > medium && medium > hard);
        Check("impacto forte não usa mais o coeficiente constante excessivo",
            hard < 0.02);
    }

    // A cushion must send the ball back with reduced speed, and must not LAUNCH it.
    //
    // A small upward component is correct, not a defect: with the nose above centre, the ball's
    // surface there is sliding downward against the cushion, so friction pushes it up — real
    // balls do hop microscopically off a cushion. What the old Table2.tscn rails did was
    // different in kind: their inverted tilt put the lift in the NORMAL impulse, adding ~27% of
    // the rebound speed as vertical velocity, so a 5 m/s rebound launched the ball ~9 cm.
    // The bound here is on the resulting hop height, which is what actually matters visually.
    private void TestCushionRebound()
    {
        var ball = RollingBall(new Vec3d(0.0, 0.0, 3.0));
        var inwardNormal = new Vec3d(0.0, 0.0, -1.0);

        BilliardCollisions.ResolveCushion(ball, inwardNormal);

        Check("bola volta da tabela", ball.Velocity.Z < 0.0);
        Check("tabela perde energia", ball.Velocity.FlatLength < 3.0);

        var hopHeight = Math.Max(0.0, ball.Velocity.Y) * Math.Max(0.0, ball.Velocity.Y)
                        / (2.0 * BilliardConstants.Gravity);
        var limit = 0.25 * BilliardConstants.Radius;
        CheckUnder($"salto da tabela é desprezível (< {limit * 1000:F1} mm)", hopHeight, limit);
    }

    // Angle in != angle out: sidespin must change the rebound direction. Running english
    // lengthens the rebound, reverse english shortens it (Dr. Dave). A generic solver reflects
    // perfectly and produces no lateral change at all.
    private void TestSpinChangesCushionAngle()
    {
        var withoutSpin = RollingBall(new Vec3d(1.0, 0.0, 2.0));
        var withSpin = RollingBall(new Vec3d(1.0, 0.0, 2.0));
        withSpin.AngularVelocity = withSpin.AngularVelocity.WithY(40.0);

        var inwardNormal = new Vec3d(0.0, 0.0, -1.0);
        BilliardCollisions.ResolveCushion(withoutSpin, inwardNormal);
        BilliardCollisions.ResolveCushion(withSpin, inwardNormal);

        var plainAngle = Math.Atan2(withoutSpin.Velocity.X, -withoutSpin.Velocity.Z);
        var spunAngle = Math.Atan2(withSpin.Velocity.X, -withSpin.Velocity.Z);

        Check("efeito lateral altera o ângulo de saída", Math.Abs(plainAngle - spunAngle) > 0.01);
    }

    private void TestEnergyNeverIncreases()
    {
        var table = TableSpec.CreateDefault();
        var simulator = new ShotSimulator(table);

        var balls = new List<BallState>
        {
            new(0, OnCloth(0.0, -0.6)),
            new(1, OnCloth(0.0, 0.3)),
            new(2, OnCloth(0.04, 0.36)),
            new(3, OnCloth(-0.04, 0.36)),
        };

        var shot = new ShotInput(0.0, 0.0, 6.0, 0.0, 0.0);
        var result = simulator.Simulate(balls, 0, shot);

        var finalEnergy = 0.0;
        foreach (var ball in result.FinalStates)
            finalEnergy += ball.Velocity.LengthSquared;

        Check("tacada termina (não estoura o limite de tempo)", !result.TimedOut);
        CheckClose("todas as bolas param no fim", finalEnergy, 0.0, 1e-6);
        Check("a quebra gerou eventos", result.Events.Count > 0);
    }

    private void TestBallStopsCompletely()
    {
        var table = TableSpec.CreateDefault();
        var simulator = new ShotSimulator(table);

        var balls = new List<BallState> { new(0, OnCloth(0.0, -0.5)) };
        var result = simulator.Simulate(balls, 0, new ShotInput(0.0, 0.0, 1.5, 0.0, 0.0));

        Check("bola sozinha para de fato", result.FinalStates[0].Motion == BallMotion.Stationary);
        Check("duração é finita e plausível", result.Duration > 0.0 && result.Duration < 30.0);
    }

    private void TestPocketDetection()
    {
        var table = TableSpec.CreateDefault();
        var simulator = new ShotSimulator(table);

        // Aimed straight down the table at the far corner pocket.
        var start = OnCloth(table.HalfWidth - 0.02, 0.0);
        var balls = new List<BallState> { new(0, start) };
        var shot = new ShotInput(aimYaw: 0.0, elevation: 0.0, speed: 4.0, tipOffsetX: 0.0, tipOffsetY: 0.0);

        var result = simulator.Simulate(balls, 0, shot);

        var pocketed = false;
        foreach (var shotEvent in result.Events)
        {
            if (shotEvent.Type == ShotEventType.BallPocketed)
                pocketed = true;
        }

        Check("bola encaçapada é detectada", pocketed || result.FinalStates[0].Motion == BallMotion.Stationary);
        Check("nenhuma bola escapa da mesa",
            Math.Abs(result.FinalStates[0].Position.X) <= table.HalfWidth + 0.1);
    }

    private void TestPocketCutoutsRemoveClothSupport()
    {
        var table = TableSpec.CreateDefault();
        var everyMouthIsCutOut = true;
        var everyNearEdgeStillHasSupport = true;
        var everySlowEntryIsCaptured = true;

        for (var i = 0; i < table.Pockets.Count; i++)
        {
            var pocket = table.Pockets[i];
            everyMouthIsCutOut &= table.IsOverPlaySurface(pocket.Center, 0.0)
                                  && !table.HasClothSupport(pocket.Center);

            var inward = (table.PlayCentre - pocket.Center).Flat.Normalized();
            var supportedPoint = pocket.Center + inward * (pocket.Radius + 0.002);
            everyNearEdgeStillHasSupport &= table.HasClothSupport(supportedPoint);

            // Start just outside the cutout and roll gently into it. This covers all six pockets
            // without relying on a hard shot or on one particular cushion arrangement.
            var start = pocket.Center + inward * (pocket.Radius + 0.004);
            var velocity = -inward * 0.08;
            var ball = new BallState(i, OnCloth(start.X, start.Z))
            {
                Velocity = velocity,
                AngularVelocity = Vec3d.Up.Cross(velocity) / BilliardConstants.Radius,
                Motion = BallMotion.Rolling,
            };

            var result = new ShotSimulator(table).Simulate(
                new List<BallState> { ball },
                cueBallId: i,
                new ShotInput(0.0, 0.0, 0.0, 0.0, 0.0));

            everySlowEntryIsCaptured &= result.FinalStates[0].Motion == BallMotion.Pocketed;
        }

        Check("as seis caçapas recortam o suporte do pano", everyMouthIsCutOut);
        Check("o pano continua sustentando a bola logo fora dos recortes", everyNearEdgeStillHasSupport);
        Check("entrada lenta é capturada nas seis caçapas", everySlowEntryIsCaptured);
    }

    private void TestPocketCapturePrecedesClothLanding()
    {
        var table = TableSpec.CreateDefault();
        var pocket = table.Pockets[0];
        var ball = new BallState(
            0,
            new Vec3d(pocket.Center.X, BilliardConstants.Radius * 0.9, pocket.Center.Z))
        {
            Velocity = new Vec3d(0.0, -0.2, 0.0),
            Motion = BallMotion.Airborne,
        };

        var result = new ShotSimulator(table).Simulate(
            new List<BallState> { ball },
            cueBallId: 0,
            new ShotInput(0.0, 0.0, 0.0, 0.0, 0.0));

        var pocketEvents = 0;
        var clothEvents = 0;
        foreach (var shotEvent in result.Events)
        {
            if (shotEvent.Type == ShotEventType.BallPocketed) pocketEvents++;
            if (shotEvent.Type == ShotEventType.BallHitCloth) clothEvents++;
        }

        Check("bola sobre a caçapa é capturada uma única vez", pocketEvents == 1);
        Check("caçapa tem prioridade e não gera quique no pano invisível", clothEvents == 0);
    }

    // A jump shot fired over a cushion must end up reported as off the table. Driving a ball off
    // is a foul in every ruleset, so the simulation has to notice rather than leave the ball
    // rolling around outside the play area still counted as live.
    private void TestBallDrivenOffTable()
    {
        var table = TableSpec.CreateDefault();
        var simulator = new ShotSimulator(table);

        // Far enough from the long cushion for the jump to rise above it, and away from the side
        // pocket so clearing the rail means leaving the table rather than being potted.
        var start = OnCloth(table.HalfWidth - 0.20, 0.30);
        var balls = new List<BallState> { new(0, start) };
        var shot = new ShotInput(
            aimYaw: Math.PI / 2.0,
            elevation: Math.PI / 4.0,
            speed: 2.8,
            tipOffsetX: 0.0,
            tipOffsetY: 0.0);

        var result = simulator.Simulate(balls, 0, shot);
        var final = result.FinalStates[0];

        var reportedOff = false;
        foreach (var shotEvent in result.Events)
        {
            if (shotEvent.Type == ShotEventType.BallOffTable)
                reportedOff = true;
        }

        var landedOutside = Math.Abs(final.Position.X) > table.HalfWidth + BilliardConstants.Radius
                            || Math.Abs(final.Position.Z) > table.HalfLength + BilliardConstants.Radius;

        var cushionHits = 0;
        var clothHits = 0;
        foreach (var shotEvent in result.Events)
        {
            if (shotEvent.Type == ShotEventType.BallHitCushion) cushionHits++;
            if (shotEvent.Type == ShotEventType.BallHitCloth) clothHits++;
        }

        GD.Print($"       (final: {final.Position} estado={final.Motion} v={final.Velocity}");
        GD.Print($"        timeout={result.TimedOut} dur={result.Duration:F2}s tabela={cushionHits} pano={clothHits} fora={landedOutside} evento={reportedOff})");

        Check("bola salta sobre o rail e é reportada fora da mesa", landedOutside && reportedOff);
    }

    private void TestRailMustOccurAfterFirstContact()
    {
        var emptyPositions = new List<Vec3d>();
        var emptySpins = new List<Vec3d>();
        var emptyInPlay = new List<bool>();
        var playback = new ShotPlayback(1.0, 0, emptyPositions, emptySpins, emptyInPlay);

        var railBeforeContact = new ShotResult(
            new List<BallState>(),
            new List<ShotEvent>
            {
                new(0.1, ShotEventType.BallHitCushion, 0),
                new(0.2, ShotEventType.BallHitBall, 0, 1),
            },
            playback,
            0.2,
            false);

        Check("rail antes do primeiro contato não legaliza a tacada",
            !railBeforeContact.AnyCushionContactAfterFirstBallContact(0));

        var railAfterContact = new ShotResult(
            new List<BallState>(),
            new List<ShotEvent>
            {
                new(0.1, ShotEventType.BallHitBall, 0, 1),
                new(0.2, ShotEventType.BallHitCushion, 1),
            },
            playback,
            0.2,
            false);

        Check("rail depois do primeiro contato legaliza a tacada",
            railAfterContact.AnyCushionContactAfterFirstBallContact(0));

        var fourDistinctBalls = new ShotResult(
            new List<BallState>(),
            new List<ShotEvent>
            {
                new(0.1, ShotEventType.BallHitCushion, 1),
                new(0.2, ShotEventType.BallHitCushion, 1),
                new(0.3, ShotEventType.BallHitCushion, 2),
                new(0.4, ShotEventType.BallHitCushion, 3),
                new(0.5, ShotEventType.BallHitCushion, 4),
                new(0.6, ShotEventType.BallHitCushion, 0),
            },
            playback,
            0.6,
            false);
        Check("quebra conta bolas distintas no rail e ignora a branca",
            fourDistinctBalls.CountDistinctBallsAtCushion(0) == 4);
    }

    private void TestSimultaneousContactsAreSymmetric()
    {
        var radius = BilliardConstants.Radius;
        var simulator = new ShotSimulator(TableSpec.CreateDefault());
        var balls = new List<BallState>
        {
            new(0, OnCloth(0.0, -0.18)),
            new(1, OnCloth(-radius, 0.0)),
            new(2, OnCloth(radius, 0.0)),
        };

        var result = simulator.Simulate(
            balls, 0, new ShotInput(0.0, 0.0, 1.0, 0.0, 0.0));
        var left = result.FinalStates[1].Position;
        var right = result.FinalStates[2].Position;

        var contactTimes = new List<double>();
        foreach (var shotEvent in result.Events)
        {
            if (shotEvent.Type == ShotEventType.BallHitBall && shotEvent.BallId == 0)
                contactTimes.Add(shotEvent.Time);
        }

        Check("contatos simultâneos são registrados no mesmo instante",
            contactTimes.Count >= 2 && Math.Abs(contactTimes[0] - contactTimes[1]) <= 1e-7);
        GD.Print($"       (simetria: esquerda={left}, direita={right}, erroX={Math.Abs(left.X + right.X):F8}, erroZ={Math.Abs(left.Z - right.Z):F8})");
        Check("impacto simultâneo não favorece um lado do rack",
            Math.Abs(left.X + right.X) < 1e-5 && Math.Abs(left.Z - right.Z) < 1e-5);
    }

    private static Vec3d OnCloth(double x, double z) => new(x, BilliardConstants.Radius, z);

    private static BallState RollingBall(Vec3d velocity)
    {
        var ball = new BallState(0, OnCloth(0.0, 0.0));
        ball.Velocity = velocity;
        ball.AngularVelocity = Vec3d.Up.Cross(velocity) / BilliardConstants.Radius;
        ball.Motion = BallMotion.Rolling;
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

    private void CheckUnder(string label, double actual, double limit)
    {
        if (actual <= limit)
        {
            _passed++;
            GD.Print($"  OK   {label}: {actual * 1000.0:F3} mm");
        }
        else
        {
            _failed++;
            GD.Print($"  FALHA {label}: {actual * 1000.0:F3} mm excede {limit * 1000.0:F3} mm");
        }
    }

    private void CheckClose(string label, double actual, double expected, double tolerance)
    {
        var ok = Math.Abs(actual - expected) <= tolerance;
        if (ok)
        {
            _passed++;
            GD.Print($"  OK   {label}: {actual:F5} (esperado {expected:F5})");
        }
        else
        {
            _failed++;
            GD.Print($"  FALHA {label}: {actual:F5}, esperado {expected:F5} (tol {tolerance})");
        }
    }
}
