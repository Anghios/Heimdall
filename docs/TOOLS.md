<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Tools Reference

*Also available in French: [fr/TOOLS.md](fr/TOOLS.md).*

Developer reference for Heimdall's built-in tools, external tool
providers, and tool-hosting infrastructure.

## Overview

Heimdall ships 58 built-in tools registered by `ToolRegistry`. The raw
`Entry(` token appears more often in `ToolRegistry.cs` because it also matches
the internal `ToolEntry` record, the dynamic external-tool registration path,
and the private helper method. The effective built-in count is the set of
registry entries in the constructor:

- Network: 17
- Security: 15
- Encoding: 6
- System: 14
- External native tools: 6

External provider tools are detected at runtime and appended to the registry
after startup scans. They are not part of the 58 built-in count.

## Built-In Tool Catalog

Names below are sourced from `locales/en.json`; IDs are the stable registry
keys used by command palette, tabs, split panes, and tool lookup.

### Network

| ID | Name |
|---|---|
| `PING` | Ping Monitor |
| `DNS` | DNS Lookup |
| `CERT` | Certificate Inspector |
| `PORTSCAN` | Port Scanner |
| `SUBNET` | Subnet Calculator |
| `IPCONV` | IP Address Converter |
| `HTTP` | HTTP Status Codes |
| `WHOIS` | WHOIS Lookup |
| `HTTPHEADERS` | HTTP Header Analyzer |
| `BANNER` | Banner Grabber |
| `TCPTRACE` | Traceroute |
| `SNMPWALK` | SNMP Walker |
| `ARPMON` | ARP Monitor |
| `FWTEST` | Firewall Tester |
| `NETMAP` | Network Cartography |
| `NETCALC` | Network Calculator |
| `TCPPING` | TCP Ping |

### Security

| ID | Name |
|---|---|
| `HASH` | Hash Generator |
| `HMAC` | HMAC Generator |
| `PASSWORD` | Password Generator |
| `SSHKEY` | SSH Key Generator |
| `CERTGEN` | Certificate Generator |
| `JWT` | JWT Parser |
| `TOTP` | TOTP Generator |
| `PWDAUDIT` | Password Auditor |
| `SSHAUDIT` | SSH Key Auditor |
| `TLSAUDIT` | TLS Auditor |
| `DNSSEC` | DNS Security Checker |
| `SMBENUM` | SMB Enumerator |
| `DEFAULTCREDS` | Default Credential Scanner |
| `CVELOOKUP` | CVE Lookup |
| `SECNUMCLOUD` | SecNumCloud Audit |

### Encoding

| ID | Name |
|---|---|
| `BASE64` | Base64 Encoder / Decoder |
| `URLENC` | URL Encoder / Decoder |
| `JSON` | JSON Formatter |
| `REGEX` | Regex Tester |
| `DIFF` | Text Diff |
| `TEXTCASE` | Text Case Converter |

### System

| ID | Name |
|---|---|
| `CHMOD` | Chmod Calculator |
| `DATETIME` | DateTime Converter |
| `UUID` | UUID Generator |
| `ULID` | ULID Generator |
| `CRONTAB` | Crontab Builder |
| `LOGVIEW` | Log Viewer |
| `HOSTS` | Hosts File Editor |
| `SSHCONFIG` | SSH Config Generator |
| `CRONJOB` | Cron Job Manager |
| `SERVICES` | Service Status Dashboard |
| `NOTES` | Notes |
| `DIAGRAM` | Diagram Editor |
| `CMDLIB` | Command Library |
| `PRIVLAUNCH` | Privilege Launcher |

### External Native

| ID | Name |
|---|---|
| `WOL` | Wake-on-LAN |
| `OPENPORTS` | Open Ports |
| `NETIF` | Network Interfaces |
| `ROUTES` | Route Table |
| `DNSBATCH` | DNS Batch Resolver |
| `WIFI` | WiFi Networks |

## Tool Infrastructure

