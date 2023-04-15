﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;

namespace LateToTheParty.Controllers
{
    public static class LocationSettingsController
    {
        public static int LastOriginalEscapeTime { get; private set; } = -1;
        public static LocationSettingsClass.Location LastLocationSelected { get; private set; } = null;

        private static string[] CarExtractNames = new string[0];
        private static Dictionary<string, LocationSettings> OriginalSettings = new Dictionary<string, LocationSettings>();
        private static BackendConfigSettingsClass.GClass1307.GClass1314 matchEndConfig = null;
        private static int MinimumTimeForSurvived = -1;

        public static void ClearOriginalSettings()
        {
            LoggingController.LogInfo("Discarding original raid settings...");
            OriginalSettings.Clear();
        }

        public static void ModifyLocationSettings(LocationSettingsClass.Location location, bool isScavRun)
        {
            if (CarExtractNames.Length == 0)
            {
                LoggingController.Logger.LogInfo("Getting car extract names...");
                CarExtractNames = Controllers.ConfigController.GetCarExtractNames();
            }

            // Get the singleton instance for match-end experience configuration and get the default value for minimum time to get a "Survived" status
            // NOTE: You have to get the singleton instance each time this method runs!
            matchEndConfig = Singleton<BackendConfigSettingsClass>.Instance.Experience.MatchEnd;
            if (MinimumTimeForSurvived < 0)
            {
                MinimumTimeForSurvived = matchEndConfig.SurvivedTimeRequirement;
                LoggingController.LogInfo("Default minimum time for Survived status: " + MinimumTimeForSurvived);
            }

            // Restore the orginal settings for the selected location before modifying them (or factors will be applied multiple times)
            LastLocationSelected = location;
            RestoreSettings(LastLocationSelected);
            LastOriginalEscapeTime = LastLocationSelected.EscapeTimeLimit;

            double timeReductionFactor = GenerateTimeReductionFactor(isScavRun);
            if (timeReductionFactor == 1)
            {
                LoggingController.LogInfo("Using original settings. Escape time: " + LastLocationSelected.EscapeTimeLimit);

                // Need to reset the minimum survival time to the default value
                AdjustMinimumSurvivalTime(LastLocationSelected);

                // Need to reset loot multipliers to original values
                if (!ConfigController.Config.DestroyLootDuringRaid.Enabled)
                {
                    Controllers.ConfigController.SetLootMultipliers(1);
                }

                return;
            }

            LastLocationSelected.EscapeTimeLimit = (int)(LastLocationSelected.EscapeTimeLimit * timeReductionFactor);
            LoggingController.LogInfo("Changed escape time to " + LastLocationSelected.EscapeTimeLimit);
            AdjustMinimumSurvivalTime(LastLocationSelected);

            if (!ConfigController.Config.DestroyLootDuringRaid.Enabled && ConfigController.Config.LootMultipliers.Length > 0)
            {
                double lootMultiplierFactor = GetLootRemainingFactor(timeReductionFactor);
                LoggingController.LogInfo("Adjusting loot multipliers by " + lootMultiplierFactor);
                Controllers.ConfigController.SetLootMultipliers(lootMultiplierFactor);
            }

            AdjustTrainTimes(LastLocationSelected);

            if (ConfigController.Config.VExChanceReductions.Length > 0)
            {
                double vexChanceFactor = Interpolate(ConfigController.Config.VExChanceReductions, timeReductionFactor);
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
            return Interpolate(ConfigController.Config.LootMultipliers, timeRemainingFactor);
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

                    if (CarExtractNames.Contains(exit.Name))
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

                if (CarExtractNames.Contains(exit.Name))
                {
                    settings.VExChance = exit.Chance;
                }
            }
            OriginalSettings.Add(location.Id, settings);
        }

        private static double GenerateTimeReductionFactor(bool isScav)
        {
            Random random = new Random();

            Configuration.EscapeTimeConfig config = isScav ? ConfigController.Config.Scav : ConfigController.Config.PMC;

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

            LoggingController.LogInfo("Changed minimum survival time to " + matchEndConfig.SurvivedTimeRequirement);
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

                LoggingController.LogInfo("Train extract " + exit.Name + ": MaxTime=" + exit.MaxTime + ", MinTime=" + exit.MinTime);
            }
        }

        private static void AdjustVExChance(LocationSettingsClass.Location location, double reductionFactor)
        {
            foreach (GClass1198 exit in location.exits)
            {
                if (CarExtractNames.Contains(exit.Name))
                {
                    exit.Chance *= (float)reductionFactor;
                    LoggingController.LogInfo("Vehicle extract " + exit.Name + " chance reduced to " + exit.Chance);
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

                LoggingController.LogInfo("Wave adjusted: MinTime=" + wave.time_min + ", MaxTime=" + wave.time_max);
            }
        }
    }
}
