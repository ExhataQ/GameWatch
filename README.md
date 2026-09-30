# GameWatch

GameWatch is a Windows .NET 8 WPF network monitor. Run the app as administrator (the application manifest requests elevation) to enable ETW per-process traffic counters and Game Mode.

## Views

- **Live activity:** adapter throughput and graph remain visible while the process list updates. Process icons come from executable files. The list shows hosted Windows services for shared service processes and the remote TCP endpoints currently visible for each PID.
- **Traffic by time:** two draggable handles choose a date/time interval from the current session. The default follows the last five minutes. Per-app download and upload bytes are collected every two seconds and retained in memory for up to 24 hours; restarting the app clears the history. Partial samples at range boundaries are estimated proportionally.
- **Downloads:** Refresh lists active BITS jobs with their remote source and local target through Windows PowerShell. It also lists `.crdownload` and `.part` files in the current user's Downloads directory; these do not expose a source URL or files saved in custom folders.

`svchost.exe` hosts Windows services, not browser tabs. Several services may share one PID, so its network bytes cannot reliably be assigned to one hosted service. Browser tabs and HTTPS downloads cannot be identified by a remote IP or ETW byte counter; Delivery Optimization jobs are not BITS jobs.

Build with `dotnet build GameWatch/GameWatch.csproj` and run tests with `dotnet test GameWatch.Tests/GameWatch.Tests.csproj` on Windows with the .NET 8 SDK.
