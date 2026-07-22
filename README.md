# IKAutomation

IKAutomation is a Windows WPF automation application for **Infinity Kingdom** running on **LDPlayer**. The project is being migrated from an older Facebook automation codebase, while retaining the proven WPF shell, `Auto_LDPlayer`, image matching, device helpers, cancellation, diagnostics, and logging infrastructure.

> The solution and executable still use some legacy names such as `ADB_Tool_Automation_Post_FB`. New Infinity Kingdom code should use the abstractions and services under `Core` and `Infrastructure`; do not extend the legacy Facebook workflow.

## Current capabilities

- Select an LDPlayer/LDMultiPlayer instance, including instances whose names contain spaces.
- Interact with LDPlayer through `ILdPlayerClient`, backed by `Auto_LDPlayer`.
- Detect game states with template matching instead of relying on fixed delays alone.
- Navigate from the city to the World Map.
- Check whether an allowed team is ready before starting a farm run.
- Select and run up to 20 LDPlayer instances concurrently with isolated per-device results.
- Preflight all selected instances before farming and retry failed instances without stopping healthy ones.
- Search Iron, Stone, Wood, or Food using configurable resource and level priorities.
- Fall back between resource types and levels when a target cannot be found or used.
- Verify resource popups, select an available farm team, and verify march dispatch.
- Stop an active or waiting run through per-run cancellation.
- Save diagnostic screenshots and detailed workflow status.
- Optionally send failure notifications through a Telegram bot.
- Maintain a per-device continuous-supervisor state model for long-running orchestration.

The gameplay workflow remains bounded and verifiable. Multi-device orchestration is kept outside the gameplay workflow; the continuous-supervisor foundation repeatedly schedules bounded runs and is being integrated incrementally for unattended operation.

## Requirements

- Windows 10 or Windows 11.
- Visual Studio 2022 with the **.NET desktop development** workload.
- .NET Framework 4.8.1 Developer Pack.
- LDPlayer 9 with `ldconsole.exe` available.
- Infinity Kingdom installed in the selected LDPlayer instance.
- The game language set to Vietnamese.
- Emulator resolution set to **1280 x 720**.
- ADB debugging enabled in LDPlayer when required by the installed LDPlayer version.

The Android package configured by default is:

```text
com.gtarcade.ioe.global
```

## Repository layout

```text
IKAutomation.sln                  Visual Studio solution
ADB/                              Main WPF application
  Core/                           Domain models, interfaces, and workflows
  Infrastructure/                 LDPlayer, vision, navigation, notifications, and persistence
  UI/                             Diagnostic WPF window
  Data/InfinityKingdom/           Runtime templates and configuration
Tests/                            Focused executable test projects
```

Runtime templates are stored under:

```text
ADB\Data\InfinityKingdom\1280x720\vi
```

Do not replace these with full-screen captures or images containing dynamic map content, coordinates, timers, or quantities.

## Initial configuration

Clone the repository and open the solution:

```cmd
git clone https://github.com/BaoNghiaNghia/IKAutomation.git
cd IKAutomation
start IKAutomation.sln
```

Update `LDCONSOLE_PATH` in `ADB\App.config` if LDPlayer is installed elsewhere. The repository default is:

```xml
<add key="LDCONSOLE_PATH" value="C:\LDPlayer\LDPlayer9\ldconsole.exe" />
```

Before running automation:

1. Start the required LDPlayer instance from LDMultiPlayer.
2. Set its resolution to 1280 x 720.
3. Start Infinity Kingdom and set the game language to Vietnamese.
4. Confirm that the instance name shown in IKAutomation matches the LDMultiPlayer name.
5. Keep the emulator visible and avoid manually interacting with it during a workflow.

## Build and run

Visual Studio is the recommended build path for the legacy WPF project:

1. Open `IKAutomation.sln`.
2. Set `ADB_Tool_Automation_Post_FB_Project` as the startup project.
3. Select `Release` and `Any CPU`.
4. Choose **Build > Build Solution**.
5. Run from Visual Studio or start the generated executable.

The Release executable is currently produced at:

```text
ADB\bin\Release\ADB_Tool_Automation_Post_FB.exe
```

Command-line builds are also supported when the .NET Framework targeting pack and all legacy package references resolve correctly:

```cmd
dotnet restore IKAutomation.sln
dotnet build IKAutomation.sln -c Release
```

If `dotnet build` cannot resolve `Auto_LDPlayer`, `KAutoHelper`, or `Tesseract`, build with Visual Studio/MSBuild using the installed .NET Framework 4.8.1 developer tools. Do not replace `Auto_LDPlayer` with raw ADB as a workaround.

## Running One-Shot Farm

Start the application, click **IK Device Diagnostic**, and select the target LDPlayer instance. In the One-Shot Farm section:

