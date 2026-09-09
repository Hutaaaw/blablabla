using System;
using System.Collections.Generic;
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

        // ID unik kustom untuk elemen dropdown kontrol di menu Options
        public const int CONTROL_STYLE_CUSTOM_ID = 987654;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            ModHelper = helper;
            ModMonitor = Monitor;

            try
            {
                var harmony = new Harmony(ModManifest.UniqueID);

                // Patch semua constructor OptionsPage agar opsi CinderJoy disuntikkan ke menu Pengaturan
                foreach (var ctor in typeof(OptionsPage).GetConstructors())
                {
                    try
                    {
                        harmony.Patch(
                            original: ctor,
                            postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionsPageConstructorPostfix))
                        );
                    }
                    catch (Exception ex)
                    {
                        ModMonitor.Log($"Info patch ctor OptionsPage: {ex.Message}", LogLevel.Trace);
                    }
                }

                // Patch optionValueChange untuk menangkap interaksi pengubahan nilai dropdown di menu Options
                var optionValueChangeMethod = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.optionValueChange));
                if (optionValueChangeMethod != null)
                {
                    harmony.Patch(
                        original: optionValueChangeMethod,
                        postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionValueChangePostfix))
                    );
                }
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"Peringatan Inisialisasi Harmony: {ex.Message}", LogLevel.Warn);
            }

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            RegisterGenericModConfigMenu();
            ModMonitor.Log("CinderJoyTap v1.3.0 berhasil dimuat. Integrasi Options Menu Native + GMCM Aktif.", LogLevel.Info);
        }

        public static void OnOptionsPageConstructorPostfix(OptionsPage __instance)
        {
            try
            {
                if (__instance?.options == null) return;

                // 1. Cek apakah dropdown opsi CinderJoy sudah terpasang
                bool customOptionExists = false;
                foreach (var element in __instance.options)
                {
                    if (element != null && element.whichOption == CONTROL_STYLE_CUSTOM_ID)
                    {
                        customOptionExists = true;
                        break;
                    }
                }

                // 2. Jika belum, tambahkan item OptionsDropDown kustom baru ke dalam menu
                if (!customOptionExists)
                {
                    var dropDown = new OptionsDropDown("CinderJoy Control Scheme", CONTROL_STYLE_CUSTOM_ID);
                    dropDown.dropDownOptions.Add("JoypadOnly");
                    dropDown.dropDownDisplayOptions.Add("Joypad Only");

                    dropDown.dropDownOptions.Add("TapToMove");
                    dropDown.dropDownDisplayOptions.Add("Tap to Move");

                    dropDown.dropDownOptions.Add("Hybrid");
                    dropDown.dropDownDisplayOptions.Add("Hybrid (Joypad + Tap)");

                    dropDown.selectedOption = Config.Mode switch
                    {
                        ControlMode.JoypadOnly => 0,
                        ControlMode.TapToMove => 1,
                        ControlMode.Hybrid => 2,
                        _ => 2
                    };

                    __instance.options.Add(dropDown);
                }

                // 3. Modifikasi juga pilihan Control Style native Android jika ditemukan
                foreach (var element in __instance.options)
                {
                    if (element is OptionsDropDown nativeDropDown && 
                       (element.whichOption == 52 || element.label?.Equals("Control Style", StringComparison.OrdinalIgnoreCase) == true))
                    {
                        if (!nativeDropDown.dropDownOptions.Contains("Hybrid"))
                        {
                            nativeDropDown.dropDownOptions.Add("Hybrid");
                            nativeDropDown.dropDownDisplayOptions.Add("Joypad + Tap to Move");
                        }

                        nativeDropDown.selectedOption = Config.Mode switch
                        {
                            ControlMode.JoypadOnly => 0,
                            ControlMode.TapToMove => 1,
                            ControlMode.Hybrid => 2,
                            _ => 2
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                ModMonitor?.Log($"Gagal menyuntikkan menu Options: {ex.Message}", LogLevel.Trace);
            }
        }

        public static void OnOptionValueChangePostfix(int whichOption, int value)
        {
            try
            {
                if (whichOption == CONTROL_STYLE_CUSTOM_ID || whichOption == 52)
                {
                    ControlMode selectedMode = value switch
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
                        ModMonitor?.Log($"Skema Kontrol diubah via Options Menu ke: {Config.Mode}", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                ModMonitor?.Log($"Gagal memperbarui nilai opsi: {ex.Message}", LogLevel.Trace);
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            ApplyNativeControlState();
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady || Game1.player == null) return;

            if (Config.Mode == ControlMode.Hybrid)
            {
                EnsureHybridControls();
            }
        }

        public static void ApplyNativeControlState()
        {
            if (!Context.IsWorldReady) return;

            switch (Config.Mode)
            {
                case ControlMode.JoypadOnly:
                    Game1.options.controlStyle = 0;
                    break;

                case ControlMode.TapToMove:
                    Game1.options.controlStyle = 1;
                    break;

                case ControlMode.Hybrid:
                    Game1.options.controlStyle = 2;
                    break;
            }
        }

        private void EnsureHybridControls()
        {
            if (Game1.options.controlStyle != 2)
            {
                Game1.options.controlStyle = 2;
            }

            // Hentikan auto-walk CinderTap saat tombol DPad/Analog digerakkan
            if (Game1.player != null && (Game1.player.isMoving() || Game1.oldPadState.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.DPadUp)))
            {
                if (Game1.player.controller != null)
                {
                    Game1.player.controller = null;
                }
            }
        }

        private void RegisterGenericModConfigMenu()
        {
            var configMenu = ModHelper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null) return;

            configMenu.Register(
                mod: ModManifest,
                reset: () => Config = new ModConfig(),
                save: () =>
                {
                    ModHelper.WriteConfig(Config);
                    ApplyNativeControlState();
                }
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                getValue: () => Config.Enabled,
                setValue: value => Config.Enabled = value,
                name: () => "Aktifkan Mod",
                tooltip: () => "Aktifkan atau matikan fungsi CinderJoyTap."
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () => Config.Mode.ToString(),
                setValue: value =>
                {
                    if (Enum.TryParse<ControlMode>(value, out var parsedMode))
                    {
                        Config.Mode = parsedMode;
                    }
                },
                name: () => "Skema Kontrol",
                tooltip: () => "Pilih mode kontrol: JoypadOnly, TapToMove, atau Hybrid.",
                allowedValues: new[] { "JoypadOnly", "TapToMove", "Hybrid" }
            );
        }
    }

    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, Func<string, string>? formatAllowedValue = null, string? fieldId = null);
    }
}