`ToolRegistry` is the single source of truth for built-in tool metadata and
factory creation. Adding a built-in tool means adding one registry entry plus
the corresponding `IToolView` implementation.

`ToolDescriptor` carries the stable metadata:

- `Id`: short lookup key such as `PING`
- `Category`: `Network`, `Security`, `Encoding`, `System`, or `External`
- `CategoryLabelKey`: i18n key for the category header
- `LabelKey`: i18n key for the display name
- `LabelWithArgKey`: optional i18n key for command-palette suggestions with a
  target argument
- `CommandPrefixes`: command palette aliases
- `IsNetworkTool`: whether standalone launch should prompt for a target
- `IconResourceKey`: XAML geometry resource key
- `DescriptionKey`: optional explicit description key; when null, the UI uses
  the `ToolDesc{Id}` convention

`IToolView` is the runtime contract for tool views. It exposes:

- `Initialize(ToolContext?, LocalizationManager?)`
- `CanClose()`
- `Dispose()`

`ToolsTabPopulationService` builds both the full Tools tab and the sidebar
Tools tree from the registry. It owns favorites, recents, search filtering,
category grouping, tool cards, and context inheritance.

`SidebarToolCategoryViewModel` and `SidebarToolItemViewModel` back the sidebar
Tools tree. Search uses precomputed lower-case searchable text (`name +
aliases`) so filtering does not allocate repeatedly.

Icons use geometry resources such as `Geo.Tool.PortScanner` and category brush
resources such as `ToolNetworkBrush`. `ToolRegistry.GetGeometryKey()` and
`ToolRegistry.GetCategoryBrushKey()` exist for XAML converters that do not
have DI access.

Gateway routing is view-level, not purely registry-level. `ToolGatewayConnector`
creates an SSH client for remote command execution through an SSH gateway and
requires a pinned gateway host key before connecting. Current views with a
`CmbRouteVia` route selector are:

- `BannerGrabberView`
- `CertInspectorView`
- `DefaultCredentialView`
- `DnsLookupView`
- `DnsSecurityView`
- `FirewallTesterView`
- `HttpHeaderAnalyzerView`
- `NetworkCartographyView`
- `PingToolView`
- `PortScannerView`
- `SecNumCloudAuditView`
- `SmbEnumeratorView`
- `SnmpWalkerView`
- `TcpTracerouteView`
- `TlsAuditView`
- `WhoisLookupView`

Tools that shell out through a gateway must keep per-probe timeouts and
explicit shell selection. For `/dev/tcp` checks, use `timeout ... bash -c ...`
so filtered ports do not leave remote shell processes running after the SSH
command channel is killed.

## External Tool Providers

Runtime-detected third-party tools use `IExternalToolProvider`,
`ExternalToolProviderService`, `ExternalToolInfo`, and
`ExternalToolWrapperView`.

Current provider implementations:

- `SysinternalsToolProvider`: scans Sysinternals tools such as PsExec, PsInfo,
  PsList, PsService, PsPing, Tcpvcon, Autorunsc, Sigcheck, AccessChk, Handle,
  ListDLLs, Disk Usage, and Whois.
- `NirSoftToolProvider`: scans NirSoft tools such as PingInfoView, CurrPorts,
  NetworkLatencyView, WakeMeOnLan, FastResolver, CountryTraceRoute,
  DNSDataView, NetResView, NetworkInterfacesView, WifiInfoView,
  Wireless Network Watcher, FullEventLogView, TaskSchedulerView, USBDeview,
  BlueScreenView, and ProduKey.
- `NanaRunToolProvider`: scans CLI-capable NanaRun project tools currently
  represented by MinSudo and SynthRdp.

`ExternalToolProviderService.ScanAll()` aggregates providers and applies
user-configured search paths from `AppSettings` (`SysinternalsPath`,
`NirSoftPath`, `NanaRunPath`). Detected tools are registered into
`ToolRegistry` as dynamic `ToolDescriptor` entries using IDs in the format:

```text
EXT:PROVIDER:TOOLID
```

