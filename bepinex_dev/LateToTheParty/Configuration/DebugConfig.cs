﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LateToTheParty.Configuration
{
    public class DebugConfig
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("loot_path_visualization")]
        public LootPathVisualizationConfig LootPathVisualization { get; set; } = new LootPathVisualizationConfig();

        public DebugConfig()
        {

        }
    }
}