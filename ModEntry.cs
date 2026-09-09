using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace CinderJoyTap
{
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null!);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null!, string fieldId = null!);
        void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string> tooltip = null!, float? min = null, float? max = null, float? interval = null, Func<float, string> formatValue = null!, string fieldId = null!);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null!, string[] allowedValues = null!, Func<string, string> formatAllowedValue = null!, string fieldId = null!);
    }

    public class ModEntry : Mod
    {
        public static ModConfig Config { get; private set; } = new ModConfig();
        public static IModHelper ModHelper { get; private set; } = null!;
        public static IMonitor ModMonitor { get; private set; } = null!;

        public const int CONTROL_STYLE_CUSTOM_ID = 987654;
        public const int ADJUST_JOYSTICK_CUSTOM_ID = 987655;

        private Vector2 joystickCenter;
        private Vector2 knobPosition;
        private bool isDragging = false;
        private int activeTouchId = -1;
        private Vector2 inputVector = Vector2.Zero;

        private Texture2D? circleTexture;

        // --- MODE ADJUSTMENT ---
        private static bool isAdjusting = false;
        private bool isDraggingInAdjust = false;
        private Rectangle saveButtonRect;
        private Rectangle sizePlusRect;
        private Rectangle sizeMinusRect;
        private Rectangle opacityPlusRect;
        private Rectangle opacityMinusRect;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            ModHelper = helper;
            ModMonitor = Monitor;

            var harmony = new Harmony(ModManifest.UniqueID);

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
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.Display.WindowResized += OnWindowResized;

            RecalculatePosition();
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            circleTexture = CreateCircleTexture(128);
            RecalculatePosition();
            SetupGMCM();
        }

        private void SetupGMCM()
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null) return;

            configMenu.Register(
                mod: ModManifest,
                reset: () => { Config = new ModConfig(); RecalculatePosition(); },
                save: () => { Helper.WriteConfig(Config); RecalculatePosition(); }
            );

            configMenu.AddSectionTitle(ModManifest, () => "Pengaturan Joystick In-Game");

            configMenu.AddBoolOption(
                mod: ModManifest,
                getValue: () => Config.Enabled,
                setValue: value => Config.Enabled = value,
                name: () => "Aktifkan Mod",
                tooltip: () => "Nyalakan atau matikan virtual joystick."
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () => Config.Mode.ToString(),
                setValue: value => {
                    if (Enum.TryParse<ControlMode>(value, out var mode))
                        Config.Mode = mode;
                },
                name: () => "Mode Kontrol",
                allowedValues: new[] { "JoypadOnly", "TapToMove", "Hybrid" },
                formatAllowedValue: value => value switch
                {
                    "JoypadOnly" => "Joypad",
                    "TapToMove" => "Tap to Move",
                    "Hybrid" => "Joypad + Tap to Move",
                    _ => value
                }
            );
        }

        private void OnWindowResized(object? sender, WindowResizedEventArgs e)
        {
            RecalculatePosition();
        }

        private void RecalculatePosition()
        {
            float screenHeight = Game1.uiViewport.Height;
            joystickCenter = new Vector2(Config.OffsetX, screenHeight - Config.OffsetY);
            knobPosition = joystickCenter;
        }

        // --- HARMONY PATCHES ---

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

                bool hasAdjustBtn = false;
                foreach (var element in __instance.options)
                {
                    if (element.whichOption == ADJUST_JOYSTICK_CUSTOM_ID || element.label?.Equals("Adjust Joypad", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        element.whichOption = ADJUST_JOYSTICK_CUSTOM_ID;
                        hasAdjustBtn = true;
                        break;
                    }
                }

                if (!hasAdjustBtn)
                {
                    var adjustButton = new OptionsButton("Adjust Joypad", () =>
                    {
                        isAdjusting = true;
                        Game1.activeClickableMenu = null;
                        ModMonitor.Log("Masuk ke Mode Adjust Joypad.", LogLevel.Info);
                    });
                    adjustButton.whichOption = ADJUST_JOYSTICK_CUSTOM_ID;
                    __instance.options.Add(adjustButton);
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
                            ModMonitor.Log($"Skema Kontrol diubah ke: {Config.Mode}", LogLevel.Info);
                        }
                    }
                    else if (element.whichOption == ADJUST_JOYSTICK_CUSTOM_ID && element.bounds.Contains(x, y))
                    {
                        isAdjusting = true;
                        Game1.activeClickableMenu = null;
                        ModMonitor.Log("Masuk ke Mode Adjust Joypad.", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"Gagal memproses klik Options: {ex.Message}", LogLevel.Error);
            }
        }

        // --- UPDATE & INPUT HANDLING ---

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (isAdjusting)
            {
                HandleAdjustModeInput();
                return;
            }

            if (!Config.Enabled || !Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.eventUp || Game1.dialogueUp)
            {
                if (isDragging) ResetJoystick();
                return;
            }

            if (Config.Mode == ControlMode.JoypadOnly && Game1.player.controller != null)
            {
                Game1.player.controller = null;
            }

            if (Config.Mode == ControlMode.TapToMove)
            {
                if (isDragging) ResetJoystick();
                return;
            }

            HandleTouchInput();
            ApplyPlayerMovement();
        }

        private void HandleAdjustModeInput()
        {
            TouchCollection touchCollection = TouchPanel.GetState();
            float uiScale = Game1.options.uiScale;

            foreach (TouchLocation touch in touchCollection)
            {
                Vector2 touchPos = touch.Position / uiScale;
                Point pt = new Point((int)touchPos.X, (int)touchPos.Y);

                if (touch.State == TouchLocationState.Pressed)
                {
                    if (saveButtonRect.Contains(pt))
                    {
                        SaveAndExitAdjustment();
                        return;
                    }

                    if (sizePlusRect.Contains(pt))
                    {
                        Config.BaseRadius = Math.Min(250f, Config.BaseRadius + 10f);
                        Config.KnobRadius = Config.BaseRadius * 0.4f;
                        return;
                    }
                    if (sizeMinusRect.Contains(pt))
                    {
                        Config.BaseRadius = Math.Max(50f, Config.BaseRadius - 10f);
                        Config.KnobRadius = Config.BaseRadius * 0.4f;
                        return;
                    }

                    if (opacityPlusRect.Contains(pt))
                    {
                        Config.Opacity = Math.Min(1.0f, Config.Opacity + 0.1f);
                        return;
                    }
                    if (opacityMinusRect.Contains(pt))
                    {
                        Config.Opacity = Math.Max(0.1f, Config.Opacity - 0.1f);
                        return;
                    }

                    if (Vector2.Distance(touchPos, joystickCenter) <= Config.BaseRadius * 1.5f)
                    {
                        isDraggingInAdjust = true;
                    }
                }
                else if (touch.State == TouchLocationState.Moved && isDraggingInAdjust)
                {
                    joystickCenter = touchPos;
                    knobPosition = joystickCenter;

                    Config.OffsetX = joystickCenter.X;
                    Config.OffsetY = Game1.uiViewport.Height - joystickCenter.Y;
                }
                else if (touch.State == TouchLocationState.Released)
                {
                    isDraggingInAdjust = false;
                }
            }
        }

        private void SaveAndExitAdjustment()
        {
            isAdjusting = false;
            isDraggingInAdjust = false;
            ModHelper.WriteConfig(Config);
            RecalculatePosition();
            Game1.addHUDMessage(new HUDMessage("Pengaturan Joystick Disimpan!"));
        }

        private void HandleTouchInput()
        {
            TouchCollection touchCollection = TouchPanel.GetState();
            bool foundActiveTouch = false;

            float uiScale = Game1.options.uiScale;

            foreach (TouchLocation touch in touchCollection)
            {
                Vector2 touchPos = touch.Position / uiScale;

                if (touch.State == TouchLocationState.Pressed)
                {
                    if (Vector2.Distance(touchPos, joystickCenter) <= Config.BaseRadius * 1.2f)
                    {
                        isDragging = true;
                        activeTouchId = touch.Id;
                        foundActiveTouch = true;
                        UpdateKnobPosition(touchPos);
                        break;
                    }
                }
                else if (touch.State == TouchLocationState.Moved && touch.Id == activeTouchId)
                {
                    foundActiveTouch = true;
                    UpdateKnobPosition(touchPos);
                    break;
                }
                else if (touch.State == TouchLocationState.Released && touch.Id == activeTouchId)
                {
                    ResetJoystick();
                    return;
                }
            }

            if (isDragging && !foundActiveTouch)
            {
                ResetJoystick();
            }
        }

        private void UpdateKnobPosition(Vector2 touchPos)
        {
            Vector2 offset = touchPos - joystickCenter;
            float distance = offset.Length();

            if (distance > Config.BaseRadius)
            {
                offset = Vector2.Normalize(offset) * Config.BaseRadius;
            }

            knobPosition = joystickCenter + offset;

            float normalizedDistance = offset.Length() / Config.BaseRadius;
            if (normalizedDistance < Config.Deadzone)
            {
                inputVector = Vector2.Zero;
            }
            else
            {
                inputVector = Vector2.Normalize(offset) * ((normalizedDistance - Config.Deadzone) / (1f - Config.Deadzone));
            }

            if (Config.OverrideCinderTap && inputVector != Vector2.Zero && Game1.player.controller != null)
            {
                Game1.player.controller = null;
            }
        }

        private void ApplyPlayerMovement()
        {
            if (inputVector == Vector2.Zero || Game1.player == null) return;

            float speed = Game1.player.getMovementSpeed();
            Vector2 velocity = inputVector * speed;

            Game1.player.Halt();

            if (Math.Abs(inputVector.X) > Math.Abs(inputVector.Y))
            {
                if (inputVector.X > 0)
                {
                    Game1.player.SetMovingRight(true);
                    Game1.player.FacingDirection = Game1.right;
                }
                else
                {
                    Game1.player.SetMovingLeft(true);
                    Game1.player.FacingDirection = Game1.left;
                }
            }
            else
            {
                if (inputVector.Y > 0)
                {
                    Game1.player.SetMovingDown(true);
                    Game1.player.FacingDirection = Game1.down;
                }
                else
                {
                    Game1.player.SetMovingUp(true);
                    Game1.player.FacingDirection = Game1.up;
                }
            }

            Game1.player.Position += velocity;
        }

        private void ResetJoystick()
        {
            isDragging = false;
            activeTouchId = -1;
            knobPosition = joystickCenter;
            inputVector = Vector2.Zero;
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!Config.Enabled || circleTexture == null) return;

            SpriteBatch spriteBatch = e.SpriteBatch;

            if (isAdjusting)
            {
                spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.45f);

                Rectangle baseRectPreview = new Rectangle(
                    (int)(joystickCenter.X - Config.BaseRadius),
                    (int)(joystickCenter.Y - Config.BaseRadius),
                    (int)(Config.BaseRadius * 2),
                    (int)(Config.BaseRadius * 2)
                );
                spriteBatch.Draw(circleTexture, baseRectPreview, Color.Yellow * 0.6f);

                Rectangle knobRectPreview = new Rectangle(
                    (int)(knobPosition.X - Config.KnobRadius),
                    (int)(knobPosition.Y - Config.KnobRadius),
                    (int)(Config.KnobRadius * 2),
                    (int)(Config.KnobRadius * 2)
                );
                spriteBatch.Draw(circleTexture, knobRectPreview, Color.White * 0.9f);

                int screenW = Game1.uiViewport.Width;

                saveButtonRect = new Rectangle(screenW - 180, 20, 150, 60);
                IClickableMenu.drawTextureBox(spriteBatch, saveButtonRect.X, saveButtonRect.Y, saveButtonRect.Width, saveButtonRect.Height, Color.White);
                SpriteText.drawStringHorizontallyCenteredAt(spriteBatch, "OK / Save", saveButtonRect.Center.X, saveButtonRect.Center.Y - 12);

                sizeMinusRect = new Rectangle(20, 20, 60, 60);
                sizePlusRect = new Rectangle(90, 20, 60, 60);
                IClickableMenu.drawTextureBox(spriteBatch, sizeMinusRect.X, sizeMinusRect.Y, sizeMinusRect.Width, sizeMinusRect.Height, Color.White);
                IClickableMenu.drawTextureBox(spriteBatch, sizePlusRect.X, sizePlusRect.Y, sizePlusRect.Width, sizePlusRect.Height, Color.White);
                SpriteText.drawStringHorizontallyCenteredAt(spriteBatch, "-", sizeMinusRect.Center.X, sizeMinusRect.Center.Y - 12);
                SpriteText.drawStringHorizontallyCenteredAt(spriteBatch, "+", sizePlusRect.Center.X, sizePlusRect.Center.Y - 12);

                opacityMinusRect = new Rectangle(180, 20, 60, 60);
                opacityPlusRect = new Rectangle(250, 20, 60, 60);
                IClickableMenu.drawTextureBox(spriteBatch, opacityMinusRect.X, opacityMinusRect.Y, opacityMinusRect.Width, opacityMinusRect.Height, Color.White);
                IClickableMenu.drawTextureBox(spriteBatch, opacityPlusRect.X, opacityPlusRect.Y, opacityPlusRect.Width, opacityPlusRect.Height, Color.White);
                SpriteText.drawStringHorizontallyCenteredAt(spriteBatch, "O-", opacityMinusRect.Center.X, opacityMinusRect.Center.Y - 12);
                SpriteText.drawStringHorizontallyCenteredAt(spriteBatch, "O+", opacityPlusRect.Center.X, opacityPlusRect.Center.Y - 12);

                return;
            }

            if (Config.Mode == ControlMode.TapToMove || !Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.eventUp)
                return;

            Rectangle baseRect = new Rectangle(
                (int)(joystickCenter.X - Config.BaseRadius),
                (int)(joystickCenter.Y - Config.BaseRadius),
                (int)(Config.BaseRadius * 2),
                (int)(Config.BaseRadius * 2)
            );
            spriteBatch.Draw(circleTexture, baseRect, Color.White * Config.Opacity);

            Rectangle knobRect = new Rectangle(
                (int)(knobPosition.X - Config.KnobRadius),
                (int)(knobPosition.Y - Config.KnobRadius),
                (int)(Config.KnobRadius * 2),
                (int)(Config.KnobRadius * 2)
            );
            spriteBatch.Draw(circleTexture, knobRect, Color.White * (Config.Opacity + 0.35f));
        }

        private Texture2D CreateCircleTexture(int diameter)
        {
            Texture2D texture = new Texture2D(Game1.graphics.GraphicsDevice, diameter, diameter);
            Color[] colorData = new Color[diameter * diameter];

            float radius = diameter / 2f;
            float radiusSq = radius * radius;

            for (int x = 0; x < diameter; x++)
            {
                for (int y = 0; y < diameter; y++)
                {
                    int index = x + y * diameter;
                    Vector2 pos = new Vector2(x - radius, y - radius);

                    if (pos.LengthSquared() <= radiusSq)
                        colorData[index] = Color.White;
                    else
                        colorData[index] = Color.Transparent;
                }
            }

            texture.SetData(colorData);
            return texture;
        }
    }
}