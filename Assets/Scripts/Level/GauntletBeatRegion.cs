using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // A trigger volume over one beat's STARTING platform in The Gauntlet. Entering it tells
    // the run logger which beat the player is currently attempting - re-entering the same
    // region after falling to a recovery ledge counts as a fresh attempt at that beat.
    public class GauntletBeatRegion : MonoBehaviour
    {
        [Tooltip("1-5, matching the level document's beat numbering.")]
        public int beatIndex = 1;
        [Tooltip("The scene's run logger - wired by the setup script.")]
        public GauntletRunLogger logger;

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<KineticCubeController>() == null) return;
            logger?.ReportBeatEntered(beatIndex);
        }
    }
}
