﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LateToTheParty.Configuration
{
    public class DestroyLootDuringRaidConfig
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("exclusion_radius")]
        public double ExclusionRadius { get; set; } = 1;

        [JsonProperty("min_loot_age")]
        public double MinLootAge { get; set; } = 60;

        [JsonProperty("max_destroy_rate")]
        public double MaxDestroyRate { get; set; } = 10;

        [JsonProperty("map_traversal_speed_mps")]
        public double MapTraversalSpeed { get; set; } = 2;

        [JsonProperty("min_distance_traveled_for_update")]
        public double MinDistanceTraveledForUpdate { get; set; } = 1;

        [JsonProperty("min_time_before_update_ms")]
        public double MinTimeBeforeUpdate { get; set; } = 30;

        [JsonProperty("max_time_before_update_ms")]
        public double MaxTimeBeforeUpdate { get; set; } = 5000;

        [JsonProperty("max_calc_time_per_frame_ms")]
        public double MaxCalcTimePerFrame { get; set; } = 5;

        [JsonProperty("max_time_without_destroying_any_loot")]
        public double MaxTimeWithoutDestroyingAnyLoot { get; set; } = 60;

        [JsonProperty("ignore_items_dropped_by_player")]
        public IgnoreItemsDroppedByPlayerConfig IgnoreItemsDroppedByPlayer { get; set; } = new IgnoreItemsDroppedByPlayerConfig();

        [JsonProperty("ignore_items_on_dead_bots")]
        public IgnoreItemsOnDeadBotsConfig IgnoreItemsOnDeadBots { get; set; } = new IgnoreItemsOnDeadBotsConfig();

        [JsonProperty("excluded_parents")]
        public string[] ExcludedParents { get; set; } = new string[0];

        [JsonProperty("check_loot_accessibility")]
        public CheckLootAccessibilityConfig CheckLootAccessibility { get; set; } = new CheckLootAccessibilityConfig();

        [JsonProperty("loot_ranking")]
        public LootRankingConfig LootRanking { get; set; } = new LootRankingConfig();

        public DestroyLootDuringRaidConfig()
        {

        }
    }
}
