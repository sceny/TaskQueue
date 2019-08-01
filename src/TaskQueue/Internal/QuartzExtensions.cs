using System;
using Quartz;

namespace Sceny.Internal
{
    public static class QuartzExtensions
    {
        public static TaskRunner GetTaskRunner(this JobDataMap jobData)
        {
            if (jobData is null)
                throw new ArgumentNullException(nameof(jobData));
            if (!(jobData[QuartzFactory.TaskRunnerKey] is TaskRunner taskRunner))
                throw new ArgumentNullException($"{nameof(JobDataMap)}[{QuartzFactory.TaskRunnerKey}]");
            return taskRunner;
        }
    }
}