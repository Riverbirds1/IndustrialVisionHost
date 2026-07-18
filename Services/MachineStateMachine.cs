using System;
using System.Collections.Generic;
using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public sealed class MachineStateMachine
    {
        private static readonly IReadOnlyDictionary<MachineState, MachineState[]>
            AllowedTransitions =
                new Dictionary<MachineState, MachineState[]>
                {
                    [MachineState.Idle] =
                        new[]
                        {
                            MachineState.Ready,
                            MachineState.Fault,
                            MachineState.Emergency
                        },
                    [MachineState.Ready] =
                        new[]
                        {
                            MachineState.Idle,
                            MachineState.Running,
                            MachineState.Fault,
                            MachineState.Emergency
                        },
                    [MachineState.Running] =
                        new[]
                        {
                            MachineState.Completed,
                            MachineState.Fault,
                            MachineState.Emergency
                        },
                    [MachineState.Completed] =
                        new[]
                        {
                            MachineState.Ready,
                            MachineState.Idle,
                            MachineState.Fault,
                            MachineState.Emergency
                        },
                    [MachineState.Fault] =
                        new[]
                        {
                            MachineState.Idle,
                            MachineState.Ready,
                            MachineState.Emergency
                        },
                    [MachineState.Emergency] =
                        new[] { MachineState.Idle }
                };

        private readonly object stateSync = new object();
        private MachineState currentState = MachineState.Idle;
        private string lastReason = "程序启动";

        public event EventHandler<MachineStateChangedEventArgs>? StateChanged;

        public MachineState CurrentState
        {
            get
            {
                lock (stateSync)
                {
                    return currentState;
                }
            }
        }

        public string LastReason
        {
            get
            {
                lock (stateSync)
                {
                    return lastReason;
                }
            }
        }

        public bool CanTransitionTo(MachineState targetState)
        {
            lock (stateSync)
            {
                return targetState == currentState ||
                    Array.IndexOf(
                        AllowedTransitions[currentState],
                        targetState) >= 0;
            }
        }

        public bool TryTransition(
            MachineState targetState,
            string reason,
            out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "状态转换原因不能为空。",
                    nameof(reason));
            }

            MachineState previousState;
            MachineStateChangedEventArgs? eventArgs = null;

            lock (stateSync)
            {
                previousState = currentState;

                if (targetState == currentState)
                {
                    lastReason = reason.Trim();
                    errorMessage = null;
                    return true;
                }

                if (Array.IndexOf(
                        AllowedTransitions[currentState],
                        targetState) < 0)
                {
                    errorMessage =
                        $"不允许从 {currentState} 转换到 {targetState}。";
                    return false;
                }

                currentState = targetState;
                lastReason = reason.Trim();
                eventArgs = new MachineStateChangedEventArgs(
                    previousState,
                    targetState,
                    lastReason,
                    DateTime.Now);
                errorMessage = null;
            }

            StateChanged?.Invoke(this, eventArgs);
            return true;
        }
    }

    public sealed class MachineStateChangedEventArgs : EventArgs
    {
        public MachineStateChangedEventArgs(
            MachineState previousState,
            MachineState currentState,
            string reason,
            DateTime changedAt)
        {
            PreviousState = previousState;
            CurrentState = currentState;
            Reason = reason;
            ChangedAt = changedAt;
        }

        public MachineState PreviousState { get; }

        public MachineState CurrentState { get; }

        public string Reason { get; }

        public DateTime ChangedAt { get; }
    }
}
