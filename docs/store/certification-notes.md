# Microsoft Store certification notes

## Restricted capability justification

The package declares `runFullTrust` because it is a WinUI 3 desktop application that maintains a notification-area icon and calls the documented Windows `PowrProf.dll` power-scheme APIs. It runs at medium integrity as the current user. It does not request elevation, install a service, modify the registry, access arbitrary files, or communicate over a network.

## Core test flow

1. Launch the app and dismiss or accept the sign-in startup explanation.
2. Confirm that the current Windows power plan is shown on the overview card and tray flyout.
3. Open **Quick slot mappings** and bind at least two slots to plans available on the test device.
4. Select a mapped slot from the main window, tray flyout, or tray context menu and confirm Windows reports the same active plan.
5. Add a short non-overlapping daily range targeting another mapped slot, enable automatic switching, and verify the change at the range boundary.
6. Manually select a different slot during the range and verify it remains active until the next schedule boundary.
7. Attempt to add an overlapping range and confirm the UI rejects it.
8. Use **Create and bind missing standard plans** only if the test environment permits adding plans; the app asks for confirmation and does not delete existing plans.
9. Exit through the tray menu and confirm the app warns that automatic switching will stop.

## Expected environment-dependent behavior

Some Modern Standby, OEM-managed, or organization-managed systems expose only one power plan or reject duplication. The app presents a localized error and allows mapping another available plan. This is expected and is not a failure of the application.

## Data and network

No sign-in is required. The app does not collect telemetry and makes no network requests. Settings are stored under the package's `LocalState` directory.
