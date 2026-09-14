using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Offset")]
    public float offsetX = 3f; //horizontal offset (more = more to the right)
    public float offsetY = 1f; //vertical offset (more = more to below)

    [Header("Smoothing")]
    public float smoothSpeed = 8f; //make the camera lerp (more = faster, less smooth)

    private Vector3 targetPos;

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        targetPos = new Vector3(target.position.x + offsetX, target.position.y + offsetY, transform.position.z);

        transform.position = Vector3.Lerp(transform.position, targetPos, smoothSpeed * Time.deltaTime);
    }

    //called when the world position is resetted, prevents lerping when resetting so cant see world pos resetted
    public void OnWorldShift(Vector3 shiftAmount)
    {
        transform.position -= shiftAmount;  //directly move the camera to the resetted position

        targetPos -= shiftAmount; //do the same for targetPos so wont lerp
    }
}