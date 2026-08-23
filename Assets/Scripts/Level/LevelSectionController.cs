using System;
using System.Collections.Generic;
using UnityEngine;
using KineticEnergy.Player;

namespace KineticEnergy.Level
{

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

            PointHazardsAt(CurrentSection);
            CaptureEnemySections();
            ResetCheckpoints();

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
            GrantCheckpointEnergy(target);
            ResetLevelState();
            ResetCheckpoints();
        }

        void GrantCheckpointEnergy(Transform spawn)
        {
            if (controller == null || spawn == null) return;
            foreach (Checkpoint checkpoint in FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
            {
                if (checkpoint.RespawnTarget != spawn) continue;
                controller.EnsureEnergyAtLeast(checkpoint.minActivationEnergyFraction);
                return;
            }
        }

        void OnEnable()
        {
            DamageWalls.PlayerRespawned += OnPlayerRespawned;
        }

        void OnDisable()
        {
            DamageWalls.PlayerRespawned -= OnPlayerRespawned;
        }

        void OnPlayerRespawned()
        {

            if (controller == null) controller = FindAnyObjectByType<KineticCubeController>();
            GrantCheckpointEnergy(ActiveRespawn);
            ResetLevelState();
            ResetCheckpoints();
        }

        public void ResetCheckpoints()
        {
            foreach (Checkpoint checkpoint in FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
            {
                checkpoint.SetClaimed(ActiveRespawn != null && checkpoint.RespawnTarget == ActiveRespawn);
            }
        }

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

            controller?.RespawnAtPoint(section.spawnPoint.position);
            GrantCheckpointEnergy(section.spawnPoint);

            ResetLevelState();
        }

        public void ResetLevelState()
        {
            int activeIndex = ActiveSectionIndex;

            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Include))
            {
                if (ShouldRevive(enemy, activeIndex)) enemy.ResetToSpawn();
            }
            foreach (FlyingEnemy flyer in FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include))
            {
                if (ShouldRevive(flyer, activeIndex)) flyer.ResetToSpawn();
            }
            foreach (TurretEnemy turret in FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include))
            {
                if (ShouldRevive(turret, activeIndex)) turret.ResetToSpawn();
            }

            foreach (EnemyProjectile shot in FindObjectsByType<EnemyProjectile>(FindObjectsInactive.Exclude)) Destroy(shot.gameObject);
            foreach (RotatingWall wall in FindObjectsByType<RotatingWall>(FindObjectsInactive.Include)) wall.ResetToStart();
        }

        bool ShouldRevive(Component enemy, int activeIndex)
        {

            if (!enemySection.TryGetValue(enemy, out int ownerIndex)) return true;
            return ownerIndex >= activeIndex;
        }

        int SectionIndexFor(Vector3 position)
        {
            int index = 0;
            for (int i = 0; i < sections.Length; i++)
            {
                Transform spawn = sections[i].spawnPoint;
                if (spawn != null && position.x >= spawn.position.x) index = i;
            }
            return index;
        }

        int ActiveSectionIndex
        {
            get
            {
                if (ActiveRespawn == null) return 0;
                for (int i = 0; i < sections.Length; i++)
                {
                    if (sections[i] != null && sections[i].spawnPoint == ActiveRespawn) return i;
                }
                return SectionIndexFor(ActiveRespawn.position);
            }
        }

        void CaptureEnemySections()
        {
            enemySection.Clear();
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Include))
            {
                enemySection[enemy] = SectionIndexFor(enemy.transform.position);
            }
            foreach (FlyingEnemy flyer in FindObjectsByType<FlyingEnemy>(FindObjectsInactive.Include))
            {
                enemySection[flyer] = SectionIndexFor(flyer.transform.position);
            }
            foreach (TurretEnemy turret in FindObjectsByType<TurretEnemy>(FindObjectsInactive.Include))
            {
                enemySection[turret] = SectionIndexFor(turret.transform.position);
            }
        }

        readonly Dictionary<Component, int> enemySection = new Dictionary<Component, int>();

        void PointHazardsAt(int index)
        {
            if (sections == null || index < 0 || index >= sections.Length) return;
            SetActiveRespawn(sections[index].spawnPoint);
        }

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

