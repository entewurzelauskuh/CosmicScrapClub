// Hand-rolled wrapper that mirrors the Generate-C#-Class output Unity would
// produce from CubeFlyInputActions.inputactions. Defining the bindings in code
// avoids a hard dependency on the editor's wrapper-generation pass running
// before first compile.

using System;
using UnityEngine.InputSystem;

namespace CubeFly.Input
{
    public class CubeFlyInputActions : IDisposable
    {
        readonly InputActionMap _buildMap;
        readonly InputActionMap _flyMap;

        public BuildActions Build { get; }
        public FlyActions Fly { get; }

        public CubeFlyInputActions()
        {
            // ---------- Build map (BuildScene mouse + keyboard) ----------
            _buildMap = new InputActionMap("Build");
            // LMB places when the Place tool is active and deletes when the
            // Delete tool is active; the toolbar selects the tool. RMB no
            // longer carries any build-mode meaning.
            InputAction place    = _buildMap.AddAction("Place",    InputActionType.Button, "<Mouse>/leftButton");
            // R rotates the current placement by 90° around Z; T does X.
            InputAction rotateZ  = _buildMap.AddAction("RotateZ",  InputActionType.Button, "<Keyboard>/r");
            InputAction rotateX  = _buildMap.AddAction("RotateX",  InputActionType.Button, "<Keyboard>/t");
            Build = new BuildActions(_buildMap, place, rotateZ, rotateX);

            // ---------- Fly map (FlyScene keyboard + mouse-look camera) ----------
            _flyMap = new InputActionMap("Fly");

            // Thrust: 3D directional composite.
            //   W = forward (+Z local),  S = backward (-Z)
            //   D = side right (+X),     A = side left  (-X)
            //   Space = up (+Y),         C = down       (-Y)
            InputAction thrust = _flyMap.AddAction("Thrust", InputActionType.Value);
            thrust.expectedControlType = "Vector3";
            thrust.AddCompositeBinding("3DVector")
                .With("Up",       "<Keyboard>/space")
                .With("Down",     "<Keyboard>/c")
                .With("Left",     "<Keyboard>/a")
                .With("Right",    "<Keyboard>/d")
                .With("Forward",  "<Keyboard>/w")
                .With("Backward", "<Keyboard>/s");

            // Pitch: nose up/down via Up/Down arrows (returns -1 / +1).
            InputAction pitch = _flyMap.AddAction("Pitch", InputActionType.Value);
            pitch.expectedControlType = "Axis";
            pitch.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/downArrow")
                .With("Positive", "<Keyboard>/upArrow");

            // Yaw: turn left/right via Left/Right arrows.
            InputAction yaw = _flyMap.AddAction("Yaw", InputActionType.Value);
            yaw.expectedControlType = "Axis";
            yaw.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");

            // Roll: Q = anti-clockwise (negative axis), E = clockwise.
            InputAction roll = _flyMap.AddAction("Roll", InputActionType.Value);
            roll.expectedControlType = "Axis";
            roll.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q")
                .With("Positive", "<Keyboard>/e");

            // Look: raw mouse delta. The camera reads this only while
            // LookHeld is pressed (free-look). The construct does not.
            InputAction look = _flyMap.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            look.expectedControlType = "Vector2";

            // LookHeld: right mouse button. Gates free-look; on release the
            // camera snaps back to the default behind-the-construct pose.
            InputAction lookHeld = _flyMap.AddAction("LookHeld", InputActionType.Button, "<Mouse>/rightButton");

            // Fire: LMB. Held-down semantics — FlyShootingController polls
            // IsPressed() each frame; per-weapon reload throttles the rate.
            InputAction fire = _flyMap.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");

            Fly = new FlyActions(_flyMap, thrust, pitch, yaw, roll, look, lookHeld, fire);
        }

        public void Dispose()
        {
            _buildMap?.Disable();
            _flyMap?.Disable();
        }

        public readonly struct BuildActions
        {
            readonly InputActionMap _map;
            public InputAction Place    { get; }
            public InputAction RotateZ  { get; }
            public InputAction RotateX  { get; }

            public BuildActions(InputActionMap map,
                InputAction place, InputAction rotateZ, InputAction rotateX)
            {
                _map = map;
                Place    = place;
                RotateZ  = rotateZ;
                RotateX  = rotateX;
            }

            public void Enable() => _map.Enable();
            public void Disable() => _map.Disable();
        }

        public readonly struct FlyActions
        {
            readonly InputActionMap _map;
            public InputAction Thrust   { get; }
            public InputAction Pitch    { get; }
            public InputAction Yaw      { get; }
            public InputAction Roll     { get; }
            public InputAction Look     { get; }
            public InputAction LookHeld { get; }
            public InputAction Fire     { get; }

            public FlyActions(InputActionMap map,
                InputAction thrust, InputAction pitch, InputAction yaw,
                InputAction roll, InputAction look, InputAction lookHeld,
                InputAction fire)
            {
                _map     = map;
                Thrust   = thrust;
                Pitch    = pitch;
                Yaw      = yaw;
                Roll     = roll;
                Look     = look;
                LookHeld = lookHeld;
                Fire     = fire;
            }

            public void Enable() => _map.Enable();
            public void Disable() => _map.Disable();
        }
    }
}
