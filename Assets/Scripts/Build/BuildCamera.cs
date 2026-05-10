using UnityEngine;
using UnityEngine.InputSystem;

namespace CubeFly.Build
{
    public class BuildCamera : MonoBehaviour
    {
        [SerializeField] Vector3 target = Vector3.zero;
        [SerializeField] float orbitSensitivity = 0.25f;
        [SerializeField] float zoomSensitivity = 0.01f;
        [SerializeField] float minDistance = 3f;
        [SerializeField] float maxDistance = 30f;
        [SerializeField] float startDistance = 5f;
        [SerializeField] float startElevation = 30f;
        [SerializeField] float startAzimuth = 30f;

        float _distance;
        float _azimuth;
        float _elevation;

        const string TAG = "BuildCamera";

        void Awake()
        {
            _distance = startDistance;
            _azimuth = startAzimuth;
            _elevation = startElevation;
            ApplyTransform();
            Debug.unityLogger.Log(TAG, $"BuildCamera initialised. Distance {_distance}, Elevation {_elevation}°");
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _azimuth += delta.x * orbitSensitivity;
                _elevation -= delta.y * orbitSensitivity;
                _elevation = Mathf.Clamp(_elevation, -80f, 80f);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _distance -= scroll * zoomSensitivity;
                _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            }

            ApplyTransform();
        }

        void ApplyTransform()
        {
            Quaternion rot = Quaternion.Euler(_elevation, _azimuth, 0f);
            transform.position = target + rot * new Vector3(0f, 0f, -_distance);
            transform.LookAt(target);
        }
    }
}
