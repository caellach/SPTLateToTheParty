﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace LateToTheParty.Controllers
{
    public class BotConversionController : MonoBehaviour
    {
        private static bool EscapeTimeShared = false;

        private void Update()
        {
            if ((!Singleton<GameWorld>.Instantiated) || (Camera.main == null))
            {
                EscapeTimeShared = false;

                return;
            }

            // Only send the message once
            if (EscapeTimeShared)
            {
                return;
            }

            float raidTimeElapsed = Aki.SinglePlayer.Utils.InRaid.RaidTimeUtil.GetElapsedRaidSeconds();
            
            // Don't run the script before the raid begins
            if (raidTimeElapsed < 3)
            {
                return;
            }

            float raidTimeRemaining = Aki.SinglePlayer.Utils.InRaid.RaidTimeUtil.GetRemainingRaidSeconds();
            int totalRaidTime = (int)Math.Ceiling(raidTimeRemaining + raidTimeElapsed);

            // Share the escape time and current time remaining with the server
            ConfigController.ShareEscapeTime(totalRaidTime, raidTimeRemaining);
            EscapeTimeShared = true;
        }
    }
}
