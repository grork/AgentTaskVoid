# Usage Ergonomics

Questions about the shape of the CLI surface and how consumers interact with it:
identifying and updating tasks, cleanup, input modeling, grouping, and wrapping.

## Questions

- [`ERGO-1`: How callers reference and revise a task over time](./ERGO-1-how-callers-reference-and-revise-a-task-over-time.md) -- EXPANDED
- [`ERGO-2`: Garbage collection of orphaned / user-hidden AppTaskInfo entries](./ERGO-2-garbage-collection-of-orphaned-user-hidden-apptaskinfo-entries.md) -- DECIDED
- [`ERGO-3`: Expressing the different input types ergonomically within API restrictions](./ERGO-3-expressing-the-different-input-types-ergonomically-within-api-restrictions.md) -- EXPANDED
- [`ERGO-4`: Grouping vs separating icons across multiple consumers (glomming control)](./ERGO-4-grouping-vs-separating-icons-across-multiple-consumers-glomming-control.md) -- EXPANDED
- [`ERGO-5`: Providing a wrapper that runs another script/tool and manages its lifecycle](./ERGO-5-providing-a-wrapper-that-runs-another-script-tool-and-manages-its-lifecycle.md) -- DECIDED
- [`ERGO-6`: The identifier a caller holds to address a task across invocations](./ERGO-6-identifier-a-caller-holds-to-address-a-task-across-invocations.md) -- DECIDED
- [`ERGO-7`: Whether the CLI keeps persistent state of its own](./ERGO-7-whether-the-cli-keeps-persistent-state-of-its-own.md) -- DECIDED
- [`ERGO-8`: Update verbs for ergonomic revision given whole-content replacement](./ERGO-8-update-verbs-for-ergonomic-revision-given-whole-content-replacement.md) -- DECIDED
- [`ERGO-9`: Overall command-surface shape for content input](./ERGO-9-overall-command-surface-shape-for-content-input.md) -- DECIDED
- [`ERGO-10`: Guarding unsupported state x content x mutator combinations](./ERGO-10-guarding-unsupported-state-x-content-x-mutator-combinations.md) -- DECIDED
- [`ERGO-11`: Expressing structured and multi-valued inputs on the command line](./ERGO-11-expressing-structured-and-multi-valued-inputs-on-the-command-line.md) -- DECIDED
- [`ERGO-12`: Defaults for parameters that are secretly required](./ERGO-12-defaults-for-parameters-that-are-secretly-required.md) -- DECIDED
- [`ERGO-13`: Empirical: is grouping keyed on the exact icon URI string?](./ERGO-13-empirical-is-grouping-keyed-on-the-exact-icon-uri-string.md) -- DECIDED
- [`ERGO-14`: The CLI surface for expressing grouping intent](./ERGO-14-cli-surface-for-expressing-grouping-intent.md) -- DECIDED
- [`ERGO-15`: Default grouping when the consumer specifies nothing](./ERGO-15-default-grouping-when-the-consumer-specifies-nothing.md) -- DECIDED
- [`ERGO-16`: Ownership and isolation between consumers sharing one identity](./ERGO-16-ownership-and-isolation-between-consumers-sharing-one-identity.md) -- DECIDED
- [`ERGO-17`: Configuration surface for recurring defaults](./ERGO-17-configuration-surface-for-recurring-defaults.md) -- DECIDED
- [`ERGO-18`: The shipped command name](./ERGO-18-shipped-command-name.md) -- DECIDED
- [`ERGO-19`: Should `update` invocations also trigger the user-hidden GC sweep?](./ERGO-19-should-update-invocations-also-trigger-the-user-hidden-gc-sweep.md) -- DECIDED
- [`ERGO-20`: Icon representation -- specifying an icon without image files](./ERGO-20-icon-representation-specifying-an-icon-without-image-files.md) -- DECIDED
- [`ERGO-21`: The sidecar store design](./ERGO-21-sidecar-store-design.md) -- DECIDED
- [`ERGO-22`: Icon glyph -> PNG rendering](./ERGO-22-icon-glyph-png-rendering.md) -- DECIDED
- [`ERGO-23`: Clean up of sidecar files](./ERGO-23-clean-up-of-sidecar-files.md) -- DECIDED
- [`ERGO-24`: The default deepLink URI value](./ERGO-24-default-deeplink-uri-value.md) -- DECIDED
- [`ERGO-25`: `start` on an already-live handle (re-entrancy semantics)](./ERGO-25-start-on-an-already-live-handle.md) -- DECIDED
- [`ERGO-26`: Config file location and format](./ERGO-26-config-file-location-and-format.md) -- DECIDED
- [`ERGO-27`: The consolidated v1 command surface](./ERGO-27-consolidated-v1-command-surface.md) -- DECIDED
- [`ERGO-28`: Theme-awareness of the icon provided to the platform](./ERGO-28-theme-awareness-of-the-provided-icon.md) -- DECIDED
- [`ERGO-29`: Caller-supplied (external) icons -- bring-your-own image, and extraction from an exe/app](./ERGO-29-caller-supplied-external-icons.md) -- DECIDED
- [`ERGO-30`: A repo-scoped defaults file the tool auto-discovers (icons, titles, glomming)](./ERGO-30-repo-scoped-defaults-config.md) -- DECIDED
- [`ERGO-31`: The v2 semantic verb contract (the engine's public integration API)](./ERGO-31-v2-semantic-verb-contract.md) -- DECIDED
- [`ERGO-32`: A low-level / raw card-control API](./ERGO-32-low-level-raw-card-control-api.md) -- DEFERRED
- [`ERGO-33`: The card title/subtitle chain, ending in a never-blank default](./ERGO-33-card-title-subtitle-chain-never-blank-default.md) -- DECIDED
- [`ERGO-34`: Icons should be randomly picked when they are not explicitily supplied](./ERGO-34-icons-should-be-random-when-not-supplied.md) -- DECIDED
- [`ERGO-35`: Card URI opening the ATV folder is confusing to the user; it should open to the repo root](./ERGO-35-card-uri-should-open-repo-folder.md) -- DECIDED