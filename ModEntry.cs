using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace CinderJoystick
{
    public class ModEntry : Mod
    {
        public static ModConfig Config { get; private set; } = new ModConfig();
        public static IModHelper ModHelper { get; private set; } = null!;
        public static IMonitor ModMonitor { get; private set; } = null!;

        public const int CONTROL_STYLE_CUSTOM_ID = 987654;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            ModHelper = helper;
            ModMonitor = Monitor;

            var harmony = new Harmony(ModManifest.UniqueID);

            // Harmony Patches untuk menyuntikkan skema kontrol Hybrid ke menu Options bawaan game
            harmony.Patch(
                original: AccessTools.Constructor(typeof(OptionsPage), new[] { typeof(int), typeof(int), typeof(int), typeof(int) }),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionsPageConstructorPostfix))
            );

            harmony.Patch(
                original: AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.receiveLeftClick)),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionsPageReceiveLeftClickPostfix))
            );

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            ModMonitor.Log("CinderJoystick berhasil dimuat. Mengintegrasikan Virtual Joypad Native.", LogLevel.Info);
        }

        // --- HARMONY PATCHES UNTUK MENU OPTIONS NATIVE GAME --- //

        public static void OnOptionsPageConstructorPostfix(OptionsPage __instance)
        {
            try
            {
                OptionsDropDown? controlStyleDropDown = null;

                foreach (var element in __instance.options)
                {
                    if (element is OptionsDropDown dropDown && 
                       (element.label?.Equals("Control Style", StringComparison.OrdinalIgnoreCase) == true || element.whichOption == 52))
                    {
                        controlStyleDropDown = dropDown;
                        break;
                    }
                }

                if (controlStyleDropDown != null)
                {
                    controlStyleDropDown.whichOption = CONTROL_STYLE_CUSTOM_ID;
                    controlStyleDropDown.dropDownOptions.Clear();
                    controlStyleDropDown.dropDownDisplayOptions.Clear();

                    controlStyleDropDown.dropDownOptions.Add("JoypadOnly");
                    controlStyleDropDown.dropDownDisplayOptions.Add("Joypad");

                    controlStyleDropDown.dropDownOptions.Add("TapToMove");
                    controlStyleDropDown.dropDownDisplayOptions.Add("Tap to Move");

                    controlStyleDropDown.dropDownOptions.Add("Hybrid");
                    controlStyleDropDown.dropDownDisplayOptions.Add("Joypad + Tap to Move");

                    controlStyleDropDown.selectedOption = Config.Mode switch
                    {
                        ControlMode.JoypadOnly => 0,
                        ControlMode.TapToMove => 1,
                        ControlMode.Hybrid => 2,
                        _ => 2
                    };
                }
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"Gagal menyuntikkan menu Options: {ex.Message}", LogLevel.Error);
            }
        }

        public static void OnOptionsPageReceiveLeftClickPostfix(OptionsPage __instance, int x, int y)
        {
            try
            {
                foreach (var element in __instance.options)
                {
                    if (element.whichOption == CONTROL_STYLE_CUSTOM_ID && element is OptionsDropDown dropDown)
                    {
                        ControlMode selectedMode = dropDown.selectedOption switch
                        {
                            0 => ControlMode.JoypadOnly,
                            1 => ControlMode.TapToMove,
                            2 => ControlMode.Hybrid,
                            _ => ControlMode.Hybrid
                        };

                        if (Config.Mode != selectedMode)
                        {
                            Config.Mode = selectedMode;
                            ModHelper.WriteConfig(Config);
                            ApplyNativeControlState();
                            ModMonitor.Log($"Skema Kontrol diubah ke: {Config.Mode}", LogLevel.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"Gagal memproses pilihan menu Options: {ex.Message}", LogLevel.Error);
            }
        }

        // --- PENGELOLAAN MANAJEMEN KONTROL NATIVE GAME --- //

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;

            // Setiap tick, pastikan status Virtual Joypad native game disinkronkan
            EnsureNativeJoypadState();
        }

        private static void ApplyNativeControlState()
        {
            if (!Context.IsWorldReady) return;

            switch (Config.Mode)
            {
                case ControlMode.JoypadOnly:
                    // Aktifkan Joypad Native Bawaan Game (Joypad & Buttons)
                    Game1.options.controlStyle = 0;
                    break;

                case ControlMode.TapToMove:
                    // Aktifkan Tap to Move Native Bawaan Game
                    Game1.options.controlStyle = 1;
                    break;

                case ControlMode.Hybrid:
                    // Paksa game mengaktifkan Joypad Native (Joystick + Action/Tool Buttons)
                    // sambil tetap mengizinkan sistem Tap-To-Move/CinderTap membaca input layar
                    Game1.options.controlStyle = 2; // "Joypad + Tap to Move" mode di engine Android
                    break;
            }
        }

        private void EnsureNativeJoypadState()
        {
            if (Config.Mode == ControlMode.Hybrid)
            {
                // Memastikan tombol Action & Tool serta Joystick Native tetap muncul di layar
                if (Game1.options.controlStyle != 2)
                {
                    Game1.options.controlStyle = 2;
                }

                // Jika pemain sedang menggerakkan analog Joypad native, hentikan auto-walk pathfinding CinderTap
                if (Game1.player != null && (Game1.player.isMoving() || Game1.oldPadState.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.DPadUp)))
                {
                    if (Game1.player.controller != null)
                    {
                        Game1.player.controller = null;
                    }
                }
            }
        }
    }
}