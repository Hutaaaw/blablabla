namespace CinderJoyTap
{
    public enum ControlMode
    {
        JoypadOnly = 0,   // Hanya Virtual Joypad
        TapToMove = 1,    // Hanya CinderTap
        Hybrid = 2        // Joypad + CinderTap
    }

    public class ModConfig
    {
        public bool Enabled { get; set; } = true;
        public ControlMode Mode { get; set; } = ControlMode.Hybrid;
    }
}