`ExternalToolWrapperView` launches the detected executable, applies `{Host}`
and `{Port}` placeholders from `ToolContext`, captures stdout/stderr, and
displays output as text or structured data depending on `OutputFormat`.
Tools with `RequiresElevation` show an upfront elevation warning.

Licensing rule: third-party binaries are detected and wrapped only. Do not
redistribute NirSoft, Sysinternals, NanaRun, or other third-party tools inside
Heimdall packages unless their license explicitly allows it.

## SecNumCloud Audit Engine

`SecNumCloudAuditEngine` orchestrates the `SECNUMCLOUD` tool. It is aligned to
four SecNumCloud-oriented chapters:

- Network
- Cryptography
- Access Control
- Operations

The engine currently runs 15 checks across discovery, network exposure, TLS,
SSH, SMB, SNMP, HTTP headers, DNS records, default credentials, CVE banner
matching, and operational posture.

Progress is exposed through events:

- `PhaseProgress`
- `StatusChanged`
- `CheckCompleted`

Exports:

- `HtmlReportGenerator` creates a standalone HTML report.
- `CsvEvidenceExporter` exports audit evidence rows.
- `DrawIoExporter` in `Heimdall.Core.Discovery` creates Draw.io topology
  diagrams for discovery/cartography data.

Localization is injected into `SecNumCloudAuditEngine` through a
`Func<string, string>` delegate. Keep audit output strings localizable and do
not hardcode user-facing text in the engine.

## Diagram Editor

The `DIAGRAM` tool embeds the draw.io editor in a WebView2 surface. The editor
is vendored under `src/Heimdall.App/Assets/drawio/` and never reaches the
network: it is loaded with `offline=1&stealth=1`, and the vendored `index.html`
carries a Content-Security-Policy that keeps every request on the local virtual
host `heimdall-drawio.local`.

### Two ways in

- **From the Tools list**, with an empty canvas.
- **From Network Cartography**, with the Edit Diagram button. The scan is turned
  into a draw.io document and handed to the editor as unsaved content. It has no
  file behind it, so the first save asks where to put it.

### Toolbar

draw.io's own menu bar and toolbar are hidden. Inside a WebView2 iframe they
look interactive but do not dispatch reliably, so Heimdall provides the command
surface: New, Open, Save, Save As, Export PNG, Export SVG, Line, Format, Undo,
Redo, zoom out, 100%, zoom in, Duplicate, Delete. The shape library, the canvas
and the format panel are draw.io's own and work normally. Right-clicking the
canvas opens a Heimdall context menu that drives the same draw.io actions.

### Saving

The header shows the current file name, followed by `(modified)` while the
editor holds changes that are not on disk. Save writes to the current file;
Save As always asks for a new one. Ctrl+S inside the editor does the same thing
as the Save button. Closing the tab, or replacing the document with New or
Open, asks what to do with unsaved work.

The editor state lives in `DiagramDocumentState`: it holds the file path, the
content the editor last reported, and the content last read from or written to
disk. Everything else reads that record, which is why a save, a Save As and the
unsaved-changes prompt can never disagree.

### Language, theme and WebView2

The host page is opened as `heimdall-host.html?lang=<en|fr>&theme=<dark|light>`
and forwards both to draw.io, so the editor follows Heimdall rather than the
operating system locale. Browser accelerator keys are off: F5 would reload the
host page and drop the diagram. Without a WebView2 runtime, the tool shows a
fallback message instead of an editor.

The assets are about 44 MB across 2300 files and are excluded from Debug builds
(`Heimdall.App.csproj`), so the tool reports "Release builds only" when run from
a Debug output.

### Upgrading the vendored editor

Run `scripts/Vendor-DrawIo.ps1` against an upstream `src/main/webapp` checkout.
It copies the shipped subset, re-applies Heimdall's two edits (the
`heimdallDrawioApp` hook in `js/bootstrap.js`, the Content-Security-Policy in
`index.html`) and rewrites the version rows of `VENDORED.md` and both
`THIRD-PARTY-NOTICES` files. `DiagramEditorGuardTests` then checks that those
manifests match the version the bundle reports. Smoke-test the tool end to end
before committing: the guards read source, not behaviour.

