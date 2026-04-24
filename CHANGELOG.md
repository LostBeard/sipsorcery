# SpawnDev.SIPSorcery Changelog

This file tracks changes to the **SpawnDev fork** of SIPSorcery. Upstream SIPSorcery changelog lives at https://github.com/sipsorcery-org/sipsorcery.

Format: version entries newest first, SpawnDev-fork lines only unless explicitly calling out what we merged from upstream.

---

## [10.0.5-rc.4] - SpawnDev fork - 2026-04-24

### Added
- **`TurnServerConfig.RelayPortRangeStart` / `TurnServerConfig.RelayPortRangeEnd`** - inclusive bounds for the per-allocation relay-socket port range. When both are set (non-zero, valid), each TURN Allocate binds its relay UDP socket within the range instead of using OS ephemeral port assignment. Required for NAT port-forwarding deployments where only a known range is forwarded from the router to the TURN host.
- New private `CreateRelaySocket()` helper walks the range from a random start offset (avoids colliding on the low end under concurrency) and falls back to OS ephemeral if every port in the range is in use.

### Rationale
Stock SipSorcery (and rc.1-rc.3 of the fork) binds `new UdpClient(new IPEndPoint(IPAddress.Any, 0))` per allocation - the OS picks from the full ephemeral range (typically 49152-65535 on Linux, 1025-65535 on Windows). Behind a consumer-grade NAT where you cannot forward 16,000+ UDP ports, this makes the TURN relay unreachable even with proper control-channel forwarding. The fix is standard for production TURN: coturn has `--min-port`/`--max-port`, pion has `RelayAddressGenerator` port ranges.

### Upstream candidacy
Drop-in extension (new optional config properties, backward-compatible default). Can be proposed upstream alongside the rc.3 ResolveHmacKey hook as a "TURN REST API + NAT" feature bundle.

## [10.0.5-rc.3] - SpawnDev fork - 2026-04-24

