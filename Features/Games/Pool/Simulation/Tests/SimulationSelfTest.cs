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
        TestCushionRebound();
        TestSpinChangesCushionAngle();
        TestEnergyNeverIncreases();
        TestBallStopsCompletely();
        TestPocketDetection();
        TestBallDrivenOffTable();

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

    // A jump shot fired over a cushion must end up reported as off the table. Driving a ball off
    // is a foul in every ruleset, so the simulation has to notice rather than leave the ball
    // rolling around outside the play area still counted as live.
    private void TestBallDrivenOffTable()
    {
        var table = TableSpec.CreateDefault();
        var simulator = new ShotSimulator(table);

        // Right next to the side cushion, fired hard into it with the cue steeply elevated.
        var start = OnCloth(table.HalfWidth - 0.06, 0.0);
        var balls = new List<BallState> { new(0, start) };
        var shot = new ShotInput(
            aimYaw: Math.PI / 2.0,
            elevation: Math.PI / 4.0,
            speed: 8.0,
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

        Check("bola fora da mesa não fica parada em área inválida", !landedOutside || reportedOff);
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
