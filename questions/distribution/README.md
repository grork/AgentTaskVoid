# Distribution / Deployment

How the tool reaches its consumers: the packaging artifact, how identity is granted at
install time, signing, and the delivery channel.

Un-deferred 2026-07-04 -- the operator explicitly lifted the brief's distribution
deferral (brief assertion 2) once the identity model hit bedrock. Rationale: identity is
an INSTALL-TIME concern, so the packaging shape drives dev provisioning (INFRA-17,
"Dogfood/run ergonomics without a load-bearing script"), test isolation (INFRA-16,
"Test-time identity provisioning and deep isolation"), and downstream the watchdog
(LIFE-6) and any future activation receiver (INTER-1). The operations (cert acquisition,
winget publication mechanics) stay deferred to ship time; only the direction is decided
now.

## Questions

- [`DIST-1`: The end-user distribution vehicle](./DIST-1-end-user-distribution-vehicle.md) -- DECIDED
- [`DIST-2`: Signing / certificate acquisition](./DIST-2-signing-certificate-acquisition.md) -- DEFERRED
- [`DIST-3`: Dev vs release identity (PFN) divergence](./DIST-3-dev-vs-release-identity-pfn-divergence.md) -- DECIDED
- [`DIST-4`: Posture for the zero-pre-install script consumer](./DIST-4-posture-for-the-zero-pre-install-script-consumer.md) -- DECIDED
- [`DIST-5`: End-to-end packaged-AOT release verification](./DIST-5-end-to-end-packaged-aot-release-verification.md) -- EXPANDED
- [`DIST-6`: Package upgrade while the watchdog is running](./DIST-6-package-upgrade-while-the-watchdog-is-running.md) -- DECIDED
- [`DIST-7`: The release full-package manifest](./DIST-7-release-full-package-manifest.md) -- DECIDED
- [`DIST-8`: The joined release-leg spike](./DIST-8-joined-release-leg-spike.md) -- DECIDED
- [`DIST-9`: Uninstall behavior with live tasks and a running watchdog](./DIST-9-uninstall-behavior-with-live-tasks-and-a-running-watchdog.md) -- DECIDED
- [`DIST-10`: Getting `atv` onto the machine alongside the host plugin/integration](./DIST-10-getting-atv-onto-the-machine-alongside-the-host-plugin.md) -- DEFERRED
- [`DIST-11`: How the per-host integration artifact is delivered, placed on disk, and wired into the host's config](./DIST-11-integration-artifact-delivery-placement-and-host-config-wiring.md) -- DECIDED
- [`DIST-12`: Daily driver on the retail identity; dev vacates `atv`; plugin command override](./DIST-12-daily-driver-retail-identity-and-plugin-command-override.md) -- DECIDED
