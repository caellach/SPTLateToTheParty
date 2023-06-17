﻿using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using LateToTheParty.CoroutineExtensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace LateToTheParty.Controllers
{
    public class NavMeshController: MonoBehaviour
    {
        public static bool IsUpdatingDoorsBlockers { get; private set; } = false;

        private static Dictionary<Door, MeshCollider> doorMeshColliders = new Dictionary<Door, MeshCollider>(); 
        private static Dictionary<Door, NavMeshObstacle> doorBlockers = new Dictionary<Door, NavMeshObstacle>();
        private static EnumeratorWithTimeLimit enumeratorWithTimeLimit = new EnumeratorWithTimeLimit(ConfigController.Config.OpenDoorsDuringRaid.MaxCalcTimePerFrame);
        private static Stopwatch updateTimer = Stopwatch.StartNew();

        public static void Clear()
        {
            if (IsUpdatingDoorsBlockers)
            {
                enumeratorWithTimeLimit.Abort();
                TaskWithTimeLimit.WaitForCondition(() => !IsUpdatingDoorsBlockers);
            }

            doorBlockers.Clear();
            doorMeshColliders.Clear();

            updateTimer.Restart();
        }

        private void LateUpdate()
        {
            // Clear all arrays if not in a raid to reset them for the next raid
            if ((!Singleton<GameWorld>.Instantiated) || (Camera.main == null))
            {
                Clear();
                return;
            }

            // Ensure enough time has passed since the last check
            if (updateTimer.ElapsedMilliseconds < 2 * 1000)
            {
                return;
            }

            if (doorMeshColliders.Count > 0)
            {
                // Update the nav mesh to reflect the door state changes
                StartCoroutine(UpdateDoorBlockers());
                updateTimer.Restart();

                return;
            }

            foreach (MeshCollider collider in FindObjectsOfType<MeshCollider>())
            {
                CheckIfColliderIsDoor(collider);
            }
        }

        public static void CheckIfColliderIsDoor(MeshCollider meshCollider)
        {
            if (doorMeshColliders.ContainsValue(meshCollider))
            {
                return;
            }

            if (meshCollider.gameObject.layer != LayerMaskClass.DoorLayer)
            {
                return;
            }

            GameObject doorObject = meshCollider.transform.parent.gameObject;
            Door door = doorObject.GetComponent<Door>();

            if (door == null)
            {
                return;
            }

            doorMeshColliders.Add(door, meshCollider);
        }

        public static IEnumerator UpdateDoorBlockers()
        {
            IsUpdatingDoorsBlockers = true;

            // Update door blockers
            enumeratorWithTimeLimit.Reset();
            yield return enumeratorWithTimeLimit.Run(doorMeshColliders.Keys.ToArray(), UpdateDoorBlocker);

            IsUpdatingDoorsBlockers = false;
        }

        public static bool IsPositionAccessible(Vector3 sourcePosition, Vector3 targetPosition, string positionName)
        {
            RemoveAccessibilityPaths(positionName);

            float searchDistanceSource = 10;
            float searchDistanceTarget = 2;
            NavMeshPath path = new NavMeshPath();

            if (!NavMesh.SamplePosition(sourcePosition, out NavMeshHit sourceNearestPoint, searchDistanceSource, NavMesh.AllAreas))
            {
                return false;
            }

            if (!NavMesh.SamplePosition(targetPosition, out NavMeshHit targetNearestPoint, searchDistanceTarget, NavMesh.AllAreas))
            {
                return false;
            }

            NavMesh.CalculatePath(sourceNearestPoint.position, targetNearestPoint.position, NavMesh.AllAreas, path);

            Vector3[] pathPoints = new Vector3[path.corners.Length];
            float heightOffset = 1.5f;
            for (int i = 0; i < pathPoints.Length; i++)
            {
                pathPoints[i] = new Vector3(path.corners[i].x, path.corners[i].y + heightOffset, path.corners[i].z);
            }

            if (path.status != NavMeshPathStatus.PathComplete)
            {
                for (int i = 0; i < pathPoints.Length; i++)
                {
                    pathPoints[i].y -= 0.25f;
                }
                PathRender.AddPath(positionName + "_path", pathPoints, Color.white);
                return false;
            }

            PathRender.AddPath(positionName + "_path", pathPoints, Color.blue);

            float distToNavMesh = Vector3.Distance(targetPosition, pathPoints.Last());
            Vector3 direction = pathPoints.Last() - targetPosition;
            RaycastHit[] targetRaycastHits = Physics.RaycastAll(targetPosition, direction, distToNavMesh);
            RaycastHit[] targetRaycastHitsFiltered = targetRaycastHits.Where(r => r.distance > 0.3).ToArray();
            if (targetRaycastHitsFiltered.Length > 0)
            {
                for (int ray = 0; ray < targetRaycastHitsFiltered.Length; ray++)
                {
                    Vector3[] circlepoints = PathRender.GetSpherePoints(targetRaycastHitsFiltered[ray].point, 0.05f, 10);
                    PathRender.AddPath(positionName + "_ray" + ray, circlepoints, Color.red);
                    LoggingController.LogInfo(positionName + " Collider: " + targetRaycastHitsFiltered[ray].collider.name + " (Distance: " + targetRaycastHitsFiltered[ray].distance + " / " + distToNavMesh + ")");
                }

                PathRender.AddPath(positionName + "_end", new Vector3[] { pathPoints.Last(), targetPosition }, Color.red);
                return false;
            }

            PathRender.AddPath(positionName + "_end", new Vector3[] { pathPoints.Last(), targetPosition }, Color.green);
            return true;
        }

        public static void RemoveAccessibilityPaths(string positionName)
        {
            PathRender.RemovePaths(positionName);
        }

        private static void UpdateDoorBlocker(Door door)
        {
            if (door.DoorState == EDoorState.Open)
            {
                DestroyDoorBlocker(door);
                return;
            }

            CreateDoorBlocker(door);
        }

        private static void DestroyDoorBlocker(Door door)
        {
            if (!doorBlockers.ContainsKey(door))
            {
                return;
            }

            if (doorBlockers[door] == null)
            {
                return;
            }

            //Destroy(doorBlockers[door].gameObject);
            Destroy(doorBlockers[door]);
            doorBlockers[door] = null;
            PathRender.RemovePath(CreateDoorBlockerID(door));
            LoggingController.LogInfo("Remove door blocker for " + door.Id);
        }

        private static void CreateDoorBlocker(Door door)
        {
            if (doorBlockers.ContainsKey(door) && (doorBlockers[door] != null))
            {
                return;
            }

            GameObject doorBlockerObj = new GameObject("Door_" + door.Id.Replace(" ", "_") + "_Blocker");
            //GameObject doorBlockerObj = new GameObject("ObstacleObject");
            doorBlockerObj.transform.SetParent(doorMeshColliders[door].transform);
            doorBlockerObj.transform.position = doorMeshColliders[door].bounds.center;

            NavMeshObstacle obstacle = doorBlockerObj.AddComponent<NavMeshObstacle>();
            if (doorBlockers.ContainsKey(door))
            {
                doorBlockers[door] = obstacle;
            }
            else
            {
                doorBlockers.Add(door, obstacle);
            }

            doorBlockers[door].size = doorMeshColliders[door].bounds.size;
            doorBlockers[door].carving = true;
            doorBlockers[door].carveOnlyStationary = false;

            Vector3 ellipsoidSize = PathRender.IncreaseVector3ToMinSize(doorBlockers[door].size, 0.2f);
            Vector3[] circlepoints = PathRender.GetEllipsoidPoints(door.transform.position, ellipsoidSize, 10);
            PathRender.AddPath(CreateDoorBlockerID(door), circlepoints, Color.yellow);
        }

        private static string CreateDoorBlockerID(Door door)
        {
            return door.Id.Replace(" ", "") + "_blocker";
        }
    }
}