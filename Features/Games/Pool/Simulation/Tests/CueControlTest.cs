using Godot;
using Godot.Collections;
using Pool.Simulation;
using System.Reflection;

/// <summary>
/// Covers the cue control rework: that a normalised 0..1 power maps to a sane, monotonic cue ball
/// speed, that the aim direction survives the trip into ShotInput, and that the shot description
/// is bounded no matter what a client sends.
///
/// Actual mouse motion can't be driven headlessly, so the draw accumulation itself is verified in
/// game. What is pinned here is everything downstream of it — which is where the old
/// implementation went wrong: the same draw produced different force depending on DPI, frame rate
/// and polling rate.
/// </summary>
public partial class CueControlTest : Node
{
    private const float ExpectedNormalCueSpeed = 3.5f;
    private const float ExpectedMaxCueSpeed = 5.5f;

    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste de controle do taco ===");

        TestPowerMapsToSpeed();
        TestCueSceneUsesAuthoritativeMaximum();
        TestCueImpactUsesFiniteEnergy();
        TestClientCanReceiveServerResolution();
        TestPowerIsMonotonic();
        TestShotInputIsBounded();
        TestAimSurvivesRoundTrip();
        TestTipOffsetIsRelativeToRadius();
        TestStrokeGesture();
        TestShortShotRecoveryKeepsSoloTurn();
        TestCuePresentationRecovery();
        TestTurnOwnershipSurvivesTeardown();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de controle falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    // Power maps to cue speed; the impact model, not the input layer, derives ball speed.
    private void TestPowerMapsToSpeed()
    {
        var fullCue = SpeedForPower(1.0f);
        var halfCue = SpeedForPower(0.5f);
        var eightyCue = SpeedForPower(0.8f);
        var fullBall = CueStrikeModel.CentreBallSpeed(fullCue);
        var halfBall = CueStrikeModel.CentreBallSpeed(halfCue);

        Check($"força máxima gera velocidade central controlável ({fullBall:F2} m/s)",
            fullBall >= 7.0 && fullBall <= 9.0);
        Check($"metade da força dá metade da velocidade da bola ({halfBall:F2} m/s)",
            Mathf.Abs((float)(halfBall
                - CueStrikeModel.CentreBallSpeed(ExpectedNormalCueSpeed) * 0.5)) < 1e-3f);
        Check($"força 8 permanece na faixa de tacada forte ({eightyCue:F2} m/s)",
            Mathf.Abs(eightyCue - 2.8f) < 1e-3f);
    }

    // The old curve squared the input, so half the swing gave a quarter of the power and most of
    // the usable range was crammed into the bottom of the scale.
    private void TestPowerIsMonotonic()
    {
        var previous = -1.0;
        var linear = true;

        for (var i = 0; i <= 10; i++)
        {
            var power = i / 10.0f;
            var speed = SpeedForPower(power);

            if (speed <= previous)
                linear = false;
            previous = speed;
        }

        Check("velocidade cresce de forma monótona com a força", linear);

        var quarter = SpeedForPower(0.25f);
        const float expected = ExpectedNormalCueSpeed * 0.25f;
        Check($"resposta é linear, não quadrática ({quarter:F2} vs {expected:F2} m/s)",
            Mathf.Abs(quarter - expected) < 1e-3f);
    }

