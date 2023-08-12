﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LateToTheParty.Configuration
{
    public class QuestTemplatesConfig
    {
        [JsonProperty("quests")]
        public RawQuestClass[] Quests { get; set; } = new RawQuestClass[0];

        public QuestTemplatesConfig()
        {

        }
    }
}
