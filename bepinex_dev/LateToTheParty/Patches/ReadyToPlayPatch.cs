﻿using Aki.Reflection.Patching;
using EFT.UI.Matchmaker;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EFT.UI;
using Comfort.Common;
using LateToTheParty.Configuration;

namespace LateToTheParty.Patches
{
    public class ReadyToPlayPatch : ModulePatch
    {
        public static Dictionary<string, LocationSettings> OriginalSettings { get; private set; } = new Dictionary<string, LocationSettings>();
        public static int LastOriginalEscapeTime { get; private set; } = -1;
        public static LocationSettingsClass.Location LastLocationSelected { get; private set; } = null;

        private static BackendConfigSettingsClass.GClass1307.GClass1314 matchEndConfig = null;
        private static int MinimumTimeForSurvived = -1;

        protected override MethodBase GetTargetMethod()
        {
            // Method 45 always runs, but sometimes twice. Method 42 runs before pressing "Ready", but won't work if you press "Ready" early.
            string methodName = "method_45";
            if (LateToThePartyPlugin.ModConfig.Debug)
            {
                methodName = "method_42";
            }

            return typeof(MainMenuController).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [PatchPostfix]
        private static void PatchPostfix(bool __result, RaidSettings ___raidSettings_0)
        {
            // Don't bother running the code if the game wouldn't allow you into a raid anyway
            if (!__result)
            {
                return;
            }

            // Get the singleton instance for match-end experience configuration and get the default value for minimum time to get a "Survived" status
            // NOTE: You have to get the singleton instance each time this method runs!
            matchEndConfig = Singleton<BackendConfigSettingsClass>.Instance.Experience.MatchEnd;
            if (MinimumTimeForSurvived < 0)
            {
                MinimumTimeForSurvived = matchEndConfig.SurvivedTimeRequirement;
                Logger.LogInfo("Default minimum time for Survived status: " + MinimumTimeForSurvived);
            }

            // Restore the orginal settings for the selected location before modifying them (or factors will be applied multiple times)
            LastLocationSelected = ___raidSettings_0.SelectedLocation;
            RestoreSettings(LastLocationSelected);
            LastOriginalEscapeTime = LastLocationSelected.EscapeTimeLimit;

            double timeReductionFactor = GenerateTimeReductionFactor(___raidSettings_0.IsScav);
            if (timeReductionFactor == 1)
            {
                Logger.LogInfo("Using original settings. Escape time: " + LastLocationSelected.EscapeTimeLimit);

                // Need to reset the minimum survival time to the default value
                AdjustMinimumSurvivalTime(LastLocationSelected);

                // Need to reset loot multipliers to original values
                if (!LateToThePartyPlugin.ModConfig.DestroyLootDuringRaid.Enabled)
                {
                    Controllers.ConfigController.SetLootMultipliers(1);
                }
                
                return;
            }

            LastLocationSelected.EscapeTimeLimit = (int)(LastLocationSelected.EscapeTimeLimit * timeReductionFactor);
            Logger.LogInfo("Changed escape time to " + LastLocationSelected.EscapeTimeLimit);
            AdjustMinimumSurvivalTime(LastLocationSelected);

            if (!LateToThePartyPlugin.ModConfig.DestroyLootDuringRaid.Enabled && LateToThePartyPlugin.ModConfig.LootMultipliers.Length > 0)
            {
                double lootMultiplierFactor = GetLootRemainingFactor(timeReductionFactor);
                Logger.LogInfo("Adjusting loot multipliers by " + lootMultiplierFactor);
               Controllers.ConfigController.SetLootMultipliers(lootMultiplierFactor);
            }

            AdjustTrainTimes(LastLocationSelected);

            if (LateToThePartyPlugin.ModConfig.VExChanceReductions.Length > 0)
            {
                double vexChanceFactor = Interpolate(LateToThePartyPlugin.ModConfig.VExChanceReductions, timeReductionFactor);
                AdjustVExChance(LastLocationSelected, vexChanceFactor);
            }
            
            AdjustBotWaveTimes(LastLocationSelected);
        }

        public static double Interpolate(double[][] array, double value)
        {
            if (array.Length == 1)
            {
                return array.Last()[1];
            }

            if (value <= array[0][0])
            {
                return array[0][1];
            }

            for (int i = 1; i < array.Length; i++)
            {
                if (array[i][0] >= value)
                {
                    if (array[i][0] - array[i - 1][0] == 0)
                    {
                        return array[i][1];
                    }

                    return array[i - 1][1] + (value - array[i - 1][0]) * (array[i][1] - array[i - 1][1]) / (array[i][0] - array[i - 1][0]);
                }
            }

            return array.Last()[1];
        }

        public static double GetLootRemainingFactor(double timeRemainingFactor)
        {
            return Interpolate(LateToThePartyPlugin.ModConfig.LootMultipliers, timeRemainingFactor);
        }

        private static void RestoreSettings(LocationSettingsClass.Location location)
        {
            if (OriginalSettings.ContainsKey(location.Id))
            {
                location.EscapeTimeLimit = OriginalSettings[location.Id].EscapeTimeLimit;
                
                foreach (GClass1198 exit in location.exits)
                {
                    if (exit.PassageRequirement == EFT.Interactive.ERequirementState.Train)
                    {
                        exit.Count = OriginalSettings[location.Id].TrainWaitTime;
                        exit.MinTime = OriginalSettings[location.Id].TrainMinTime;
                        exit.MaxTime = OriginalSettings[location.Id].TrainMaxTime;
                    }

                    if (LateToThePartyPlugin.CarExtractNames.Contains(exit.Name))
                    {
                        exit.Chance = OriginalSettings[location.Id].VExChance;
                    }
                }

                return;
            }

            LocationSettings settings = new LocationSettings(location.EscapeTimeLimit);
            foreach (GClass1198 exit in location.exits)
            {
                if (exit.PassageRequirement == EFT.Interactive.ERequirementState.Train)
                {
                    settings.TrainWaitTime = exit.Count;
                    settings.TrainMinTime = exit.MinTime;
                    settings.TrainMaxTime = exit.MaxTime;
                }

                if (LateToThePartyPlugin.CarExtractNames.Contains(exit.Name))
                {
                    settings.VExChance = exit.Chance;
                }
            }
            OriginalSettings.Add(location.Id, settings);
        }

        private static double GenerateTimeReductionFactor(bool isScav)
        {
            Random random = new Random();

            Configuration.EscapeTimeConfig config = isScav ? LateToThePartyPlugin.ModConfig.Scav : LateToThePartyPlugin.ModConfig.PMC;

            if (random.NextDouble() > config.Chance)
            {
                return 1;
            }

            return (config.TimeFactorMax - config.TimeFactorMin) * random.NextDouble() + config.TimeFactorMin;
        }

        private static void AdjustMinimumSurvivalTime(LocationSettingsClass.Location location)
        {
            double minRaidTimeForRunThrough = (OriginalSettings[location.Id].EscapeTimeLimit * 60) - MinimumTimeForSurvived;
            double survTimeReq = Math.Max(1, Math.Min(MinimumTimeForSurvived, (location.EscapeTimeLimit * 60) - minRaidTimeForRunThrough));
            matchEndConfig.SurvivedTimeRequirement = (int)survTimeReq;

            Logger.LogInfo("Changed minimum survival time to " + matchEndConfig.SurvivedTimeRequirement);
        }

        private static void AdjustTrainTimes(LocationSettingsClass.Location location)
        {
            int timeReduction = (OriginalSettings[location.Id].EscapeTimeLimit - location.EscapeTimeLimit) * 60;
            int minTimeBeforeActivation = 60;
            
            foreach (GClass1198 exit in location.exits)
            {
                if (exit.PassageRequirement != EFT.Interactive.ERequirementState.Train)
                {
                    continue;
                }

                int maxTimebeforeActivation = (location.EscapeTimeLimit * 60) - (int)Math.Ceiling(exit.ExfiltrationTime) - exit.Count - 60;

                exit.MaxTime -= timeReduction;
                exit.MinTime -= timeReduction;

                if (exit.MinTime < minTimeBeforeActivation)
                {
                    exit.MaxTime += (minTimeBeforeActivation - exit.MinTime);
                    exit.MinTime = minTimeBeforeActivation;
                }

                if (exit.MaxTime >= maxTimebeforeActivation)
                {
                    exit.MaxTime = maxTimebeforeActivation;
                }

                if (exit.MaxTime <= exit.MinTime)
                {
                    exit.MaxTime = exit.MinTime + 1;
                }

                Logger.LogInfo("Train extract " + exit.Name + ": MaxTime=" + exit.MaxTime + ", MinTime=" + exit.MinTime);
            }
        }

        private static void AdjustVExChance(LocationSettingsClass.Location location, double reductionFactor)
        {
            foreach (GClass1198 exit in location.exits)
            {
                if (LateToThePartyPlugin.CarExtractNames.Contains(exit.Name))
                {
                    exit.Chance *= (float)reductionFactor;
                    Logger.LogInfo("Vehicle extract " + exit.Name + " chance reduced to " + exit.Chance);
                }
            }
        }

        private static void AdjustBotWaveTimes(LocationSettingsClass.Location location)
        {
            int timeReduction = (OriginalSettings[location.Id].EscapeTimeLimit - location.EscapeTimeLimit) * 60;
            int minTimeBeforeActivation = 20;

            foreach (WildSpawnWave wave in location.waves)
            {
                wave.time_max -= timeReduction;
                wave.time_min -= timeReduction;

                if (wave.time_min < minTimeBeforeActivation)
                {
                    wave.time_max += (minTimeBeforeActivation - wave.time_min);
                    wave.time_min = minTimeBeforeActivation;
                }

                if (wave.time_max <= wave.time_min)
                {
                    wave.time_max = wave.time_min + 1;
                }

                Logger.LogInfo("Wave adjusted: MinTime=" + wave.time_min + ", MaxTime=" + wave.time_max);
            }
        }
    }
}
