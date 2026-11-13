## Download
- [Windows x64 ZIP]({{DOWNLOAD_URL}})
- [SHA-256 checksum]({{CHECKSUM_URL}})

## Getting started
Extract the ZIP file and run `PlcScope.exe`. Keep all extracted files together.

Requirements: Windows 10 or later (x64). The .NET runtime is included.

## Changes
### Changed
- Publish the executable, fixed-name distribution ZIP and SHA-256 checksum under `artifacts/release/` in local builds and CI.
- Remove the separate launcher batch file and update the application icon.

### Fixed
- Reject incomplete VirusTotal responses without a matching API-provided executable SHA-256.

<!-- virustotal:start -->
{{VIRUSTOTAL_CONTENT}}
<!-- virustotal:end -->
