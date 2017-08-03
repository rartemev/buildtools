﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Build.Framework;
using Microsoft.Build.Tasks;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Build.Tasks
{
    /// <summary>
    /// Run a command and retry if the exit code is not 0.
    /// </summary>
    public class ExecWithRetries : Microsoft.Build.Utilities.Task, ICancelableTask
    {
        [Required]
        public string Command { get; set; }

        public bool IgnoreStandardErrorWarningFormat { get; set; }

        public int MaxAttempts { get; set; } = 5;

        /// <summary>
        /// Base, in seconds, raised to the power of the number of retries so far.
        /// </summary>
        public double RetryDelayBase { get; set; } = 6;

        /// <summary>
        /// A constant, in seconds, added to (base^retries) to find the delay before retrying.
        /// 
        /// The default is -1 to make the first retry instant, because ((base^0)-1) == 0.
        /// </summary>
        public double RetryDelayConstant { get; set; } = -1;

        private CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();

        private Exec _runningExec;

        public void Cancel()
        {
            _runningExec?.Cancel();
            _cancelTokenSource.Cancel();
        }

        public override bool Execute()
        {
            for (int i = 0; i < MaxAttempts; i++)
            {
                string attemptMessage = $"(attempt {i + 1}/{MaxAttempts})";

                _runningExec = new Exec
                {
                    BuildEngine = BuildEngine,
                    Command = Command,
                    IgnoreStandardErrorWarningFormat = IgnoreStandardErrorWarningFormat,
                    LogStandardErrorAsError = false,
                    IgnoreExitCode = true
                };

                if (!_runningExec.Execute())
                {
                    Log.LogError("Child Exec task failed to execute.");
                    break;
                }

                int exitCode = _runningExec.ExitCode;
                if (exitCode == 0)
                {
                    return true;
                }

                string message = $"Exec FAILED: exit code {exitCode} {attemptMessage}";

                if (i + 1 == MaxAttempts || _cancelTokenSource.IsCancellationRequested)
                {
                    Log.LogError(message);
                    break;
                }

                Log.LogMessage(MessageImportance.High, message);

                TimeSpan delay = TimeSpan.FromSeconds(
                    Math.Pow(RetryDelayBase, i) + RetryDelayConstant);

                Log.LogMessage(MessageImportance.High, $"Retrying after {delay}...");

                try
                {
                    Task.Delay(delay, _cancelTokenSource.Token).Wait();
                }
                catch (AggregateException e) when (e.InnerException is TaskCanceledException)
                {
                    break;
                }
            }
            return false;
        }
    }
}
