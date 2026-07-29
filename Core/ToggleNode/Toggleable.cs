using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Toggleable : Node
{
    public enum InitialStateEnum { Enabled, Disabled }

    [ExportGroup("Setup")]
    [Export] public InitialStateEnum InitialState = InitialStateEnum.Enabled;
    [Export] public Array<NodePath> Targets = new();

    [ExportGroup("What to toggle")]
    [Export] public bool ToggleProcess = true;
    [Export] public bool TogglePhysics = true;
    [Export] public bool ToggleVisible = false;
    [Export] public bool DisableChildren = true;
    [Export] public bool DisableCameras = false;

    public override void _Ready()
    {
        if (InitialState == InitialStateEnum.Disabled)
            Disable();
        else
            Enable();
    }

    public void Disable()
    {
        Apply(false);
    }

    public void Enable()
    {
        Apply(true);
    }

    private void Apply(bool on)
    {
        foreach (var path in Targets)
        {
            var root = GetNodeOrNull(path);
            if (root == null)
                continue;

            ApplyToNode(root, on);

            if (DisableChildren)
            {
                foreach (var child in GetAllDescendants(root))
                    ApplyToNode(child, on);
            }
        }
    }

    private void ApplyToNode(Node n, bool on)
    {
        if (ToggleProcess)
            n.SetProcess(on);

        if (TogglePhysics)
            n.SetPhysicsProcess(on);

        if (ToggleVisible)
        {
            if (n is CanvasItem ci)
                ci.Visible = on;
            else if (n is Node3D n3d)
                n3d.Visible = on;
        }

        if (DisableCameras && n is Camera3D camera)
            camera.Current = on;
    }

    private Array<Node> GetAllDescendants(Node root)
    {
        var result = new Array<Node>();
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var c in node.GetChildren())
            {
                result.Add(c);
                stack.Push(c);
            }
        }

        return result;
    }
}
