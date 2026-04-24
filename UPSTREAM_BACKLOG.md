# Upstream SIPSorcery PR Backlog

Tracks changes in the SpawnDev fork of SIPSorcery that are candidates for upstreaming back to `sipsorcery-org/sipsorcery`. Each entry names the change, explains the rationale, and records when / how we plan to propose it.

**Upstream posture:** max 1 PR per 7-14 days per the crew's pace rule - batch small fixes, propose substantive features only after local verification. See `feedback_pace_upstream_contributions.md`.

---

## Ready to propose (verified, single-commit-clean)

_None currently open. Most recent merged upstream PR: #1510 (MESSAGE-INTEGRITY without FINGERPRINT)._

## Verified locally, holding until production bake

### `TurnServerConfig.RelayPortRangeStart` / `RelayPortRangeEnd` - bounded relay port range

**Added:** 2026-04-24 (fork 10.0.5-rc.4)
**Files:** `src/SIPSorcery/net/TURN/TurnServer.cs` (TurnServerConfig class + CreateRelaySocket helper + HandleAllocate)

**What:**
- Two new optional int properties on `TurnServerConfig` (default 0 = "not configured").
- When both are set with valid bounds, per-allocation relay sockets bind within the range instead of requesting OS ephemeral port assignment.
- Random start offset + fallback to ephemeral if every port in the range is in use.

**Why:**
Production TURN deployments behind NAT need a known port range to forward. Stock SipSorcery forces the operator to forward the OS's full ephemeral range (16,000+ ports) which is impractical on consumer routers and a broad attack surface on anything else. Matches coturn's `--min-port` / `--max-port` and pion's `RelayAddressGenerator` ranges.

**Upstream readiness:**
- [x] Fork compiles clean
- [x] Integration test in DesktopTurnAuthTests proves the bound range is honored
- [x] Backward compatible (defaults to unset / OS ephemeral)
- [ ] Add unit test inside SIPSorcery's own `test/` tree
- [ ] Co-propose with ResolveHmacKey hook as a "TURN REST API + NAT" feature bundle

### `TurnServerConfig.ResolveHmacKey` - per-request HMAC key resolver hook

**Added:** 2026-04-24 (fork 10.0.5-rc.3)
**Files:** `src/SIPSorcery/net/TURN/TurnServer.cs` (TurnServerConfig class + ProcessMessage dispatcher + HandleAllocate signature)

**What:**
- New optional `Func<string, byte[]?>? ResolveHmacKey` on `TurnServerConfig`.
- When set, the server resolves the MESSAGE-INTEGRITY HMAC-SHA1 key per-request from the STUN USERNAME attribute (invoking the delegate).
- When unset, the server falls back to the precomputed static long-term credential key - existing behavior unchanged.
- Returning `null` from the resolver rejects the request with 401 Unauthorized.

**Why:**
Enables the TURN REST API pattern (RFC 8489 §9.2) as used by Twilio, Cloudflare, and coturn's `--use-auth-secret`. Without this, multi-tenant / time-limited / tracker-gated TURN credentials require either a wrapper proxy or a per-tenant TurnServer instance.

**Shape:**
Drop-in extension point - existing `TurnServerConfig` consumers see no API change (new property has a safe default), existing `TurnServer` internals call the resolver through a small private helper that gracefully handles null / no-USERNAME / rejecter-returned-null.

**Upstream readiness:**
- [x] Fork compiles clean across net48 / net8.0 / net9.0 / net10.0
- [x] 11 integration tests pass (SpawnDev.RTC.DemoConsole / DesktopTurnAuthTests)
- [x] Backward compatible (no existing caller affected)
- [ ] Propagate the nullable-reference type annotation to the upstream project (upstream does not enable `<Nullable>` at the csproj level - wrap the new code in a file-scoped `#nullable enable` block, as our fork already does)
- [ ] Add a matching unit test inside SIPSorcery's own `test/` tree (our integration tests live outside the fork)
- [ ] Burn in for ~2 weeks in SpawnDev.RTC production signaling before proposing upstream

**When to propose:** after hub.spawndev.com runs this for 2+ weeks with real WebRTC traffic. Burn-in gives us production data to cite in the PR description.

## Local-only (no upstream plans)

### SRTP profile restrictions (browser-compatible only)
### DTLS stack preserved at BouncyCastle (not SharpSRTP rewrite)
### ECDSA P-256 default cert generation
### NotifySecureRenegotiation override for Pion compat
### MKI disabled per RFC 8827

_These are documented in `SpawnDev.RTC/CLAUDE.md` as fork-strategy reasons. They are opinionated deviations that upstream may not want; we keep them local._
