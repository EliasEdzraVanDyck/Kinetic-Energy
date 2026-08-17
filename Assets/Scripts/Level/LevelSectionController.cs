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

            if (controller == null) controller = FindAnyObjectByType<KineticCubeController>();
            // The controller's own respawn does the whole job: position, velocity, flight
            // state and the camera pose all reset exactly as they do on a hazard death.
            controller?.RespawnAtPoint(section.spawnPoint.position);

            // Every enemy returns to its spawn, so a section is always entered in its
            // opening state rather than mid-fight from a previous visit.
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Include)) enemy.ResetToSpawn();
            foreach (FlyingEnemy flyer in FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include)) flyer.ResetToSpawn();
            foreach (TurretEnemy turret in FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include)) turret.ResetToSpawn();
            foreach (EnemyProjectile shot in FindObjectsByType<EnemyProjectile>(FindObjectsInactive.Exclude)) Destroy(shot.gameObject);
            foreach (RotatingWall wall in FindObjectsByType<RotatingWall>(FindObjectsInactive.Include)) wall.ResetToStart();
        }

        void PointHazardsAt(int index)
        {
            if (sections == null || index < 0 || index >= sections.Length) return;
            Transform spawn = sections[index].spawnPoint;
            if (spawn == null || hazards == null) return;
            foreach (DamageWalls hazard in hazards)
            {
                if (hazard != null) hazard.respawnPoint = spawn;
            }
        }
    }
}
