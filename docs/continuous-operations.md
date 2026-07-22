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
- `Quarantined` is activated by the Phase 2 watchdog when an attempt ignores
  cancellation or the bounded recovery ladder is exhausted. Phase 3 will add
  rolling failure thresholds and manual unquarantine controls.

The initial defaults are a 15-minute normal cycle interval and a 2-minute
technical-failure retry delay. Persisted supervisor settings will follow after
the recovery policies below are in place.

### Phase 2 - Watchdog and recovery ladder (implemented)

Track the latest workflow progress and current operation. Recovery captures a
fresh screenshot and validates the LDPlayer boundary. If an active gameplay
operation makes no progress for five minutes, cancel only its current attempt
and apply this bounded ladder. The intentional ready-team wait uses a separate
20-minute threshold so the normal 15-minute check interval is not treated as a
hang:

1. Retry screenshot capture.
2. Validate LDPlayer/ADB availability.
3. Relaunch Infinity Kingdom.
4. Restart only the affected LDPlayer instance.
5. Run preflight again.
6. Escalate to quarantine when recovery remains unsuccessful.

The supervisor now records the latest progress timestamp and operation for each
device. A five-minute no-progress watchdog cancels only that device's active
attempt and allows ten seconds for cooperative shutdown. Recovery never starts
while the old attempt may still own native LDPlayer work; such a device is
quarantined immediately. Responsive failures run the bounded ladder above and
return through a fresh workflow preflight. A failed ladder quarantines only the
affected instance while healthy device loops continue. Ordinary bounded
gameplay outcomes keep the existing independent retry behavior and do not
restart LDPlayer as if they were infrastructure failures.

### Phase 3 - Backoff, circuit breaker, and quarantine (implemented)

- Use bounded exponential retry delays with jitter, for example 30 seconds,
  2 minutes, then 10 minutes.
- Count technical failures in a rolling window per device.
- Quarantine a device after a configurable threshold, initially five technical
  failures in 30 minutes.
- Keep other devices active; automatic cooldown recovery is implemented, while
  an explicit manual unquarantine control remains a UI follow-up.
- Recheck quarantined devices after a cooling period, initially 30 minutes.

Technical retries now use 30-second, 2-minute, and 10-minute delay tiers plus
a stable per-device jitter of up to 15 seconds. Each device owns an independent
rolling 30-minute technical-failure window. Five failures open its circuit and
move only that device to `Quarantined`; the supervisor continues healthy device
loops. After a 30-minute cooldown the recovery ladder probes the instance. A
successful probe closes the circuit, clears the rolling failure counter, and
requires a fresh preflight. Failed probes remain quarantined and repeat only
after another cancellation-aware cooldown. An attempt that ignores watchdog
cancellation remains hard-quarantined because starting another input path would
risk concurrent commands on the same emulator.

### Phase 4 - Log, diagnostics, and disk retention (implemented)

- `Logger` appends across restarts and rotates daily or when `log.txt` reaches
  20 MB. Rotated files are kept under `Logs` for 30 days.
- `FileSystemOperationalMaintenanceService` runs at most once per configured
  interval even when many device loops request maintenance concurrently.
- PNG/JSON diagnostics older than 14 days are removed first. Remaining files
  are then deleted oldest-first until the 5 GB diagnostic quota is respected.
- Free space is checked after cleanup. Diagnostic writes are suspended below
  10 GB and resume only above 12 GB, providing hysteresis and preventing disk
  pressure from flapping. Gameplay loops remain isolated and continue without
  optional screenshots.
- Maintenance and all diagnostic-write suppression honor cancellation. The
  configured diagnostic root is resolved under the application directory and
  cleanup never scans outside that root.

### Phase 5 - Persistent checkpoints (implemented)

- Each device owns a versioned JSON checkpoint under
  `%LOCALAPPDATA%\IKAutomation\Checkpoints`, outside the repository and runtime
  output folders.
- Checkpoints record the supervisor snapshot, last success/error, observed
  resource/level/team, next attempt time, watchdog/recovery/circuit counters,
  disk status, and rolling technical-failure timestamps.
- Writes use a same-directory temporary file, flushed to disk and atomically
  replace the prior checkpoint. Concurrent device loops never share a file.
- Meaningful state/counter/resource/team changes are persisted immediately;
  unchanged progress is throttled to one checkpoint write per 30 seconds to
  keep 15-day multi-device runs from generating unnecessary disk I/O.
- Invalid or unsupported checkpoints are renamed with an `.invalid-*` suffix;
  that device starts from a clean state while healthy device checkpoints remain
  usable.
- After application or host restart, persisted metadata is restored but the
  execution state is always forced to `Preflight`. Stored gameplay state never
  causes a Tap, Back, Swipe, or dispatch; normal live detection must pass again.
- Checkpoint failures are non-fatal and isolated to the affected device. Every
  checkpoint operation honors the active cancellation token.

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
