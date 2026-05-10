using CubeFly.Core;
using CubeFly.Input;
using UnityEngine;

namespace CubeFly.Fly
{
    public class FlyCamera : MonoBehaviour
    {
        [SerializeField] Transform construct;
        [SerializeField] float followSpeed = 5f;
        [SerializeField] float lookHeightOffset = 0.5f;
        [SerializeField] float baseDistance = 5f;
        [SerializeField] float minDistance = 5f;
        [SerializeField] float maxDistance = 50f;
        [SerializeField] float mouseSensitivity = 0.2f;
        [SerializeField] float minPitch = -60f;
        [SerializeField] float maxPitch = 60f;
        [SerializeField] float snapBackSpeed = 8f;

        // Local-space offset baseline (behind/above the construct), computed
        // once from bounds. The camera then gets construct.TransformPoint(...)
        // applied each frame, so it follows the construct's full orientation —
        // pitch, yaw, AND roll. Mouse-look layers an additional yaw/pitch on
        // top, but only while LookHeld (right mouse) is pressed.
        Vector3 _baseOffset;
        float _yawOffset;
        float _pitchOffset;
        bool _cursorLocked;

        CubeFlyInputActions _input;

        const string TAG = "FlyCamera";

        void Awake()
        {
            _input = new CubeFlyInputActions();
        }

        void OnEnable() => _input.Fly.Enable();
        void OnDisable()
        {
            _input.Fly.Disable();
            ReleaseCursor();
        }
        void OnDestroy() => _input?.Dispose();

        void Start()
        {
            Bounds bounds = GameData.GetConstructBounds();
            float diagonalSize = bounds.extents.magnitude;
            float dist = Mathf.Clamp(baseDistance + diagonalSize * 1.5f, minDistance, maxDistance);
            _baseOffset = new Vector3(0f, dist * 0.4f, -dist);

            if (construct != null)
            {
                transform.position = construct.TransformPoint(_baseOffset);
                Vector3 lookTarget = construct.position + construct.up * lookHeightOffset;
                transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, construct.up);
            }

            Debug.unityLogger.Log(TAG,
                $"FlyCamera initialised. Construct bounds diagonal: {diagonalSize:F2}. " +
                $"Computed distance: {dist:F2}. Offset: {_baseOffset}");
        }

        void Update()
        {
            // While paused, behave as if RMB is released: free the
            // cursor (so the player can click the menu buttons),
            // ignore mouse delta, and let the offset snap back to
            // neutral over real time. Time.timeScale = 0 already
            // freezes the LateUpdate Lerps via Time.deltaTime, so the
            // camera body doesn't follow during the pause anyway.
            bool paused = PauseMenu.Instance != null && PauseMenu.Instance.IsOpen;
            bool lookHeld = !paused && _input.Fly.LookHeld.IsPressed();

            if (lookHeld)
            {
                if (!_cursorLocked) LockCursor();
                Vector2 lookInput = _input.Fly.Look.ReadValue<Vector2>();
                _yawOffset   += lookInput.x * mouseSensitivity;
                _pitchOffset -= lookInput.y * mouseSensitivity;
                _pitchOffset  = Mathf.Clamp(_pitchOffset, minPitch, maxPitch);
            }
            else
            {
                if (_cursorLocked) ReleaseCursor();
                // Snap back to neutral so the camera returns to behind the
                // construct. Frame-rate-independent exponential approach.
                float k = 1f - Mathf.Exp(-snapBackSpeed * Time.deltaTime);
                _yawOffset   = Mathf.Lerp(_yawOffset, 0f, k);
                _pitchOffset = Mathf.Lerp(_pitchOffset, 0f, k);
            }
        }

        void LateUpdate()
        {
            if (construct == null) return;

            // Free-look: rotate the local offset by the accumulated mouse
            // angles, then sample in the construct's local frame so the
            // baseline pose follows pitch/yaw/roll of the ship.
            Quaternion orbit = Quaternion.Euler(_pitchOffset, _yawOffset, 0f);
            Vector3 rotatedLocalOffset = orbit * _baseOffset;
            Vector3 desired = construct.TransformPoint(rotatedLocalOffset);
            transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.deltaTime);

            // Look at a point above the construct in its OWN frame, and use
            // the construct's up vector — this rolls the camera with the ship
            // so the static-stuck feel holds during banking.
            Vector3 lookTarget = construct.position + construct.up * lookHeightOffset;
            Quaternion targetRot = Quaternion.LookRotation(lookTarget - transform.position, construct.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, followSpeed * Time.deltaTime);
        }

        void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _cursorLocked = true;
        }

        void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorLocked = false;
        }
    }
}