1. Select the allowed resource types.
2. Set level priority, for example `7,6,5`.
3. Set team priority, for example `4,3,2,1`.
4. Configure the team recheck interval and maximum wait duration.
5. Optionally enable Team 1 and the unoccupied-resource-only filter.
6. Save the settings if they should be reused.
7. Select one or more LDPlayer instances and click **Run Selected Devices** for
   a bounded batch, or **Run Continuous** to keep supervising each device until
   **Stop** is clicked.

Click **Stop** to cancel the current run or a readiness wait. By default, the readiness gate checks every 15 minutes and stops after 12 hours if no allowed team becomes available.

Saved UI preferences are written outside the repository to:

```text
%LOCALAPPDATA%\IKAutomation\farm-ui-preferences.json
```

## Telegram failure notifications

Telegram integration is optional. Never put the bot token or chat ID in `App.config`, source code, commits, screenshots, or issue reports.

1. Create a bot with BotFather and send `/start` to the new bot from the destination Telegram account.
2. Obtain the destination chat ID.
3. Store both values as user environment variables from `cmd`:

```cmd
setx IKAUTOMATION_TELEGRAM_BOT_TOKEN "YOUR_NEW_BOT_TOKEN"
setx IKAUTOMATION_TELEGRAM_CHAT_ID "YOUR_CHAT_ID"
```

4. Restart Visual Studio and IKAutomation so the new environment is loaded.

The application reports notification failures separately from the farm result. HTTP 401 or 404 normally means the token is invalid or revoked; recreate the token in BotFather, update the environment variable, and restart the application.

## Tests

Tests are focused console projects and do not require a real LDPlayer. Examples:

```cmd
dotnet run --project Tests\IKAutomation.OneShotFarm.Tests\IKAutomation.OneShotFarm.Tests.csproj -c Release
dotnet run --project Tests\IKAutomation.ResourceSearchExecution.Tests\IKAutomation.ResourceSearchExecution.Tests.csproj -c Release
dotnet run --project Tests\IKAutomation.MarchDispatch.Tests\IKAutomation.MarchDispatch.Tests.csproj -c Release
dotnet run --project Tests\IKAutomation.TelegramNotifications.Tests\IKAutomation.TelegramNotifications.Tests.csproj -c Release
```

Additional focused projects under `Tests\` cover vision, game detection, navigation, resource search, resource popup detection, team selection, and level fallback.

## Diagnostics and troubleshooting

Workflow failures include the outcome, last completed step, detected state, attempted resources and levels, eligible teams, message, and diagnostic image path. Generated evidence is normally placed beneath the active build output, for example:

```text
ADB\bin\Release\Diagnostics
```

Common checks:

- **Screenshot capture fails:** verify the selected LDMultiPlayer instance name, confirm it is running, and check LDPlayer ADB/debug settings.
- **State or template is not detected:** confirm Vietnamese language and 1280 x 720 resolution, then inspect the saved diagnostic frame.
- **World Map cannot be verified:** close unexpected dialogs and confirm the expected navigation icon is visible.
- **No eligible team:** verify that at least one team in the configured priority list is marked ready.
- **Telegram message is missing:** send `/start` to the bot, verify both environment variables, and restart the application.

Do not commit `bin`, `obj`, `.vs`, logs, diagnostics, screenshots, local settings, or credentials.

## Architecture notes

- Core workflows do not depend on WPF controls.
- New automation calls `ILdPlayerClient`; direct `Auto_LDPlayer` calls belong in its infrastructure adapter.
- Input actions are based on fresh screenshots and current template bounds rather than blind hard-coded taps.
- State transitions are verified after input.
- Async operations propagate `CancellationToken`, and polling is bounded.
- Device operations use the shared lock/workflow lease to prevent overlapping input.
- `ContinuousFarmSupervisor` owns independent device loops and state snapshots; it reuses the bounded multi-device runner instead of adding gameplay logic to WPF.
- The long-running operations roadmap and state model are documented in `docs/continuous-operations.md`.

## Known limitations

- Runtime recognition currently targets the Vietnamese 1280 x 720 UI.
- Continuous-supervisor core services are implemented, but watchdog, recovery ladder, circuit breaker, persistence, and adaptive concurrency are still staged roadmap work before a 15-day unattended run is considered production-ready.
- Some Facebook-era code, names, and project metadata still remain during migration.
- Screenshot capture depends on the behavior and configuration of the installed LDPlayer version.
- NuGet may report an existing `Emgu.CV` version warning because the legacy `Auto_LDPlayer` dependency requests a different version. Package versions are intentionally not upgraded as part of the migration work.

## Development rules

Read `AGENTS.md` before making changes. Keep gameplay workflows out of WPF event handlers, reuse the existing abstractions, add focused tests, and never commit credentials or generated runtime artifacts.
