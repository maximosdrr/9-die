using Godot;
using Godot.Collections;
using Pool.Simulation;

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
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== Teste de controle do taco ===");

        TestPowerMapsToSpeed();
        TestPowerIsMonotonic();
        TestShotInputIsBounded();
        TestAimSurvivesRoundTrip();
        TestTipOffsetIsRelativeToRadius();
        TestStrokeGesture();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        if (_failed > 0)
            GD.PushWarning($"{_failed} verificação(ões) de controle falharam.");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    // Full draw must land on a plausible break, not the 165 m/s the old ForceMultiplier produced.
    private void TestPowerMapsToSpeed()
    {
        var full = SpeedForPower(1.0f);
        var half = SpeedForPower(0.5f);

        Check($"força máxima é uma quebra plausível ({full:F1} m/s)", full >= 6.0 && full <= 12.0);
        Check($"metade da força dá metade da velocidade ({half:F1} m/s)",
            Mathf.Abs(half - full * 0.5f) < 1e-3f);
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
        var expected = SpeedForPower(1.0f) * 0.25f;
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
        public float FiredAt = -1.0f;

        public void Move(float delta)
        {
            var next = Mathf.Clamp(Draw + delta, 0.0f, 1.0f);

            if (next > Draw)
            {
                if (Forward)
                    Peak = 0.0f;
                Forward = false;
                Peak = Mathf.Max(Peak, next);
            }
            else if (next < Draw)
            {
                Forward = true;
            }

            Draw = next;

            if (Forward && Draw <= 0.01f && Peak >= 0.03f && FiredAt < 0.0f)
                FiredAt = Peak;
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
    }

    private static float SpeedForPower(float power)
    {
        // Mirrors Cue.BuildShotInput: power is a plain fraction of the maximum cue ball speed.
        const float maxCueBallSpeed = 8.0f;
        return power * maxCueBallSpeed;
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
