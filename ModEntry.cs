using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CinderJoyTap
{
    /// <summary>
    /// Mod Mediator (Penengah) antara CinderTap (Tap-to-Move) dan Virtual Joystick Cinderbox.
    /// </summary>
    public class ModEntry : Mod
    {
        public static ModConfig Config { get; private set; } = new ModConfig();

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Input.ButtonsChanged += OnButtonsChanged;
            helper.Events.Input.CursorMoved += OnCursorMoved;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            Monitor.Log("CinderJoyTap Bridge (Mod Penengah CinderTap & Joystick) berhasil aktif.", LogLevel.Info);

            bool hasCinderTap = Helper.ModRegistry.IsLoaded("Eky.CinderTap");
            if (hasCinderTap)
            {
                Monitor.Log("Mod CinderTap terdeteksi! Sistem jembatan siap bekerja.", LogLevel.Info);
            }
            else
            {
                Monitor.Log("CinderTap tidak ditemukan. Mod tetap memantau input joystick.", LogLevel.Warn);
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady || Game1.player == null) return;

            // Mode 0: Hanya Joystick -> selalu batalkan pathfinding tap
            if (Config.Mode == ControlMode.JoypadOnly)
            {
                if (Game1.player.controller != null)
                {
                    Game1.player.controller = null;
                }
                return;
            }

            // Mode 2: Hybrid -> Jika pemain menggerakkan Joystick, hentikan auto-walk CinderTap
            if (Config.Mode == ControlMode.Hybrid)
            {
                if (IsJoystickOrKeyActive() && Game1.player.controller != null)
                {
                    Game1.player.controller = null;
                    Game1.player.Halt();
                }
            }
        }

        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady || Game1.player == null) return;

            if (Config.Mode == ControlMode.Hybrid && IsJoystickOrKeyActive() && Game1.player.controller != null)
            {
                Game1.player.controller = null;
                Game1.player.Halt();
            }
        }

        private void OnCursorMoved(object? sender, CursorMovedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady || Game1.player == null) return;

            // Jika pemain sedang menggunakan joystick, abaikan pergerakan kursor/tap
            if (Config.Mode == ControlMode.Hybrid && IsJoystickOrKeyActive() && Game1.player.controller != null)
            {
                Game1.player.controller = null;
            }
        }

        /// <summary>
        /// Mendeteksi apakah input pergerakan manual (WASD / Gamepad Analog) sedang aktif.
        /// </summary>
        private bool IsJoystickOrKeyActive()
        {
            try
            {
                // A. Cek Analog Stick & D-Pad Gamepad Virtual Cinderbox (XInput)
                var padState = Game1.input.GetGamePadState();
                if (padState.IsConnected)
                {
                    if (padState.ThumbSticks.Left.LengthSquared() > 0.04f ||
                        padState.DPad.Up == Microsoft.Xna.Framework.Input.ButtonState.Pressed ||
                        padState.DPad.Down == Microsoft.Xna.Framework.Input.ButtonState.Pressed ||
                        padState.DPad.Left == Microsoft.Xna.Framework.Input.ButtonState.Pressed ||
                        padState.DPad.Right == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
                    {
                        return true;
                    }
                }

                // B. Cek Input Keyboard Virtual WASD / Panah Arah
                if (Helper.Input.IsDown(SButton.W) || Helper.Input.IsDown(SButton.A) ||
                    Helper.Input.IsDown(SButton.S) || Helper.Input.IsDown(SButton.D) ||
                    Helper.Input.IsDown(SButton.Up) || Helper.Input.IsDown(SButton.Left) ||
                    Helper.Input.IsDown(SButton.Down) || Helper.Input.IsDown(SButton.Right))
                {
                    return true;
                }

                // C. Cek pergerakan karakter internal tanpa controller pathfinding
                if (Game1.player != null && Game1.player.isMoving() && Game1.player.controller == null)
                {
                    return true;
                }
            }
            catch
            {
                // Menyerap exception jika buffer input belum siap
            }

            return false;
        }
    }
}