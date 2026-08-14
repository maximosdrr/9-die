using System.Linq;
using Godot;

public partial class PlayerCharacterIntegrationTest : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        CallDeferred(MethodName.Run);
    }

    private void Run()
    {
        var playerScene = GD.Load<PackedScene>("res://World/Player/Player.tscn");
        var player = playerScene?.Instantiate<Player>();
        Check("a cena do jogador carrega", player != null);

        if (player != null)
        {
            AddChild(player);
            var visual = player.CharacterVisual;
            Check("o personagem novo substitui o visual antigo", visual != null);
            Check("as partes continuam separadas no esqueleto",
                visual?.Skeleton?.GetChildren().OfType<MeshInstance3D>().Count() == 5);

            var expected = new[]
            {
                CharacterVisual.Clips.Idle,
                CharacterVisual.Clips.Walk,
                CharacterVisual.Clips.Sit,
                CharacterVisual.Clips.IdleSit,
                CharacterVisual.Clips.SitHoldingCards,
                CharacterVisual.Clips.PickCards,
                CharacterVisual.Clips.IdleSitHoldingCards,
                CharacterVisual.Clips.IdleHoldingCardsDown,
            };
            Check("as oito animações de produção estão importadas",
                expected.All(visual.HasAnimation));
            Check("a escala do personagem cabe no cenário",
                Mathf.IsEqualApprox(Mathf.Abs(visual.RigRoot.Scale.X), visual.CharacterScale));
            Check("o marcador de cartas acompanha o osso da mão sem herdar centímetros",
                visual.Skeleton.FindBone("CC_Base_L_Hand") >= 0
                && visual.CardGrip != null
                && Mathf.IsEqualApprox(visual.CardGrip.GlobalBasis.Scale.X, 1.0f));
            Check("o modificador do pescoço está no final do esqueleto",
                visual.NeckModifier != null && visual.NeckModifier.GetParent() == visual.Skeleton);
            Check("o jogador local instancia o corpo sem cabeça para Idle e Walk",
                player.FirstPersonVisual?.HasAnimation(CharacterVisual.Clips.Idle) == true
                && player.FirstPersonVisual.HasAnimation(CharacterVisual.Clips.Walk));
            player.PlayFirstPersonSequence(
                CharacterVisual.Clips.Sit, "", CharacterVisual.Clips.IdleSit, 0.0);
            Check("sem um Sit_FP, a vista sentada entra diretamente no IdleSit_FP",
                player.FirstPersonVisual?.Animator?.CurrentAnimation
                == CharacterVisual.Clips.IdleSit);
            player.PlayFirstPersonAnimation(CharacterVisual.Clips.Idle, 0.0);
            var pickDuration = visual.PlayCardPickupSequence(0.10, 0.0);
            Check("o fluxo 3P começa em PickCards",
                pickDuration > 1.6
                && visual.Animator.CurrentAnimation == CharacterVisual.Clips.PickCards);
            visual._Process(pickDuration + 0.01);
            Check("depois de pegar, o fluxo 3P entra no idle de olhar",
                visual.Animator.CurrentAnimation == CharacterVisual.Clips.IdleSitHoldingCards);
            visual._Process(0.11);
            Check("depois de olhar, o fluxo 3P retorna às cartas abaixadas",
                visual.Animator.CurrentAnimation == CharacterVisual.Clips.IdleHoldingCardsDown);
            Check("Sit entrega uma pose contínua ao idle comum do dominó",
                PosesMatch(visual, CharacterVisual.Clips.Sit, atEnd: true,
                    CharacterVisual.Clips.IdleSit, atSecondEnd: false));

            player.QueueFree();
        }

        var handsScene = GD.Load<PackedScene>(
            "res://Games/Poker/Components/Hands/PlayerFirstPersonHands.tscn");
        var hands = handsScene?.Instantiate<PlayerFirstPersonHands>();
        Check("o poker usa o corpo sem cabeça em primeira pessoa", hands != null);
        if (hands != null)
        {
            AddChild(hands);
            var firstPersonExpected = new[]
            {
                CharacterVisual.Clips.Idle,
                CharacterVisual.Clips.Walk,
                CharacterVisual.Clips.IdleSit,
                CharacterVisual.Clips.PickCards,
                CharacterVisual.Clips.IdleSitHoldingCards,
                CharacterVisual.Clips.IdleHoldingCardsDown,
            };
            Check("o corpo em primeira pessoa possui todos os clipes produzidos",
                hands.Animator != null
                && firstPersonExpected.All(clip => hands.Animator.HasAnimation(clip)));
            Check("o corpo em primeira pessoa preserva quatro partes e não exporta a cabeça",
                hands.ImportedRig.FindChildren("*", "MeshInstance3D", true, false).Count == 4
                && hands.ImportedRig.FindChild("*Head*", true, false) == null);
            Check("a mão em primeira pessoa tem o mesmo marcador normalizado",
                hands.Skeleton?.FindBone("CC_Base_L_Hand") >= 0
                && hands.CardGrip != null
                && Mathf.IsEqualApprox(hands.CardGrip.GlobalBasis.Scale.X, 1.0f));
            Check("os braços em primeira pessoa preservam a profundidade das superfícies",
                hands.ImportedRig.GetChildren().Count > 0
                && hands.ImportedRig.FindChildren("*", "MeshInstance3D", true, false)
                    .OfType<MeshInstance3D>()
                    .All(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount())
                        .All(surface => mesh.GetActiveMaterial(surface) is BaseMaterial3D
                        {
                            NoDepthTest: false,
                        })));
            hands.QueueFree();
        }

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        GetTree().Quit(_failed == 0 ? 0 : 1);
    }

    private static bool PosesMatch(
        CharacterVisual visual,
        string first,
        bool atEnd,
        string second,
        bool atSecondEnd)
    {
        var firstPose = CapturePose(visual, first, atEnd);
        var secondPose = CapturePose(visual, second, atSecondEnd);
        if (firstPose.Length == 0 || firstPose.Length != secondPose.Length)
            return false;

        for (var bone = 0; bone < firstPose.Length; bone++)
        {
            if (firstPose[bone].Origin.DistanceTo(secondPose[bone].Origin) > 0.004f)
                return false;

            var firstRotation = firstPose[bone].Basis.Orthonormalized().GetRotationQuaternion();
            var secondRotation = secondPose[bone].Basis.Orthonormalized().GetRotationQuaternion();
            if (firstRotation.AngleTo(secondRotation) > Mathf.DegToRad(2.0f))
                return false;
        }

        return true;
    }

    private static Transform3D[] CapturePose(
        CharacterVisual visual, string clip, bool atEnd)
    {
        var animation = visual.Animator.GetAnimation(clip);
        if (animation == null)
            return System.Array.Empty<Transform3D>();

        visual.Animator.Play(clip);
        visual.Animator.Seek(atEnd ? animation.Length : 0.0, update: true);
        visual.Animator.Advance(0.0);

        var result = new Transform3D[visual.Skeleton.GetBoneCount()];
        for (var bone = 0; bone < result.Length; bone++)
            result[bone] = visual.Skeleton.GlobalTransform
                           * visual.Skeleton.GetBoneGlobalPose(bone);
        return result;
    }

    private void Check(string label, bool condition)
    {
        if (condition)
        {
            _passed++;
            GD.Print("PASS: ", label);
            return;
        }

        _failed++;
        GD.PushError("FAIL: " + label);
    }
}
