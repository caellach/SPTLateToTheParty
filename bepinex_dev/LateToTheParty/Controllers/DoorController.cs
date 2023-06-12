﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using LateToTheParty.CoroutineExtensions;
using UnityEngine;
using UnityEngine.AI;

namespace LateToTheParty.Controllers
{
    public class DoorController : MonoBehaviour
    {
        public static bool IsTogglingDoors { get; private set; } = false;
        public static int InteractiveLayer { get; set; }

        private static List<Door> validDoors = new List<Door>();
        private static Dictionary<Door, bool> canToggleDoorDict = new Dictionary<Door, bool>();
        private GamePlayerOwner gamePlayerOwner = null;
        private static MethodInfo canStartInteractionMethodInfo = typeof(WorldInteractiveObject).GetMethod("CanStartInteraction", BindingFlags.NonPublic | BindingFlags.Instance);
        private static Stopwatch updateTimer = Stopwatch.StartNew();
        private static EnumeratorWithTimeLimit enumeratorWithTimeLimit = new EnumeratorWithTimeLimit(ConfigController.Config.OpenDoorsDuringRaid.MaxCalcTimePerFrame);
        private static int doorsToToggle = 1;
        private static int validDoorCount = -1;

        public static void Clear()
        {
            if (IsTogglingDoors)
            {
                enumeratorWithTimeLimit.Abort();
                TaskWithTimeLimit.WaitForCondition(() => !IsTogglingDoors);
            }

            validDoors.Clear();
            canToggleDoorDict.Clear();
            updateTimer.Restart();

            validDoorCount = -1;
        }

        private void Update()
        {
            if (!ConfigController.Config.OpenDoorsDuringRaid.Enabled)
            {
                return;
            }

            // Clear all arrays if not in a raid to reset them for the next raid
            if ((!Singleton<GameWorld>.Instantiated) || (Camera.main == null))
            {
                Clear();
                return;
            }

            // Wait until the previous task completes
            if (IsTogglingDoors)
            {
                return;
            }

            // Ensure enough time has passed since the last door event
            if ((updateTimer.ElapsedMilliseconds < ConfigController.Config.OpenDoorsDuringRaid.TimeBetweenEvents * 1000) && (validDoors.Count > 0))
            {
                return;
            }

            // Get the current number of seconds remaining in the raid and calculate the fraction of total raid time remaining
            float escapeTimeSec = GClass1425.EscapeTimeSeconds(Singleton<AbstractGame>.Instance.GameTimer);
            float raidTimeElapsed = (LocationSettingsController.LastOriginalEscapeTime * 60f) - escapeTimeSec;

            // Don't run the script before the raid begins
            if (raidTimeElapsed < 3)
            {
                return;
            }

            // Do not change doors too early or late into the raid
            if ((validDoors.Count > 0) && ((raidTimeElapsed < ConfigController.Config.OpenDoorsDuringRaid.MinRaidET) || (escapeTimeSec < ConfigController.Config.OpenDoorsDuringRaid.MinRaidTimeRemaining)))
            {
                return;
            }

            // Only find doors once per raid
            doorsToToggle = (int)Math.Max(1, Math.Round(validDoors.Count * ConfigController.Config.OpenDoorsDuringRaid.PercentageOfDoorsPerEvent / 100.0));
            if (validDoorCount == -1)
            {
                gamePlayerOwner = FindObjectOfType<GamePlayerOwner>();
                FindAllValidDoors();
                validDoorCount = validDoors.Count;

                doorsToToggle *= (int)Math.Ceiling(Math.Max(raidTimeElapsed - ConfigController.Config.OpenDoorsDuringRaid.MinRaidET, 0) / ConfigController.Config.OpenDoorsDuringRaid.TimeBetweenEvents);
            }

            // Ensure there are doors to toggle
            if (doorsToToggle == 0)
            {
                return;
            }

            // Try to change the state of doors
            StartCoroutine(ToggleDoors(doorsToToggle));
            updateTimer.Restart();
        }