### Draw.io export without the editor

`DrawIoExporter` in `Heimdall.Core.Discovery` turns a `NetworkScanSnapshot` into
draw.io XML, one swimlane per classified role plus one for hosts with no open
port. It takes a `Func<string, string>` localizer: role names are the
classifier's own identities and stay as they are, but the swimlane headings and
the node lines go through the locale catalogue. The lane colour and its node style
come from a single `RolePalette` record, so they cannot drift apart.

## Command Library And TwinShell

The `CMDLIB` tool embeds the TwinShell command library inside Heimdall.

Primary components:

- `TwinShellBootstrapper`: registers TwinShell persistence, repositories,
  services, localization bridge, settings bridge, and Git sync services in the
  Heimdall DI container.
- `CommandLibraryView`: WPF tool view.
- `CommandLibraryViewModel` and partial classes: filtering, command actions,
  history, favorites, generation, and UI state.
- TwinShell projects: `TwinShell.Core`, `TwinShell.Persistence`, and
  `TwinShell.Infrastructure`.

Persistence uses SQLite through `TwinShellDbContext`. The shared database path
is under the user's local application data in a `TwinShell` directory.

Seed data lives in `data/seed/actions/`. Files whose names start with `_` are
ignored; the current seed set contains 514 action JSON files.

Key user flows:

- fuzzy search with platform, category, and risk filters
- parameterized command generation
- favorites and command history
- import/export in TwinShell-compatible JSON
- Git sync through `IGitSyncService`
- Send to Terminal through `ToolContext.SendCommandAction`

System seed actions are protected: edit/delete actions are hidden for
non-user-created commands, and import merge skips system actions.

## Where To Find Things

- `src/Heimdall.App/Services/ToolRegistry.cs`: built-in registry and dynamic
  external registration.
- `src/Heimdall.Core/Models/ToolDescriptor.cs`: tool metadata record.
- `src/Heimdall.Core/Models/IToolView.cs`: tool view contract.
- `src/Heimdall.App/Services/ToolsTabPopulationService.cs`: full Tools tab
  and sidebar Tools population.
- `src/Heimdall.App/ViewModels/SidebarToolsViewModels.cs`: sidebar Tools
  tree view models.
- `src/Heimdall.App/Services/ToolGatewayConnector.cs`: SSH gateway routing
  helper for tools.
- `src/Heimdall.Core/Configuration/ExternalToolProvider.cs`: external provider
  model and interface.
- `src/Heimdall.Core/Configuration/SysinternalsToolProvider.cs`: Sysinternals
  provider.
- `src/Heimdall.Core/Configuration/NirSoftToolProvider.cs`: NirSoft provider.
- `src/Heimdall.Core/Configuration/NanaRunToolProvider.cs`: NanaRun provider.
- `src/Heimdall.App/Services/ExternalToolProviderService.cs`: provider
  aggregation and scan orchestration.
- `src/Heimdall.App/Views/Tools/ExternalToolWrapperView.xaml.cs`: generic
  external tool host.
- `src/Heimdall.App/Services/SecNumCloudAuditEngine.cs`: SecNumCloud audit
  orchestration.
- `src/Heimdall.App/Services/HtmlReportGenerator.cs`: HTML audit report.
- `src/Heimdall.App/Services/CsvEvidenceExporter.cs`: CSV evidence export.
- `src/Heimdall.Core/Discovery/DrawIoExporter.cs`: Draw.io topology export.
- `src/Heimdall.App/Services/TwinShellBootstrapper.cs`: TwinShell DI and seed
  initialization.
- `src/Heimdall.App/Views/Tools/CommandLibraryView.xaml.cs`: Command Library
  tool view.
- `src/Heimdall.App/ViewModels/CommandLibraryViewModel*.cs`: Command Library
  view-model slices.
- `data/seed/actions/`: command library seed actions.
