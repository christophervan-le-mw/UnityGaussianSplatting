using System.Linq;
using UnityEngine;


namespace GaussianSplatting.Runtime
{
    public class CameraVisualizer : MonoBehaviour
    {
        public GaussianSplatRenderAsset renderAsset;
        public GameObject cameraModelPrefab; // Reference to the FBX model prefab

        private GaussianSplatRenderAsset.CameraInfo[] _previousCameras;

        public void Update()
        {
            if(renderAsset.cameras == null)
            {
                return;
            }
            // Check if cameras are null or have changed
            if (_previousCameras != null &&  AreCamerasEqual(_previousCameras, renderAsset.cameras))
            {
                return;
            }

            // Update previous cameras
            _previousCameras = (GaussianSplatRenderAsset.CameraInfo[])renderAsset.cameras.Clone();

            // Remove existing camera objects
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            // Instantiate new camera objects
            foreach (var camInfo in renderAsset.cameras)
            {
                GameObject camObj = Instantiate(cameraModelPrefab, transform);
                camObj.name = "VisualizedCamera";

                camObj.transform.position = camInfo.pos;
                camObj.transform.rotation = Quaternion.LookRotation(camInfo.axisZ, camInfo.axisY);
            }
        }

        private static bool AreCamerasEqual(GaussianSplatRenderAsset.CameraInfo[] cams1, GaussianSplatRenderAsset.CameraInfo[] cams2)
        {
            if (cams1.Length != cams2.Length)
            {
                return false;
            }

            return !cams1.Where((t, i) => !t.Equals(cams2[i])).Any();
        }
    }
}