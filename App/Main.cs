using Godot;

public partial class Main : Node3D
{
    [Export] public GlobalCamera Camera;
    [Export] public LevelMultiplayerManager LevelManager;

    /// <summary>The level being played. Every table inside it is wired to the camera.</summary>
    [Export] public Node3D Level;

    /// <summary>
    /// Serialized export roots for assets that production code loads dynamically by path or UID.
    /// </summary>
    [Export] public RuntimeResourceManifest RuntimeResources { get; set; }

    private NetworkProvider _networkProvider;
    private bool _sessionWorldReloadPending;

    public override void _Ready()
    {
        LevelManager.Camera = Camera;
        WireTables(Level);

        _networkProvider = NetworkManager.Instance?.NetworkProvider;
        if (_networkProvider != null)
            _networkProvider.ServerDisconnected += OnServerDisconnected;
    }

    public override void _ExitTree()
    {
        if (_networkProvider != null)
            _networkProvider.ServerDisconnected -= OnServerDisconnected;
        _networkProvider = null;
    }

    private void OnServerDisconnected()
    {
        ScheduleSessionWorldReload();
    }

    /// <summary>
    /// A disconnected client's scene contains authoritative snapshots from the old session.
    /// Rebuilding the composition root is the deterministic reset boundary for tables, hands,
    /// turn state, spawned players, media and presentation before another connection is allowed.
    /// </summary>
    internal bool ScheduleSessionWorldReload(bool deferReload = true)
    {
        if (_sessionWorldReloadPending)
            return false;

        _sessionWorldReloadPending = true;
        if (deferReload)
            CallDeferred(MethodName.ReloadSessionWorld);
        return true;
    }

    private void ReloadSessionWorld()
    {
        if (!IsInsideTree())
            return;

        var error = GetTree().ReloadCurrentScene();
        if (error != Error.Ok)
        {
            _sessionWorldReloadPending = false;
            GD.PushWarning($"Could not rebuild the world after disconnect: {error}.");
        }
    }

    /// <summary>
    /// Hands the camera to every table in the level.
    ///
    /// This used to point at a single table by path, which quietly broke the moment the bar got a
    /// second one: a game that never receives the camera still runs, but every view change is
    /// null-guarded, so the player is seated and then left staring wherever they happened to be
    /// facing. Walking the tree means adding a table to a level needs no wiring at all.
    /// </summary>
    private void WireTables(Node node)
    {
        if (node == null)
            return;

        if (node is Table table)
            table.SetCamera(Camera);

        foreach (var child in node.GetChildren())
            WireTables(child);
    }
}
