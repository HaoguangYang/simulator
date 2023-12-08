/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using SimpleJSON;
using UnityEngine;

namespace Simulator.Api.Commands
{
    class Run : IDistributedCommand
    {
        public string Name => "simulator/run";

        public void Execute(JSONNode args)
        {
            var api = ApiManager.Instance;
            var sim = SimulatorManager.Instance;

            var timeLimit = args["time_limit"].AsFloat;
            if (timeLimit != 0)
            {
                var frameLimit = (int)(timeLimit / Time.fixedDeltaTime);
                api.FrameLimit = api.CurrentFrame + frameLimit;
            }
            else
            {
                api.FrameLimit = 0;
                api.API_Notified = false;
            }

            var timeScale = args["time_scale"];
            if (timeScale == null || timeScale.IsNull)
            {
                api.TimeScale = 1f;
            }
            else
            {
                api.TimeScale = timeScale.AsFloat;
            }
            SimulatorManager.SetTimeScale(api.TimeScale);

            sim.AnalysisManager.AnalysisInit();
        }
    }
}
