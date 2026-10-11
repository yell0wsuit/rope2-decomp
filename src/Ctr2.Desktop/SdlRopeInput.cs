using System.Numerics;

using Ctr2.Engine.Physics;

using SDL3;

namespace Ctr2.Desktop
{
    /// <summary>Maps primary SDL mouse events into the pixel-space fitted rope input controller.</summary>
    /// <param name="input">Controller owning pending swipes and accepted replay records.</param>
    public sealed class SdlRopeInput(RopeInput input)
    {
        /// <summary>Handles an event from the preview window, canceling stale or unfocused drags.</summary>
        /// <param name="evt">SDL event belonging to the preview window.</param>
        /// <param name="windowWidth">Window coordinate width.</param>
        /// <param name="windowHeight">Window coordinate height.</param>
        /// <param name="pixelWidth">Drawable pixel width.</param>
        /// <param name="pixelHeight">Drawable pixel height.</param>
        public void Handle(SDL.Event evt, int windowWidth, int windowHeight, int pixelWidth, int pixelHeight)
        {
            SDL.EventType type = (SDL.EventType)evt.Type;
            if (type is SDL.EventType.WindowFocusLost or SDL.EventType.WindowMinimized or SDL.EventType.WindowResized or SDL.EventType.WindowPixelSizeChanged ||
                windowWidth <= 0 || windowHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
            {
                input.Cancel();
                return;
            }
            Vector2 scale = new((float)pixelWidth / windowWidth, (float)pixelHeight / windowHeight);
            switch (type)
            {
                case SDL.EventType.MouseButtonDown when evt.Button.Button == 1 && evt.Button.Which != uint.MaxValue:
                    input.Begin(new Vector2(evt.Button.X, evt.Button.Y) * scale, pixelWidth, pixelHeight);
                    break;
                case SDL.EventType.MouseMotion when evt.Motion.Which != uint.MaxValue:
                    input.Move(new Vector2(evt.Motion.X, evt.Motion.Y) * scale, pixelWidth, pixelHeight);
                    break;
                case SDL.EventType.MouseButtonUp when evt.Button.Button == 1 && evt.Button.Which != uint.MaxValue:
                    input.Move(new Vector2(evt.Button.X, evt.Button.Y) * scale, pixelWidth, pixelHeight);
                    input.End();
                    break;
                case SDL.EventType.First:
                    break;
                case SDL.EventType.Quit:
                    break;
                case SDL.EventType.Terminating:
                    break;
                case SDL.EventType.LowMemory:
                    break;
                case SDL.EventType.WillEnterBackground:
                    break;
                case SDL.EventType.DidEnterBackground:
                    break;
                case SDL.EventType.WillEnterForeground:
                    break;
                case SDL.EventType.DidEnterForeground:
                    break;
                case SDL.EventType.LocaleChanged:
                    break;
                case SDL.EventType.SystemThemeChanged:
                    break;
                case SDL.EventType.DisplayOrientation:
                    break;
                case SDL.EventType.DisplayAdded:
                    break;
                case SDL.EventType.DisplayRemoved:
                    break;
                case SDL.EventType.DisplayMoved:
                    break;
                case SDL.EventType.DisplayDesktopModeChanged:
                    break;
                case SDL.EventType.DisplayCurrentModeChanged:
                    break;
                case SDL.EventType.DisplayContentScaleChanged:
                    break;
                case SDL.EventType.UsableBoundsChanged:
                    break;
                case SDL.EventType.WindowShown:
                    break;
                case SDL.EventType.WindowHidden:
                    break;
                case SDL.EventType.WindowExposed:
                    break;
                case SDL.EventType.WindowMoved:
                    break;
                case SDL.EventType.WindowResized:
                    break;
                case SDL.EventType.WindowPixelSizeChanged:
                    break;
                case SDL.EventType.WindowMetalViewResized:
                    break;
                case SDL.EventType.WindowMinimized:
                    break;
                case SDL.EventType.WindowMaximized:
                    break;
                case SDL.EventType.WindowRestored:
                    break;
                case SDL.EventType.WindowMouseEnter:
                    break;
                case SDL.EventType.WindowMouseLeave:
                    break;
                case SDL.EventType.WindowFocusGained:
                    break;
                case SDL.EventType.WindowFocusLost:
                    break;
                case SDL.EventType.WindowCloseRequested:
                    break;
                case SDL.EventType.WindowHitTest:
                    break;
                case SDL.EventType.WindowICCProfChanged:
                    break;
                case SDL.EventType.WindowDisplayChanged:
                    break;
                case SDL.EventType.WindowDisplayScaleChanged:
                    break;
                case SDL.EventType.WindowSafeAreaChanged:
                    break;
                case SDL.EventType.WindowOccluded:
                    break;
                case SDL.EventType.WindowEnterFullscreen:
                    break;
                case SDL.EventType.WindowLeaveFullscreen:
                    break;
                case SDL.EventType.WindowDestroyed:
                    break;
                case SDL.EventType.WindowHDRStateChanged:
                    break;
                case SDL.EventType.WindowSettingsChanged:
                    break;
                case SDL.EventType.KeyDown:
                    break;
                case SDL.EventType.KeyUp:
                    break;
                case SDL.EventType.TextEditing:
                    break;
                case SDL.EventType.TextInput:
                    break;
                case SDL.EventType.KeymapChanged:
                    break;
                case SDL.EventType.KeyboardAdded:
                    break;
                case SDL.EventType.KeyboardRemoved:
                    break;
                case SDL.EventType.TextEditingCandidates:
                    break;
                case SDL.EventType.ScreenKeyboardShown:
                    break;
                case SDL.EventType.ScreenKeyboardHidden:
                    break;
                case SDL.EventType.MouseMotion:
                    break;
                case SDL.EventType.MouseButtonDown:
                    break;
                case SDL.EventType.MouseButtonUp:
                    break;
                case SDL.EventType.MouseWheel:
                    break;
                case SDL.EventType.MouseAdded:
                    break;
                case SDL.EventType.MouseRemoved:
                    break;
                case SDL.EventType.JoystickAxisMotion:
                    break;
                case SDL.EventType.JoystickBallMotion:
                    break;
                case SDL.EventType.JoystickHatMotion:
                    break;
                case SDL.EventType.JoystickButtonDown:
                    break;
                case SDL.EventType.JoystickButtonUp:
                    break;
                case SDL.EventType.JoystickAdded:
                    break;
                case SDL.EventType.JoystickRemoved:
                    break;
                case SDL.EventType.JoystickBatteryUpdated:
                    break;
                case SDL.EventType.JoystickUpdateComplete:
                    break;
                case SDL.EventType.GamepadAxisMotion:
                    break;
                case SDL.EventType.GamepadButtonDown:
                    break;
                case SDL.EventType.GamepadButtonUp:
                    break;
                case SDL.EventType.GamepadAdded:
                    break;
                case SDL.EventType.GamepadRemoved:
                    break;
                case SDL.EventType.GamepadRemapped:
                    break;
                case SDL.EventType.GamepadTouchpadDown:
                    break;
                case SDL.EventType.GamepadTouchpadMotion:
                    break;
                case SDL.EventType.GamepadTouchpadUp:
                    break;
                case SDL.EventType.GamepadSensorUpdate:
                    break;
                case SDL.EventType.GamepadUpdateComplete:
                    break;
                case SDL.EventType.GamepadSteamHandleUpdated:
                    break;
                case SDL.EventType.FingerDown:
                    break;
                case SDL.EventType.FingerUp:
                    break;
                case SDL.EventType.FingerMotion:
                    break;
                case SDL.EventType.FingerCanceled:
                    break;
                case SDL.EventType.PinchBegin:
                    break;
                case SDL.EventType.PinchUpdate:
                    break;
                case SDL.EventType.PinchEnd:
                    break;
                case SDL.EventType.ClipboardUpdate:
                    break;
                case SDL.EventType.DropFile:
                    break;
                case SDL.EventType.DropText:
                    break;
                case SDL.EventType.DropBegin:
                    break;
                case SDL.EventType.DropComplete:
                    break;
                case SDL.EventType.DropPosition:
                    break;
                case SDL.EventType.AudioDeviceAdded:
                    break;
                case SDL.EventType.AudioDeviceRemoved:
                    break;
                case SDL.EventType.AudioDeviceFormatChanged:
                    break;
                case SDL.EventType.SensorUpdate:
                    break;
                case SDL.EventType.PenProximityIn:
                    break;
                case SDL.EventType.PenProximityOut:
                    break;
                case SDL.EventType.PenDown:
                    break;
                case SDL.EventType.PenUp:
                    break;
                case SDL.EventType.PenButtonDown:
                    break;
                case SDL.EventType.PenButtonUp:
                    break;
                case SDL.EventType.PenMotion:
                    break;
                case SDL.EventType.PenAxis:
                    break;
                case SDL.EventType.CameraDeviceAdded:
                    break;
                case SDL.EventType.CameraDeviceRemoved:
                    break;
                case SDL.EventType.CameraDeviceApproved:
                    break;
                case SDL.EventType.CameraDeviceDenied:
                    break;
                case SDL.EventType.RenderTargetsReset:
                    break;
                case SDL.EventType.RenderDeviceReset:
                    break;
                case SDL.EventType.RenderDeviceLost:
                    break;
                case SDL.EventType.Private0:
                    break;
                case SDL.EventType.Private1:
                    break;
                case SDL.EventType.Private2:
                    break;
                case SDL.EventType.Private3:
                    break;
                case SDL.EventType.PollSentinel:
                    break;
                case SDL.EventType.User:
                    break;
                case SDL.EventType.Last:
                    break;
                case SDL.EventType.EnumPadding:
                    break;
                default:
                    break;
            }
        }
    }
}
