using Godot;
using Godot.Collections;

[GlobalClass]
public partial class StateMachine : Node3D
{
    [Signal]
    public delegate void StateChangedEventHandler(string type, Dictionary metadata);

    public State Current;
    public State Previous;
    public Dictionary<string, State> States = new();
    public Dictionary CurrentMetadata = new();

    [Export] public bool Enabled = true;
    [Export] public string InitialState;
    [Export] public PublicStateSyncronizer PublicStateSyncronizer;
    [Export] public AuthorityStateSynchronizer AuthorityStateSynchronizer;
    [Export] public bool CheckForMultiplayerAuthorityOnStateHandleInput = false;

    public override void _Ready()
    {
        SetupStates();
        SetupInitialState();

        PublicStateSyncronizer?.Setup(this);
        AuthorityStateSynchronizer?.Setup(this);
    }

    public void ChangeState(string type, Dictionary metadata)
    {
        if (AuthorityStateSynchronizer != null)
        {
            if (!IsMultiplayerAuthority() && !AuthorityStateSynchronizer.IsIncomingNetworkChange)
                return;
        }

        if (Current.Type == type)
            return;

        if (!States.TryGetValue(type, out var newState) || newState == null)
        {
            GD.PushError($"State {type} not found");
            return;
        }

        CurrentMetadata = metadata;
        EmitSignal(SignalName.StateChanged, type, metadata);
        Current.Exit(metadata);

        Previous = Current;
        Current = newState;

        Current.Enter(metadata);
    }

    public override void _Process(double delta)
    {
        Current?.Process(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        Current?.PhysicsProcess(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (CheckForMultiplayerAuthorityOnStateHandleInput && !IsMultiplayerAuthority())
            return;

        Current?.HandleInput(@event);
    }

    private void SetupStates()
    {
        var parent = GetParent() as Node3D;

        foreach (var child in GetChildren())
        {
            if (child is State state)
            {
                state.StateMachine = this;
                state.Parent = parent;
                state.Setup(parent);
                States[state.Type] = state;
            }
        }
    }

    private void SetupInitialState()
    {
        if (!States.TryGetValue(InitialState, out var state) || state == null)
        {
            GD.PushError("Initial state not found: ", InitialState);
            return;
        }

        Current = state;
        state.Enter(new Dictionary());
    }
}
