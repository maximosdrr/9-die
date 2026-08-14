using Godot;
using Poker.Rules;

/// <summary>Non-headless visual harness for the imported standing and seated production poses.</summary>
public partial class PlayerCharacterVisualCheck : Node3D
{
    [Export] public string OutputPath = "user://player_character_visual.png";
    private int _frames;

    public override void _Ready()
    {
        BuildGround();
        SpawnCharacter(new Vector3(-1.8f, 0.0f, 0.0f), CharacterVisual.Clips.IdleHoldingCardsDown);
        SpawnCharacter(new Vector3(0.0f, 0.0f, 0.0f), CharacterVisual.Clips.IdleSit);
        SpawnCharacter(new Vector3(1.8f, 0.0f, 0.0f), CharacterVisual.Clips.Idle);

        var camera = new Camera3D
        {
            Current = true,
            Fov = 39.0f,
            Position = new Vector3(3.8f, 1.85f, -5.2f),
        };
        AddChild(camera);
        camera.LookAt(new Vector3(0.0f, 0.92f, 0.0f), Vector3.Up);

        var key = new DirectionalLight3D
        {
            LightEnergy = 1.7f,
            ShadowEnabled = true,
            RotationDegrees = new Vector3(-48.0f, -25.0f, 0.0f),
        };
        AddChild(key);

        var fill = new OmniLight3D
        {
            LightEnergy = 2.0f,
            OmniRange = 12.0f,
            Position = new Vector3(-2.5f, 3.2f, -3.0f),
        };
        AddChild(fill);

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.025f, 0.03f, 0.04f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.55f, 0.65f),
                AmbientLightEnergy = 1.0f,
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

    private void SpawnCharacter(Vector3 position, string clip)
    {
        var scene = GD.Load<PackedScene>("res://World/Player/Components/CharacterVisual.tscn");
        var character = scene.Instantiate<CharacterVisual>();
        character.Position = position;
        AddChild(character);
        character.Play(clip, 0.0);
        character.Animator.Seek(clip == CharacterVisual.Clips.IdleHoldingCardsDown ? 1.0 : 0.1,
            update: true);

        if (clip == CharacterVisual.Clips.IdleSit)
            character.SetCameraLook(0.75f, -0.28f);

        if (clip == CharacterVisual.Clips.IdleHoldingCardsDown)
        {
            character._Process(0.0);
            AddCards(character.CardGrip);
        }
    }

    private void AddCards(Node3D grip)
    {
        var scene = GD.Load<PackedScene>("res://Games/Poker/Components/Cards/PokerCard.tscn");
        var spec = PokerLayoutSpec.Default;
        for (var index = 0; index < 2; index++)
        {
            var card = scene.Instantiate<PokerCard>();
            grip.AddChild(card);
            card.Configure(Poker.Rules.CardId.From(
                index == 0 ? Poker.Rules.CardId.Ace : Poker.Rules.CardId.King,
                index == 0 ? Poker.Rules.CardId.Spades : Poker.Rules.CardId.Hearts), spec);

            var lateral = (index - 0.5f) * spec.CardWidth * 0.42f;
            var fan = Mathf.DegToRad(index == 0 ? -7.0f : 7.0f);
            card.Transform = new Transform3D(
                new Basis(Vector3.Forward, fan) * HandFan.LongAxisUpFromMinusZ,
                new Vector3(lateral, index * spec.CardThickness * 1.5f, 0.0f));
        }
    }

    private void BuildGround()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.13f, 0.15f),
            Roughness = 0.9f,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(8.0f, 5.0f), Material = material },
        });

        // Poker's felt is 0.718 m above the seat root. This reference slab makes any clipping in the
        // authored hand pose immediately visible in the diagnostic image.
        AddChild(new MeshInstance3D
        {
            Position = new Vector3(-1.8f, 0.715f, -0.65f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(1.45f, 0.035f, 0.85f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.055f, 0.22f, 0.16f),
                    Roughness = 0.95f,
                },
            },
        });
    }
}
