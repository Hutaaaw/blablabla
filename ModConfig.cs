namespace CinderJoyTap
{
    public enum ControlMode
    {
        JoypadOnly,      // Joypad Only
        TapToMove,       // Tap to Move Only
        Hybrid           // Joypad + Tap to Move
    }

    public class ModConfig
    {
        public bool Enabled { get; set; } = true;
        public ControlMode Mode { get; set; } = ControlMode.Hybrid;
        public float BaseRadius { get; set; } = 110f;
        public float KnobRadius { get; set; } = 45f;
        public float Deadzone { get; set; } = 0.2f;
        public float Opacity { get; set; } = 0.4f;
        
        // Offset dari sudut kiri bawah layar (X, Y)
        public float OffsetX { get; set; } = 200f;
        public float OffsetY { get; set; } = 200f;
        
        // Membatalkan jalur auto-walk saat joystick digerakkan
        public bool OverrideCinderTap { get; set; } = true;
    }
}