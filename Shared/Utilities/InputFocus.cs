using Godot;

public static class InputFocus
{
    public static bool IsCaptured => Input.MouseMode == Input.MouseModeEnum.Captured;

    public static void Capture() => Input.MouseMode = Input.MouseModeEnum.Captured;

    public static void Release() => Input.MouseMode = Input.MouseModeEnum.Visible;
}
