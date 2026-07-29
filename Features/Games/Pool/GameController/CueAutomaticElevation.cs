using Godot;

[GlobalClass]
public partial class CueAutomaticElevation : Node
{
    [Export] public float PhysicalMargin = 0.08f;
    [Export] public Cue Cue;
    [Export] public RayCast3D CueHandleSensor;
    [Export] public Node3D AimPivot;

    public override void _PhysicsProcess(double delta)
    {
        if (!IsMultiplayerAuthority())
            return;

        UpdateSafeAngleLimit();
    }

    private void UpdateSafeAngleLimit()
    {
        if (Cue == null || CueHandleSensor == null || AimPivot == null)
            return;

        var safeLimit = 0.0f;

        if (CueHandleSensor.IsColliding())
        {
            var collisionPoint = CueHandleSensor.GetCollisionPoint();
            var diffY = (collisionPoint.Y + PhysicalMargin) - AimPivot.GlobalPosition.Y;

            if (diffY > 0)
            {
                var pivotPos2D = new Vector2(AimPivot.GlobalPosition.X, AimPivot.GlobalPosition.Z);
                var colPos2D = new Vector2(collisionPoint.X, collisionPoint.Z);
                var distanceToObstacle = pivotPos2D.DistanceTo(colPos2D);

                distanceToObstacle = Mathf.Max(distanceToObstacle, 0.1f);
                var angleRad = Mathf.Atan2(diffY, distanceToObstacle);

                safeLimit = -Mathf.Abs(angleRad);
            }
        }

        safeLimit = Mathf.Clamp(safeLimit, Mathf.DegToRad(-45.0f), 0.0f);
        Cue.MinSafeAngle = safeLimit;
    }
}
