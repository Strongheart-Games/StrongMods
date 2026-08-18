# Overnight — measured defect: `XmlKeyValueStore` persists numbers in the current culture

**Date:** 2026-08-17 (unattended session) · **Severity:** silent data corruption, dormant today.
**Status:** **measured**, not inferred. Candidate `type:bug` / `mod:StrongUtils` issue — filing is the human's
call (an unattended session does not open issues).

## What happens

`StrongUtils/KeyValueStore/XmlKeyValueStore.cs` writes float/double values with
`value.ToString("R")` (lines 51 and 55, and the `TestAndSet` pair at 158 and 162) and reads them back with
`Convert.ChangeType(entry.Raw, typeof(T))` (line 72). **Both use the current culture**, so the value that
lands in the XML file is locale-shaped. A round trip inside one locale therefore works, and a round trip
*across* locales silently returns a different number.

## Reproduction (run 2026-08-17 against V3.1.0 b14, game unit, this machine)

Store `3.5f` with `CultureInfo.CurrentCulture = de-DE`, then reopen the same file under `en-US`:

```
de-DE file:        <Entry key="pi" type="Float" value="3,5" />
de-DE read back:   3,5      (correct)
en-US read back:   35       (wrong — off by a factor of 10)
```

`Convert.ChangeType("3,5", typeof(float))` under `en-US` reads the comma as a thousands separator, so it
parses to 35. No exception, no log line, no fallback to the default — the caller gets a plausible wrong
number.

The probe was a throwaway; it is **not** left in the suite, because a test asserting the current behavior
would pin the bug in place.

## Why it matters, and why it is not urgent

- **Not urgent:** the key-value store is dormant this season — `KeyValueStore.Init` has no live callers (the
  seed doc lists `KeyValueStore` among the intentionally-dormant StrongUtils features). Nothing writes a
  float today.
- **It does matter later:** production is Linux (`CONTEXT.md`) and development is this Windows machine, so a
  store file written on one and read on the other is exactly the cross-locale case, whenever the host locale
  differs. It also breaks a file carried between a server and an admin's machine.
- The same current-culture dependence applies to `TestAndSet(float/double)`: the expected value is formatted
  in the current culture and compared ordinally against a raw string that may have been written in another,
  so a legitimate compare-and-swap silently declines.

## The fix, if the owner wants it

Format and parse with `CultureInfo.InvariantCulture` on both sides:

- `value.ToString("R", CultureInfo.InvariantCulture)` in the four float/double writers.
- In `Get<T>`, `Convert.ChangeType(entry.Raw, typeof(T), CultureInfo.InvariantCulture)`.

Persisted files written before the change stay readable as long as the writing locale used `.`; a file
written under a comma locale would need a migration, which is the argument for changing it while the feature
is still dormant. `bool.ToString()` and `int/long.ToString()` are culture-safe in practice for these types, so
they need no change — but passing invariant culture uniformly documents the intent.

## Two smaller things in the same file, read from source

- **`Load()` has no tolerance for a damaged file** (line 258). `Enum.Parse` on the `type` attribute throws on
  an unknown or missing tag, and `doc.Root` is dereferenced unguarded. The constructor calls `Load()`, so a
  hand-edited or truncated `kvstore.xml` throws out of `KeyValueStore.Init`, which has no `try`. Compare
  `ConfigManager`, which tolerates more.
- **`Clear()` reports a null old value** (line 121) while `Remove()` reports the real one (line 110). The
  interface doc for `VarChangedEventArgs.OldRawValue` says "null on Created", so a subscriber that trusts the
  doc cannot tell a cleared key's previous value. Asserted in the new tests only as far as the change *type*,
  deliberately, so the inconsistency is not pinned.
