using Godot;

[GlobalClass]
public partial class PoolStartGameUI : Node3D
{
    [Export] public float RotationSpeed = 0.5f;

    private Label3D _label;

    public override void _Ready()
    {
        _label = GetNode<Label3D>("Label3D");
    }

    public override void _Process(double delta)
    {
        _label.RotateY(RotationSpeed * (float)delta);
    }

    public void SetText(string text)
    {
        _label.Text = text;
    }
}
