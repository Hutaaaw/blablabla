using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace CinderJoyTap
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

            // Patch OptionsPage untuk menambahkan opsi Hybrid (Joypad + Tap to Move)
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
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            ModMonitor.Log("CinderJoyTap siap. Mengintegrasikan Virtual Joypad Native (MobileAtlas) & CinderTap.", LogLevel.Info);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            ApplyNativeControlState();
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;

            // Pastikan game menggunakan MobileAtlas_tencent & Joypad native
            EnsureNativeJoypadState();
        }

        /// <summary>
        /// Mengatur nilai controlStyle native pada engine Android.
        /// Nilai 2 = Joypad (MobileAtlas) + Tap to Move (CinderTap)
        /// </summary>
        private static void SetNativeControlStyle(int styleValue)
        {
            try
            {
                var prop = AccessTools.Property(typeof(Options), "controlStyle") 
                        ?? AccessTools.Property(typeof(Options), "ControlStyle");
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(Game1.options, styleValue);
                    return;
                }

                var field = AccessTools.Field(typeof(Options), "controlStyle") 
                         ?? AccessTools.Field(typeof(Options), "ControlStyle");
                if (field != null)
                {
                    field.SetValue(Game1.options, styleValue);
                }
            }
            catch (Exception ex)
            {
                ModMonitor?.Log($"Gagal mengatur controlStyle: {ex.Message}", LogLevel.Trace);
            }
        }

        private static int GetNativeControlStyle()
        {
            try
            {
                var prop = AccessTools.Property(typeof(Options), "controlStyle") 
                        ?? AccessTools.Property(typeof(Options), "ControlStyle");
                if (prop != null)
                {
                    return Convert.ToInt32(prop.GetValue(Game1.options));
                }

                var field = AccessTools.Field(typeof(Options), "controlStyle") 
                         ?? AccessTools.Field(typeof(Options), "ControlStyle");
                if (field != null)
                {
                    return Convert.ToInt32(field.GetValue(Game1.options));
                }
            }
            catch { }
            return -1;
        }

        private static void ApplyNativeControlState()
        {
            if (!Context.IsWorldReady || Game1.options == null) return;

            switch (Config.Mode)
            {
                case ControlMode.JoypadOnly:
                    SetNativeControlStyle(0);
                    break;

                case ControlMode.TapToMove:
                    SetNativeControlStyle(1);
                    break;

                case ControlMode.Hybrid:
                    // Style 2 mengaktifkan renderer MobileAtlas_tencent sekaligus pembacaan Tap-to-Move
                    SetNativeControlStyle(2);
                    break;
            }
        }

        private void EnsureNativeJoypadState()
        {
            if (Config.Mode == ControlMode.Hybrid)
            {
                int currentStyle = GetNativeControlStyle();
                if (currentStyle != -1 && currentStyle != 2)
                {
                    SetNativeControlStyle(2);
                }

                // SINKRONISASI CINDERTAP & JOYPAD:
                // Jika pemain menyentuh analog joystick native, langsung hentikan pathfinding auto-walk CinderTap
                if (Game1.player != null && Game1.player.controller != null && Game1.player.isMoving())
                {
                    // Membatalkan auto-walk CinderTap saat analog digerakkan manual
                    Game1.player.controller = null;
                }
            }
        }

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
                    controlStyleDropDown.dropDownDisplayOptions.Add("Joypad + Tap to Move (MobileAtlas)");

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
                ModMonitor?.Log($"Gagal menyuntikkan menu Options: {ex.Message}", LogLevel.Error);
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
                            ModMonitor?.Log($"Mode kontrol diubah ke: {Config.Mode}", LogLevel.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModMonitor?.Log($"Gagal memproses klik Options: {ex.Message}", LogLevel.Error);
            }
        }
    }
}