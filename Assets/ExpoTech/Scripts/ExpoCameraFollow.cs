using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ExpoTech
{
    /// <summary>
    /// Câmera de demonstração que mantém o drone sempre à vista.
    /// - Seguir: atrás e acima do drone (ignora o balanço do corpo).
    /// - Topo: vista de cima.
    /// Se uma parede ficar entre a câmera e o drone, testa posições mais altas e próximas até enxergá-lo.
    /// Tecla C (ou botão no painel) troca o modo.
    /// </summary>
    public class ExpoCameraFollow : MonoBehaviour
    {
        public enum Mode { Seguir, Topo }

        public Transform target;
        public Mode mode = Mode.Seguir;
        public float distance = 6f;
        public float height = 3f;
        public float topHeight = 12f;
        public float lookHeight = 0.6f;
        public float smooth = 4f;
        [Tooltip("Raio usado para checar se algo bloqueia a vista")] public float probeRadius = 0.3f;
        public LayerMask obstacleMask = ~0;

        Transform robotRoot;
        bool snapped;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Start()
        {
            if (target != null) robotRoot = target.root;
            Snap();
        }

        /// <summary>Vai direto para a posição ideal (sem suavizar).</summary>
        public void Snap()
        {
            if (target == null) return;
            transform.position = DesiredPosition();
            transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position);
            snapped = true;
        }

        public void ToggleMode() => mode = mode == Mode.Seguir ? Mode.Topo : Mode.Seguir;

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null && k.cKey.wasPressedThisFrame) ToggleMode();
#else
            if (Input.GetKeyDown(KeyCode.C)) ToggleMode();
#endif
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (!snapped) Snap();
            float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, DesiredPosition(), k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(LookPoint() - transform.position), k);
        }

        Vector3 LookPoint() => target.position + Vector3.up * lookHeight;

        Vector3 DesiredPosition()
        {
            Vector3 fwd = target.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            if (mode == Mode.Topo)
                return Visible(target.position - fwd * 1.5f + Vector3.up * topHeight);

            // Candidatas, da preferida (atrás) até quase em cima: a primeira sem obstáculo vence.
            Vector3[] candidates =
            {
                target.position - fwd * distance + Vector3.up * height,
                target.position - fwd * distance * 0.6f + Vector3.up * height * 1.6f,
                target.position - fwd * distance * 0.3f + Vector3.up * height * 2.4f,
                target.position - fwd * 1f + Vector3.up * topHeight * 0.8f,
            };
            foreach (var c in candidates)
                if (!Blocked(c)) return c;
            return Visible(candidates[candidates.Length - 1]);
        }

        /// <summary>Se ainda houver bloqueio, aproxima a câmera até logo antes do obstáculo.</summary>
        Vector3 Visible(Vector3 desired)
        {
            Vector3 from = LookPoint();
            Vector3 dir = desired - from;
            float dist = dir.magnitude;
            float nearest = dist;
            int n = Physics.SphereCastNonAlloc(from, probeRadius, dir / dist, hits, dist, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!IsRobot(hits[i].collider) && hits[i].distance < nearest) nearest = hits[i].distance;
            return from + dir / dist * Mathf.Max(1.5f, nearest - 0.2f);
        }

        bool Blocked(Vector3 cameraPos)
        {
            Vector3 from = LookPoint();
            Vector3 dir = cameraPos - from;
            float dist = dir.magnitude;
            int n = Physics.SphereCastNonAlloc(from, probeRadius, dir / dist, hits, dist, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!IsRobot(hits[i].collider)) return true;
            return false;
        }

        bool IsRobot(Collider c) => robotRoot != null && c.transform.IsChildOf(robotRoot);
    }
}
