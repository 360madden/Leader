namespace LeaderDecoder.Models
{
    public class GameState
    {
        public bool IsValid { get; set; }
        public int PlayerHP { get; set; }
        public int TargetHP { get; set; }
        
        // Flags
        public bool IsCombat { get; set; }
        public bool HasTarget { get; set; }
        public bool IsMoving { get; set; }
        public bool IsAlive { get; set; }
        public bool IsMounted { get; set; }

        // Spatial Data
        public float CoordX { get; set; }
        public float CoordY { get; set; }
        public float CoordZ { get; set; }
        
        // Identity
        public string PlayerTag { get; set; } = "____";
        public byte ZoneHash { get; set; }

        // Telemetry Data (Raw from Lua)
        public float RawFacing { get; set; }

        // Estimated Data (Computed by NavigationKernel)
        public float VelocityX { get; set; }
        public float VelocityZ { get; set; }
        public float SmoothedVelocityX { get; set; }
        public float SmoothedVelocityZ { get; set; }
        public float TravelSpeed { get; set; }
        public bool HasTravelVector { get; set; }
        public float EstimatedHeading { get; set; }
        public bool IsHeadingLocked { get; set; }
    }
}
