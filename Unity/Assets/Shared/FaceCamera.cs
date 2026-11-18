using UnityEngine;

namespace RoomIsLying
{
    /// <summary>Turns a glow card toward the viewer (flames, halos). Cheap: one transform write per frame.</summary>
    public class FaceCamera : MonoBehaviour
    {
        Transform _cam;

        void LateUpdate()
        {
            if (_cam == null) { var c = Camera.main; if (c == null) return; _cam = c.transform; }
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.position, Vector3.up);
        }
    }
}
