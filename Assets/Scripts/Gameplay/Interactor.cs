using UnityEngine;

namespace Jagti.Gameplay
{
    /// <summary>Looks for a crate under the crosshair; hold E to search it.</summary>
    public class Interactor : MonoBehaviour
    {
        public Transform cameraPivot;
        public float reach = 2.8f;

        public LootCrate Target { get; private set; }
        public float Progress { get; private set; }   // 0..1 while holding E

        void Start()
        {
            if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
        }

        void Update()
        {
            Target = null;
            if (GameState.Paused || GameState.PlayerDead || cameraPivot == null)
            {
                Progress = 0f;
                return;
            }
            RaycastHit hit;
            if (Physics.Raycast(cameraPivot.position, cameraPivot.forward, out hit, reach, ~0, QueryTriggerInteraction.Ignore))
                Target = hit.collider.GetComponentInParent<LootCrate>();

            if (Target != null && !Target.searched && Input.GetKey(KeyCode.E))
            {
                if (Progress == 0f) NoiseEvents.Emit(transform.position, 6f); // rummaging is not silent
                Progress += Time.deltaTime / Mathf.Max(0.1f, Target.searchTime);
                if (Progress >= 1f)
                {
                    Target.Search(gameObject);
                    Progress = 0f;
                }
            }
            else Progress = 0f;
        }
    }
}
