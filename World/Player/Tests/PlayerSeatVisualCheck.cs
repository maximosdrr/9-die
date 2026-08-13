using Godot;

/// <summary>Rendered side view used to verify that the seated pelvis lands inside the real chair.</summary>
public partial class PlayerSeatVisualCheck : Node3D
{
    [Export] public string OutputPath = "user://player_seat_visual.png";
    private int _frames;

    public override void _Ready()
    {
        var poker = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn").Instantiate<Node3D>();
        AddChild(poker);
        poker.GetNode<Node3D>("TableFixture/PokerTableModel").Hide();
        poker.GetNode<Node3D>("BoardHolder").Hide();
        poker.GetNode<Node3D>("SeatPresenter").Hide();

        for (var index = 1; index < 4; index++)
            poker.GetNode<Node3D>($"TableFixture/WoodenChair_00{6 + index}").Hide();

        var seat = poker.GetNode<Marker3D>("Seats/Seat0");
        var character = GD.Load<PackedScene>(
                "res://World/Player/Components/CharacterVisual.tscn")
            .Instantiate<CharacterVisual>();
        poker.AddChild(character);
        character.GlobalTransform = seat.GlobalTransform;
        character.Play(CharacterVisual.Clips.IdleSitHoldingCards, 0.0);
        character.Animator.Seek(1.0, update: true);

        var camera = new Camera3D
        {
            Current = true,
            Fov = 38.0f,
            Position = new Vector3(-1.75f, 0.92f, -1.65f),
        };
        AddChild(camera);
        camera.LookAt(new Vector3(0.0f, 0.62f, -1.1f), Vector3.Up);

        AddChild(new DirectionalLight3D
        {
            LightEnergy = 2.0f,
            RotationDegrees = new Vector3(-45.0f, -35.0f, 0.0f),
        });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.025f, 0.03f, 0.04f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
                AmbientLightEnergy = 1.1f,
            },
        });
    }

    public override void _Process(double delta)
    {
        if (++_frames < 24)
            return;

        GetViewport().GetTexture().GetImage().SavePng(OutputPath);
        GD.Print("IMAGEM: ", ProjectSettings.GlobalizePath(OutputPath));
        GetTree().Quit(0);
    }
}
