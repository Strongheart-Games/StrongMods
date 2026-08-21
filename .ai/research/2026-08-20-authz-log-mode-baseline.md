# AuthZ log-mode baseline

## Scope and evidence

This is a source-only baseline for issue #132. It uses the checked-out AuthZ implementation and its shipped operator
documentation; it does not include external packet details, reproduction steps, or traffic captures.

The current working tree contained no AuthZ settings file or server-log artifact (`*.log`, `output_log*.txt`, or
`Player.log`) outside generated and scratch locations. Therefore, no honest live traffic is available locally for this
review. This is an observation of the workspace at review time, not evidence that no server has ever run AuthZ. The
project's own prototype record independently says nothing had run against a live server and lists honest-traffic
false-positive rate and busy-server prefix cost as unverified (`AuthZ/.ai/prototype-report.md:6`, `:76-85`).

## Current invariant and mode behavior

`InvariantEngine.RegisterAll` registers 19 invariants across five implemented families: 7 sender-binding, 4 ownership,
3 domain, 3 lifecycle, and 2 authority/editor rules (`AuthZ/InvariantEngine.cs:47-74`). Their base `Mode` defaults to
`Log` (`AuthZ/Invariant.cs:26-27`), as does `Settings.DefaultMode` (`AuthZ/Settings.cs:21`). The seeded configuration
also sets both the global default and every named invariant to `log` (`AuthZ/Docs/AuthZ.default.xml:25-58`).

Mode behavior is explicit:

| Mode | Runtime effect | Patch/cost effect |
|---|---|---|
| `off` | Does not evaluate the invariant. | A package with only off invariants is not patched (`AuthZ/InvariantEngine.cs:94-111`). |
| `log` | Evaluates, records a failure, and permits the packet. | The guarded package is patched and evaluated (`AuthZ/InvariantEngine.cs:143-171`). |
| `block` | Evaluates, records a failure marked `BLOCKED`, and rejects the packet when the invariant fails. | Same evaluation path as `log`; the only difference is `allow = false` after the record (`AuthZ/InvariantEngine.cs:163-171`; `AuthZ/ViolationLog.cs:53-64`). |

Settings load before Harmony patching, then modes are applied before the patch target set is resolved
(`AuthZ/ModApi.cs:18-26`; `AuthZ/InvariantEngine.cs:177-186`). Missing, unreadable, or not-yet-resolvable settings leave
the default log mode in force (`AuthZ/ModApi.cs:38-63`). An unrecognized mode falls back to the configured default and
emits a warning; unknown invariant IDs are also warned (`AuthZ/Settings.cs:65-96`).

The guard runs only on a server; client execution immediately allows the packet (`AuthZ/NetPackageGuard.cs:25-34`).
Packets with no sender are also allowed without evaluation (`AuthZ/InvariantEngine.cs:130-135`). A check exception is
logged and allowed, so an implementation failure does not convert into packet loss (`AuthZ/InvariantEngine.cs:148-157`).

## Logging and measurable bounds

Failures are attributed to the authenticated sender description, not to an asserted identity, and include invariant ID,
package type, expected fact, and observed detail (`AuthZ/ViolationLog.cs:29-64`). `Severity.Violation` uses a warning;
`Severity.Suspicious` uses ordinary output (`AuthZ/InvariantMode.cs:15-22`; `AuthZ/ViolationLog.cs:60-64`).

The only explicit log-rate bound is three individual lines per client-and-invariant pair per 60-second window. Further
failures are counted, suppressed, and summarized once the next failure closes that window
(`AuthZ/ViolationLog.cs:13-18`, `AuthZ/ViolationLog.cs:34-50`, `AuthZ/ViolationLog.cs:89-96`). Totals are retained per
client for the session and both totals and rate counters are removed on disconnect
(`AuthZ/ViolationLog.cs:26-27`, `AuthZ/ViolationLog.cs:67-87`; `AuthZ/InvariantEngine.cs:188-201`).

The implementation exposes these measurable cost proxies, but no timing, allocation, or throughput measurements. A
version-scoped source scan recorded 190 `NetPackage` subclasses, 124 server-reachable packages, 25 calling the entity
sender validator, and 3 calling the user validator (`AuthZ/.ai/netpackage-invariants.md:28-46`). These are static
surface counts, not live workload measurements.

