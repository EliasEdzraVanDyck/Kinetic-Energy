using System;
using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{
    // The element-test level's section index. Each section introduces ONE kind of
    // platform, obstacle or enemy; the pause menu's Sections screen jumps straight to any
    // of them so a single element can be played over and over without replaying the run.
    //
    // Jumping does not reload the scene - it teleports the player and REPOINTS every
    // hazard's respawn at that section, so dying keeps you where you were testing.
    public class LevelSectionController : MonoBehaviour
    {
        [Serializable]
        public class Section
        {
            [Tooltip("Shown on the pause menu's Sections screen.")]
            public string label = "Section";
            [Tooltip("Where the player lands when this section is selected (and respawns while testing it).")]
            public Transform spawnPoint;
        }

        public Section[] sections = Array.Empty<Section>();

        [Tooltip("Hazards whose respawn point follows the selected section - the damage floor, laser gates, anything that sends you back.")]
        public DamageWalls[] hazards = Array.Empty<DamageWalls>();

        [Tooltip("The section the level starts on.")]
        public int startingSection = 0;

        public int CurrentSection { get; private set; }

        KineticCubeController controller;

        void Start()
        {
            controller = FindAnyObjectByType<KineticCubeController>();
            CurrentSection = Mathf.Clamp(startingSection, 0, Mathf.Max(sections.Length - 1, 0));
            // The starting section still repoints the hazards, so a first-section death
            // does not fall back to whatever the prefab happened to carry.
            PointHazardsAt(CurrentSection);
            ResetCheckpoints();

            // An attack that empties the tank sends the player back to their checkpoint -
            // with nothing left to launch with there is no way to recover in place.
            if (controller != null) controller.EnergyEmptiedByHit += RespawnAtCheckpoint;
        }

        void OnDestroy()
        {
            if (controller != null) controller.EnergyEmptiedByHit -= RespawnAtCheckpoint;
        }

        public void RespawnAtCheckpoint()
        {
            Transform target = ActiveRespawn;
            if (target == null && sections != null && sections.Length > 0) target = sections[0].spawnPoint;
            if (target == null || controller == null) return;

            controller.RespawnAtPoint(target.position);
            ResetLevelState();
            ResetCheckpoints();
        }

        void OnEnable()
        {
            DamageWalls.PlayerRespawned += ResetCheckpoints;
        }

        void OnDisable()
        {
            DamageWalls.PlayerRespawned -= ResetCheckpoints;
        }

        // Every pad comes back for the retry EXCEPT the one you are respawning at - that
        // one is already yours, so it stays claimed and stays hidden. Run on a respawn and
        // on a section jump alike.
        public void ResetCheckpoints()
        {
            foreach (Checkpoint checkpoint in FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
            {
                checkpoint.SetClaimed(ActiveRespawn != null && checkpoint.RespawnTarget == ActiveRespawn);
            }
        }

        // Called by the pause menu's per-section buttons (the index arrives as a string,
        // which is what a UnityEvent can carry from a persistent listener).
        public void GoToSection(string index)
        {
            if (!int.TryParse(index, out int parsed)) return;
            TeleportTo(parsed);
        }

        public void TeleportTo(int index)
        {
            if (sections == null || index < 0 || index >= sections.Length) return;
            Section section = sections[index];
            if (section == null || section.spawnPoint == null) return;

            CurrentSection = index;
            PointHazardsAt(index);
            ResetCheckpoints();

            if (controller == null) controller = FindAnyObjectByType<KineticCubeController>();
            // The controller's own respawn does the whole job: position, velocity, flight
            // state and the camera pose all reset exactly as they do on a hazard death.
            controller?.RespawnAtPoint(section.spawnPoint.position);

            ResetLevelState();
        }

        // Every enemy returns to its spawn and live shots clear, so a section is always
        // entered in its opening state rather than mid-fight from a previous visit.
        void ResetLevelState()
        {
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Include)) enemy.ResetToSpawn();
            foreach (FlyingEnemy flyer in FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include)) flyer.ResetToSpawn();
            foreach (TurretEnemy turret in FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include)) turret.ResetToSpawn();
            foreach (EnemyProjectile shot in FindObjectsByType<EnemyProjectile>(FindObjectsInactive.Exclude)) Destroy(shot.gameObject);
            foreach (RotatingWall wall in FindObjectsByType<RotatingWall>(FindObjectsInactive.Include)) wall.ResetToStart();
        }

        void PointHazardsAt(int index)
        {
            if (sections == null || index < 0 || index >= sections.Length) return;
            SetActiveRespawn(sections[index].spawnPoint);
        }

        // The single place "where back is" lives - claimed either by touching a checkpoint
        // pad or by jumping to a section from the pause menu.
        public void SetActiveRespawn(Transform spawn)
        {
            if (spawn == null || hazards == null) return;
            ActiveRespawn = spawn;
            foreach (DamageWalls hazard in hazards)
            {
                if (hazard != null) hazard.respawnPoint = spawn;
            }
        }

        public Transform ActiveRespawn { get; private set; }
    }
}
