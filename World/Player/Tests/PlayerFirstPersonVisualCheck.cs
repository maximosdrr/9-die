using Godot;
using Poker.Rules;

/// <summary>Rendered diagnostic for the poker arms, real card nodes and table clearance.</summary>
public partial class PlayerFirstPersonVisualCheck : Node3D
{
    [Export] public string OutputPath = "user://player_first_person_visual.png";
    private int _frames;

    public override void _Ready()
    {
        var camera = new Camera3D { Current = true, Fov = 55.0f };
        AddChild(camera);

        var poker = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn").Instantiate();
        var authoring = poker.GetNode<PokerExperienceAuthoring>("ExperienceAuthoring");
        var authoredHandPose = authoring.FirstPersonHandsPreview.Transform;
        var authoredCardsPose = authoring.FirstPersonCardsPose.Transform;
        poker.Free();

        var hands = GD.Load<PackedScene>(
                "res://Games/Poker/Components/Hands/PlayerFirstPersonHands.tscn")
            .Instantiate<PlayerFirstPersonHands>();
        AddChild(hands);
        hands.Transform = authoredHandPose;
        hands.Animator.Play(CharacterVisual.Clips.IdleSitHoldingCards);
        hands.Animator.Seek(1.0, update: true);
        hands.UpdateCardGrip();
        AddCards(hands.CardGrip, authoredCardsPose);

        var felt = new MeshInstance3D
        {
            Position = new Vector3(0.0f, -0.34f, -0.62f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(1.55f, 0.04f, 1.0f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.045f, 0.20f, 0.14f),
                    Roughness = 0.95f,
                },
            },
        };
        AddChild(felt);

        AddChild(new DirectionalLight3D
        {
            LightEnergy = 1.8f,
            RotationDegrees = new Vector3(-35.0f, -25.0f, 0.0f),
        });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.025f, 0.03f, 0.04f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
                AmbientLightEnergy = 1.3f,
            },
        });
    }

    public override void _Process(double delta)
    {
        if (++_frames < 24)
            return;

        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(OutputPath);
        GD.Print("IMAGEM: ", ProjectSettings.GlobalizePath(OutputPath));
        GetTree().Quit(0);
    }

    private static void AddCards(Node3D grip, Transform3D cardsPose)
    {
        var scene = GD.Load<PackedScene>("res://Games/Poker/Components/Cards/PokerCard.tscn");
        var spec = PokerLayoutSpec.Default;
        var fanSpec = new HandFanSpec(11.0f, 0.40f, 0.0f,
            0.0f, HandFan.LongAxisUpFromMinusZ);
        for (var index = 0; index < 2; index++)
        {
            var card = scene.Instantiate<PokerCard>();
            grip.AddChild(card);
            card.Configure(Poker.Rules.CardId.From(
                index == 0 ? Poker.Rules.CardId.Queen : Poker.Rules.CardId.Jack,
                index == 0 ? Poker.Rules.CardId.Hearts : Poker.Rules.CardId.Clubs), spec);
            card.Transform = cardsPose * HandFan.SlotTransform(
                index, HandFan.NaturalCentre(2), false, fanSpec);
        }
    }

}
