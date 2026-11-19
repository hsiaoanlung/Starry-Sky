// Simple orbit camera: right/left mouse drag = rotate, scroll = zoom, auto-rotate when idle.
using UnityEngine;

namespace Nyx
{
    public class NyxOrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 18f;
        public float minDistance = 0.5f;
        public float maxDistance = 100f;
        public float rotateSpeed = 4f;
        public float zoomSpeed = 2f;
        [Tooltip("Degrees per second while the mouse is not dragging")]
        public float autoRotate = 6f;

        float _yaw = 30f, _pitch = 20f;

        void LateUpdate()
        {
            Vector3 center = target ? target.position : Vector3.zero;
            bool dragging = NyxInput.Dragging;
            if (dragging)
            {
                Vector2 d = NyxInput.MouseDelta;
                _yaw += d.x * rotateSpeed;
                _pitch -= d.y * rotateSpeed;
            }
            distance *= 1f - NyxInput.Scroll * zoomSpeed * 0.05f;
            if (!dragging) _yaw += autoRotate * Time.deltaTime;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(center - rot * Vector3.forward * distance, rot);
        }
    }
}
