﻿using LateToTheParty.Controllers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LateToTheParty.Models
{
    public class EnumeratorWithTimeLimit
    {
        public bool IsRunning { get; private set; } = false;
        public bool IsCompleted { get; private set; } = false;

        private Stopwatch cycleTimer = new Stopwatch();
        private double maxTimePerIteration;
        private bool stopRequested = false;
        private bool hadToWait = false;

        public EnumeratorWithTimeLimit(double _maxTimePerIteration)
        {
            maxTimePerIteration = _maxTimePerIteration;
        }

        public IEnumerator Run<TItem>(IEnumerable<TItem> collection, Action<TItem> collectionItemAction)
        {
            Action<TItem> action = ((item) =>
            {
                collectionItemAction(item);
            });
            yield return Run_Internal(collection, action);
        }

        public IEnumerator Run<TItem, T1>(IEnumerable<TItem> collection, Action<TItem, T1> collectionItemAction, T1 param1)
        {
            Action<TItem> action = ((item) =>
            {
                collectionItemAction(item, param1);
            });
            yield return Run_Internal(collection, action);
        }

        public IEnumerator Run<TItem, T1, T2>(IEnumerable<TItem> collection, Action<TItem, T1, T2> collectionItemAction, T1 param1, T2 param2)
        {
            Action<TItem> action = ((item) =>
            {
                collectionItemAction(item, param1, param2);
            });
            yield return Run_Internal(collection, action);
        }

        private IEnumerator Run_Internal<TItem>(IEnumerable<TItem> collection, Action<TItem> action)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("There is already a coroutine running.");
            }

            IsCompleted = false;
            IsRunning = true;
            hadToWait = false;

            cycleTimer.Restart();

            foreach (TItem item in collection)
            {
                try
                {
                    action(item);
                }
                catch (Exception ex)
                {
                    LoggingController.LogError("Aborting coroutine iteration for " + item.ToString());
                    LoggingController.LogError(ex.ToString());
                }

                if (cycleTimer.ElapsedMilliseconds > maxTimePerIteration)
                {
                    hadToWait = true;
                    LoggingController.LogWarning("Waiting for next frame... (Cycle time: " + cycleTimer.ElapsedMilliseconds + ")", true);

                    yield return null;
                    cycleTimer.Restart();
                }

                if (stopRequested)
                {
                    IsRunning = false;
                    yield break;
                }
            }

            IsRunning = false;
            IsCompleted = true;

            if (hadToWait)
            {
                LoggingController.LogWarning("Waiting for next frame...done. (Cycle time: " + cycleTimer.ElapsedMilliseconds + ")", true);
            }
        }

        public void Abort()
        {
            stopRequested = true;
        }
    }
}