# GameWatch

GameWatch is a Windows .NET 8 WPF network monitor. Run the app as administrator (the application manifest requests elevation) to enable ETW per-process traffic counters and Game Mode.

## Views

- **Live activity:** adapter throughput and the mini graph stay visible above the tabs. The process list shows executable icons (with a Windows default fallback), hosted services and live remote endpoints. Selecting a row reveals the complete hosted-services list and connection endpoints grouped by the owning module returned by Windows. The settings button opens refresh-speed and adapter options.
- **Traffic by time:** two draggable handles choose a date/time interval from the current session. Presets follow the last 5 or 15 minutes, 1 or 24 hours, or all available data. Date/time labels and a 200-bucket traffic sparkline sit under/behind the track. Per-app bytes are sampled every two seconds; samples older than an hour are compacted into one-minute buckets. History is kept in memory for up to 24 hours and clears when the app restarts. Partial samples at range boundaries are estimated proportionally.
- **Downloads:** While this tab is visible it refreshes about every ten seconds, or on button click. It lists active BITS jobs, Delivery Optimization status and partial browser files (`.crdownload`, `.part`, `.partial`, `.opdownload`) in the Windows known Downloads folder. BITS supplies a source and target path; Delivery Optimization supplies its file ID, size, bytes, status, and a source URL when Windows exposes one. File-type icons and BITS/DoSvc hints point to the relevant service host.

`svchost.exe` hosts Windows services, not browser tabs. Owner-module information labels **connections, not byte counts per service**; a module is not always a unique service. Browser tabs and HTTPS download URLs cannot be identified by remote IP or ETW byte counters. Browser files outside the known Downloads folder are not scanned. Browser History databases are not read.

Build with `dotnet build GameWatch/GameWatch.csproj` and run tests with `dotnet test GameWatch.Tests/GameWatch.Tests.csproj` on Windows with the .NET 8 SDK.

Run `python make_bundle.py` to create `GameWatch_bundle.zip` with source and tests, excluding `bin/`, `obj/` and `.git/`. The dormant `NetstatParser` utility is retained for its existing unit tests; the app never launches `netstat.exe`.
