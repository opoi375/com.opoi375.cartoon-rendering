

using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 相机深度模式的切换
    /// </summary>
    public class CameraDepthTextureMode : MonoBehaviour
    {
        [SerializeField] DepthTextureMode depthTextureMode;

        private void OnValidate()
        {
            SetCameraDepthTextureMode();
        }

        private void Awake()
        {
            SetCameraDepthTextureMode();
        }

        private void SetCameraDepthTextureMode()
        {
            GetComponent<Camera>().depthTextureMode = depthTextureMode;
        }
    }
}