        private IEnumerator ToggleDoors(int doorsToToggle)
        {
            try
            {
                IsTogglingDoors = true;

                // Check which doors are eligible to be toggled
                enumeratorWithTimeLimit.Reset();
                yield return enumeratorWithTimeLimit.Run(validDoors.AsEnumerable(), UpdateIfDoorCanBeToggled);
                IEnumerable<Door> doorsThatCanBeToggled = canToggleDoorDict.Where(d => d.Value).Select(d => d.Key);

                // Toggle requested number of doors
                enumeratorWithTimeLimit.Reset();
                yield return enumeratorWithTimeLimit.Repeat(doorsToToggle, ToggleRandomDoor, doorsThatCanBeToggled, ConfigController.Config.OpenDoorsDuringRaid.MaxCalcTimePerFrame);
            }
            finally
            {
                IsTogglingDoors = false;
            }
        }

        private void FindAllValidDoors()
        {
            validDoors.Clear();

            LoggingController.LogInfo("Searching for valid doors...");
            Door[] allDoors = UnityEngine.Object.FindObjectsOfType<Door>();
            LoggingController.LogInfo("Searching for valid doors...found " + allDoors.Length + " possible doors.");

            foreach (Door door in allDoors)
            {
                if (!IsValidDoor(door, true))
                {
                    continue;
                }

                // If the door is eligible for toggling during the raid, add it to the dictionary
                validDoors.Add(door);
            }

            LoggingController.LogInfo("Searching for valid doors...found " + validDoors.Count + " doors.");
        }

        private void UpdateIfDoorCanBeToggled(Door door)
        {
            if (canToggleDoorDict.ContainsKey(door))
            {
                canToggleDoorDict[door] = CanToggleDoor(door);
            }
            else
            {
                canToggleDoorDict.Add(door, CanToggleDoor(door));
            }
        }

        private bool CanToggleDoor(Door door)
        {
            // Ensure you're still in the raid to avoid NRE's when it ends
            if ((Camera.main == null) || (door.transform == null))
            {
                return false;
            }

            // Ignore doors that are too close to you
            Vector3 yourPosition = Camera.main.transform.position;
            float doorDist = Vector3.Distance(yourPosition, door.transform.position);
            if (doorDist < ConfigController.Config.OpenDoorsDuringRaid.ExclusionRadius)
            {
                return false;
            }

            return true;
        }

        private bool IsValidDoor(Door door, bool logResult = false)
        {
            // Redundant check
            /*if (!door.Operatable)
            {
                if (logResult) LoggingController.LogInfo("Searching for valid doors...door " + door.Id + " is inoperable.");
                return false;
            }*/

            if (door.gameObject.layer != InteractiveLayer)
            {
                if (logResult) LoggingController.LogInfo("Searching for valid doors...door " + door.Id + " is inoperable (wrong layer).");
                return false;
            }

            // Ensure there are context menu options for the door
            GClass2645 availableActions = GClass1766.GetAvailableActions(gamePlayerOwner, door);
            if ((availableActions == null) || (availableActions.Actions.Count == 0))
            {
                if (logResult) LoggingController.LogInfo("Searching for valid doors...door " + door.Id + " has no interaction options.");
                return false;
            }

            if (door.DoorState != EDoorState.Open && door.DoorState != EDoorState.Shut && door.DoorState != EDoorState.Locked)
            {
                if (logResult) LoggingController.LogInfo("Searching for valid doors...door " + door.Id + " has an invalid state: " + door.DoorState);
                return false;
            }

            // Get all items to search for key ID's
            Dictionary<string, Item> allItems = ItemHelpers.GetAllItems();

            if (door.DoorState == EDoorState.Locked)
            {
                if (allItems.ContainsKey(door.KeyId) && !ConfigController.Config.OpenDoorsDuringRaid.CanOpenLockedDoors)
                {
                    if (logResult) LoggingController.LogInfo("Searching for valid doors...door " + door.Id + " is locked and not allowed to be opened.");
                    return false;
                }

                if (!allItems.ContainsKey(door.KeyId) && !door.CanBeBreached)
                {
                    if (logResult) LoggingController.LogInfo("Searching for valid doors...door " + door.Id + " is locked and has no valid key.");
                    return false;
                }

                if (door.CanBeBreached && !ConfigController.Config.OpenDoorsDuringRaid.CanBreachDoors)
                {
                    if (logResult) LoggingController.LogInfo("Searching for valid doors...door " + door.Id + " is not allowed to be breached.");
                    return false;
                }
            }

            return true;
        }

