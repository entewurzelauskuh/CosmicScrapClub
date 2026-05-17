using UnityEngine;
using UnityEngine.InputSystem;

namespace CubeFly.Desert
{
    /// <summary>
    /// Minimal play-mode free-fly camera for evaluating the desert level.
    /// Hold right mouse to look; WASD to move; E/Q for up/down; Shift to boost.
    /// </summary>
    public class FreeFlyCamera : MonoBehaviour
    {
        public float moveSpeed = 35f;
        public float boostMultiplier = 3f;
        public float lookSensitivity = 0.12f;

        float _yaw;
        float _pitch;

        void Start()
        {
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
                return;

            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 local = Vector3.zero;
            if (kb.wKey.isPressed) local += Vector3.forward;
            if (kb.sKey.isPressed) local += Vector3.back;
            if (kb.aKey.isPressed) local += Vector3.left;
            if (kb.dKey.isPressed) local += Vector3.right;

            Vector3 world = Vector3.zero;
            if (kb.eKey.isPressed) world += Vector3.up;
            if (kb.qKey.isPressed) world += Vector3.down;

            float speed = moveSpeed * (kb.leftShiftKey.isPressed ? boostMultiplier : 1f);
            Vector3 move = transform.TransformDirection(local.normalized) + world.normalized;
            transform.position += move * speed * Time.deltaTime;
        }
    }
}
