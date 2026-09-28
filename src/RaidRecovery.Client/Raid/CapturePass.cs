using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RaidRecovery.Client.Raid
{
    /// <summary>
    /// Reading the state of a raid, cut into steps and spread over several frames. Each frame runs steps until
    /// its time is used up, then hands over to the game. A step is never cut in two: the longest one is the
    /// most a single frame can cost, which is why the pass remembers it.
    /// </summary>
    internal sealed class CapturePass
    {
        private readonly Queue<Step> _steps = new Queue<Step>();
        private readonly Stopwatch _stopwatch = new Stopwatch();

        public bool Manual { get; set; }

        /// <summary>Time spent on the main thread, all frames added up.</summary>
        public double TotalMs { get; private set; }

        /// <summary>The most one frame had to give.</summary>
        public double LongestFrameMs { get; private set; }

        public double LongestStepMs { get; private set; }

        public string LongestStep { get; private set; } = "none";

        public int Frames { get; private set; }

        /// <summary>Set when a step the snapshot cannot do without has failed.</summary>
        public Exception Failure { get; private set; }

        /// <param name="required">false for a step whose failure only costs its own part, a bot for instance.</param>
        public void Add(string name, Action action, bool required = true)
        {
            _steps.Enqueue(new Step { Name = name, Action = action, Required = required });
        }

        /// <returns>true once every step has run, or one the snapshot cannot do without has failed.</returns>
        public bool Run(double budgetMs)
        {
            Frames++;
            var frameMs = 0.0;

            // At least one step per frame, whatever it costs: otherwise a step longer than the budget would never run
            do
            {
                if (_steps.Count == 0)
                {
                    break;
                }

                var step = _steps.Dequeue();
                _stopwatch.Restart();
                try
                {
                    step.Action();
                }
                catch (Exception ex)
                {
                    if (step.Required)
                    {
                        Failure = ex;
                        _steps.Clear();
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Left out of the snapshot ({step.Name}): {ex.Message}");
                    }
                }

                var stepMs = _stopwatch.Elapsed.TotalMilliseconds;
                frameMs += stepMs;
                if (stepMs > LongestStepMs)
                {
                    LongestStepMs = stepMs;
                    LongestStep = step.Name;
                }
            } while (frameMs < budgetMs);

            TotalMs += frameMs;
            LongestFrameMs = Math.Max(LongestFrameMs, frameMs);
            return _steps.Count == 0;
        }

        private sealed class Step
        {
            public string Name;
            public Action Action;
            public bool Required;
        }
    }
}