### Added
- **`TurnServerConfig.ResolveHmacKey`** - extension point for ephemeral / rotating / REST-API-issued TURN credentials (RFC 8489 §9.2, the "TURN REST API" pattern used by Twilio, Cloudflare, coturn's `--use-auth-secret`). When set, the server invokes the delegate with each incoming request's STUN USERNAME to resolve the per-request HMAC-SHA1 key for MESSAGE-INTEGRITY. Returning `null` rejects with 401 Unauthorized (unknown / expired username). When left unset, the server falls back to the classic RFC 5389 long-term-credential path using the static `Username` / `Realm` / `Password` triple (existing behavior unchanged).
- `ResolveHmacKeyForRequest(STUNMessage)` dispatcher helper in `TurnServer` that extracts USERNAME and delegates to the resolver. Called once per request before the message-type switch, so Allocate / Refresh / CreatePermission / ChannelBind all share the same resolved key.
- `HandleAllocate` now takes an explicit `byte[]? hmacKey` parameter instead of reading `_hmacKey` directly, so the resolved key propagates correctly through auth.

### Rationale
Classic long-term credentials require the TURN server to hold a user database. The TURN REST API pattern (used by every production TURN-as-a-service offering) lets an app backend mint time-limited `(username, password)` pairs from a shared secret; the server validates by recomputing the HMAC and never touches a user database. Before this change, consuming the fork from a multi-tenant signaling app required a wrapper proxy or a per-tenant server instance. Now a single `TurnServer` can serve any number of tenants with expiry, tracker-gating, or period-rotation enforced by the delegate.

### Upstream candidacy
The shape of this extension point is drop-in to current SipSorcery `main` - no existing callers break, no existing tests change, no new dependencies. Logged as a candidate upstream PR in the fork backlog once verified in production deployment.

## [10.0.5-rc.2] - SpawnDev fork - 2026-04-23

### Added
- Per-association SCTP burst tunables. `SctpAssociation.MaxBurst` + `SctpAssociation.BurstPeriodMilliseconds` expose the RFC 4960 §7.2.2 knobs for deployments where the spec defaults (4 chunks / 50 ms period) cap throughput below link capacity.
- Typical loopback / LAN tuning: `MaxBurst = 32, BurstPeriodMilliseconds = 10` gives ~8x end-to-end throughput improvement at sub-10ms RTT (~0.19 MB/s -> ~1.5 MB/s on measurement bench).
- Defaults preserved for WAN compatibility.
- New regression test `SctpDataSenderUnitTest.Throughput_MaxBurstTunable_ProcessesLargerQueueFaster` (720 chunks, 1008 KB, under 2 s threshold).
- `MAX_BURST` public const retained for API back-compat; instance uses new internal `_maxBurst` field that defaults to the const.

## [10.0.5-rc.1] - SpawnDev fork - 2026-04-23

### Fixed
- SctpDataSender producer-consumer lost-wakeup race that capped data-channel throughput at roughly `MAX_BURST*MTU/BURST_PERIOD` (~100 KB/s for MTU=1300 + default 50ms burst period) on localhost loopback, where SACKs round-trip in microseconds and the Reset-after-send window got hit almost every burst.
- Fix: `_senderMre.Reset()` moved to the TOP of DoSend's loop so Set() fired during the send work is preserved through to the next Wait(burstPeriod), which wakes the thread promptly.
- RFC 4960 §7.2.2 defaults unchanged.
- Measured on regression test `SctpDataSenderUnitTest.Throughput_FastSackWake_ExceedsBurstCeiling` (504 KB loopback, synchronous SACK delivery): pre-fix 5613 ms / 89.8 KB/s -> post-fix 94 ms / 5.4 MB/s (60x speedup).
- Unblocks SpawnDev.ILGPU.P2P's multi-MB tensor transfers.
- Dropped net462 from the test TFM list (fork's lowest supported is net48).

## [10.0.4] - SpawnDev fork - 2026-04-22

### Changed
- Stable release under the new PackageId `SpawnDev.SIPSorcery`.

## [10.0.4-local.1] - SpawnDev fork - 2026-04-22

### Changed
- First release under the new PackageId `SpawnDev.SIPSorcery`. Content unchanged from the earlier "SIPSorcery 10.0.4-pre" fork build that was never distributed publicly. Previously bundled inside SpawnDev.RTC 1.1.1 / 1.1.2-rc.1 nupkgs via workaround - now shipped as a first-class package for clean nuget.org resolution.

---

## Pre-fork upstream history (for reference only)

- **10.0.4-pre** - New SRTP and DTLS implementation (huge thanks to @jimm98y).
- **10.0.3** - Removed null SRTP ciphers.
- **10.0.2** - Removed use of master key index for SRTP.
- **10.0.1** - Support for .NET 10.0 added.
- **8.0.23** - Bug fixes.
- **8.0.22** - Stable release.
- **8.0.21-pre** - Improvements to OPUS encoder wiring.
- **8.0.20-pre** - Improvements to the audio pipeline to avoid consumers needing to handle raw RTP packets.
- **8.0.15-pre** - BouncyCastle update (thanks to @joaoladeiraext).
- **8.0.14** - Fix for OPUS codec using multiple channels.
- **8.0.13** - Added ASP.NET web socket signalling option for WebRTC. Bug fixes.
- **8.0.12** - Bug fixes.
- **8.0.11** - Real-time text (thanks to @xBasov). Bug fixes.
- **8.0.10** - H265 and MJEG packetisation (thanks to @Drescher86), RTCP feedback improvements (thanks to @ispysoftware).
- **8.0.9** - Minor improvements and bug fixes.
- **8.0.7** - Bug fixes and all sipsorcery packages release.
- **8.0.6** - NuGet publish.
- **8.0.4** - Bug fixes.
- **8.0.3** - Bug fixes.
- **8.0.1-pre** - Performance improvements (thanks to @weltmeyer). Add ECDSA as default option for WebRTC DTLS.
- **8.0.0** - RTP header extension improvements (thanks to @ChristopheI). Major version to 8 to reflect highest .net runtime supported.
