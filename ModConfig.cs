namespace CinderJoystick
{
    public enum ControlMode
    {
        JoypadOnly,      // Joypad Bawaan Game
        TapToMove,       // Tap to Move / CinderTap
        Hybrid           // Joypad Native + Tap to Move (CinderJoy)
    }

    public class ModConfig
    {
        public bool Enabled { get; set; } = true;
        public ControlMode Mode { get; set; } = ControlMode.Hybrid;
    }
}