        private void ToggleRandomDoor(IEnumerable<Door> eligibleDoors, int maxCalcTime_ms)
        {
            // Randomly sort eligible doors
            System.Random randomObj = new System.Random();
            IEnumerable<Door> randomlyOrderedKeys = eligibleDoors.OrderBy(e => randomObj.NextDouble());

            // Try to find a door to toggle, but do not wait too long
            Stopwatch calcTimer = Stopwatch.StartNew();
            while (calcTimer.ElapsedMilliseconds < maxCalcTime_ms)
            {
                // Randomly select a new door state
                EDoorState newState = EDoorState.Open;
                if (randomObj.Next(0, 100) < ConfigController.Config.OpenDoorsDuringRaid.ChanceOfClosingDoors)
                {
                    newState = EDoorState.Shut;
                }

                // Try to make the desired change to each door in the randomly-sorted enumerator
                foreach (Door door in randomlyOrderedKeys)
                {
                    //LoggingController.LogInfo("Attempting to change door " + door.Id + " to " + newState + "...");
                    if (ToggleDoor(door, newState))
                    {
                        return;
                    }
                }
            }
        }

        private bool ToggleDoor(Door door, EDoorState newState)
        {
            // Check if the door is already in the desired state
            if (newState == EDoorState.Shut && (door.DoorState == EDoorState.Shut || door.DoorState == EDoorState.Locked))
            {
                return false;
            }
            if (newState == EDoorState.Open && door.DoorState == EDoorState.Open)
            {
                return false;
            }

            // Unlock or "breach" the door if necessary
            if ((door.DoorState == EDoorState.Locked) && (newState == EDoorState.Open))
            {
                if (door.KeyId.Length > 0)
                {
                    LoggingController.LogInfo("Unlocking door: " + door.Id + " (Key ID: " + door.KeyId + ")");
                }
                else
                {
                    LoggingController.LogInfo("Preparing to breach door: " + door.Id);
                }

                door.DoorState = EDoorState.Shut;
                door.OnEnable();

                // This doesn't work
                //door.Interact(new GClass2600(EInteractionType.Unlock));
            }

            // Ignore doors that are currently being opened/closed                    
            if (!(bool)canStartInteractionMethodInfo.Invoke(door, new object[] { newState, true }))
            {
                return false;
            }

            if ((door.DoorState != EDoorState.Open) && (door.DoorState != EDoorState.Locked) && (newState == EDoorState.Open))
            {
                LoggingController.LogInfo("Opening door: " + door.Id);
                //door.DoorState = EDoorState.Open;
                //door.OnEnable();

                // This plays the opening noise and animation
                door.Interact(new GClass2600(EInteractionType.Open));
                return true;
            }

            if ((door.DoorState == EDoorState.Open) && (newState == EDoorState.Shut))
            {
                LoggingController.LogInfo("Closing door: " + door.Id);
                //door.DoorState = EDoorState.Open;
                //door.OnEnable();

                // This plays the opening noise and animation
                door.Interact(new GClass2600(EInteractionType.Close));
                return true;
            }

            return false;
        }
    }
}