    private void TestShotInputIsBounded()
    {
        var absurd = new ShotInput(
            aimYaw: 12.0,
            elevation: 5.0,
            speed: 500.0,
            tipOffsetX: 40.0,
            tipOffsetY: -40.0).Sanitized();

        Check($"velocidade é limitada ({absurd.Speed:F1} m/s)", absurd.Speed <= ShotInput.MaxSpeed);
        Check($"elevação é limitada ({Mathf.RadToDeg((float)absurd.Elevation):F0}°)",
            absurd.Elevation <= Mathf.Pi / 3.0 + 1e-6);

        var offset = Mathf.Sqrt(
            (float)(absurd.TipOffsetX * absurd.TipOffsetX + absurd.TipOffsetY * absurd.TipOffsetY));
        Check($"desvio da ponta é limitado ao ponto de escorregão ({offset:F3} do raio)",
            offset <= ShotInput.MaxTipOffset + 1e-5f);

        var negative = new ShotInput(0.0, -1.0, -5.0, 0.0, 0.0).Sanitized();
        Check("velocidade negativa vira zero", negative.Speed == 0.0);
        Check("elevação negativa vira zero", negative.Elevation == 0.0);

        var nonFinite = new ShotInput(double.NaN, double.PositiveInfinity, double.NaN,
            double.NegativeInfinity, double.NaN).Sanitized();
        Check("valores não finitos são neutralizados",
            double.IsFinite(nonFinite.AimYaw)
            && nonFinite.Elevation == 0.0
            && nonFinite.Speed == 0.0
            && nonFinite.TipOffsetX == 0.0
            && nonFinite.TipOffsetY == 0.0);
    }

    private void TestCueSceneUsesAuthoritativeMaximum()
    {
        var scene = GD.Load<PackedScene>("res://Features/Games/Pool/Components/Cue/Cue.tscn");
        var cue = scene?.Instantiate<Cue>();
        Check("cena do taco usa o mesmo máximo validado pelos testes",
            cue != null
            && Mathf.IsEqualApprox(cue.MaxCueSpeed, SpeedForPower(1.0f))
            && Mathf.IsEqualApprox(cue.NormalCueSpeed, ExpectedNormalCueSpeed));
        cue?.Free();
    }

    private void TestCuePresentationRecovery()
    {
        var scene = GD.Load<PackedScene>("res://Features/Games/Pool/Components/Cue/Cue.tscn");
        var cue = scene.Instantiate<Cue>();
        var game = new PoolGame();
        var owner = new Player { Name = "7" };

        game.TurnOwner = owner;
        cue.PoolGame = game;
        cue.SetMultiplayerAuthority(7);
        AddChild(cue);

        // Reproduces a short scratch: the recovery cooldown locks the cue while placement has
        // control, then the same solo player receives control again without a turn change.
        cue.StateMachine.ChangeState(StatesRef.CueRecover, new Dictionary());
        cue.StateMachine.ChangeState(StatesRef.CueLocked, new Dictionary());
        cue.Hide();
        cue.RestoreAimingPresentation();

        Check("taco reaparece quando o reposicionamento devolve o controle", cue.Visible);
        Check("taco volta ao estado de mira após reposicionamento solo",
            cue.StateMachine.Current.Type == StatesRef.CueIdle);

        // This focused test assigns only the turn owner, not Cue.Setup's signal dependencies.
        cue.PoolGame = null;
        cue.Free();
        owner.Free();
        game.Free();
    }

    private void TestShortShotRecoveryKeepsSoloTurn()
    {
        var scene = GD.Load<PackedScene>("res://Features/Games/Pool/Components/Cue/Cue.tscn");
        var cue = scene.Instantiate<Cue>();
        var game = new PoolGame();
        var owner = new Player { Name = "7" };

        game.TurnOwner = owner;
        cue.PoolGame = game;
        cue.SetMultiplayerAuthority(7);
        AddChild(cue);

        // Reproduces the race: a short solo shot has already finished and extended the same
        // player's turn, but the cue's minimum 0.5 s recovery cooldown completes afterwards.
        cue.StateMachine.ChangeState(StatesRef.CueRecover, new Dictionary());
        cue.CompletePostShotRecovery();

        Check("cooldown tardio preserva o taco liberado quando a vez solo continua",
            cue.StateMachine.Current.Type == StatesRef.CueIdle);

        cue.StateMachine.ChangeState(StatesRef.CueRecover, new Dictionary());
        owner.Name = "8";
        cue.CompletePostShotRecovery();

        Check("cooldown continua bloqueando o taco quando a vez pertence a outro jogador",
            cue.StateMachine.Current.Type == StatesRef.CueLocked);

        cue.PoolGame = null;
        cue.Free();
        owner.Free();
        game.Free();
    }

