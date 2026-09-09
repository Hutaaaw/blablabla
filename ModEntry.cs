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

        public const int CONTROL_STYLE_CUSTOM_ID = 987654;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            ModHelper = helper;
            ModMonitor = Monitor;

            try
            {
                var harmony = new Harmony(ModManifest.UniqueID);

                foreach (var ctor in typeof(OptionsPage).GetConstructors())
                {
                    try
                    {
                        harmony.Patch(
                            original: ctor,
                            postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionsPageConstructorPostfix))
                        );
                    }
                    catch { }
                }

                // Menggunakan Reflection string agar tidak terjadi error CS0117 saat dikompilasi di PC/GitHub Actions
                var optionValueChangeMethod = AccessTools.Method(typeof(OptionsPage), "optionValueChange");
                if (optionValueChangeMethod != null)
                {
                    harmony.Patch(
                        original: optionValueChangeMethod,
                        postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionValueChangePostfix))
                    );
                }

                var receiveLeftClickMethod = AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.receiveLeftClick));
                if (receiveLeftClickMethod != null)
                {
                    harmony.Patch(
                        original: receiveLeftClickMethod,
                        postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnReceiveLeftClickPostfix))
                    );
                }
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"Peringatan Harmony Patch: {ex.Message}", LogLevel.Warn);
            }

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        public static void SetNativeControlStyle(int value)
        {
            try
            {
                if (Game1.options == null) return;
                var field = AccessTools.Field(typeof(Options), "controlStyle") 
                         ?? AccessTools.Field(typeof(Options), "ControlStyle");
                if (field != null)
                {
                    field.SetValue(Game1.options, value);
                    return;
                }

                var prop = AccessTools.Property(typeof(Options), "controlStyle") 
                        ?? AccessTools.Property(typeof(Options), "ControlStyle");
                if (prop != null)
                {
                    prop.SetValue(Game1.options, value);
                }
            }
            catch { }
        }

        public static int GetNativeControlStyle()
        {
            try
            {
                if (Game1.options == null) return 0;
                var field = AccessTools.Field(typeof(Options), "controlStyle") 
                         ?? AccessTools.Field(typeof(Options), "ControlStyle");
                if (field != null)
                    return Convert.ToInt32(field.GetValue(Game1.options));

                var prop = AccessTools.Property(typeof(Options), "controlStyle") 
                        ?? AccessTools.Property(typeof(Options), "ControlStyle");
                if (prop != null)
                    return Convert.ToInt32(prop.GetValue(Game1.options));
            }
            catch { }
            return 0;
        }

        public static void OnReceiveLeftClickPostfix(OptionsPage __instance)
        {
            try
            {
                if (__instance?.options == null) return;
                foreach (var element in __instance.options)
                {
                    if (element != null && (element.whichOption == CONTROL_STYLE_CUSTOM_ID || element.whichOption == 52) && element is OptionsDropDown dropDown)
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
                            ModMonitor?.Log($"Skema Kontrol diubah via Options Menu ke: {Config.Mode}", LogLevel.Info);
                        }
                    }
                }
            }
            catch { }
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
            catch { }
        }

        public static void ApplyNativeControlState()
        {
            if (!Context.IsWorldReady) return;

            switch (Config.Mode)
            {
                case ControlMode.JoypadOnly:
                    SetNativeControlStyle(0);
                    break;

                case ControlMode.TapToMove:
                    SetNativeControlStyle(1);
                    break;

                case ControlMode.Hybrid:
                    SetNativeControlStyle(2);
                    break;
            }
        }

        private void EnsureHybridControls()
        {
            if (GetNativeControlStyle() != 2)
            {
                SetNativeControlStyle(2);
            }

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