using UnityEngine;

/// <summary>
/// Camera em terceira pessoa que segue o Drone suavemente e nao atravessa paredes.
/// Coloque na Main Camera (solta na Hierarchy) e arraste o Drone para o campo Target.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Alvo")]
    public Transform target;

    [Header("Posicao")]
    public Vector3 offset = new Vector3(0f, 3f, -8f);   // x: lado, y: altura, z: distancia (negativo = atras)
    public float lookHeight = 1f;

    [Header("Suavizacao")]
    public float positionSmooth = 8f;
    public float rotationSmooth = 5f;

    [Header("Colisao da camera")]
    public bool avoidWalls = true;
    public LayerMask collisionMask = ~0;
    public float cameraRadius = 0.3f;
    public float minDistance = 1.5f;

    void LateUpdate()
    {
        if (target == null) return;

        Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Vector3 lookPoint = target.position + Vector3.up * lookHeight;
        Vector3 desiredPos = target.position + yaw * offset;

        if (avoidWalls)
        {
            Vector3 toCam = desiredPos - lookPoint;
            float dist = toCam.magnitude;
            if (dist > 0.01f)
            {
                Vector3 dir = toCam / dist;
                RaycastHit[] hits = Physics.SphereCastAll(lookPoint, cameraRadius, dir, dist,
                                                          collisionMask, QueryTriggerInteraction.Ignore);
                float best = dist;
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(target)) continue;
                    if (h.distance <= 0f) continue;
                    if (h.distance < best) best = h.distance;
                }
                if (best < dist)
                    desiredPos = lookPoint + dir * Mathf.Max(minDistance, best - 0.05f);
            }
        }

        float posT = 1f - Mathf.Exp(-positionSmooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPos, posT);

        Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        float rotT = 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotT);
    }
}