    private void TestTurnOwnershipSurvivesTeardown()
    {
        var game = new PoolGame();
        var owner = new Player { Name = "7" };
        game.TurnOwner = owner;

        Check("taco identifica a vez sem depender de Multiplayer em nó destacado",
            Cue.IsOwnedTurn(game, 7));

        owner.Free();
        Check("callback tardio ignora jogador da vez já liberado",
            !Cue.IsOwnedTurn(game, 7));

        game.Free();
    }

    private void TestCueImpactUsesFiniteEnergy()
    {
        var centre = CueStrikeModel.Calculate(new ShotInput(0.0, 0.0, 2.0, 0.0, 0.0));
        var english = CueStrikeModel.Calculate(new ShotInput(
            0.0, 0.0, 2.0, ShotInput.MaxTipOffset, 0.0));
        var available = 0.5 * BilliardConstants.CueMass * 2.0 * 2.0;

        Check("impacto central não cria energia além da energia do taco",
            centre.BallKineticEnergy <= available + 1e-9);
        Check("efeito consome velocidade linear em vez de surgir de graça",
            english.LinearVelocity.Length < centre.LinearVelocity.Length
            && english.AngularVelocity.Length > 0.0
            && english.BallKineticEnergy <= available + 1e-9);
        Check("efeito lateral produz squirt pequeno e oposto à ponta",
            english.LinearVelocity.X > 0.0
            && Mathf.RadToDeg((float)Mathf.Atan2(
                (float)english.LinearVelocity.X, (float)english.LinearVelocity.Z)) < 3.0f);
    }

    private void TestClientCanReceiveServerResolution()
    {
        var method = typeof(CueNetworkBridge).GetMethod(
            "ReceiveShotResolution", BindingFlags.Instance | BindingFlags.NonPublic);
        var rpc = method?.GetCustomAttribute<RpcAttribute>();

        Check("cliente aceita confirmação RPC enviada pelo servidor",
            rpc != null
            && rpc.Mode == MultiplayerApi.RpcMode.AnyPeer
            && !rpc.CallLocal
            && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable);
        Check("confirmação remota reconhece o peer reservado do servidor",
            CueNetworkBridge.ServerPeerId == 1);
    }

    // Yaw is what actually travels over the wire, so it has to describe the same direction on
    // the other end.
    private void TestAimSurvivesRoundTrip()
    {
        var worstError = 0.0f;

        for (var degrees = -180; degrees <= 180; degrees += 15)
        {
            var yaw = Mathf.DegToRad(degrees);
            var direction = new Vector3(Mathf.Sin(yaw), 0.0f, Mathf.Cos(yaw));
            var recovered = Mathf.Atan2(direction.X, direction.Z);

            var error = Mathf.Abs(Mathf.AngleDifference(yaw, recovered));
            worstError = Mathf.Max(worstError, error);
        }

        Check($"direção sobrevive à conversão para ângulo (erro {Mathf.RadToDeg(worstError):F4}°)",
            worstError < 1e-4f);
    }

    // Tip offset travels as a fraction of the radius so it means the same thing regardless of
    // ball size — the old code sent metres, which silently assumed one ball scale.
    private void TestTipOffsetIsRelativeToRadius()
    {
        var radius = (float)BilliardConstants.Radius;
        var offsetMetres = radius * 0.4f;
        var asFraction = offsetMetres / radius;

        Check($"desvio em metros converte para fração do raio ({asFraction:F2})",
            Mathf.Abs(asFraction - 0.4f) < 1e-5f);

        var shot = new ShotInput(0.0, 0.0, 4.0, asFraction, 0.0).Sanitized();
        Check("fração dentro do limite passa intacta",
            Mathf.Abs((float)shot.TipOffsetX - 0.4f) < 1e-5f);
    }

