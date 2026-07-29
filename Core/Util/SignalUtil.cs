using Godot;

public static class SignalUtil
{
    public static void ConnectGuarded(GodotObject source, StringName signal, Callable callable)
    {
        if (!source.IsConnected(signal, callable))
            source.Connect(signal, callable);
    }

    public static void DisconnectGuarded(GodotObject source, StringName signal, Callable callable)
    {
        if (source.IsConnected(signal, callable))
            source.Disconnect(signal, callable);
    }
}
