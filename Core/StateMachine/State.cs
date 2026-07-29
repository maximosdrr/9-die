using Godot;
using Godot.Collections;

[GlobalClass]
public partial class State : Node3D
{
    public StateMachine StateMachine;
    public string Type;
    public Node3D Parent;

    public virtual void Process(double delta) { }

    public virtual void PhysicsProcess(double delta) { }

    public virtual void Enter(Dictionary metadata) { }

    public virtual void Exit(Dictionary metadata) { }

    public virtual void Setup(Node3D parentNode) { }

    public virtual void HandleInput(InputEvent @event) { }
}