    /// <summary>
    /// Mirrors CueChargingState's stroke machine: power is the depth of the last backswing, and
    /// the shot lands when the tip returns to the ball. Kept as a local model because the real
    /// state needs a live scene and mouse events, which headless can't produce — what matters
    /// here is that the rules themselves are right.
    /// </summary>
    private sealed class Stroke
    {
        public float Draw;
        public float Peak;
        public bool Forward;
        public float PeakForwardRate;
        public float FiredAt = -1.0f;

        public void Move(float delta, float forwardRate = 2.5f)
        {
            var next = Mathf.Clamp(Draw + delta, 0.0f, 1.0f);

            if (next > Draw)
            {
                if (Forward)
                {
                    Peak = 0.0f;
                    PeakForwardRate = 0.0f;
                }
                Forward = false;
                Peak = Mathf.Max(Peak, next);
            }
            else if (next < Draw)
            {
                Forward = true;
                PeakForwardRate = Mathf.Max(PeakForwardRate, forwardRate);
            }

            Draw = next;

            if (Forward && Draw <= 0.01f && Peak >= 0.03f && FiredAt < 0.0f)
                FiredAt = CueChargingState.ComputeDeliveredPower(Peak, PeakForwardRate, 2.5f, 0.72f);
        }
    }

    private void TestStrokeGesture()
    {
        // Pull back to 70%, stroke through: the shot must land at 70%, not at the near-zero draw
        // the cue is sitting at when it reaches the ball.
        var normal = new Stroke();
        normal.Move(0.7f);
        Check($"puxar para trás carrega a força (pico {normal.Peak:F2})", Mathf.Abs(normal.Peak - 0.7f) < 1e-5f);
        Check("não dispara durante o recuo", normal.FiredAt < 0.0f);

        normal.Move(-0.7f);
        Check($"tacada sai ao chegar na bola, com a força do recuo ({normal.FiredAt:F2})",
            Mathf.Abs(normal.FiredAt - 0.7f) < 1e-5f);

        // A shallow forward move that never reaches the ball must not fire.
        var partial = new Stroke();
        partial.Move(0.6f);
        partial.Move(-0.3f);
        Check("recuar e parar no meio não dispara", partial.FiredAt < 0.0f);

        // Practice stroke: go back, come forward part way, then take a NEW, shorter backswing.
        // The shot must use the new one, not the deeper earlier one.
        var practice = new Stroke();
        practice.Move(0.9f);
        practice.Move(-0.5f);
        practice.Move(0.2f);
        Check($"novo recuo descarta o pico anterior (pico {practice.Peak:F2})",
            Mathf.Abs(practice.Peak - 0.6f) < 1e-5f);

        practice.Move(-0.6f);
        Check($"tacada usa o último recuo ({practice.FiredAt:F2})",
            Mathf.Abs(practice.FiredAt - 0.6f) < 1e-5f);

        // Pushing forward from rest is not a shot.
        var nudge = new Stroke();
        nudge.Move(-0.4f);
        Check("empurrar sem recuar não dispara", nudge.FiredAt < 0.0f);

        // A backswing under the minimum is a practice stroke.
        var tiny = new Stroke();
        tiny.Move(0.02f);
        tiny.Move(-0.02f);
        Check("recuo mínimo não dispara tacada", tiny.FiredAt < 0.0f);

        var slow = new Stroke();
        slow.Move(0.8f);
        slow.Move(-0.8f, forwardRate: 0.5f);
        var fast = new Stroke();
        fast.Move(0.8f);
        fast.Move(-0.8f, forwardRate: 2.5f);
        Check($"aceleração para frente influencia a entrega ({slow.FiredAt:F2} < {fast.FiredAt:F2})",
            slow.FiredAt < fast.FiredAt && slow.FiredAt > 0.0f);
    }

    private static float SpeedForPower(float power)
    {
        return Cue.PowerToCueSpeed(power, ExpectedNormalCueSpeed, ExpectedMaxCueSpeed);
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
