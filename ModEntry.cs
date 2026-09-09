using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
​namespace CinderJoystick
{
public class ModEntry : Mod
{
public static ModConfig Config { get; private set; } = new ModConfig();
public static IModHelper ModHelper { get; private set; } = null!;
public static IMonitor ModMonitor { get; private set; } = null!;
​public const int CONTROL_STYLE_CUSTOM_ID = 987654;
​private Vector2 joystickCenter;
private Vector2 knobPosition;
private bool isDragging = false;
private int activeTouchId = -1;
private Vector2 inputVector = Vector2.Zero;
​private Texture2D? circleTexture;
​public override void Entry(IModHelper helper)
{
Config = helper.ReadConfig<ModConfig>();
ModHelper = helper;
ModMonitor = Monitor;
​// Harmony Patch untuk menyuntikkan menu Control Style native
var harmony = new Harmony(ModManifest.UniqueID);
​harmony.Patch(
original: AccessTools.Constructor(typeof(OptionsPage), new[] { typeof(int), typeof(int), typeof(int), typeof(int) }),
postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionsPageConstructorPostfix))
);
​harmony.Patch(
original: AccessTools.Method(typeof(OptionsPage), nameof(OptionsPage.optionButtonClick)),
postfix: new HarmonyMethod(typeof(ModEntry), nameof(OnOptionButtonClickPostfix))
);
​helper.Events.GameLoop.GameLaunched += OnGameLaunched;
helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
helper.Events.Display.RenderedHud += OnRenderedHud;
helper.Events.Display.WindowResized += OnWindowResized;
​RecalculatePosition();
}
​private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
{
circleTexture = CreateCircleTexture(128);
RecalculatePosition();
}
​private void OnWindowResized(object? sender, WindowResizedEventArgs e)
{
RecalculatePosition();
}
​private void RecalculatePosition()
{
float screenHeight = Game1.uiViewport.Height;
joystickCenter = new Vector2(Config.OffsetX, screenHeight - Config.OffsetY);
knobPosition = joystickCenter;
}
​// --- HARMONY PATCHES UNTUK MENU CONTROL STYLE NATIVE --- //
​public static void OnOptionsPageConstructorPostfix(OptionsPage __instance)
{
try
{
OptionsDropDown? controlStyleDropDown = null;
​foreach (var element in __instance.options)
{
if (element is OptionsDropDown dropDown &&
(element.label.Equals("Control Style", StringComparison.OrdinalIgnoreCase) || element.whichOption == 52))
{
controlStyleDropDown = dropDown;
break;
}
}
​if (controlStyleDropDown != null)
{
controlStyleDropDown.whichOption = CONTROL_STYLE_CUSTOM_ID;
controlStyleDropDown.dropDownOptions.Clear();
controlStyleDropDown.dropDownDisplayOptions.Clear();
​controlStyleDropDown.dropDownOptions.Add("JoypadOnly");
controlStyleDropDown.dropDownDisplayOptions.Add("Joypad");
​controlStyleDropDown.dropDownOptions.Add("TapToMove");
controlStyleDropDown.dropDownDisplayOptions.Add("Tap to Move");
​controlStyleDropDown.dropDownOptions.Add("Hybrid");
controlStyleDropDown.dropDownDisplayOptions.Add("Joypad + Tap to Move");
​controlStyleDropDown.selectedOption = Config.Mode switch
{
ControlMode.JoypadOnly => 0,
ControlMode.TapToMove => 1,
ControlMode.Hybrid => 2,
_ => 2
};
}
else
{
var newDropDown = new OptionsDropDown("Control Style", CONTROL_STYLE_CUSTOM_ID);
newDropDown.dropDownOptions.Add("JoypadOnly");
newDropDown.dropDownDisplayOptions.Add("Joypad");
​newDropDown.dropDownOptions.Add("TapToMove");
newDropDown.dropDownDisplayOptions.Add("Tap to Move");
​newDropDown.dropDownOptions.Add("Hybrid");
newDropDown.dropDownDisplayOptions.Add("Joypad + Tap to Move");
​newDropDown.selectedOption = Config.Mode switch
{
ControlMode.JoypadOnly => 0,
ControlMode.TapToMove => 1,
ControlMode.Hybrid => 2,
_ => 2
};
​__instance.options.Add(newDropDown);
}
}
catch (Exception ex)
{
ModMonitor.Log($"Gagal menyuntikkan menu Control Style: {ex.Message}", LogLevel.Error);
}
}
​public static void OnOptionButtonClickPostfix(int whichOption, int selectedResult)
{
if (whichOption == CONTROL_STYLE_CUSTOM_ID)
{
Config.Mode = selectedResult switch
{
0 => ControlMode.JoypadOnly,
1 => ControlMode.TapToMove,
2 => ControlMode.Hybrid,
_ => ControlMode.Hybrid
};
​ModHelper.WriteConfig(Config);
ModMonitor.Log($"Skema Kontrol diubah ke: {Config.Mode}", LogLevel.Info);
}
}
​// --- UPDATE & INPUT HANDLING --- //
​private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
{
if (!Config.Enabled || !Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.eventUp || Game1.dialogueUp)
{
if (isDragging) ResetJoystick();
return;
}
​if (Config.Mode == ControlMode.JoypadOnly && Game1.player.controller != null)
{
Game1.player.controller = null;
}
​if (Config.Mode == ControlMode.TapToMove)
{
if (isDragging) ResetJoystick();
return;
}
​HandleTouchInput();
ApplyPlayerMovement();
}
​private void HandleTouchInput()
{
TouchCollection touchCollection = TouchPanel.GetState();
bool foundActiveTouch = false;
​float uiScale = Game1.options.uiScale;
​foreach (TouchLocation touch in touchCollection)
{
Vector2 touchPos = touch.Position / uiScale;
​if (touch.State == TouchLocationState.Pressed)
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
else if ((touch.State == TouchLocationState.Moved || touch.State == TouchLocationState.StateThatDoesntExist) && touch.Id == activeTouchId)
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
​if (isDragging && !foundActiveTouch)
{
ResetJoystick();
}
}
​private void UpdateKnobPosition(Vector2 touchPos)
{
Vector2 offset = touchPos - joystickCenter;
float distance = offset.Length();
​if (distance > Config.BaseRadius)
{
offset = Vector2.Normalize(offset) * Config.BaseRadius;
}
​knobPosition = joystickCenter + offset;
​float normalizedDistance = offset.Length() / Config.BaseRadius;
if (normalizedDistance < Config.Deadzone)
{
inputVector = Vector2.Zero;
}
else
{
inputVector = Vector2.Normalize(offset) * ((normalizedDistance - Config.Deadzone) / (1f - Config.Deadzone));
}
​if (Config.OverrideCinderTap && inputVector != Vector2.Zero && Game1.player.controller != null)
{
Game1.player.controller = null;
}
}
​private void ApplyPlayerMovement()
{
if (inputVector == Vector2.Zero || Game1.player == null) return;
​float speed = Game1.player.getMovementSpeed();
Vector2 velocity = inputVector * speed;
​Game1.player.SetMoving((byte)0);
​if (Math.Abs(inputVector.X) > Math.Abs(inputVector.Y))
{
if (inputVector.X > 0) Game1.player.SetMoving(Game1.SetMovingRight);
else Game1.player.SetMoving(Game1.SetMovingLeft);
}
else
{
if (inputVector.Y > 0) Game1.player.SetMoving(Game1.SetMovingDown);
else Game1.player.SetMoving(Game1.SetMovingUp);
}
​Game1.player.Position += velocity;
}
​private void ResetJoystick()
{
isDragging = false;
activeTouchId = -1;
knobPosition = joystickCenter;
inputVector = Vector2.Zero;
}
​private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
{
if (!Config.Enabled || Config.Mode == ControlMode.TapToMove || !Context.IsWorldReady || circleTexture == null || Game1.activeClickableMenu != null || Game1.eventUp)
return;
​SpriteBatch spriteBatch = e.SpriteBatch;
​Rectangle baseRect = new Rectangle(
(int)(joystickCenter.X - Config.BaseRadius),
(int)(joystickCenter.Y - Config.BaseRadius),
(int)(Config.BaseRadius * 2),
(int)(Config.BaseRadius * 2)
);
spriteBatch.Draw(circleTexture, baseRect, Color.White * Config.Opacity);
​Rectangle knobRect = new Rectangle(
(int)(knobPosition.X - Config.KnobRadius),
(int)(knobPosition.Y - Config.KnobRadius),
(int)(Config.KnobRadius * 2),
(int)(Config.KnobRadius * 2)
);
spriteBatch.Draw(circleTexture, knobRect, Color.White * (Config.Opacity + 0.35f));
}
​private Texture2D CreateCircleTexture(int diameter)
{
Texture2D texture = new Texture2D(Game1.graphics.GraphicsDevice, diameter, diameter);
Color[] colorData = new Color[diameter * diameter];
​float radius = diameter / 2f;
float radiusSq = radius * radius;
​for (int x = 0; x < diameter; x++)
{
for (int y = 0; y < diameter; y++)
{
int index = x + y * diameter;
Vector2 pos = new Vector2(x - radius, y - radius);
​if (pos.LengthSquared() <= radiusSq)
colorData[index] = Color.White;
else
colorData[index] = Color.Transparent;
}
}
​texture.SetData(colorData);
return texture;
}
}
}