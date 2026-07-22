# Continuous operation roadmap

This document defines the reliability work required to run IKAutomation across
multiple LDPlayer instances for up to 15 days. Gameplay actions remain bounded
inside the existing One-Shot Farm workflow. Long-lived scheduling, recovery,
health monitoring, and retention belong to orchestration services outside WPF.

## Operating principles

- A failure on one device must not cancel, overwrite, or block healthy devices.
- Every device owns an independent lifecycle, retry schedule, and last-known state.
- The global automation limit remains 20 devices; later adaptive throttling may
  lower the active limit when the host is under pressure.
- Every wait, retry, lock acquisition, and device loop must honor the active
  `CancellationToken`.
- Gameplay inputs still require a fresh screenshot, fresh template bounds, and
  post-input state verification.
- Technical failures and expected game outcomes remain separate.

## Per-device state model

```text
Preflight -> Ready -> Running -> Waiting
    |                    |          |
    +--------------------+----------+-> Recovering
                                        |
                                        +-> Preflight
                                        +-> Quarantined

Any active state -> Stopped (user cancellation or supervisor shutdown)
```

| State | Meaning |
| --- | --- |
| `Preflight` | Validate LDPlayer, World Map, screenshot, roster, and allowed teams before a bounded cycle. |
| `Ready` | The device passed its gate and is waiting for or entering an execution slot. |
| `Running` | A bounded One-Shot Farm cycle is executing. |
| `Waiting` | The device is waiting for a ready team or the next scheduled cycle. |
| `Recovering` | The last cycle failed technically; only this device is delayed for recovery/retry. |
| `Quarantined` | Repeated failures exceeded policy; automatic gameplay input is paused for this device. |
| `Stopped` | The supervisor was cancelled or the device loop was intentionally stopped. |

Every state snapshot records the device name, cycle count, consecutive failures,
last transition, last success/failure, next attempt time, status message, and last
error. Snapshots are copied before publication so UI observers cannot mutate the
supervisor's live state.

## Implementation phases

### Phase 1 - Continuous supervisor and device state model (implemented)

- `IContinuousFarmSupervisor` exposes the long-running orchestration boundary.
- `ContinuousFarmSupervisor` creates one cancellable loop per selected device.
- Each loop invokes the existing bounded runner with one device, so the shared
  runner still enforces the maximum concurrency of 20.
- Successful cycles enter `Waiting` before the next scheduled cycle.
- Failed cycles enter `Recovering` and retry independently; healthy devices keep
  running.
- Cancellation interrupts both normal-cycle and failure-retry delays and moves
  every device to `Stopped`.
- The WPF window exposes **Run Continuous** and renders each device's latest
  supervisor state in the device list. The existing bounded run and retry
  buttons remain available.
- `Quarantined` is part of the model but is not activated until Phase 3.

The initial defaults are a 15-minute normal cycle interval and a 2-minute
technical-failure retry delay. Persisted supervisor settings will follow after
the recovery policies below are in place.

### Phase 2 - Watchdog and recovery ladder (next)

Track last screenshot, last verified state, last gameplay progress, current
operation, and lock duration. If a device makes no verified progress for 3-5
minutes, cancel only its current attempt and apply this bounded ladder:

1. Retry screenshot capture.
2. Validate LDPlayer/ADB availability.
3. Relaunch Infinity Kingdom.
4. Restart only the affected LDPlayer instance.
5. Run preflight again.
6. Escalate to quarantine when recovery remains unsuccessful.

### Phase 3 - Backoff, circuit breaker, and quarantine

- Use bounded exponential retry delays with jitter, for example 30 seconds,
  2 minutes, then 10 minutes.
- Count technical failures in a rolling window per device.
- Quarantine a device after a configurable threshold, initially five technical
  failures in 30 minutes.
- Keep other devices active and expose manual retry/unquarantine controls.
- Recheck quarantined devices after a cooling period, initially 30 minutes.

### Phase 4 - Log, diagnostics, and disk retention

- Rotate application logs daily or at 10-20 MB.
- Keep logs for 30 days and error diagnostics for 7-15 days.
- Do not retain routine success screenshots unless sampled.
- Stop creating new diagnostics and alert when free disk space falls below a
  configured threshold, initially 10 GB.

### Phase 5 - Persistent checkpoints

Persist each device's supervisor state, last success/error, current resource,
team, next check time, and recovery counters outside the repository. Restore a
safe `Preflight` transition after application or host restart; never resume by
sending a blind input from a stored gameplay state.

### Phase 6 - Adaptive concurrency and staggered animation handling

- Start conservatively at 4-6 active devices and adjust below the hard limit of
  20 using CPU, memory, screenshot latency, and LDPlayer/ADB error rate.
- Stagger device starts and repeated actions by 2-10 seconds.
- Stagger emulator restarts by 30-60 seconds.
- For animated transitions, require the relevant ROI to be stable for 2-3
  frames, allow a bounded transient `Unknown`, prioritize overlays/dialogs, and
  rematch from a fresh frame before tapping.
- Never use full-screen brightness to infer day/night or transition completion.

### Phase 7 - Operational notifications and heartbeat

- Notify Telegram on quarantine, recovery, emulator restart, disk pressure, and
  supervisor shutdown.
- Send an aggregated heartbeat every six hours with active, waiting, recovering,
  and quarantined device counts.
- Deduplicate repeated errors so a failing device cannot flood Telegram.

## Readiness for a 15-day run

Do not treat the system as unattended-production-ready until Phases 2-5 are
implemented and exercised in a staged soak test: 2 hours, 12 hours, 24 hours,
72 hours, and finally 15 days. Record memory growth, handle count, disk growth,
screenshot latency, failure rate, recovery rate, and per-device state duration.