| Scope | Proxy visible in source | Bound or qualifier |
|---|---|---|
| Patch surface | One `ProcessPackage` method per package type with at least one enabled invariant; inherited declarations are deduplicated. | 19 rules currently map to 15 distinct package types by the registration list; the source intentionally avoids patching all 124 package types (`AuthZ/InvariantEngine.cs:14-19`, `AuthZ/InvariantEngine.cs:86-112`). |
| Per inbound guarded packet | Iterates its runtime type and base types, then the enabled invariants registered to each type. | No measured upper latency. A package sharing rules can evaluate more than one; the current registration has two such shared package types (`AuthZ/InvariantEngine.cs:126-171`). |
| Sender binding | One claimed-entity versus sender-entity integer comparison; a diagnostic failure additionally looks up the claimed entity. | No world lookup on the passing path; documented as no tuning and no known false positives (`AuthZ/Invariants/SenderBindingInvariants.cs:8-11`, `:20-30`; `AuthZ/Docs/invariants.md:26-47`). |
| Ownership | Reads the killing-mode preference every call; when active, resolves a target and ownership/allowance relationships. | Inert outside `NoKilling`; missing target or unresolved ownership resolves to allowed (`AuthZ/ServerRules.cs:3-10`; `AuthZ/Invariants/OwnershipInvariants.cs:28-46`; `AuthZ/Ownership.cs:34-77`). |
| Lifecycle | One `HashSet<int>` membership operation per enabled once-per-connection invariant. | One state entry per participating connection; cleared on disconnect (`AuthZ/Invariants/LifecycleInvariants.cs:19-39`). |
| Domain | Local config lookups and comparisons, except no derived experience limit exists. | `exp.delta` uses configured positive ceiling 100000 by default (`AuthZ/Settings.cs:23-24`, `:39-47`; `AuthZ/Invariants/DomainInvariants.cs:18-38`, `:57-94`). |
| Violation logging | Two dictionaries keyed by sender/invariant or sender, plus string construction and logging only after a failed check. | Three detailed lines per key/window, then one deferred summary when a later failure reaches the window close (`AuthZ/ViolationLog.cs:26-64`, `:89-106`). |

## False-positive model

The shipped documentation classifies sender binding as having no known false positives, ownership and lifecycle as low
risk, and the editor rules as no-false-positive only for a server not being edited live (`AuthZ/Docs/invariants.md:47`,
`:77-78`, `:97`, `:120-129`). The source encodes conservative allowances: ownership permits passengers, allowed users,
crossplay identities, missing entities, and unowned/unknown ownership rather than guessing
(`AuthZ/Ownership.cs:47-66`, `:101-138`; `AuthZ/Invariants/OwnershipInvariants.cs:34-43`). `buff.known-name` and
`exp.delta` are deliberately `Suspicious`; the former can be affected by server/client mod mismatch and the latter has
no game-derived maximum (`AuthZ/Invariants/DomainInvariants.cs:43-48`, `:69-94`; `AuthZ/Docs/invariants.md:112-118`).

Plausibility and rate families are not implemented. Their documentation says their thresholds require a week of real
server logs, so neither adds a current baseline metric (`AuthZ/Docs/invariants.md:131-141`).

## Evidence required before block mode

The source's exact policy is per invariant: blocking is opt-in, and an operator must confirm the rule against that
operator's own logs before leaving it in block mode (`AuthZ/ModApi.cs:93-96`; `AuthZ/README.md:36-39`). It does not
define a numeric sample duration or an acceptable violation rate. Accordingly, the evidence gate supported by the
current repository is:

1. Run the specific invariant in `log` mode on the intended server and deployment configuration, and retain that
   server's own sanitized records.
2. Confirm the observed rule behavior is consistent with legitimate traffic for that configuration, including its
   killing mode, live-editor use, and loaded mod set where those affect the rule.
3. For `exp.delta`, establish the local observed maximum before tightening its ceiling; for rules marked `Suspicious`,
   treat a violation as a signal to investigate rather than standalone authority to block.
4. Promote one invariant at a time through its explicit `block` setting, since the engine makes the decision separately
   for each invariant (`AuthZ/Invariant.cs:8-10`; `AuthZ/Settings.cs:65-68`).

There is no checked-in honest-traffic evidence satisfying this gate. Block-mode readiness is therefore unproven for all
current invariants from the available local evidence.
